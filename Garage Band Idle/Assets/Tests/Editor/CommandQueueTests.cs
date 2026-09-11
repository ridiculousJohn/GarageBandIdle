using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Monetization;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The queue every session transaction is submitted to (design doc 12.9):
    // what runs at the call, what waits, what a drain is bounded by, and what a
    // caller is told. The commands' own behavior stays in their suites - what
    // these rows read is WHEN each one ran.
    public class CommandQueueTests
    {
        private static GameConfig Config()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.maxGameSpeed = 4;
            return config;
        }

        // A tree, a session over it, a refresh counter, and the ad seam with a
        // save site - the shape the rows start from.
        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly GameSession Session;
            public readonly FakeAdService Ads = new();
            public readonly AdManager AdManager;
            public int Refreshes;
            public int Saves;

            public Fixture()
            {
                var config = Config();
                Session = new GameSession(Tree.Root, config);
                AdManager = new AdManager(Session, Ads, config, () => Saves++);
                Session.Refreshed += () => Refreshes++;
            }

            // The switch a live player makes: the stamp is now, so no window is
            // owed and the phase lands Live.
            public void Enter()
            {
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            public GameContext Ctx => Tree.Ctx(Tree.Tier1);

            // The tap's yield is the whole economy these rows need, so the
            // balance counts transactions.
            public BigNumber Cash =>
                Tree.Tier1.balances.TryGetValue("cash", out var held) ? held : BigNumber.Zero;
        }

        // A trigger action that dies mid-sweep, which is a transaction throwing
        // out of the middle of the pipeline.
        private class Throw : GameAction
        {
            public override void Execute(GameContext ctx) =>
                throw new InvalidOperationException("the trigger action died mid-sweep.");
        }

        // ---- what runs at the call ----

        // Submission with an empty queue runs the entry at once (12.9): an
        // optimization, so a row may observe it, and every top-level command in
        // the suite is written over it.
        [Test]
        public void A_command_with_an_empty_queue_runs_at_the_call()
        {
            var f = new Fixture();
            f.Enter();
            var refreshes = f.Refreshes;

            f.Session.FireProducer(f.Ctx, f.Tree.TapProducer);

            Assert.AreEqual((BigNumber)1, f.Cash, "the tap's yield is banked when the call returns");
            Assert.AreEqual(refreshes + 1, f.Refreshes, "one transaction, one refresh");
        }

        // ---- what waits ----

        // The executing entry holds the front for the whole of its run, so a
        // command a refresh handler issues lands behind it and runs at the
        // drain as its own transaction, with its own refresh (12.9/12.11).
        [Test]
        public void A_command_submitted_from_a_refresh_waits_for_the_drain()
        {
            var f = new Fixture();
            f.Enter();
            var armed = true;
            f.Session.Refreshed += () =>
            {
                if (!armed)
                    return;
                armed = false;
                f.Session.FireProducer(f.Ctx, f.Tree.TapProducer);
            };
            var refreshes = f.Refreshes;

            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Opener);

            Assert.AreEqual(BigNumber.Zero, f.Cash, "the handler's tap is behind the transaction that refreshed");
            Assert.AreEqual(refreshes + 1, f.Refreshes, "which is the only one that has run");

            f.Session.Drain();

            Assert.AreEqual((BigNumber)1, f.Cash, "the drain runs it");
            Assert.AreEqual(refreshes + 2, f.Refreshes, "as a transaction of its own");
        }

        // The drain's bound is the count it started with. A handler that always
        // submits would never let it return otherwise, and an entry enqueued
        // inside a drain belongs to the next frame.
        [Test]
        public void The_drain_runs_what_was_queued_when_it_started_and_no_more()
        {
            var f = new Fixture();
            f.Enter();
            f.Session.Refreshed += () => f.Session.FireProducer(f.Ctx, f.Tree.TapProducer);

            f.Session.FireProducer(f.Ctx, f.Tree.TapProducer);
            Assert.AreEqual((BigNumber)1, f.Cash, "the call ran one transaction");

            f.Session.Drain();
            Assert.AreEqual((BigNumber)2, f.Cash, "the drain ran the one entry present when it started");

            f.Session.Drain();
            Assert.AreEqual((BigNumber)3, f.Cash, "and the next drain runs the one that pass submitted");
        }

        // The queue runs from the front, so two commands issued from one
        // handler run in the order they were submitted.
        [Test]
        public void Queued_commands_run_in_submission_order()
        {
            var f = new Fixture();
            f.Enter();
            var order = new List<string>();
            var armed = true;
            f.Session.Refreshed += () =>
            {
                if (!armed)
                    return;
                armed = false;
                f.Session.FireProducer(f.Ctx, f.Tree.TapProducer, ran => order.Add("tap"));
                f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Capstone, ran => order.Add("beat"));
            };

            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Opener);
            Assert.IsEmpty(order, "both wait behind the transaction that submitted them");

            f.Session.Drain();

            CollectionAssert.AreEqual(new[] { "tap", "beat" }, order, "submission order is run order");
        }

        // A transaction that throws is removed all the same, so the queue is
        // never parked behind a dead entry.
        [Test]
        public void A_throwing_transaction_is_removed_and_the_queue_keeps_moving()
        {
            var f = new Fixture();
            f.Enter();
            // Armed between transactions, so the sweep that runs it belongs to
            // the command below.
            f.Tree.Tier1Trigger.condition = new Always();
            f.Tree.Tier1Trigger.actions.Add(new Throw());

            Assert.Throws<InvalidOperationException>(() => f.Session.FireProducer(f.Ctx, f.Tree.TapProducer));
            Assert.AreEqual((BigNumber)1, f.Cash, "the mutation ran before the sweep died");

            // Closed the same way it was armed, so the next command's own sweep
            // is quiet and what the row reads is the queue.
            f.Tree.Tier1Trigger.condition = new Not { condition = new Always() };
            f.Session.FireProducer(f.Ctx, f.Tree.TapProducer);

            Assert.AreEqual((BigNumber)2, f.Cash, "the next command found an empty queue and ran at the call");
        }

        // ---- what the caller is told ----

        // A caller that needs the outcome passes a callback, which is told the
        // command's own answer after the transaction has run (12.9).
        [Test]
        public void The_completed_callback_reports_the_commands_answer()
        {
            var f = new Fixture();
            f.Enter();

            bool? tapped = null;
            f.Session.FireProducer(f.Ctx, f.Tree.TapProducer, ran => tapped = ran);
            Assert.AreEqual(true, tapped, "firing has no gate of its own, so it always runs");
            Assert.AreEqual((BigNumber)1, f.Cash);

            bool? bought = null;
            f.Session.TryBuy(f.Ctx, f.Tree.PracticeAmp, ran => bought = ran);
            Assert.AreEqual(false, bought, "an unaffordable amp is the command's own refusal");
            Assert.IsFalse(f.Tree.Tier1.generatorCounts.ContainsKey("practice_amp"), "and nothing was written");
        }

        // ---- the frame ----

        // The frame drains what the last frame's refreshes submitted before it
        // banks time (12.9), so a queued command runs on the next frame whether
        // or not a tick is due.
        [Test]
        public void Accumulate_drains_before_it_banks()
        {
            var f = new Fixture();
            f.Enter();
            var armed = true;
            f.Session.Refreshed += () =>
            {
                if (!armed)
                    return;
                armed = false;
                f.Session.FireProducer(f.Ctx, f.Tree.TapProducer);
            };

            f.Session.AcknowledgeStory(f.Tree.Ctx(f.Tree.Ch1), f.Tree.Opener);
            Assert.AreEqual(BigNumber.Zero, f.Cash, "the handler's tap is on the queue");

            // A tenth of a second, under the authored interval: nothing is due.
            f.Session.Accumulate(f.Tree.Now.AddSeconds(0.1));

            Assert.AreEqual((BigNumber)1, f.Cash, "the frame ran the queued tap");
            Assert.IsNull(f.Session.LastTick, "and banked the tenth without ticking");
        }

        // ---- the ad callback's save ----

        // The save waits for the transaction that settled: the callback is told
        // the claim's own answer, and only a settled claim reaches disk (12.9).
        [Test]
        public void The_ad_callbacks_save_waits_for_the_settled_claim()
        {
            var f = new Fixture();
            f.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            f.Tree.Ch1.lastActiveUtc = f.Tree.Now.AddSeconds(-1000);
            f.Session.SwitchChapter(f.Tree.Ch1, f.Tree.Now);
            Assert.AreEqual(SessionPhase.AwaitingIdleClaim, f.Session.Phase);

            f.AdManager.RequestIdleDouble();
            Assert.AreEqual(0, f.Saves, "a request settles nothing, so it saves nothing");

            f.AdManager.Update(f.Tree.Now);

            Assert.AreEqual(SessionPhase.Live, f.Session.Phase, "the claim settled");
            Assert.AreEqual(1, f.Saves, "and the save followed it");

            // The other answer: OK settled the offer while the ad was up, so
            // the callback's claim is refused and nothing is written.
            var g = new Fixture();
            g.Tree.Tier1.generatorCounts["practice_amp"] = 1;
            g.Tree.Ch1.lastActiveUtc = g.Tree.Now.AddSeconds(-1000);
            g.Session.SwitchChapter(g.Tree.Ch1, g.Tree.Now);
            g.AdManager.RequestIdleDouble();
            g.Session.ClaimIdle(g.Tree.Now);
            Assert.AreEqual(SessionPhase.Live, g.Session.Phase);

            g.AdManager.Update(g.Tree.Now);

            Assert.AreEqual(0, g.Saves, "a refused claim saves nothing");
        }
    }
}
