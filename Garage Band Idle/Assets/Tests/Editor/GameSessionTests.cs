using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The session's phase table and the transaction pipeline - the wrapped
    // systems' own behavior stays in their suites.
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

        // A trigger action that issues a session command - the shape that
        // submits a transaction from inside the one that is executing (12.9).
        private class IssueSessionCommand : GameAction
        {
            public GameSession session;
            public ProducerDefinition producer;

            public override void Execute(GameContext ctx) => session.FireProducer(ctx, producer);
        }

        // ---- the phase table ----

        [Test]
        public void A_locked_chapter_cannot_be_entered_until_its_completion_requirement_is_met()
        {
            var tree = new TestTree();
            var definition = TestTree.MakeChapter("ch2");
            definition.unlock = new FlagSet { flagId = "ch1_complete" };
            tree.Chapters.Add(definition);
            tree.Rebuild();
            var chapter = (ChapterScopeState)TestNavigation.Node(tree.Root, definition);
            var session = new GameSession(tree.Root, Config());
            session.SwitchChapter(tree.Ch1, tree.Now);
            var stamp = tree.Ch1.lastActiveUtc;
            var refreshes = 0;
            session.Refreshed += () => refreshes++;

            session.SwitchChapter(chapter, tree.Now.AddSeconds(1));
            Assert.AreSame(tree.Ch1, session.ForegroundChapter);
            Assert.AreEqual(tree.Ch1.ScopeId, tree.Root.currentChapterId);
            Assert.AreEqual(stamp, tree.Ch1.lastActiveUtc);
            Assert.AreEqual(0, refreshes);

            tree.Root.flags.Add("ch1_complete");
            session.SwitchChapter(chapter, tree.Now.AddSeconds(2));
            Assert.AreSame(chapter, session.ForegroundChapter);
            Assert.AreEqual("ch2", tree.Root.currentChapterId);
        }

        [Test]
        public void NoChapter_never_ticks()
        {
            var f = new Fixture();
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.generatorCounts["practice_amp"] = 1;

            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));

            Assert.AreEqual((BigNumber)1000, f.Tree.Tier1.balances["cash"]);   // the amp's rate never ran
            Assert.AreEqual(0, f.Refreshes);
        }

        [Test]
        public void AwaitingIdleClaim_never_ticks_and_admits_the_switch()
        {
            var f = new Fixture();
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);   // the amp's idle rate makes a real offer

            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);

            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));
            Assert.AreEqual((BigNumber)1000, f.Tree.Tier1.balances["cash"]);   // the amp's rate never ran

            // Backgrounding leaves the stamp - the unpaid window recomputes on
            // return.
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

            f.Session.FireProducer(ctx, f.Tree.TapProducer);                                           // +1 cash
            f.Session.TryBuy(ctx, f.Tree.PracticeAmp, 1);                                              // -60
            f.Session.TryBuy(ctx, f.Tree.StagePresence);                                               // -250
            f.Session.SetActiveMembers(ctx, f.Tree.LearnCovers, new[] { f.Tree.Cover1 });
            f.Session.TryStartEvent(ctx, f.Tree.TimedGig);
            Assert.IsNotNull(f.Tree.Tier1.activeEvent, "the gig is the host's standing attempt");
            f.Session.TryDismissEvent(ctx, f.Tree.TimedGig);
            f.Session.TryRung(ctx);
            f.Session.Tick(10, f.Tree.Now.AddSeconds(10));                                             // +5 from the amp

            // What each command wrote, which is how a void command is read.
            Assert.AreEqual(1, f.Tree.Tier1.generatorCounts["practice_amp"]);
            Assert.IsTrue(f.Tree.Tier1.purchasedUpgrades.Contains("stage_presence"));
            Assert.IsTrue(f.Tree.Tier1.activeMembers[f.Tree.LearnCovers.Id].Contains(f.Tree.Cover1.Id));
            Assert.IsTrue(f.Tree.Tier1.flags.Contains("fans_revealed"));
            Assert.IsNull(f.Tree.Tier1.activeEvent);
            Assert.AreEqual((BigNumber)696, f.Tree.Tier1.balances["cash"]);
        }

        // ---- the pipeline ----

        [Test]
        public void A_commands_own_mutation_arms_a_trigger_that_fires_in_the_same_transaction()
        {
            var f = new Fixture();
            f.Tree.Tier1Trigger.condition = new EarnedTotalAtLeast { currency = f.Tree.Cash, threshold = 1 };
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);   // the entry sweep sees earned 0

            f.Session.FireProducer(f.Tree.Ctx(f.Tree.Tier1), f.Tree.TapProducer);

            Assert.IsTrue(f.Tree.Tier1.flags.Contains("fans_revealed"));
            Assert.IsTrue(f.Tree.Tier1.firedTriggers.Contains("tier1_trigger"));
        }

        [Test]
        public void Exactly_one_refresh_per_completed_transaction_and_none_on_a_refusal()
        {
            var f = new Fixture();
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(1, f.Refreshes);

            f.Session.FireProducer(f.Tree.Ctx(f.Tree.Tier1), f.Tree.TapProducer);
            Assert.AreEqual(2, f.Refreshes);

            // A refused buy, a nonpositive dt, and a same-chapter switch all
            // run no pipeline. The buy's answer is what its callback is told.
            bool? bought = null;
            f.Session.TryBuy(f.Tree.Ctx(f.Tree.Tier1), f.Tree.PracticeAmp, 1, ran => bought = ran);
            Assert.AreEqual(false, bought, "an unaffordable amp is refused");
            f.Session.Tick(0, f.Tree.Now);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(2, f.Refreshes);
        }

        // A count is one command (12.2/12.9): one spend, one count write, one
        // sweep at the close and one refresh, whether the player tapped "+1"
        // five times or "+5" once. The latch the crossing arms is set once.
        [Test]
        public void A_bulk_buy_is_one_transaction()
        {
            var f = new Fixture();
            var ctx = f.Tree.Ctx(f.Tree.Tier1);
            f.Tree.Tier1.balances["cash"] = 1000;
            f.Tree.Tier1.earnedTotals["cash"] = 1000;                  // the amp's gate wants 100 earned
            f.Tree.Tier1Trigger.condition = new OwnedCountAtLeast { generator = f.Tree.PracticeAmp, count = 3 };
            f.Tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);            // the entry sweep sees no amps
            var before = f.Refreshes;

            bool? bought = null;
            f.Session.TryBuy(ctx, f.Tree.PracticeAmp, 5, ran => bought = ran);

            Assert.AreEqual(true, bought);
            Assert.AreEqual(before + 1, f.Refreshes, "one transaction, one refresh");
            Assert.AreEqual(5, f.Tree.Tier1.generatorCounts["practice_amp"]);
            Assert.IsTrue(f.Tree.Tier1.firedTriggers.Contains("tier1_trigger"),
                "the count crossed three inside the buy and the close's sweep latched it");
        }

        // The tick's purchase phase goes through the same path the row's button
        // takes (12.9), so the transaction's own close sweeps over what it
        // bought rather than leaving the crossing for the next tick.
        [Test]
        public void A_ticks_autobuy_is_latched_by_the_sweep_at_the_transactions_close()
        {
            var tree = new TestTree();
            var carrier = TestTree.MakeDefinition<ModifierDefinition>("hands_free");
            carrier.effects.Add(new Effect { target = "practice_amp", stat = Stat.AutoBuy, multiplier = 1 });
            tree.Tier1Def.modifiers.Add(carrier);
            tree.Tier1Trigger.condition = new OwnedCountAtLeast { generator = tree.PracticeAmp, count = 3 };
            tree.Tier1Trigger.actions.Add(new SetFlag { flagId = "fans_revealed" });
            tree.Rebuild();
            tree.Tier1.modifierStacks["hands_free"] = 1;
            tree.Tier1.balances["cash"] = 1000;
            tree.Tier1.earnedTotals["cash"] = 1000;                  // the amp's gate wants 100 earned
            var session = new GameSession(tree.Root, Config());
            session.SwitchChapter(tree.Ch1, tree.Now);               // the entry sweep sees no amps

            session.Tick(1, tree.Now.AddSeconds(1));

            Assert.AreEqual(8, tree.Tier1.generatorCounts["practice_amp"], "the largest count 1000 cash affords");
            Assert.IsTrue(tree.Tier1.flags.Contains("fans_revealed"),
                "the count crossed three inside the tick and the close's sweep latched it");
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

        // ---- the queue and construction ----

        // A command issued from inside a running transaction is submitted
        // behind it (12.9): the entry that is executing holds the front, so the
        // submission waits for the frame's drain and runs as its own
        // transaction with its own refresh, never nested.
        [Test]
        public void A_command_issued_from_inside_a_transaction_runs_at_the_next_drain()
        {
            // From a trigger action, mid-sweep.
            var f = new Fixture();
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            f.Tree.Tier1Trigger.condition = new EarnedTotalAtLeast { currency = f.Tree.Cash, threshold = 1 };
            f.Tree.Tier1Trigger.actions.Add(new IssueSessionCommand { session = f.Session, producer = f.Tree.TapProducer });
            var refreshes = f.Refreshes;

            f.Session.FireProducer(f.Tree.Ctx(f.Tree.Tier1), f.Tree.TapProducer);

            Assert.AreEqual((BigNumber)1, f.Tree.Tier1.balances["cash"],
                "the tap the trigger issued waits behind the tap that armed it");
            Assert.AreEqual(refreshes + 1, f.Refreshes, "one transaction, one refresh");

            f.Session.Drain();

            Assert.AreEqual((BigNumber)2, f.Tree.Tier1.balances["cash"], "the drain runs it");
            Assert.AreEqual(refreshes + 2, f.Refreshes, "as a transaction of its own");

            // From a refresh handler, post-commit.
            var g = new Fixture();
            g.Session.Refreshed += () => g.Session.Tick(1, g.Tree.Now);

            g.Session.SwitchChapter(g.Tree.Ch1, g.Tree.Now);

            Assert.AreEqual(1, g.Refreshes, "the switch's own refresh is all that has run");

            g.Session.Drain();

            // The drain runs the entries present when it started and no more,
            // so the tick's own refresh submits a tick that waits again.
            Assert.AreEqual(2, g.Refreshes, "the handler's tick, and nothing the tick's refresh submitted");
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
