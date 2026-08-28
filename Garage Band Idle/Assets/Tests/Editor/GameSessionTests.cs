using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The session's phase table, the command boundary, and the transaction
    // pipeline - the wrapped systems' own behavior stays in their suites.
    public class GameSessionTests
    {
        private static GameConfig Config(double maxGameSpeed = 4)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.maxGameSpeed = maxGameSpeed;
            return config;
        }

        // A tree, a session over it, and a refresh counter - the shape every
        // test starts from.
        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;
            public int Refreshes;

            public Fixture()
            {
                Session = new GameSession(Tree.Root, Config());
                Session.Refreshed += () => Refreshes++;
            }
        }

        // A trigger action that issues a session command - the exact shape the
        // reentrancy guard exists to catch.
        private class IssueSessionCommand : GameAction
        {
            public GameSession session;
            public ProducerDefinition producer;

            public override void Execute(GameContext ctx) => session.FireProducer(ctx, producer);
        }

        // ---- the phase table ----

        [Test]
        public void NoChapter_refuses_every_chapter_local_command()
        {
            var f = new Fixture();
            var ctx = f.Tree.Ctx(f.Tree.Tier1);
            // Every command is otherwise executable, so each refusal below is
            // the phase's doing and nothing else's.
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.earnedTotals["cash"] = 1000;
            f.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            f.Tree.Tier1Def.rung = new Rung { offerCondition = new Always(), actions = { new SetFlag { flagId = "fans_revealed" } } };

            Assert.IsFalse(f.Session.TryRung(ctx));
            Assert.IsFalse(f.Session.TryBuy(ctx, f.Tree.PracticeAmp));
            Assert.IsFalse(f.Session.TryBuy(ctx, f.Tree.StagePresence));
            Assert.IsFalse(f.Session.FireProducer(ctx, f.Tree.TapProducer));
            Assert.IsFalse(f.Session.SetActiveBars(ctx, f.Tree.LearnCovers, new[] { f.Tree.Cover1 }));
            Assert.IsFalse(f.Session.TryStartEvent(ctx, f.Tree.TimedGig));
            Assert.IsFalse(f.Session.TryDismissEvent(ctx, f.Tree.TimedGig));
            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));

            Assert.IsEmpty(f.Tree.Tier1.flags);
            Assert.AreEqual((BigNumber)1000, f.Tree.Tier1.balances["cash"]);
            Assert.IsNull(f.Tree.Tier1.activeEvent);
            Assert.AreEqual(0, f.Refreshes);
        }

        [Test]
        public void AwaitingIdleClaim_refuses_mutating_commands_never_ticks_and_admits_the_switch()
        {
            var f = new Fixture();
            var ctx = f.Tree.Ctx(f.Tree.Tier1);
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.earnedTotals["cash"] = 1000;
            f.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);   // the amp's idle rate makes a real offer

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);

            Assert.IsFalse(f.Session.TryRung(ctx));
            Assert.IsFalse(f.Session.TryBuy(ctx, f.Tree.PracticeAmp));
            Assert.IsFalse(f.Session.FireProducer(ctx, f.Tree.TapProducer));
            Assert.IsFalse(f.Session.SetActiveBars(ctx, f.Tree.LearnCovers, new[] { f.Tree.Cover1 }));
            Assert.IsFalse(f.Session.TryStartEvent(ctx, f.Tree.TimedGig));
            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)1000, f.Tree.Tier1.balances["cash"]);   // the amp's rate never ran

            // The switch stays legal, and backgrounding leaves the stamp - the
            // unpaid window recomputes on return.
            f.Session.SwitchChapter(null, f.Tree.Now);
            Assert.AreEqual(SessionPhase.NoChapter, f.Session.Phase);
            Assert.IsNull(f.Session.CurrentOffer);
            Assert.AreEqual(f.Tree.Now.AddSeconds(-1000), f.Tree.Ch1.lastActiveUtc);
        }

        [Test]
        public void Live_admits_every_command_kind()
        {
            var f = new Fixture();
            var ctx = f.Tree.Ctx(f.Tree.Tier1);
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.earnedTotals["cash"] = 1000;
            f.Tree.Tier1Def.rung = new Rung { offerCondition = new Always(), actions = { new SetFlag { flagId = "fans_revealed" } } };

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);

            Assert.IsTrue(f.Session.FireProducer(ctx, f.Tree.TapProducer));                            // +1 cash
            Assert.IsTrue(f.Session.TryBuy(ctx, f.Tree.PracticeAmp));                                  // -60
            Assert.IsTrue(f.Session.TryBuy(ctx, f.Tree.StagePresence));                                // -250
            Assert.IsTrue(f.Session.SetActiveBars(ctx, f.Tree.LearnCovers, new[] { f.Tree.Cover1 }));
            Assert.IsTrue(f.Session.TryStartEvent(ctx, f.Tree.TimedGig));
            Assert.IsTrue(f.Session.TryDismissEvent(ctx, f.Tree.TimedGig));
            Assert.IsTrue(f.Session.TryRung(ctx));
            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));                                             // +5 from the amp

            Assert.AreEqual(1, f.Tree.Tier1.generatorCounts["practice_amp"]);
            Assert.IsTrue(f.Tree.Tier1.purchasedUpgrades.Contains("stage_presence"));
            Assert.IsTrue(f.Tree.Tier1.flags.Contains("fans_revealed"));
            Assert.IsNull(f.Tree.Tier1.activeEvent);
            Assert.AreEqual((BigNumber)696, f.Tree.Tier1.balances["cash"]);
        }

        // ---- the boundary ----

        [Test]
        public void The_boundary_refuses_a_dormant_chapters_scope_and_admits_the_foreground_subtree()
        {
            var tree = new TestTree();
            var ch2Def = TestTree.MakeChapter("ch2");
            tree.Chapters.Add(ch2Def);
            var root = ScopeState.Build(tree.Content);
            var ch1 = (ChapterScopeState)root.FindInSubtree(tree.Ch1Def);
            var tier1 = root.FindInSubtree(tree.Tier1Def);
            var ch2 = (ChapterScopeState)root.FindInSubtree(ch2Def);
            var session = new GameSession(root, Config());
            session.SwitchChapter(ch1, tree.Now);

            // Reachable is not the same as mutable: the dormant chapter and
            // the root both sit outside the foreground subtree.
            Assert.IsFalse(session.FireProducer(new GameContext(ch2, tree.Now), tree.TapProducer));
            Assert.IsFalse(session.FireProducer(new GameContext(root, tree.Now), tree.TapProducer));

            Assert.IsTrue(session.FireProducer(new GameContext(tier1, tree.Now), tree.TapProducer));
            Assert.AreEqual((BigNumber)1, tier1.balances["cash"]);
        }

        // ---- the pipeline ----

        [Test]
        public void A_commands_own_mutation_arms_a_trigger_that_fires_in_the_same_transaction()
        {
            var f = new Fixture();
            f.Tree.Tier1Trigger.condition = new EarnedTotalAtLeast { currency = f.Tree.Cash, threshold = 1 };
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);   // the entry sweep sees earned 0

            Assert.IsTrue(f.Session.FireProducer(f.Tree.Ctx(f.Tree.Tier1), f.Tree.TapProducer));

            Assert.IsTrue(f.Tree.Tier1.flags.Contains("fans_revealed"));
            Assert.IsTrue(f.Tree.Tier1.firedTriggers.Contains("tier1_trigger"));
        }

        [Test]
        public void Exactly_one_refresh_per_completed_transaction_and_none_on_a_refusal()
        {
            var f = new Fixture();
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(1, f.Refreshes);

            Assert.IsTrue(f.Session.FireProducer(f.Tree.Ctx(f.Tree.Tier1), f.Tree.TapProducer));
            Assert.AreEqual(2, f.Refreshes);

            // A refused buy, a nonpositive dt, and a same-chapter switch all
            // run no pipeline.
            Assert.IsFalse(f.Session.TryBuy(f.Tree.Ctx(f.Tree.Tier1), f.Tree.PracticeAmp));
            f.Session.Tick(0, f.Tree.Now);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(2, f.Refreshes);
        }

        [Test]
        public void Backgrounding_commits_and_refreshes_without_sweeping()
        {
            var f = new Fixture();
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            // Armed between transactions, so only this transaction's sweep
            // could fire it.
            f.Tree.Tier1Trigger.condition = new Always();
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            var before = f.Refreshes;

            f.Session.SwitchChapter(null, f.Tree.Now);

            Assert.AreEqual(SessionPhase.NoChapter, f.Session.Phase);
            Assert.IsNull(f.Session.ForegroundChapter);
            Assert.AreEqual(before + 1, f.Refreshes);
            Assert.IsEmpty(f.Tree.Tier1.firedTriggers);
        }

        [Test]
        public void Entering_AwaitingIdleClaim_commits_and_refreshes_without_sweeping()
        {
            var f = new Fixture();
            f.Tree.Tier1Trigger.condition = new Always();
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            f.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);
            Assert.AreEqual(1, f.Refreshes);
            // The sweep whose reset could erase the unpaid window never ran,
            // and the offer stands to present.
            Assert.IsEmpty(f.Tree.Tier1.firedTriggers);
            Assert.IsNotNull(f.Session.CurrentOffer);
        }

        [Test]
        public void The_transaction_entering_Live_performs_the_deferred_sweep()
        {
            var f = new Fixture();
            f.Tree.Tier1Trigger.condition = new Always();
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });

            // The first live sweep after switch-in (12.8): the threshold this
            // trigger models crossed while the chapter was dormant.
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);

            Assert.AreEqual(SessionPhase.Live, f.Session.Phase);
            Assert.IsTrue(f.Tree.Tier1.firedTriggers.Contains("tier1_trigger"));
        }

        // ---- reentrancy and construction ----

        [Test]
        public void A_command_issued_from_inside_a_transaction_throws()
        {
            // From a trigger action, mid-sweep.
            var f = new Fixture();
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Tree.Tier1Trigger.condition = new EarnedTotalAtLeast { currency = f.Tree.Cash, threshold = 1 };
            f.Tree.Tier1Trigger.actions.Add(new IssueSessionCommand { session = f.Session, producer = f.Tree.TapProducer });
            Assert.Throws<System.InvalidOperationException>(
                () => f.Session.FireProducer(f.Tree.Ctx(f.Tree.Tier1), f.Tree.TapProducer));

            // From a refresh handler, post-commit.
            var g = new Fixture();
            g.Session.Refreshed += () => g.Session.Tick(1, g.Tree.Now);
            Assert.Throws<System.InvalidOperationException>(
                () => g.Session.SwitchChapter(g.Tree.Ch1, g.Tree.Now));
        }

        [Test]
        public void An_invalid_config_refuses_construction()
        {
            var tree = new TestTree();

            Assert.Throws<System.InvalidOperationException>(() => new GameSession(tree.Root, null));
            Assert.Throws<System.InvalidOperationException>(() => new GameSession(tree.Root, Config(0.5)));
            Assert.Throws<System.InvalidOperationException>(() => new GameSession(tree.Root, Config(double.NaN)));

            var negativeCap = Config();
            negativeCap.idleCapSeconds = -1;
            Assert.Throws<System.InvalidOperationException>(() => new GameSession(tree.Root, negativeCap));
        }
    }
}
