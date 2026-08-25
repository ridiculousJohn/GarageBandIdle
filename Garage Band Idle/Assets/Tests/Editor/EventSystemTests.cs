using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class EventSystemTests
    {
        // ---- Start ----

        [Test]
        public void Start_refuses_an_unmet_or_null_gate()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            tree.TimedGig.availableWhen = new FlagSet { flagId = "fans_revealed" };

            Assert.IsFalse(EventSystem.CanStart(ctx, tree.TimedGig));
            Assert.IsFalse(EventSystem.TryStart(ctx, tree.TimedGig));
            Assert.Throws<System.InvalidOperationException>(() => EventSystem.Start(ctx, tree.TimedGig));

            // A null gate refuses too - the fail-closed runtime backstop behind
            // the load-time check.
            tree.TimedGig.availableWhen = null;
            Assert.IsFalse(EventSystem.CanStart(ctx, tree.TimedGig));
            Assert.IsNull(tree.Tier1.activeEvent);
        }

        [Test]
        public void Start_refuses_an_occupied_host_running_expired_or_a_siblings()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            Assert.IsTrue(EventSystem.TryStart(ctx, tree.TimedGig));

            // A running record blocks the same event and a sibling alike.
            Assert.IsFalse(EventSystem.CanStart(ctx, tree.TimedGig));
            Assert.IsFalse(EventSystem.CanStart(ctx, tree.OpenMic));

            // Expired-but-undismissed still occupies (12.8).
            tree.Tier1.activeEvent.remainingSeconds = 0;
            Assert.IsFalse(EventSystem.CanStart(ctx, tree.OpenMic));
            Assert.Throws<System.InvalidOperationException>(() => EventSystem.Start(ctx, tree.OpenMic));
            Assert.AreEqual("timed_gig", tree.Tier1.activeEvent.eventId);
        }

        [Test]
        public void Start_writes_the_record_with_the_authored_timer()
        {
            var tree = new TestTree();

            EventSystem.Start(tree.Ctx(tree.Tier1), tree.TimedGig);

            var record = tree.Tier1.activeEvent;
            Assert.AreEqual("timed_gig", record.eventId);
            Assert.AreEqual(300d, record.remainingSeconds);
            Assert.IsFalse(record.goalReached);
        }

        [Test]
        public void OnEntry_runs_in_authored_order()
        {
            var tree = new TestTree();
            // The second action's formula reads what the first deposited:
            // reversed order would pay zero.
            tree.TimedGig.onEntry.Add(new AddCurrency { currencies = { tree.Fans }, amount = 50 });
            tree.TimedGig.onEntry.Add(new AddCurrency
            {
                currencies = { tree.Ch1Records },
                formula = new RootCurveFormula { currency = tree.Fans, divisor = 1, exponent = 1 }
            });

            EventSystem.Start(tree.Ctx(tree.Tier1), tree.TimedGig);

            Assert.AreEqual((BigNumber)50, tree.Ch1.balances["ch1_records"]);
        }

        [Test]
        public void Start_resolves_the_host_outward_and_runs_onEntry_in_its_scope()
        {
            var tree = new TestTree();
            var innerDef = TestTree.MakeTier("tier_inner");
            tree.Tier1Def.children.Add(innerDef);
            var root = ScopeState.Build(tree.RootDef);   // rebuild with the child
            var inner = (TierScopeState)root.FindInSubtree(innerDef);
            var tier1 = (TierScopeState)root.FindInSubtree(tree.Tier1Def);
            // Only the rebase to the resolved host makes this reset legal:
            // tier1 is not in the acting scope's subtree.
            tree.TimedGig.onEntry.Add(new ResetScope { scope = tree.Tier1Def });
            tier1.flags.Add("fans_revealed");

            EventSystem.Start(new GameContext(inner, tree.Now), tree.TimedGig);

            Assert.IsEmpty(tier1.flags);                               // onEntry ran in the host's scope
            Assert.AreEqual("timed_gig", tier1.activeEvent.eventId);   // and the record landed at the host
            Assert.IsNull(inner.activeEvent);
        }

        [Test]
        public void An_entry_reset_leaves_the_record_in_the_fresh_payload()
        {
            var tree = new TestTree();
            tree.TimedGig.onEntry.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Tier1.flags.Add("fans_revealed");
            var oldFacts = tree.Tier1.facts;

            EventSystem.Start(tree.Ctx(tree.Tier1), tree.TimedGig);

            Assert.AreNotSame(oldFacts, tree.Tier1.facts);
            Assert.IsEmpty(tree.Tier1.flags);
            Assert.AreEqual("timed_gig", tree.Tier1.activeEvent.eventId);
        }

        [Test]
        public void The_entry_restart_banks_a_gate_met_run_and_discards_an_unmet_one()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions = { new AddCurrency { currencies = { tree.Ch1Records }, amount = 3 } }
            };
            tree.TimedGig.onEntry.Add(new RestartScope { scope = tree.Tier1Def });
            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.balances["fans"] = 60;

            EventSystem.Start(ctx, tree.TimedGig);
            Assert.AreEqual((BigNumber)3, tree.Ch1.balances["ch1_records"]);   // banked through the rung's own gate
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);      // then cleared
            Assert.AreEqual("timed_gig", tree.Tier1.activeEvent.eventId);      // record in the fresh payload

            // The unmet run: a gate the fans cannot meet no-ops the bank, and
            // the clear still runs - nothing is lost that could have been kept.
            EventSystem.Dismiss(ctx, tree.TimedGig);
            tree.Tier1.balances["fans"] = 10;
            EventSystem.Start(ctx, tree.TimedGig);
            Assert.AreEqual((BigNumber)3, tree.Ch1.balances["ch1_records"]);   // nothing more banked
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);      // discarded either way
        }

        // ---- latching ----

        [Test]
        public void A_goal_met_mid_attempt_latches_before_the_decrement()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.TimedGig);
            ctx.Deposit("fans", 100);

            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 60, tree.Now);

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
            Assert.AreEqual(240d, tree.Tier1.activeEvent.remainingSeconds);
        }

        [Test]
        public void A_goal_met_then_spent_back_below_stays_latched()
        {
            var tree = new TestTree();
            // A spendable-balance goal: meeting it is something that happened,
            // not a state the player must still hold at dismissal (12.8).
            var showcase = TestTree.MakeDefinition<EventDefinition>("showcase");
            showcase.availableWhen = new Always();
            showcase.goal = new CurrencyAtLeast { currency = tree.Cash, threshold = 100 };
            showcase.timeLimitSeconds = 300;
            tree.Tier1Def.events.Add(showcase);
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, showcase);
            ctx.Deposit("cash", 100);
            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 10, tree.Now);
            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);

            ctx.Spend("cash", 80);
            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 10, tree.Now);

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
        }

        [Test]
        public void An_untimed_record_latches_the_same_way_a_timed_one_does()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.OpenMic);

            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 60, tree.Now);
            Assert.IsFalse(tree.Tier1.activeEvent.goalReached);   // unmet: nothing latches

            ctx.Deposit("fans", 50);
            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 60, tree.Now);

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
            Assert.AreEqual(0d, tree.Tier1.activeEvent.remainingSeconds);   // no clock to burn
        }

        [Test]
        public void A_goal_first_met_after_expiry_never_latches()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.TimedGig);
            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 300, tree.Now);   // expires unmet
            Assert.AreEqual(0d, tree.Tier1.activeEvent.remainingSeconds);

            ctx.Deposit("fans", 100);
            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 60, tree.Now);

            Assert.IsFalse(tree.Tier1.activeEvent.goalReached);   // expiry stopped the latch
            Assert.IsNotNull(tree.Tier1.activeEvent);             // and removed nothing
        }

        [Test]
        public void A_goal_met_by_the_segment_that_expires_the_timer_latches()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.TimedGig);
            ctx.Deposit("fans", 100);   // the segment's production lands before the timer phase

            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 300, tree.Now);

            // Latch before decrement: the tie goes to the player (12.8).
            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
            Assert.AreEqual(0d, tree.Tier1.activeEvent.remainingSeconds);
        }

        // ---- Dismiss ----

        [Test]
        public void Dismiss_refuses_an_empty_host_and_a_siblings_record()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            Assert.IsFalse(EventSystem.CanDismiss(ctx, tree.TimedGig));   // nothing to dismiss

            EventSystem.Start(ctx, tree.TimedGig);

            Assert.IsFalse(EventSystem.CanDismiss(ctx, tree.OpenMic));
            Assert.Throws<System.InvalidOperationException>(() => EventSystem.Dismiss(ctx, tree.OpenMic));
            Assert.AreEqual("timed_gig", tree.Tier1.activeEvent.eventId);   // the refusal changed nothing
        }

        [Test]
        public void Dismiss_pays_rewards_only_on_a_reached_goal_and_runs_onEnd_either_way()
        {
            var tree = new TestTree();
            tree.TimedGig.rewards.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 5 });
            tree.TimedGig.onEnd.Add(new SetFlag { flagId = "gj1_done" });
            var ctx = tree.Ctx(tree.Tier1);

            // Unreached: onEnd alone - failure has an ending too.
            EventSystem.Start(ctx, tree.TimedGig);
            EventSystem.Dismiss(ctx, tree.TimedGig);
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);
            Assert.IsTrue(tree.Ch1.flags.Contains("gj1_done"));
            Assert.IsNull(tree.Tier1.activeEvent);

            // Reached: rewards, then onEnd.
            tree.Ch1.flags.Remove("gj1_done");
            EventSystem.Start(ctx, tree.TimedGig);
            tree.Tier1.activeEvent.goalReached = true;
            EventSystem.Dismiss(ctx, tree.TimedGig);
            Assert.AreEqual((BigNumber)5, tree.Ch1.balances["ch1_records"]);
            Assert.IsTrue(tree.Ch1.flags.Contains("gj1_done"));
        }

        [Test]
        public void The_record_is_gone_before_either_ending_list_executes()
        {
            var tree = new TestTree();
            // Both lists fire a rung gated on the record's absence: only
            // remove-first lets either one through.
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new Not { condition = new EventRecordExists { host = tree.Tier1Def } },
                actions = { new AddCurrency { currencies = { tree.Ch1Records }, amount = 1 } }
            };
            tree.TimedGig.rewards.Add(new ExecuteRung { tier = tree.Tier1Def });
            tree.TimedGig.onEnd.Add(new ExecuteRung { tier = tree.Tier1Def });
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.TimedGig);
            tree.Tier1.activeEvent.goalReached = true;

            EventSystem.Dismiss(ctx, tree.TimedGig);

            Assert.AreEqual((BigNumber)2, tree.Ch1.balances["ch1_records"]);   // both lists saw the empty host
        }

        [Test]
        public void An_onEnd_restart_banks_through_a_reward_pending_guard()
        {
            var tree = new TestTree();
            // The rung shape the stranded-reward check wants: its restart
            // cannot fire while a reward sits armed. Remove-first is what lets
            // the dismissal's own restart bank the run it is ending.
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new All
                {
                    conditions =
                    {
                        new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                        new Not { condition = new EventRewardPending { host = tree.Tier1Def } }
                    }
                },
                actions = { new AddCurrency { currencies = { tree.Ch1Records }, amount = 3 } }
            };
            tree.TimedGig.onEnd.Add(new RestartScope { scope = tree.Tier1Def });
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.TimedGig);
            tree.Tier1.balances["fans"] = 60;
            tree.Tier1.activeEvent.goalReached = true;   // the armed reward the guard reads

            EventSystem.Dismiss(ctx, tree.TimedGig);

            Assert.AreEqual((BigNumber)3, tree.Ch1.balances["ch1_records"]);   // banked: the record left first
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);      // and the tier restarted
            Assert.IsNull(tree.Tier1.activeEvent);
        }

        [Test]
        public void An_empty_onEnd_leaves_the_runs_leavings_in_place()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.TimedGig);
            ctx.Deposit("cash", 300);
            tree.Tier1.flags.Add("fans_revealed");

            EventSystem.Dismiss(ctx, tree.TimedGig);

            Assert.IsNull(tree.Tier1.activeEvent);
            Assert.AreEqual((BigNumber)300, tree.Tier1.balances["cash"]);   // ending is not resetting
            Assert.IsTrue(tree.Tier1.flags.Contains("fans_revealed"));
        }

        [Test]
        public void A_reset_from_above_kills_the_record()
        {
            var tree = new TestTree();
            EventSystem.Start(tree.Ctx(tree.Tier1), tree.TimedGig);

            new ResetScope { scope = tree.Ch1Def }.Execute(tree.Ctx(tree.Ch1));

            Assert.IsNull(tree.Tier1.activeEvent);
            Assert.IsTrue(EventSystem.CanStart(tree.Ctx(tree.Tier1), tree.TimedGig));   // the host is free again
        }

        // ---- timers ----

        [Test]
        public void Timers_decrement_on_real_seconds_floor_at_zero_and_remove_nothing()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            EventSystem.Start(ctx, tree.TimedGig);

            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 50, tree.Now);
            Assert.AreEqual(250d, tree.Tier1.activeEvent.remainingSeconds);

            EventSystem.AdvanceTimers(tree.Root, tree.Ch1, 9999, tree.Now);
            Assert.AreEqual(0d, tree.Tier1.activeEvent.remainingSeconds);   // floored, never negative

            Assert.IsNotNull(tree.Tier1.activeEvent);                    // a record never removes itself
            Assert.IsFalse(EventSystem.CanStart(ctx, tree.OpenMic));     // and still occupies the host
        }

        // ---- idle ----

        [Test]
        public void BlocksIdle_is_true_for_a_timed_event_and_false_for_an_untimed_one()
        {
            var tree = new TestTree();

            Assert.IsTrue(tree.TimedGig.BlocksIdle);
            Assert.IsFalse(tree.OpenMic.BlocksIdle);
        }
    }
}
