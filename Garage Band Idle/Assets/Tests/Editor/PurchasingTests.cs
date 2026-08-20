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

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "practice_amp"));

            // The third amp costs 60 x 1.15^2.
            AssertClose(1000 - 60 * 1.15 * 1.15, tree.Tier1.balances["cash"], "balance");
            Assert.AreEqual(3, tree.Tier1.generatorCounts["practice_amp"]);
        }

        [Test]
        public void Spending_never_touches_the_earned_total()
        {
            var tree = Ready();
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "practice_amp"));

            // Section 2's strobe-proofing: a threshold met once stays met.
            AssertClose(1000, tree.Tier1.earnedTotals["cash"], "earned total");
            AssertClose(940, tree.Tier1.balances["cash"], "balance");
        }

        [Test]
        public void An_unmet_gate_refuses_the_buy()
        {
            var tree = Ready();

            // The drummer needs three amps.
            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "drummer"));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("drummer"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");

            tree.Tier1.generatorCounts["practice_amp"] = 3;
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "drummer"));
            Assert.AreEqual(1, tree.Tier1.generatorCounts["drummer"]);
        }

        [Test]
        public void An_unauthored_gate_is_closed_not_open()
        {
            var tree = Ready();
            var gateless = TestTree.MakeDefinition<GeneratorDefinition>("gateless_gear");
            gateless.costCurrencyId = "cash";
            gateless.baseCost = 10;
            gateless.growth = 1.15;
            gateless.produces.Add(TestTree.Entry("cash", Stat.Rate, 1));
            tree.Tier1Def.generators.Add(gateless);
            tree.Defs.Add(gateless);

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "gateless_gear"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
        }

        [Test]
        public void An_unaffordable_buy_refuses_and_leaves_the_balance_alone()
        {
            var tree = Ready();
            tree.Tier1.balances["cash"] = 59.99;

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "practice_amp"));
            AssertClose(59.99, tree.Tier1.balances["cash"], "balance");
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"));
        }

        [Test]
        public void A_computed_cost_of_zero_is_refused_at_runtime()
        {
            var tree = Ready();
            var free = TestTree.MakeDefinition<GeneratorDefinition>("free_gear");
            free.availableWhen = new CurrencyAtLeast { currencyId = "cash", threshold = 0 };
            free.costCurrencyId = "cash";
            free.baseCost = 0;                                  // validation refuses this; release builds still run
            free.growth = 1.15;
            free.produces.Add(TestTree.Entry("cash", Stat.Rate, 1));
            tree.Tier1Def.generators.Add(free);
            tree.Defs.Add(free);

            // A repeatable free purchase is an unbounded rate printer, and a
            // malformed cost curve is content, not an answer about state.
            Assert.Throws<System.InvalidOperationException>(
                () => Purchasing.TryBuy(tree.Ctx(tree.Tier1), "free_gear"));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("free_gear"));
        }

        [Test]
        public void A_negative_cost_is_refused_rather_than_paid_out()
        {
            var tree = Ready();
            var paying = TestTree.MakeDefinition<UpgradeDefinition>("paying_upgrade");
            paying.gate = new CurrencyAtLeast { currencyId = "cash", threshold = 0 };
            paying.costCurrencyId = "cash";
            paying.cost = -500;                                 // validation refuses it; release builds still run
            tree.Tier1Def.upgrades.Add(paying);
            tree.Defs.Add(paying);

            // Without the guard the affordability check passes and the
            // subtraction ADDS, minting 500 cash out of malformed content.
            Assert.Throws<System.InvalidOperationException>(
                () => Purchasing.TryBuy(tree.Ctx(tree.Tier1), "paying_upgrade"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
            Assert.IsFalse(tree.Tier1.purchasedUpgrades.Contains("paying_upgrade"));
        }

        [Test]
        public void A_bought_generator_starts_contributing_immediately()
        {
            var tree = Ready();
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "practice_amp"));

            AssertClose(0.5, Producer.GetRate(tree.Tier1, tree.Defs, tree.Now, "cash"), "rate");
        }

        // ---- upgrades ----

        [Test]
        public void Buying_an_upgrade_spends_adds_the_latch_and_runs_the_payload()
        {
            var tree = Ready();
            var unlock = TestTree.MakeDefinition<UpgradeDefinition>("play_for_crowd");
            unlock.gate = new EarnedTotalAtLeast { currencyId = "cash", threshold = 100 };
            unlock.costCurrencyId = "cash";
            unlock.cost = 100;
            unlock.actions.Add(new SetFlag { flagId = "fans_revealed" });
            tree.Tier1Def.upgrades.Add(unlock);
            tree.Defs.Add(unlock);

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "play_for_crowd"));

            AssertClose(900, tree.Tier1.balances["cash"], "balance");
            Assert.IsTrue(tree.Tier1.purchasedUpgrades.Contains("play_for_crowd"));
            Assert.IsTrue(tree.Tier1.flags.Contains("fans_revealed"));
        }

        [Test]
        public void A_zero_cost_upgrade_is_legal()
        {
            var tree = Ready();
            var free = TestTree.MakeDefinition<UpgradeDefinition>("cut_demo");
            free.gate = new CurrencyAtLeast { currencyId = "fans", threshold = 0 };
            free.costCurrencyId = "cash";
            free.cost = 0;
            free.actions.Add(new SetFlag { flagId = "album" });
            tree.Tier1Def.upgrades.Add(free);
            tree.Defs.Add(free);

            // One-shot, so a free upgrade is bounded - unlike a generator's.
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "cut_demo"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
            Assert.IsTrue(tree.Ch1.flags.Contains("album"));       // the flag is homed at ch1
            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "cut_demo"));
        }

        [Test]
        public void An_upgrade_is_bought_once_until_a_reset_re_arms_it()
        {
            var tree = Ready();
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "amp_strings"));
            AssertClose(500, tree.Tier1.balances["cash"], "balance after the buy");

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "amp_strings"));
            AssertClose(500, tree.Tier1.balances["cash"], "balance after the refusal");

            tree.Tier1.Clear(tree.Now);
            tree.Tier1.balances["cash"] = 1000;
            tree.Tier1.earnedTotals["cash"] = 1000;
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "amp_strings"), "the reset re-armed it");
        }

        [Test]
        public void An_upgrade_effect_applies_only_while_its_latch_exists()
        {
            var tree = Ready();
            tree.Tier1.generatorCounts["practice_amp"] = 1;
            AssertClose(0.5, Producer.GetRate(tree.Tier1, tree.Defs, tree.Now, "cash"), "before");

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "amp_strings"));
            AssertClose(1, Producer.GetRate(tree.Tier1, tree.Defs, tree.Now, "cash"), "after");

            tree.Tier1.purchasedUpgrades.Clear();
            AssertClose(0.5, Producer.GetRate(tree.Tier1, tree.Defs, tree.Now, "cash"), "latch gone");
        }

        [Test]
        public void An_unmet_upgrade_gate_refuses_the_buy()
        {
            var tree = new TestTree();
            tree.Tier1.balances["cash"] = 1000;                   // affordable, but nothing was ever earned

            Assert.IsFalse(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "amp_strings"));
            Assert.IsFalse(tree.Tier1.purchasedUpgrades.Contains("amp_strings"));
            AssertClose(1000, tree.Tier1.balances["cash"], "balance");
        }

        // ---- dispatch ----

        [Test]
        public void One_entry_point_dispatches_by_id_and_refuses_an_unknown_one()
        {
            var tree = Ready();

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "practice_amp"), "generator");
            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "amp_strings"), "upgrade");

            Assert.Throws<System.InvalidOperationException>(
                () => Purchasing.TryBuy(tree.Ctx(tree.Tier1), "cash"), "a currency is not purchasable");
        }

        [Test]
        public void Buying_lands_in_the_declaring_scope()
        {
            var tree = Ready();

            Assert.IsTrue(Purchasing.TryBuy(tree.Ctx(tree.Tier1), "practice_amp"));

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
                () => Purchasing.TryBuy(tree.Ctx(tree.Root), "practice_amp"));
        }

        // CanBuy answers the state question the UI needs without mutating; Buy
        // refuses to run when it says no.
        [Test]
        public void CanBuy_answers_without_buying_and_Buy_asserts_it()
        {
            var tree = Ready();
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(Purchasing.CanBuy(ctx, "practice_amp"));
            Assert.IsFalse(tree.Tier1.generatorCounts.ContainsKey("practice_amp"), "CanBuy mutates nothing");

            tree.Tier1.balances["cash"] = 0;
            Assert.IsFalse(Purchasing.CanBuy(ctx, "practice_amp"));
            Assert.Throws<System.InvalidOperationException>(() => Purchasing.Buy(ctx, "practice_amp"));
        }
    }
}
