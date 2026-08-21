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
        private static TestTree Ready()
        {
            var tree = new TestTree();
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

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp));

            // The third amp costs 60 x 1.15^2.
            AssertClose(1000 - 60 * 1.15 * 1.15, tree.Tier1.balances["cash"], "balance");
            Assert.AreEqual(3, tree.Tier1.generatorCounts["practice_amp"]);
        }

        [Test]
        public void Spending_never_touches_the_earned_total()
        {
            var tree = Ready();
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp));

            // Section 2's strobe-proofing: a threshold met once stays met.
            AssertClose(1000, tree.Tier1.earnedTotals["cash"], "earned total");
            AssertClose(940, tree.Tier1.balances["cash"], "balance");
        }

        [Test]
        public void An_unmet_gate_refuses_the_buy()
        {
            var tree = Ready();

            // The drummer needs three amps.
            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.Drummer));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("drummer"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");

            tree.Tier1.generatorCounts["practice_amp"] = 3;
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.Drummer));
            Assert.AreEqual(1, tree.Tier1.generatorCounts["drummer"]);
        }

        [Test]
        public void An_unauthored_gate_is_closed_not_open()
        {
            var tree = Ready();
            var gateless = TestTree.MakeDefinition<GeneratorDefinition>("gateless_gear");
            gateless.costCurrency = tree.Cash;
            gateless.baseCost = 10;
            gateless.growth = 1.15;
            gateless.produces.Add(TestTree.Entry(tree.Cash, Stat.Rate, 1));
            tree.Tier1Def.generators.Add(gateless);

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), gateless));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
        }

        [Test]
        public void An_unaffordable_buy_refuses_and_leaves_the_balance_alone()
        {
            var tree = Ready();
            tree.Tier1.balances["cash"] = 59.99;

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp));
            AssertClose(59.99, tree.Tier1.balances["cash"], "balance");
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"));
        }

        [Test]
        public void A_computed_cost_of_zero_is_refused_at_runtime()
        {
            var tree = Ready();
            var free = TestTree.MakeDefinition<GeneratorDefinition>("free_gear");
            free.availableWhen = new CurrencyAtLeast { currency = tree.Cash, threshold = 0 };
            free.costCurrency = tree.Cash;
            free.baseCost = 0;                                  // validation refuses this; release builds still run
            free.growth = 1.15;
            free.produces.Add(TestTree.Entry(tree.Cash, Stat.Rate, 1));
            tree.Tier1Def.generators.Add(free);

            // A repeatable free purchase is an unbounded rate printer, and a
            // malformed cost curve is content, not an answer about state.
            Assert.Throws<System.InvalidOperationException>(
                () => Purchasing.TryBuy(tree.Ctx(tree.Tier1), free));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("free_gear"));
        }

        [Test]
        public void A_negative_cost_is_refused_rather_than_paid_out()
        {
            var tree = Ready();
            var paying = TestTree.MakeDefinition<UpgradeDefinition>("paying_upgrade");
            paying.gate = new CurrencyAtLeast { currency = tree.Cash, threshold = 0 };
            paying.costCurrency = tree.Cash;
            paying.cost = -500;                                 // validation refuses it; release builds still run
            tree.Tier1Def.upgrades.Add(paying);

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
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp));

            AssertClose(0.5, Producer.GetRate(tree.Tier1, tree.Now, tree.Cash), "rate");
        }

        // ---- upgrades ----

        [Test]
        public void Buying_an_upgrade_spends_adds_the_latch_and_runs_the_payload()
        {
            var tree = Ready();
            var unlock = TestTree.MakeDefinition<UpgradeDefinition>("play_for_crowd");
            unlock.gate = new EarnedTotalAtLeast { currency = tree.Cash, threshold = 100 };
            unlock.costCurrency = tree.Cash;
            unlock.cost = 100;
            unlock.actions.Add(new SetFlag { flagId = "fans_revealed" });
            tree.Tier1Def.upgrades.Add(unlock);

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), unlock));

            AssertClose(900, tree.Tier1.balances["cash"], "balance");
            Assert.IsTrue(tree.Tier1.purchasedUpgrades.Contains("play_for_crowd"));
            Assert.IsTrue(tree.Tier1.flags.Contains("fans_revealed"));
        }

        [Test]
        public void A_zero_cost_upgrade_is_legal()
        {
            var tree = Ready();
            var free = TestTree.MakeDefinition<UpgradeDefinition>("cut_demo");
            free.gate = new CurrencyAtLeast { currency = tree.Fans, threshold = 0 };
            free.costCurrency = tree.Cash;
            free.cost = 0;
            free.actions.Add(new SetFlag { flagId = "album" });
            tree.Tier1Def.upgrades.Add(free);

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
            AssertClose(0.5, Producer.GetRate(tree.Tier1, tree.Now, tree.Cash), "before");

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings));
            AssertClose(1, Producer.GetRate(tree.Tier1, tree.Now, tree.Cash), "after");

            tree.Tier1.purchasedUpgrades.Clear();
            AssertClose(0.5, Producer.GetRate(tree.Tier1, tree.Now, tree.Cash), "latch gone");
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

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp), "generator");
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.AmpStrings), "upgrade");

        }

        [Test]
        public void Buying_lands_in_the_declaring_scope()
        {
            var tree = Ready();

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), tree.PracticeAmp));

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
                () => Purchasing.TryBuy(tree.Ctx(tree.Root), tree.PracticeAmp));
        }

        // CanBuy answers the state question the UI needs without mutating; Buy
        // refuses to run when it says no.
        [Test]
        public void CanBuy_answers_without_buying_and_Buy_asserts_it()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(Purchasing.CanBuy(ctx, tree.PracticeAmp));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"), "CanBuy mutates nothing");

            tree.Tier1.balances["cash"] = 0;
            Assert.IsFalse(Purchasing.CanBuy(ctx, tree.PracticeAmp));
            Assert.Throws<System.InvalidOperationException>(() => Purchasing.Buy(ctx, tree.PracticeAmp));
        }
    }
}
