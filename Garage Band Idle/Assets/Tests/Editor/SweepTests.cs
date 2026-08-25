using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class SweepTests
    {
        [Test]
        public void A_null_trigger_condition_never_fires()
        {
            var tree = new TestTree();
            // Tier1Trigger ships with no condition: closed, never dereferenced.
            tree.Tier1Trigger.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 99 });

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);
            Assert.IsEmpty(tree.Tier1.firedTriggers);
        }

        [Test]
        public void A_trigger_fires_once_per_scope_life_and_a_reset_rearms_it()
        {
            var tree = new TestTree();
            tree.Tier1Trigger.condition = new Always();
            tree.Tier1Trigger.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 1 });

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);
            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            Assert.AreEqual(BigNumber.One, tree.Ch1.balances["ch1_records"]);   // the latch held the second pass
            Assert.IsTrue(tree.Tier1.firedTriggers.Contains("tier1_trigger"));

            new ResetScope { scope = tree.Tier1Def }.Execute(tree.Ctx(tree.Tier1));
            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            Assert.AreEqual((BigNumber)2, tree.Ch1.balances["ch1_records"]);    // the fresh life re-armed it
        }

        [Test]
        public void A_self_resetting_trigger_rearms_for_the_next_life()
        {
            var tree = new TestTree();
            tree.Tier1Trigger.condition = new Always();
            tree.Tier1Trigger.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 1 });
            tree.Tier1Trigger.actions.Add(new ResetScope { scope = tree.Tier1Def });

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            // Latch-first: the reset wiped the just-written latch with the
            // payload, and one pass still runs the list exactly once, because
            // collection closed before anything executed.
            Assert.AreEqual(BigNumber.One, tree.Ch1.balances["ch1_records"]);
            Assert.IsEmpty(tree.Tier1.firedTriggers);

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);
            Assert.AreEqual((BigNumber)2, tree.Ch1.balances["ch1_records"]);
        }

        [Test]
        public void Triggers_execute_in_tree_order_then_declaration_order()
        {
            var tree = new TestTree();
            // ch1 pays 10 first (parent before child); tier1's pair then
            // doubles and adds 5 in declaration order: (0+10)*2+5 = 25. Any
            // other order lands on a different number.
            var parentPay = TestTree.MakeDefinition<TriggerDefinition>("parent_pay");
            parentPay.condition = new Always();
            parentPay.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 10 });
            tree.Ch1Def.triggers.Add(parentPay);

            var doubler = TestTree.MakeDefinition<TriggerDefinition>("doubler");
            doubler.condition = new Always();
            doubler.actions.Add(new AddCurrency
            {
                currencies = { tree.Ch1Records },
                formula = new RootCurveFormula { currency = tree.Ch1Records, divisor = 1, exponent = 1 }
            });
            var adder = TestTree.MakeDefinition<TriggerDefinition>("adder");
            adder.condition = new Always();
            adder.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 5 });
            tree.Tier1Def.triggers.AddRange(new[] { doubler, adder });

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            Assert.AreEqual((BigNumber)25, tree.Ch1.balances["ch1_records"]);
        }

        [Test]
        public void A_trigger_armed_by_an_earlier_trigger_waits_for_the_next_sweep()
        {
            var tree = new TestTree();
            var armer = TestTree.MakeDefinition<TriggerDefinition>("armer");
            armer.condition = new Always();
            armer.actions.Add(new SetFlag { flagId = "gj1_done" });
            var armed = TestTree.MakeDefinition<TriggerDefinition>("armed");
            armed.condition = new FlagSet { flagId = "gj1_done" };
            armed.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 1 });
            tree.Ch1Def.triggers.AddRange(new[] { armer, armed });

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            // Eligibility was judged before anything ran: the armer fired, the
            // armed one waits for the next pass.
            Assert.IsTrue(tree.Ch1.flags.Contains("gj1_done"));
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);
            Assert.AreEqual(BigNumber.One, tree.Ch1.balances["ch1_records"]);
        }

        [Test]
        public void A_reset_mid_sweep_invalidates_the_rest_of_that_scope_life()
        {
            var tree = new TestTree();
            // The parent's trigger resets tier1 after collection admitted
            // tier1's own: the payload identity no longer matches, so that
            // entry is skipped - not latched, not run - and fires on the next
            // pass in its fresh life.
            var wiper = TestTree.MakeDefinition<TriggerDefinition>("wiper");
            wiper.condition = new Always();
            wiper.actions.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Ch1Def.triggers.Add(wiper);

            tree.Tier1Trigger.condition = new Always();
            tree.Tier1Trigger.actions.Add(new AddCurrency { currencies = { tree.Ch1Records }, amount = 1 });

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);   // skipped, not run
            Assert.IsEmpty(tree.Tier1.firedTriggers);                            // and not latched

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);
            Assert.AreEqual(BigNumber.One, tree.Ch1.balances["ch1_records"]);    // the wiper stays latched; tier1 fires
        }

        [Test]
        public void A_dormant_chapter_does_not_sweep()
        {
            var tree = new TestTree();
            var ch2Def = TestTree.MakeChapter("ch2");
            ch2Def.declaredFlags.Add("ch2_flag");
            var lurker = TestTree.MakeDefinition<TriggerDefinition>("lurker");
            lurker.condition = new Always();
            lurker.actions.Add(new SetFlag { flagId = "ch2_flag" });
            ch2Def.triggers.Add(lurker);
            tree.RootDef.children.Add(ch2Def);
            var root = ScopeState.Build(tree.RootDef);   // rebuild with the sibling
            var ch1 = (ChapterScopeState)root.FindInSubtree(tree.Ch1Def);
            var ch2 = (ChapterScopeState)root.FindInSubtree(ch2Def);

            Sweep.Run(root, ch1, tree.Now);

            // A threshold crossed while away fires on the first live sweep
            // after switch-in, not before.
            Assert.IsEmpty(ch2.flags);
            Assert.IsEmpty(ch2.firedTriggers);

            Sweep.Run(root, ch2, tree.Now);
            Assert.IsTrue(ch2.flags.Contains("ch2_flag"));
        }

        [Test]
        public void Root_sweeps_even_with_no_foreground_chapter()
        {
            var tree = new TestTree();
            var rootTrigger = TestTree.MakeDefinition<TriggerDefinition>("root_trigger");
            rootTrigger.condition = new Always();
            rootTrigger.actions.Add(new SetFlag { flagId = "ch1_complete" });
            tree.RootDef.triggers.Add(rootTrigger);

            Sweep.Run(tree.Root, null, tree.Now);

            Assert.IsTrue(tree.Root.flags.Contains("ch1_complete"));
            Assert.IsTrue(tree.Root.firedTriggers.Contains("root_trigger"));
        }

        [Test]
        public void The_goal_latch_lands_before_any_trigger_runs()
        {
            var tree = new TestTree();
            // The goal holds at sweep start but nothing has latched it yet; the
            // trigger's gate reads the armed reward. Latch-before-collect is
            // what lets it fire in the same pass the goal lands.
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic" };
            tree.Ctx(tree.Tier1).Deposit("fans", 50);
            var herald = TestTree.MakeDefinition<TriggerDefinition>("herald");
            herald.condition = new EventRewardPending { host = tree.Tier1Def };
            herald.actions.Add(new SetFlag { flagId = "gj1_done" });
            tree.Ch1Def.triggers.Add(herald);

            Sweep.Run(tree.Root, tree.Ch1, tree.Now);

            Assert.IsTrue(tree.Tier1.activeEvent.goalReached);
            Assert.IsTrue(tree.Ch1.flags.Contains("gj1_done"));
        }
    }
}
