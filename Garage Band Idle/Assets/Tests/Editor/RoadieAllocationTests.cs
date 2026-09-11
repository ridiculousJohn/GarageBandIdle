using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Meta;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class RoadieAllocationTests
    {
        private static GameSession Session(TestTree tree, double tickIntervalSeconds = 0.25)
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.tickIntervalSeconds = tickIntervalSeconds;
            return new GameSession(tree.Root, config);
        }

        [Test]
        public void A_valid_map_replaces_the_whole_allocation_and_drops_zero_entries()
        {
            var tree = new TestTree();
            var session = Session(tree);
            tree.Root.balances[Roadies.CurrencyId] = 1;

            bool? ran = null;
            session.SetRoadieAllocation(
                new Dictionary<string, int> { { tree.Ch1.ScopeId, 1 } }, tree.Now,
                value => ran = value);

            Assert.AreEqual(true, ran);
            Assert.AreEqual(1, tree.Root.roadieAllocation[tree.Ch1.ScopeId]);

            session.SetRoadieAllocation(
                new Dictionary<string, int> { { tree.Ch1.ScopeId, 0 } }, tree.Now,
                value => ran = value);

            Assert.AreEqual(true, ran);
            Assert.IsEmpty(tree.Root.roadieAllocation,
                "replace semantics remove the standing entry and zero is not stored");
        }

        [Test]
        public void Invalid_maps_are_refused_atomically_without_a_refresh()
        {
            var tree = new TestTree();
            var session = Session(tree);
            tree.Root.balances[Roadies.CurrencyId] = 1;
            tree.Root.roadieAllocation[tree.Ch1.ScopeId] = 1;
            var refreshes = 0;
            session.Refreshed += () => refreshes++;

            AssertRefused(session, tree.Now,
                new Dictionary<string, int> { { tree.Ch1.ScopeId, -1 } });
            AssertRefused(session, tree.Now,
                new Dictionary<string, int> { { tree.Tier1.ScopeId, 1 } });
            AssertRefused(session, tree.Now,
                new Dictionary<string, int> { { tree.Ch1.ScopeId, 2 } });

            Assert.AreEqual(1, tree.Root.roadieAllocation.Count);
            Assert.AreEqual(1, tree.Root.roadieAllocation[tree.Ch1.ScopeId]);
            Assert.AreEqual(0, refreshes, "a refused command commits no transaction");
        }

        [Test]
        public void A_queued_command_owns_a_snapshot_of_the_submitted_draft()
        {
            var tree = new TestTree();
            var session = Session(tree);
            tree.Root.balances[Roadies.CurrencyId] = 1;
            var draft = new Dictionary<string, int> { { tree.Ch1.ScopeId, 1 } };
            var submitted = false;

            session.Refreshed += () =>
            {
                if (submitted)
                    return;
                submitted = true;
                session.SetRoadieAllocation(draft, tree.Now);
            };

            session.SwitchChapter(tree.Ch1, tree.Now);
            draft[tree.Ch1.ScopeId] = 0;

            Assert.IsEmpty(tree.Root.roadieAllocation, "the command is still queued behind the switch");
            session.Drain();
            Assert.AreEqual(1, tree.Root.roadieAllocation[tree.Ch1.ScopeId],
                "later edits to the UI draft do not rewrite the queued transaction");
        }

        [Test]
        public void The_sum_across_chapters_must_fit_the_owned_pool()
        {
            var tree = new TestTree();
            var ch2Definition = TestTree.MakeChapter("ch2");
            tree.Chapters.Add(ch2Definition);
            tree.Rebuild();
            var ch2 = (ChapterScopeState)TestNavigation.Node(tree.Root, ch2Definition);
            var session = Session(tree);
            tree.Root.balances[Roadies.CurrencyId] = 3;

            bool? ran = null;
            session.SetRoadieAllocation(new Dictionary<string, int>
            {
                { tree.Ch1.ScopeId, 1 },
                { ch2.ScopeId, 2 }
            }, tree.Now, value => ran = value);

            Assert.AreEqual(true, ran, "all three owned Roadies can be split across root's chapters");
            Assert.AreEqual(1, tree.Root.roadieAllocation[tree.Ch1.ScopeId]);
            Assert.AreEqual(2, tree.Root.roadieAllocation[ch2.ScopeId]);

            session.SetRoadieAllocation(new Dictionary<string, int>
            {
                { tree.Ch1.ScopeId, 2 },
                { ch2.ScopeId, 2 }
            }, tree.Now, value => ran = value);

            Assert.AreEqual(false, ran, "four stationed Roadies exceed the BigNumber balance of three");
            Assert.AreEqual(1, tree.Root.roadieAllocation[tree.Ch1.ScopeId], "the refused replacement is atomic");
            Assert.AreEqual(2, tree.Root.roadieAllocation[ch2.ScopeId]);
        }

        [Test]
        public void A_dormant_chapters_next_claim_uses_the_current_allocation()
        {
            var tree = new TestTree();
            var session = Session(tree);
            tree.Root.balances[Roadies.CurrencyId] = 1;
            tree.Tier1.generatorCounts[tree.PracticeAmp.Id] = 1;
            tree.Ch1.lastActiveUtc = tree.Now.AddSeconds(-200);

            session.SetRoadieAllocation(
                new Dictionary<string, int> { { tree.Ch1.ScopeId, 1 } }, tree.Now);
            session.SwitchChapter(tree.Ch1, tree.Now);

            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, session.Phase);
            var cash = session.CurrentOffer.lines.Find(line => line.currency == tree.Cash);
            Assert.IsNotNull(cash);
            Assert.That(cash.amount.ToDouble(), Is.EqualTo(55.125).Within(1e-9),
                "200 seconds at 0.5/s, idle x0.5, roadie_total x1.05 and roadie_active x1.05");
        }

        [Test]
        public void Pending_time_uses_the_old_allocation_and_the_next_tick_uses_the_new_one()
        {
            var tree = new TestTree();
            var session = Session(tree, tickIntervalSeconds: 1);
            tree.Root.balances[Roadies.CurrencyId] = 1;
            tree.Tier1.generatorCounts[tree.PracticeAmp.Id] = 1;
            session.SwitchChapter(tree.Ch1, tree.Now);

            session.Accumulate(tree.Now.AddSeconds(0.5));
            session.SetRoadieAllocation(
                new Dictionary<string, int> { { tree.Ch1.ScopeId, 1 } },
                tree.Now.AddSeconds(0.5));

            Assert.That(tree.Tier1.balances[tree.Cash.Id].ToDouble(), Is.EqualTo(0.25).Within(1e-9),
                "the command flushes banked time before changing the boost");

            session.Tick(1, tree.Now.AddSeconds(1.5));
            Assert.That(tree.Tier1.balances[tree.Cash.Id].ToDouble(), Is.EqualTo(0.80125).Within(1e-9),
                "the next tick uses roadie_total and roadie_active, 1.05 x 1.05");
        }

        private static void AssertRefused(GameSession session, System.DateTime nowUtc,
                                          IReadOnlyDictionary<string, int> allocation)
        {
            bool? ran = null;
            session.SetRoadieAllocation(allocation, nowUtc, value => ran = value);
            Assert.AreEqual(false, ran);
        }

        [Test]
        public void Chapter_completion_unlocks_allocation_and_replay_does_not_relock_it()
        {
            var tree = new TestTree();
            var ch2Definition = TestTree.MakeChapter("ch2");
            ch2Definition.unlock = new FlagSet { flagId = "ch1_complete" };
            tree.Chapters.Add(ch2Definition);
            tree.Rebuild();
            var session = Session(tree);
            tree.Root.balances[Roadies.CurrencyId] = 1;
            tree.Root.roadieAllocation[tree.Ch1.ScopeId] = 1;
            var requested = new Dictionary<string, int> { { "ch2", 1 } };

            AssertRefused(session, tree.Now, requested);
            Assert.AreEqual(1, tree.Root.roadieAllocation[tree.Ch1.ScopeId]);
            Assert.IsFalse(tree.Root.roadieAllocation.ContainsKey("ch2"));

            // The capstone's durable completion fact lives at root.
            tree.Root.flags.Add("ch1_complete");
            tree.Ch1.Clear(tree.Now);
            bool? accepted = null;
            session.SetRoadieAllocation(requested, tree.Now, value => accepted = value);
            Assert.AreEqual(true, accepted);
            Assert.AreEqual(1, tree.Root.roadieAllocation["ch2"]);
            Assert.IsFalse(tree.Root.roadieAllocation.ContainsKey(tree.Ch1.ScopeId));
        }
    }
}
