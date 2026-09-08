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

            Assert.IsTrue(new CurrencyAtLeast { currency = tree.Fans, threshold = 50 }.Evaluate(ctx));
            Assert.IsFalse(new CurrencyAtLeast { currency = tree.Fans, threshold = 51 }.Evaluate(ctx));
        }

        [Test]
        public void EarnedTotalAtLeast_holds_after_spending()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit("cash", 300);
            tree.Tier1.balances["cash"] = 10;   // spent down

            Assert.IsTrue(new EarnedTotalAtLeast { currency = tree.Cash, threshold = 250 }.Evaluate(ctx));
            Assert.IsFalse(new CurrencyAtLeast { currency = tree.Cash, threshold = 250 }.Evaluate(ctx));
        }

        [Test]
        public void OwnedCountAtLeast_reads_the_generator_count()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 3;
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new OwnedCountAtLeast { generator = tree.PracticeAmp, count = 3 }.Evaluate(ctx));
            Assert.IsFalse(new OwnedCountAtLeast { generator = tree.PracticeAmp, count = 4 }.Evaluate(ctx));
        }

        [Test]
        public void FlagSet_and_UpgradePurchased_read_the_chain()
        {
            var tree = new TestTree();
            tree.Ch1.flags.Add("album");
            var cutDemo = TestTree.MakeDefinition<UpgradeDefinition>("cut_demo");
            tree.Ch1Def.upgrades.Add(cutDemo);
            tree.Ch1.purchasedUpgrades.Add("cut_demo");
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new FlagSet { flagId = "album" }.Evaluate(ctx));
            Assert.IsTrue(new UpgradePurchased { upgrade = cutDemo }.Evaluate(ctx));
            Assert.IsFalse(new FlagSet { flagId = "fans_revealed" }.Evaluate(ctx));
        }

        [Test]
        public void BarsCompleted_derives_completion_from_progress_against_fillAmount()
        {
            // The fixture's own cover group, not a second one with the same ids:
            // declaring a rival 'learn_covers' at tier1 would break chain
            // uniqueness, so the test would assert against content that cannot
            // load. cover_1 fills at 100 and cover_2 at 300.
            var tree = new TestTree();
            tree.Tier1.barProgress[tree.Cover1.Id] = 100;   // exactly full
            tree.Tier1.barProgress[tree.Cover2.Id] = 299;   // just short
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(new BarsCompleted { group = tree.LearnCovers, count = 1 }.Evaluate(ctx));
            Assert.IsFalse(new BarsCompleted { group = tree.LearnCovers, count = 2 }.Evaluate(ctx));
        }

        // Both kinds are pure fact reads with NO operand: outward from the
        // acting scope to the first interior scope holding a record, the way
        // FlagSet reads a flag (12.4). Nothing looks down.
        [Test]
        public void The_event_kinds_read_the_first_record_outward()
        {
            var tree = new TestTree();
            var exists = new EventRecordExists();
            var pending = new EventRewardPending();
            var atTier1 = tree.Ctx(tree.Tier1);

            Assert.IsFalse(exists.Evaluate(atTier1));
            Assert.IsFalse(pending.Evaluate(atTier1));

            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", remainingSeconds = 0 };
            Assert.IsTrue(exists.Evaluate(atTier1));       // expired still exists
            Assert.IsFalse(pending.Evaluate(atTier1));     // armed only by the latch

            tree.Tier1.activeEvent.goalReached = true;
            Assert.IsTrue(pending.Evaluate(atTier1));
        }

        // Root's chain is root alone, and root holds no record field at all -
        // so neither kind can ever hold there, whatever any chapter is hosting.
        [Test]
        public void The_event_kinds_are_false_on_roots_chain()
        {
            var tree = new TestTree();
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", goalReached = true };
            var atRoot = tree.Ctx(tree.Root);

            Assert.IsFalse(new EventRecordExists().Evaluate(atRoot));
            Assert.IsFalse(new EventRewardPending().Evaluate(atRoot));
        }

        // A scope's record is visible to that scope and to the scopes inside
        // it, never to its parent: a parent knows nothing of what its children
        // host (12.4).
        [Test]
        public void A_tiers_record_is_visible_to_the_tier_and_not_to_its_chapter()
        {
            var tree = new TestTree();
            var exists = new EventRecordExists();
            var pending = new EventRewardPending();

            tree.Tier1.activeEvent = new ActiveEvent { eventId = "timed_gig", goalReached = true };

            Assert.IsTrue(exists.Evaluate(tree.Ctx(tree.Tier1)));
            Assert.IsTrue(pending.Evaluate(tree.Ctx(tree.Tier1)));
            Assert.IsFalse(exists.Evaluate(tree.Ctx(tree.Ch1)), "the chapter cannot read down");
            Assert.IsFalse(pending.Evaluate(tree.Ctx(tree.Ch1)));

            // The chapter's OWN record is what the chapter reads, and the tier
            // still stops at its own - the first one outward wins.
            tree.Ch1.activeEvent = new ActiveEvent { eventId = "showcase" };
            Assert.IsTrue(exists.Evaluate(tree.Ctx(tree.Ch1)));
            Assert.IsFalse(pending.Evaluate(tree.Ctx(tree.Ch1)), "the chapter's own record is not armed");
            Assert.IsTrue(pending.Evaluate(tree.Ctx(tree.Tier1)), "and the tier never saw it");
        }

        [Test]
        public void Always_holds()
        {
            var tree = new TestTree();
            Assert.IsTrue(new Always().Evaluate(tree.Ctx(tree.Tier1)));
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
