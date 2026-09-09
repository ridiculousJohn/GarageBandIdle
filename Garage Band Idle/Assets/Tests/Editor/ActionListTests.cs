using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.UI;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The reset's own refusal (design doc 12.5) is a rule about RUNNING AN
    // ACTION LIST, so there is exactly one place it can live and exactly six
    // places that have to ask. One test per site, each with an armed reward
    // under the list's reset target: the site's guard answers no, no fact
    // moves, and the guard answers yes once the record is dismissed. The
    // seventh is the runner's own contract - a list runs whole or not at all.
    public class ActionListTests
    {
        // The fixture's untimed open mic, armed on tier1. Dismissing it is a
        // real DismissEvent rather than a nulled field, because "after the
        // record is dismissed" is what the player does.
        private static void Arm(TestTree tree) =>
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", goalReached = true };

        private static void Dismiss(TestTree tree) =>
            Assert.IsTrue(EventSystem.TryDismiss(tree.Ctx(tree.Tier1), tree.OpenMic), "the dismissal is always legal");

        // ---- 1. the rung ----

        [Test]
        public void A_rung_whose_reset_is_refused_is_not_offered_and_runs_nothing()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions =
                {
                    new AddCurrency { currencies = { tree.Ch1Records }, amount = 3 },
                    new ResetScope { scope = tree.Tier1Def }
                }
            };
            tree.Rebuild();
            tree.Tier1.balances["fans"] = 60;
            Arm(tree);
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsFalse(tree.Tier1Def.rung.IsOffered(ctx), "the condition holds; the refusal closes it");
            Assert.IsFalse(tree.Tier1Def.rung.TryExecute(ctx));
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"], "no payout");
            Assert.AreEqual((BigNumber)60, tree.Tier1.balances["fans"], "and no clear");

            Dismiss(tree);
            Assert.IsTrue(tree.Tier1Def.rung.IsOffered(ctx));
            Assert.IsTrue(tree.Tier1Def.rung.TryExecute(ctx));
            Assert.AreEqual((BigNumber)3, tree.Ch1.balances["ch1_records"]);
        }

        // ---- 2. event entry ----

        [Test]
        public void An_entry_list_whose_reset_is_refused_cannot_start()
        {
            var tree = new TestTree();
            // The armed reward sits on a tier INSIDE the entry list's reset
            // target, so the host itself is free and occupancy is not the
            // refusal being tested.
            var innerDef = TestTree.MakeTier("inner");
            var encore = TestTree.MakeDefinition<EventDefinition>("encore_set");
            encore.availableWhen = new Always();
            encore.goal = new Always();
            innerDef.events.Add(encore);
            tree.Tier1Def.children.Add(innerDef);
            tree.TimedGig.onEntry.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 2 });
            tree.TimedGig.onEntry.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Rebuild();

            var inner = (TierScopeState)TestNavigation.Node(tree.Root, innerDef);
            inner.activeEvent = new ActiveEvent { eventId = "encore_set", goalReached = true };
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsNull(tree.Tier1.activeEvent, "the host is free - only the entry list refuses");
            Assert.IsFalse(EventSystem.CanStart(ctx, tree.TimedGig));
            Assert.IsFalse(EventSystem.TryStart(ctx, tree.TimedGig));
            Assert.Throws<InvalidOperationException>(() => EventSystem.Start(ctx, tree.TimedGig));
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"], "the entry list never half-ran");
            Assert.IsNull(tree.Tier1.activeEvent, "and no record landed");

            Assert.IsTrue(EventSystem.TryDismiss(new GameContext(inner, tree.Now), encore));
            Assert.IsTrue(EventSystem.CanStart(ctx, tree.TimedGig));
            Assert.IsTrue(EventSystem.TryStart(ctx, tree.TimedGig));
            Assert.AreEqual((BigNumber)2, tree.Ch1.balances["ch1_records"]);
        }

        // ---- 3. event dismissal ----

        // The host's OWN record is armed, and the ending list resets a scope
        // that contains it: dismissal removes the record before either list
        // runs, so the question is asked as if it were already gone. What
        // refuses here is the TIER's reward, not the chapter's own.
        [Test]
        public void An_ending_list_asks_as_if_this_hosts_record_were_already_gone()
        {
            var tree = new TestTree();
            var showcase = TestTree.MakeDefinition<EventDefinition>("showcase");
            showcase.availableWhen = new Always();
            showcase.goal = new Always();
            showcase.rewards.Add(new AddCurrency { currencies = { tree.Roadies }, amount = 1 });
            showcase.onEnd.Add(new ResetScope { scope = tree.Ch1Def });
            tree.Ch1Def.events.Add(showcase);
            tree.Rebuild();

            tree.Ch1.activeEvent = new ActiveEvent { eventId = "showcase", goalReached = true };
            Arm(tree);
            tree.Ch1.balances["ch1_records"] = 9;
            var ctx = tree.Ctx(tree.Ch1);

            Assert.IsFalse(EventSystem.CanDismiss(ctx, showcase), "tier1's armed reward refuses the chapter's clear");
            Assert.IsFalse(EventSystem.TryDismiss(ctx, showcase));
            Assert.AreEqual(BigNumber.Zero, tree.Root.balances["roadies"], "no reward paid");
            Assert.AreEqual((BigNumber)9, tree.Ch1.balances["ch1_records"], "and no clear");
            Assert.IsNotNull(tree.Ch1.activeEvent, "a refused dismissal changes nothing");

            Dismiss(tree);

            // The chapter's own record is still armed, and it does not refuse
            // its own ending - that is `ignoring: host`.
            Assert.IsTrue(EventSystem.CanDismiss(ctx, showcase));
            Assert.IsTrue(EventSystem.TryDismiss(ctx, showcase));
            Assert.AreEqual(BigNumber.One, tree.Root.balances["roadies"]);
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);
        }

        // ---- 4. the sweep ----

        [Test]
        public void A_refused_trigger_is_neither_latched_nor_run_and_fires_on_a_later_pass()
        {
            var tree = new TestTree();
            tree.Tier1Trigger.condition = new Always();
            tree.Tier1Trigger.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 1 });
            tree.Tier1Trigger.actions.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Rebuild();
            Arm(tree);

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"], "not run");
            Assert.IsEmpty(tree.Tier1.firedTriggers, "and not latched, so the refusal costs the trigger nothing");

            Dismiss(tree);
            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            Assert.AreEqual(BigNumber.One, tree.Ch1.balances["ch1_records"], "it fires once the refusal lifts");
        }

        // ---- 5. an upgrade payload ----

        [Test]
        public void An_upgrade_whose_payload_is_refused_cannot_be_bought()
        {
            var tree = new TestTree();
            tree.StagePresence.actions.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Rebuild();
            tree.Ctx(tree.Tier1).Deposit("cash", 300);
            Arm(tree);
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsFalse(Purchasing.CanBuy(ctx, tree.StagePresence), "affordable and gated open; the payload refuses");
            Assert.IsFalse(Purchasing.TryBuy(ctx, tree.StagePresence));
            Assert.AreEqual((BigNumber)300, tree.Tier1.balances["cash"], "nothing was spent");
            Assert.IsEmpty(tree.Tier1.purchasedUpgrades, "and nothing latched");

            Dismiss(tree);
            Assert.IsTrue(Purchasing.CanBuy(ctx, tree.StagePresence));
            Assert.IsTrue(Purchasing.TryBuy(ctx, tree.StagePresence));
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"], "bought, and the payload cleared the tier");
        }

        // ---- 6. a bar completion ----

        [Test]
        public void A_bar_whose_completion_is_refused_is_excluded_from_the_segment()
        {
            var tree = new TestTree();
            tree.Cover1.onComplete.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Rebuild();
            tree.Tier1.balances["rehearsal"] = 1000;
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "cover_1" };
            Arm(tree);

            Segment(tree, 100);

            // Excluded before the fill math: it draws nothing from the pool and
            // its progress does not move.
            Assert.AreEqual((BigNumber)1000, tree.Tier1.balances["rehearsal"], "the pool is untouched");
            Assert.IsFalse(tree.Tier1.barProgress.ContainsKey("cover_1"), "and progress did not move");

            Dismiss(tree);
            tree.Tier1.activeBars["learn_covers"] = new HashSet<string> { "cover_1" };
            Segment(tree, 100);

            // It resumes on the first segment after the refusal lifts: 2/s over
            // 100s is 200, past the 100 threshold, and the completion clears
            // the tier it is homed at.
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["rehearsal"], "the completion reset the tier");
            Assert.IsEmpty(tree.Tier1.barProgress);
        }

        private static void Segment(TestTree tree, double dt)
        {
            var demand = BarSystem.ResolveDemand(tree.Root, tree.Now);
            BarSystem.ConsumeAndSettle(demand, dt, tree.Now.AddSeconds(dt), new TickReport(dt));
        }

        // ---- 7. the runner's own contract ----

        [Test]
        public void A_list_whose_first_action_pays_and_whose_second_is_refused_pays_nothing()
        {
            var tree = new TestTree();
            tree.Tier1Trigger.condition = new Not { condition = new Always() };   // never swept; run by hand
            tree.Tier1Trigger.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 5 });
            tree.Tier1Trigger.actions.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Rebuild();
            tree.Ctx(tree.Tier1).Deposit("cash", 42);
            Arm(tree);
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsFalse(ActionList.TryRun(tree.Tier1Trigger.actions, ctx));
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"],
                "the payout is FIRST in the list and still never happened");
            Assert.AreEqual((BigNumber)42, tree.Tier1.balances["cash"]);
            Assert.Throws<InvalidOperationException>(() => ActionList.Run(tree.Tier1Trigger.actions, ctx));

            Dismiss(tree);
            Assert.IsTrue(ActionList.TryRun(tree.Tier1Trigger.actions, ctx));
            Assert.AreEqual((BigNumber)5, tree.Ch1.balances["ch1_records"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"], "and the whole list ran");
        }

        // ---- 8. a rung named from a list ----

        // ExecuteRung answers with the list it would run, so a refusal nested
        // in the named rung closes the OUTER list - it does not read as a closed
        // gate one level down while the outer list runs "whole" around it. The
        // payout sits first so the all-or-nothing contract is what is tested.
        [Test]
        public void An_outer_list_is_refused_through_the_rung_it_names()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new Always(),
                actions =
                {
                    new AddCurrency { currencies = { tree.Ch1Records }, amount = 3 },
                    new ResetScope { scope = tree.Tier1Def }
                }
            };
            var outer = TestTree.MakeDefinition<TriggerDefinition>("outer");
            outer.condition = new Not { condition = new Always() };   // never swept; run by hand
            outer.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 5 });
            outer.actions.Add(new ExecuteRung { tier = tree.Tier1Def });
            tree.Ch1Def.triggers.Add(outer);
            tree.Rebuild();
            tree.Ctx(tree.Tier1).Deposit("cash", 42);
            Arm(tree);
            var ctx = tree.Ctx(tree.Ch1);

            var refusal = ActionList.Refuses(outer.actions, ctx);
            Assert.IsNotNull(refusal, "the named rung's reset is refused, and the outer list hears it");
            Assert.AreSame(tree.Tier1, refusal.Host);
            Assert.AreEqual("Claim your open_mic reward first", RungFeedback.RefusalText(refusal));
            Assert.IsFalse(ActionList.TryRun(outer.actions, ctx));
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"], "the outer payout never happened");
            Assert.AreEqual((BigNumber)42, tree.Tier1.balances["cash"], "and nothing was cleared");

            Dismiss(tree);
            Assert.IsTrue(ActionList.TryRun(outer.actions, ctx));
            Assert.AreEqual((BigNumber)8, tree.Ch1.balances["ch1_records"], "outer 5, then the rung's 3");
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"], "and the rung's reset ran");

            // An UNMET gate on the named rung is still the ordinary no-op: no
            // refusal, the outer list runs, the rung contributes nothing.
            tree.Tier1Def.rung.offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 };
            Assert.IsNull(ActionList.Refuses(outer.actions, ctx));
            Assert.IsTrue(ActionList.TryRun(outer.actions, ctx));
            Assert.AreEqual((BigNumber)13, tree.Ch1.balances["ch1_records"], "outer 5 only");
        }
    }
}
