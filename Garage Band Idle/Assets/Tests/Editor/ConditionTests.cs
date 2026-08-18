using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class ConditionTests
    {
        [Test]
        public void CurrencyAtLeast_compares_the_balance()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 50;
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new CurrencyAtLeast { currencyId = "fans", threshold = 50 }.Evaluate(ctx));
            Assert.IsFalse(new CurrencyAtLeast { currencyId = "fans", threshold = 51 }.Evaluate(ctx));
        }

        [Test]
        public void EarnedTotalAtLeast_holds_after_spending()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit("cash", 300);
            tree.Tier1.balances["cash"] = 10;   // spent down

            Assert.IsTrue(new EarnedTotalAtLeast { currencyId = "cash", threshold = 250 }.Evaluate(ctx));
            Assert.IsFalse(new CurrencyAtLeast { currencyId = "cash", threshold = 250 }.Evaluate(ctx));
        }

        [Test]
        public void OwnedCountAtLeast_reads_the_generator_count()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 3;
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new OwnedCountAtLeast { generatorId = "practice_amp", count = 3 }.Evaluate(ctx));
            Assert.IsFalse(new OwnedCountAtLeast { generatorId = "practice_amp", count = 4 }.Evaluate(ctx));
        }

        [Test]
        public void FlagSet_and_UpgradePurchased_read_the_chain()
        {
            var tree = new TestTree();
            tree.Ch1.flags.Add("album");
            tree.Ch1.purchasedUpgrades.Add("cut_demo");
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new FlagSet { flagId = "album" }.Evaluate(ctx));
            Assert.IsTrue(new UpgradePurchased { upgradeId = "cut_demo" }.Evaluate(ctx));
            Assert.IsFalse(new FlagSet { flagId = "fans_revealed" }.Evaluate(ctx));
        }

        [Test]
        public void BarsCompleted_derives_completion_from_progress_against_fillAmount()
        {
            var tree = new TestTree();
            var group = TestTree.MakeDefinition<BarGroupDefinition>("learn_covers");
            var cover1 = TestTree.MakeDefinition<BarDefinition>("cover_1");
            cover1.groupId = "learn_covers";
            cover1.fillAmount = 100;
            var cover2 = TestTree.MakeDefinition<BarDefinition>("cover_2");
            cover2.groupId = "learn_covers";
            cover2.fillAmount = 300;
            tree.Defs.Add(group).Add(cover1).Add(cover2);

            tree.Tier1.barProgress["cover_1"] = 100;   // exactly full
            tree.Tier1.barProgress["cover_2"] = 299;   // just short
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new BarsCompleted { groupId = "learn_covers", count = 1 }.Evaluate(ctx));
            Assert.IsFalse(new BarsCompleted { groupId = "learn_covers", count = 2 }.Evaluate(ctx));
        }

        [Test]
        public void Compound_kinds_have_fail_closed_empty_semantics()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new All().Evaluate(ctx));            // vacuous truth: no legs, no objection
            Assert.IsFalse(new Any().Evaluate(ctx));           // nothing can satisfy it
            Assert.IsFalse(new Not().Evaluate(ctx));           // unauthored inner condition stays closed
        }

        [Test]
        public void Not_inverts_and_the_story_gate_compound_works()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Ch1);
            var storyGate = new All
            {
                conditions =
                {
                    new FlagSet { flagId = "ch1_complete" },
                    new Not { condition = new FlagSet { flagId = "story_ch1_end_seen" } }
                }
            };

            Assert.IsFalse(storyGate.Evaluate(ctx));           // not complete yet

            tree.Root.flags.Add("ch1_complete");
            Assert.IsTrue(storyGate.Evaluate(ctx));            // complete, beat unseen

            tree.Root.flags.Add("story_ch1_end_seen");
            Assert.IsFalse(storyGate.Evaluate(ctx));           // acknowledged
        }
    }
}
