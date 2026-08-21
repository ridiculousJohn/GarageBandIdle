using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Firing pays the yield stat and nothing else, resolved atomically from
    // pre-fire state (design doc 12.2).
    public class FireProducerTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        [Test]
        public void Firing_pays_the_yield_entries_whose_conditions_hold()
        {
            var tree = new TestTree();
            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);

            // One unconditioned cash entry; the stage_presence entry and both
            // rehearsal entries are gated shut.
            AssertClose(1, tree.Tier1.balances["cash"], "cash");
            AssertClose(0, tree.Tier1.balances["rehearsal"], "rehearsal");

            tree.Tier1.flags.Add("rehearsal_revealed");
            tree.Tier1.purchasedUpgrades.Add("stage_presence");
            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);

            AssertClose(1 + 2, tree.Tier1.balances["cash"], "cash after the latch");
            AssertClose(1, tree.Tier1.balances["rehearsal"], "rehearsal after the reveal");
        }

        [Test]
        public void A_yield_never_pays_the_rate_entry_of_the_same_currency()
        {
            var tree = new TestTree();
            tree.Tier1.flags.Add("rehearsal_revealed");
            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);

            // The Jam declares rehearsal at yield 1 AND rate 0.5; a firing pays
            // the first and never the second.
            AssertClose(1, tree.Tier1.balances["rehearsal"], "rehearsal");
        }

        [Test]
        public void A_firing_deposits_to_the_earned_total_as_well()
        {
            var tree = new TestTree();
            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);

            AssertClose(1, tree.Tier1.earnedTotals["cash"], "earned total");
        }

        [Test]
        public void Multipliers_apply_to_a_yield_from_both_stages()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 20;                    // records_income: x1.4 on the income tag
            tree.Ch1.modifierStacks["gj_tap_1"] = 1;

            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);

            // 1 x 1.25 (the chapter's Garage Jam stack, stage 1)
            //   x 1.4 (records, stage 2 through cash's income tag)
            AssertClose(1 * 1.25 * 1.4, tree.Tier1.balances["cash"], "cash");
        }

        [Test]
        public void Every_output_is_judged_against_pre_fire_state()
        {
            // A producer whose rehearsal entry is conditioned on the cash total
            // the SAME firing crosses.
            var tree = new TestTree();
            var jam = TestTree.MakeDefinition<ProducerDefinition>("crossing_jam");
            jam.produces.Add(TestTree.Entry(tree.Cash, Stat.Yield, 100));
            jam.produces.Add(TestTree.Entry(tree.Rehearsal, Stat.Yield, 5,
                new EarnedTotalAtLeast { currency = tree.Cash, threshold = 100 }));
            tree.Tier1Def.producers.Add(jam);

            Producer.FireProducer(tree.Ctx(tree.Tier1), jam);

            // The cash deposit crosses 100, and the sibling output must not see
            // it: no output can flip another output's condition mid-fire.
            AssertClose(100, tree.Tier1.balances["cash"], "cash");
            AssertClose(0, tree.Tier1.balances["rehearsal"], "rehearsal on the crossing fire");

            Producer.FireProducer(tree.Ctx(tree.Tier1), jam);
            AssertClose(5, tree.Tier1.balances["rehearsal"], "rehearsal on the next fire");
        }

        [Test]
        public void Firing_resolves_in_the_declaring_scope()
        {
            var tree = new TestTree();

            // The deposit and the conditions belong to tier1, where the producer
            // is declared, whatever the caller's own scope holds.
            tree.Tier1.purchasedUpgrades.Add("stage_presence");
            Producer.FireProducer(tree.Ctx(tree.Tier1), tree.TapProducer);

            AssertClose(2, tree.Tier1.balances["cash"], "cash");
        }

        [Test]
        public void An_undeclared_producer_throws()
        {
            var tree = new TestTree();
            var orphan = TestTree.MakeDefinition<ProducerDefinition>("orphan_producer");
            orphan.produces.Add(TestTree.Entry(tree.Cash, Stat.Yield, 5));   // authored, but no scope declares it

            Assert.Throws<System.InvalidOperationException>(
                () => Producer.FireProducer(tree.Ctx(tree.Tier1), orphan));
        }
    }
}
