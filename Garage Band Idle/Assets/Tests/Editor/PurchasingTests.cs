using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // One buy entry point, fail-closed on every leg (design doc 12.2/12.11).
    public class PurchasingTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // Affordable and gate-open, so a test only has to break one thing.
        // `author` runs against the DEFINITIONS before the rebuild, because the
        // gather is compiled when the tree is built: a source or an upgrade
        // authored afterward sits on no plan. The balances are FACTS, so they
        // are poured onto the nodes the rebuild left.
        private static TestTree Ready(System.Action<TestTree> author = null)
        {
            var tree = new TestTree();
            if (author != null)
            {
                author(tree);
                tree.Rebuild();
            }
            tree.Tier1.balances["cash"] = 1000;
            tree.Tier1.earnedTotals["cash"] = 1000;
            return tree;
        }

        // ---- generators ----

        [Test]
        public void Buying_a_generator_spends_the_curve_cost_and_increments_the_count()
        {
            var tree = Ready();
            tree.Tier1.generatorCounts["practice_amp"] = 2;

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp, 1));

            // The third amp costs 60 x 1.15^2.
            AssertClose(1000 - 60 * 1.15 * 1.15, tree.Tier1.balances["cash"], "balance");
            Assert.AreEqual(3, tree.Tier1.generatorCounts["practice_amp"]);
        }

        [Test]
        public void Spending_never_touches_the_earned_total()
        {
            var tree = Ready();
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp, 1));

            // Section 2's strobe-proofing: a threshold met once stays met.
            AssertClose(1000, tree.Tier1.earnedTotals["cash"], "earned total");
            AssertClose(940, tree.Tier1.balances["cash"], "balance");
        }

        [Test]
        public void An_unmet_gate_refuses_the_buy()
        {
            var tree = Ready();

            // The drummer needs three amps.
            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.Drummer, 1));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("drummer"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");

            tree.Tier1.generatorCounts["practice_amp"] = 3;
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.Drummer, 1));
            Assert.AreEqual(1, tree.Tier1.generatorCounts["drummer"]);
        }

        [Test]
        public void An_unauthored_gate_is_closed_not_open()
        {
            GeneratorDefinition gateless = null;
            var tree = Ready(t =>
            {
                gateless = TestTree.MakeDefinition<GeneratorDefinition>("gateless_gear");
                gateless.costCurrency = t.Cash;
                gateless.baseCost = 10;
                gateless.growth = 1.15;
                gateless.produces.Add(TestTree.Entry(t.Cash, Stat.Rate, 1));
                t.Tier1Def.generators.Add(gateless);
            });

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), gateless, 1));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
        }

        [Test]
        public void An_unaffordable_buy_refuses_and_leaves_the_balance_alone()
        {
            var tree = Ready();
            tree.Tier1.balances["cash"] = 59.99;

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp, 1));
            AssertClose(59.99, tree.Tier1.balances["cash"], "balance");
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"));
        }

        [Test]
        public void A_computed_cost_of_zero_is_refused_at_runtime()
        {
            GeneratorDefinition free = null;
            var tree = Ready(t =>
            {
                free = TestTree.MakeDefinition<GeneratorDefinition>("free_gear");
                free.availableWhen = new CurrencyAtLeast { currency = t.Cash, threshold = 0 };
                free.costCurrency = t.Cash;
                free.baseCost = 0;                              // validation refuses this; release builds still run
                free.growth = 1.15;
                free.produces.Add(TestTree.Entry(t.Cash, Stat.Rate, 1));
                t.Tier1Def.generators.Add(free);
            });

            // A repeatable free purchase is an unbounded rate printer, and a
            // malformed cost curve is content, not an answer about state.
            Assert.Throws<System.InvalidOperationException>(
                () => Purchasing.TryBuy(tree.Ctx(tree.Tier1), free, 1));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("free_gear"));
        }

        [Test]
        public void A_negative_cost_is_refused_rather_than_paid_out()
        {
            UpgradeDefinition paying = null;
            var tree = Ready(t =>
            {
                paying = TestTree.MakeDefinition<UpgradeDefinition>("paying_upgrade");
                paying.gate = new CurrencyAtLeast { currency = t.Cash, threshold = 0 };
                paying.costCurrency = t.Cash;
                paying.cost = -500;                             // validation refuses it; release builds still run
                t.Tier1Def.upgrades.Add(paying);
            });

            // Without the guard the affordability check passes and the
            // subtraction ADDS, minting 500 cash out of malformed content.
            Assert.Throws<System.InvalidOperationException>(
                () => Purchasing.TryBuy(tree.Ctx(tree.Tier1), paying));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
            Assert.IsFalse(tree.Tier1.purchasedUpgrades.Contains("paying_upgrade"));
        }

        [Test]
        public void A_bought_generator_starts_contributing_immediately()
        {
            var tree = Ready();
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp, 1));

            AssertClose(0.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "rate");
        }

        // ---- upgrades ----

        [Test]
        public void Buying_an_upgrade_spends_adds_the_latch_and_runs_the_payload()
        {
            UpgradeDefinition unlock = null;
            var tree = Ready(t =>
            {
                unlock = TestTree.MakeDefinition<UpgradeDefinition>("play_for_crowd");
                unlock.gate = new EarnedTotalAtLeast { currency = t.Cash, threshold = 100 };
                unlock.costCurrency = t.Cash;
                unlock.cost = 100;
                unlock.actions.Add(new SetFlag { flagId = "fans_revealed" });
                t.Tier1Def.upgrades.Add(unlock);
            });

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), unlock));

            AssertClose(900, tree.Tier1.balances["cash"], "balance");
            Assert.IsTrue(tree.Tier1.purchasedUpgrades.Contains("play_for_crowd"));
            Assert.IsTrue(tree.Tier1.flags.Contains("fans_revealed"));
        }

        [Test]
        public void A_zero_cost_upgrade_is_legal()
        {
            UpgradeDefinition free = null;
            var tree = Ready(t =>
            {
                free = TestTree.MakeDefinition<UpgradeDefinition>("cut_demo");
                free.gate = new CurrencyAtLeast { currency = t.Fans, threshold = 0 };
                free.costCurrency = t.Cash;
                free.cost = 0;
                free.actions.Add(new SetFlag { flagId = "album" });
                t.Tier1Def.upgrades.Add(free);
            });

            // One-shot, so a free upgrade is bounded - unlike a generator's.
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), free));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
            Assert.IsTrue(tree.Ch1.flags.Contains("album"));       // the flag is homed at ch1
            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), free));
        }

        [Test]
        public void An_upgrade_is_bought_once_until_a_reset_re_arms_it()
        {
            var tree = Ready();
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings));
            AssertClose(500, tree.Tier1.balances["cash"], "balance after the buy");

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings));
            AssertClose(500, tree.Tier1.balances["cash"], "balance after the refusal");

            tree.Tier1.Clear(tree.Now);
            tree.Tier1.balances["cash"] = 1000;
            tree.Tier1.earnedTotals["cash"] = 1000;
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings), "the reset re-armed it");
        }

        [Test]
        public void An_upgrade_effect_applies_only_while_its_latch_exists()
        {
            var tree = Ready();
            tree.Tier1.generatorCounts["practice_amp"] = 1;
            AssertClose(0.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "before");

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings));
            AssertClose(1, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "after");

            tree.Tier1.purchasedUpgrades.Clear();
            AssertClose(0.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "latch gone");
        }

        [Test]
        public void An_unmet_upgrade_gate_refuses_the_buy()
        {
            var tree = new TestTree();
            tree.Tier1.balances["cash"] = 1000;                   // affordable, but nothing was ever earned

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings));
            Assert.IsFalse(tree.Tier1.purchasedUpgrades.Contains("amp_strings"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
        }

        // ---- dispatch ----

        [Test]
        public void Both_kinds_buy_through_their_own_entry_point()
        {
            var tree = Ready();

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp, 1), "generator");
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings), "upgrade");

        }

        [Test]
        public void Buying_lands_in_the_declaring_scope()
        {
            var tree = Ready();

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp, 1));

            Assert.AreEqual(1, tree.Tier1.generatorCounts["practice_amp"]);
            Assert.IsFalse(tree.Root.generatorCounts.ContainsKey("practice_amp"));
        }

        // The declaration lookup walks OUTWARD, so a caller above the declaring
        // scope cannot buy it - the same rule every read and write obeys.
        [Test]
        public void Buying_from_above_the_declaring_scope_throws()
        {
            var tree = Ready();
            Assert.Throws<System.InvalidOperationException>(
                () => Purchasing.TryBuy(tree.Ctx(tree.Root), tree.PracticeAmp, 1));
        }

        // CanBuy answers the state question the UI needs without mutating; Buy
        // refuses to run when it says no.
        [Test]
        public void CanBuy_answers_without_buying_and_Buy_asserts_it()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(Purchasing.CanBuy(ctx, tree.PracticeAmp, 1));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"), "CanBuy mutates nothing");

            tree.Tier1.balances["cash"] = 0;
            Assert.IsFalse(Purchasing.CanBuy(ctx, tree.PracticeAmp, 1));
            Assert.Throws<System.InvalidOperationException>(() => Purchasing.Buy(ctx, tree.PracticeAmp, 1));
        }

        // ---- the count (12.2) ----

        // The series factor is computed as its own quotient, so at n = 1 it is
        // x / x for a finite nonzero x - exactly 1, and the single buy costs
        // exactly the unit cost. Equality with ==, not a tolerance.
        [Test]
        public void The_cost_of_one_is_the_unit_cost_exactly()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);

            foreach (var owned in new[] { 0, 1, 25 })
            {
                tree.Tier1.generatorCounts["practice_amp"] = owned;
                Assert.AreEqual(tree.PracticeAmp.CostAt(owned),
                    Purchasing.CostOf(tree.PracticeAmp, ctx, 1), $"owned {owned}");
            }
        }

        // The closed form is the sum of the unit costs it replaces. The relative
        // tolerance is the growth^n - 1 subtraction's digit loss, under one digit
        // of sixteen at the authored 1.15.
        [Test]
        public void The_cost_of_n_is_the_sum_of_the_n_unit_costs()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);

            foreach (var owned in new[] { 0, 25 })
            {
                tree.Tier1.generatorCounts["practice_amp"] = owned;
                foreach (var count in new[] { 2, 10, 76 })
                {
                    var expected = BigNumber.Zero;
                    for (var i = 0; i < count; i++)
                        expected += tree.PracticeAmp.CostAt(owned + i);

                    Assert.AreEqual(expected.ToDouble(),
                        Purchasing.CostOf(tree.PracticeAmp, ctx, count).ToDouble(),
                        expected.ToDouble() * 1e-12, $"owned {owned}, count {count}");
                }
            }
        }

        // Validation refuses only a nonpositive growth, so a growth of exactly 1
        // is authorable and the flat branch is required rather than defensive.
        [Test]
        public void A_growth_of_one_costs_the_base_cost_per_unit()
        {
            GeneratorDefinition flat = null;
            var tree = Ready(t =>
            {
                flat = TestTree.MakeDefinition<GeneratorDefinition>("flat_gear");
                flat.availableWhen = new CurrencyAtLeast { currency = t.Cash, threshold = 0 };
                flat.costCurrency = t.Cash;
                flat.baseCost = 7;
                flat.growth = 1;
                flat.produces.Add(TestTree.Entry(t.Cash, Stat.Rate, 1));
                t.Tier1Def.generators.Add(flat);
            });
            var ctx = tree.Ctx(tree.Tier1);

            foreach (var count in new[] { 1, 7, 1000 })
                Assert.AreEqual((BigNumber)(7 * count), Purchasing.CostOf(flat, ctx, count), $"count {count}");
        }

        // A count below one is a caller bug at every leg, which is the ruling
        // Buy already gives a false Can - not an answer the player's own state
        // could have produced.
        [Test]
        public void A_count_below_one_throws_at_every_leg()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);

            foreach (var count in new[] { 0, -1 })
            {
                Assert.Throws<System.InvalidOperationException>(
                    () => Purchasing.CostOf(tree.PracticeAmp, ctx, count), $"CostOf {count}");
                Assert.Throws<System.InvalidOperationException>(
                    () => Purchasing.CanBuy(ctx, tree.PracticeAmp, count), $"CanBuy {count}");
                Assert.Throws<System.InvalidOperationException>(
                    () => Purchasing.Buy(ctx, tree.PracticeAmp, count), $"Buy {count}");
                Assert.Throws<System.InvalidOperationException>(
                    () => Purchasing.TryBuy(ctx, tree.PracticeAmp, count), $"TryBuy {count}");
            }

            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"), "nothing was written");
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
        }

        // M is zero when the gate is closed or one unit is unaffordable, and
        // otherwise a count the search evaluated through CostOf itself - so the
        // number the row prints is one the command accepts.
        [Test]
        public void MaxAffordable_answers_a_count_it_evaluated_or_zero()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);

            // The domain owns the gate: a closed one answers zero however much
            // the balance holds.
            tree.Tier1.earnedTotals["cash"] = 0;
            Assert.AreEqual(0, Purchasing.MaxAffordable(ctx, tree.PracticeAmp), "the gate is closed");

            tree.Tier1.earnedTotals["cash"] = 1000;
            tree.Tier1.balances["cash"] = 59;
            Assert.AreEqual(0, Purchasing.MaxAffordable(ctx, tree.PracticeAmp), "one unit short of the first unit");

            var eight = Purchasing.CostOf(tree.PracticeAmp, ctx, 8);
            tree.Tier1.balances["cash"] = eight;
            Assert.AreEqual(8, Purchasing.MaxAffordable(ctx, tree.PracticeAmp), "exactly the price of eight");
            Assert.IsTrue(Purchasing.CanBuy(ctx, tree.PracticeAmp, 8), "the answer is buyable");
            Assert.IsFalse(Purchasing.CanBuy(ctx, tree.PracticeAmp, 9), "and one more is not");

            tree.Tier1.balances["cash"] = eight - 0.01;
            Assert.AreEqual(7, Purchasing.MaxAffordable(ctx, tree.PracticeAmp), "a cent short of eight");
            Assert.IsTrue(Purchasing.CanBuy(ctx, tree.PracticeAmp, 7), "the answer is buyable");
            Assert.IsFalse(Purchasing.CanBuy(ctx, tree.PracticeAmp, 8), "and one more is not");
        }

        // The search is a doubling bracket and a bisect over the one cost
        // function, so a count no loop of unit buys could reach is still exact.
        [Test]
        public void MaxAffordable_reaches_a_count_a_loop_never_would()
        {
            GeneratorDefinition penny = null;
            var tree = Ready(t =>
            {
                penny = TestTree.MakeDefinition<GeneratorDefinition>("penny_gear");
                penny.availableWhen = new CurrencyAtLeast { currency = t.Cash, threshold = 0 };
                penny.costCurrency = t.Cash;
                penny.baseCost = 1;
                penny.growth = 1;
                penny.produces.Add(TestTree.Entry(t.Cash, Stat.Rate, 1));
                t.Tier1Def.generators.Add(penny);
            });
            tree.Tier1.balances["cash"] = 1e9;
            tree.Tier1.earnedTotals["cash"] = 1e9;

            Assert.AreEqual(1000000000, Purchasing.MaxAffordable(tree.Ctx(tree.Tier1), penny));
        }

        // A buy of n is one Spend of the series sum and one write of owned + n,
        // never a loop of unit buys.
        [Test]
        public void A_bulk_buy_is_one_spend_and_one_count_write()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.generatorCounts["practice_amp"] = 2;
            var cost = Purchasing.CostOf(tree.PracticeAmp, ctx, 5);

            Purchasing.Buy(ctx, tree.PracticeAmp, 5);

            Assert.AreEqual(7, tree.Tier1.generatorCounts["practice_amp"]);
            Assert.AreEqual((BigNumber)1000 - cost, tree.Tier1.balances["cash"], "the series sum, spent once");
            Assert.AreEqual(1, tree.Tier1.generatorCounts.Count, "one generator, one entry");
        }

        // Fail-closed against the affordability of the WHOLE count: a buy the
        // balance cannot cover is refused entire rather than trimmed.
        [Test]
        public void A_bulk_buy_the_balance_cannot_cover_refuses_whole()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);
            var oneShort = Purchasing.CostOf(tree.PracticeAmp, ctx, 3) - 0.01;
            tree.Tier1.balances["cash"] = oneShort;

            Assert.IsFalse(Purchasing.TryBuy(ctx, tree.PracticeAmp, 3));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"), "no count was written");
            Assert.AreEqual(oneShort, tree.Tier1.balances["cash"], "and nothing was spent");
        }

        // The price reads the PURCHASED count alone (12.2): a granted copy is a
        // gift, and letting it raise the price would charge for the gift.
        [Test]
        public void A_granted_count_never_raises_the_price()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.generatorCounts["practice_amp"] = 2;
            var unit = Purchasing.CostOf(tree.PracticeAmp, ctx, 1);
            var max = Purchasing.MaxAffordable(ctx, tree.PracticeAmp);

            tree.Tier1.grantedCounts["practice_amp"] = 40;

            Assert.AreEqual(tree.PracticeAmp.CostAt(2), unit, "the curve at two owned");
            Assert.AreEqual(unit, Purchasing.CostOf(tree.PracticeAmp, ctx, 1), "unmoved by forty granted");
            Assert.AreEqual(max, Purchasing.MaxAffordable(ctx, tree.PracticeAmp), "and so is the search");
        }

        // ---- cost as a stat (12.2) ----

        // A permanent cost factor on the gear tag, declared BEFORE the build the
        // gather compiles over.
        private static void Sale(TestTree tree, double multiplier)
        {
            var sale = TestTree.MakeDefinition<ModifierDefinition>("gear_sale");
            sale.effects.Add(new Effect { target = "gear", stat = Stat.Cost, multiplier = multiplier });
            tree.Tier1Def.modifiers.Add(sale);
            tree.Tier1Def.permanentModifiers.Add(sale);
        }

        // A cost effect is stage 1 on the generator itself, so a tag selector
        // reaches every generator carrying that tag and nothing else.
        [Test]
        public void A_cost_factor_on_a_tag_reaches_every_generator_carrying_it()
        {
            GeneratorDefinition untagged = null;
            var tree = Ready(t =>
            {
                untagged = TestTree.MakeDefinition<GeneratorDefinition>("kazoo");
                untagged.availableWhen = new CurrencyAtLeast { currency = t.Cash, threshold = 0 };
                untagged.costCurrency = t.Cash;
                untagged.baseCost = 40;
                untagged.growth = 1.15;
                untagged.produces.Add(TestTree.Entry(t.Cash, Stat.Rate, 1));
                t.Tier1Def.generators.Add(untagged);
                Sale(t, 0.5);
            });
            var ctx = tree.Ctx(tree.Tier1);

            AssertClose(30, Purchasing.CostOf(tree.PracticeAmp, ctx, 1), "the amp carries gear");
            AssertClose(125, Purchasing.CostOf(tree.Drummer, ctx, 1), "and so does the drummer");
            AssertClose(40, Purchasing.CostOf(untagged, ctx, 1), "the kazoo carries no tag the sale names");
        }

        // The series factor is its own quotient, so one unit under a cost factor
        // is the unit cost times that factor and nothing else - the same
        // identity the factorless case has, bit for bit.
        [Test]
        public void The_cost_of_one_under_a_factor_is_the_unit_cost_times_the_factor()
        {
            var tree = Ready(t => Sale(t, 0.5));
            var ctx = tree.Ctx(tree.Tier1);

            foreach (var owned in new[] { 0, 1, 25 })
            {
                tree.Tier1.generatorCounts["practice_amp"] = owned;
                Assert.AreEqual(tree.PracticeAmp.CostAt(owned) * (BigNumber)0.5,
                    Purchasing.CostOf(tree.PracticeAmp, ctx, 1), $"owned {owned}");
            }
        }

        // The search evaluates every probe through CostOf, so the factor rides
        // in and the answer is still a count the command accepts.
        [Test]
        public void MaxAffordable_under_a_cost_factor_is_the_largest_count_CostOf_affords()
        {
            var tree = Ready(t => Sale(t, 0.5));
            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.balances["cash"] = Purchasing.CostOf(tree.PracticeAmp, ctx, 12);

            Assert.AreEqual(12, Purchasing.MaxAffordable(ctx, tree.PracticeAmp), "exactly the price of twelve");
            Assert.IsTrue(Purchasing.CanBuy(ctx, tree.PracticeAmp, 12), "the answer is buyable");
            Assert.IsFalse(Purchasing.CanBuy(ctx, tree.PracticeAmp, 13), "and one more is not");
        }

        // An upgrade's price takes a factor the same way, and the buy spends the
        // number CostOf answers - the row prints that same call (12.11).
        [Test]
        public void An_upgrade_cost_takes_its_factor_and_the_buy_spends_it()
        {
            var tree = Ready(t =>
            {
                var deal = TestTree.MakeDefinition<ModifierDefinition>("strings_deal");
                deal.effects.Add(new Effect { target = "amp_strings", stat = Stat.Cost, multiplier = 0.2 });
                t.Tier1Def.modifiers.Add(deal);
                t.Tier1Def.permanentModifiers.Add(deal);
            });
            var ctx = tree.Ctx(tree.Tier1);

            AssertClose(100, Purchasing.CostOf(tree.AmpStrings, ctx), "500 at a fifth");
            Assert.IsTrue(Purchasing.TryBuy(ctx, tree.AmpStrings));
            AssertClose(900, tree.Tier1.balances["cash"], "the spend is that same number");
        }

        // A handicap is an ordinary carrier on the cost coordinate (12.6): it
        // rides the record EXISTING, so the price lifts when the record goes.
        [Test]
        public void An_event_handicap_on_cost_lifts_when_the_record_goes()
        {
            var tree = Ready(t =>
                t.TimedGig.handicaps.Add(new Effect { target = "practice_amp", stat = Stat.Cost, multiplier = 100 }));
            var ctx = tree.Ctx(tree.Tier1);

            AssertClose(60, Purchasing.CostOf(tree.PracticeAmp, ctx, 1), "no record");

            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 100 };
            AssertClose(6000, Purchasing.CostOf(tree.PracticeAmp, ctx, 1), "the gig's own handicap");

            tree.Tier1.activeEvent = null;
            AssertClose(60, Purchasing.CostOf(tree.PracticeAmp, ctx, 1), "and the record is gone");
        }

        // Cost has one coordinate per price, so the wildcard reaches every
        // price, a generator's and an upgrade's alike (12.2): "everything half
        // off" is one line with no target.
        [Test]
        public void A_wildcard_cost_effect_reaches_every_price()
        {
            var tree = Ready(t =>
            {
                var sale = TestTree.MakeDefinition<ModifierDefinition>("everything_half_off");
                sale.effects.Add(new Effect { stat = Stat.Cost, multiplier = 0.5 });
                t.RootDef.modifiers.Add(sale);
                t.RootDef.permanentModifiers.Add(sale);
            });
            var ctx = tree.Ctx(tree.Tier1);

            AssertClose(30, Purchasing.CostOf(tree.PracticeAmp, ctx, 1), "the amp");
            AssertClose(125, Purchasing.CostOf(tree.Drummer, ctx, 1), "the drummer");
            AssertClose(250, Purchasing.CostOf(tree.AmpStrings, ctx), "and the upgrade");
        }

        // Cost is stage 1 only: the coordinate's currency is the price's
        // currency, and no currency is ever asked for a cost - so an effect
        // naming cash reaches no generator's price (12.2).
        [Test]
        public void A_cost_effect_naming_the_cost_currency_reaches_nothing()
        {
            var tree = Ready(t =>
            {
                var cashOff = TestTree.MakeDefinition<ModifierDefinition>("cash_off");
                cashOff.effects.Add(new Effect { target = "cash", stat = Stat.Cost, multiplier = 0.5 });
                t.Tier1Def.modifiers.Add(cashOff);
                t.Tier1Def.permanentModifiers.Add(cashOff);
            });

            AssertClose(60, Purchasing.CostOf(tree.PracticeAmp, tree.Ctx(tree.Tier1), 1));
        }
    }
}
