using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class GameActionTests
    {
        // The runner takes a list, so a single action is handed to it as one.
        private static IReadOnlyList<GameAction> Only(params GameAction[] actions) => actions;

        [Test]
        public void AddCurrency_pays_every_target_from_one_evaluation()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 50;
            // The formula reads fans, and fans is ALSO the first target: with one
            // evaluation both targets get 50; per-target re-evaluation would pay
            // records 100 after the first deposit doubles fans.
            var action = new AddCurrency
            {
                currencies = { tree.Fans, tree.Records },
                formula = new RootCurveFormula { currency = tree.Fans, divisor = 1, exponent = 1 }
            };

            action.Execute(tree.Ctx(tree.Tier1));

            Assert.AreEqual((BigNumber)100, tree.Tier1.balances["fans"]);
            Assert.AreEqual((BigNumber)50, tree.Root.balances["records"]);
        }

        // The same tie, seen from the refusal side: one inactive target refuses
        // the whole grant, and the active one is NOT paid. A per-target
        // check-then-write loop would bank records and then throw on fans -
        // the drift the single evaluation exists to prevent, plus a retry that
        // pays records a second time.
        [Test]
        public void AddCurrency_refuses_every_target_when_one_is_inactive()
        {
            var tree = new TestTree();
            tree.Fans.activeWhen = new FlagSet { flagId = "fans_revealed" };
            var action = new AddCurrency { currencies = { tree.Records, tree.Fans }, amount = 5 };

            var thrown = Assert.Throws<InvalidOperationException>(() => action.Execute(tree.Ctx(tree.Tier1)));
            StringAssert.Contains("not active", thrown.Message);
            Assert.AreEqual((BigNumber)0, tree.Root.balances["records"], "the active target stayed unpaid");
            Assert.AreEqual((BigNumber)0, tree.Root.earnedTotals["records"]);

            tree.Tier1.flags.Add("fans_revealed");
            action.Execute(tree.Ctx(tree.Tier1));

            Assert.AreEqual((BigNumber)5, tree.Root.balances["records"]);
            Assert.AreEqual((BigNumber)5, tree.Tier1.balances["fans"]);
        }

        [Test]
        public void AddCurrency_without_formula_uses_the_constant()
        {
            var tree = new TestTree();

            new AddCurrency { currencies = { tree.Roadies }, amount = 1 }.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.One, tree.Root.balances["roadies"]);
        }

        [Test]
        public void AddModifier_stacking_semantics_come_from_the_definition()
        {
            var tree = new TestTree();
            var replaceMod = TestTree.MakeDefinition<ModifierDefinition>("replace_mod");
            var linear = TestTree.MakeDefinition<ModifierDefinition>("linear_mod");
            linear.stacking = StackingKind.Linear;
            tree.Ch1Def.modifiers.AddRange(new[] { replaceMod, linear });
            var ctx = tree.Ctx(tree.Tier1);

            var grantReplace = new AddModifier { scope = tree.Ch1Def, modifier = replaceMod };
            grantReplace.Execute(ctx);
            grantReplace.Execute(ctx);   // re-grant keeps count at 1

            var grantLinear = new AddModifier { scope = tree.Ch1Def, modifier = linear };
            grantLinear.Execute(ctx);
            grantLinear.Execute(ctx);    // re-grant increments

            Assert.AreEqual(1, tree.Ch1.modifierStacks["replace_mod"]);
            Assert.AreEqual(2, tree.Ch1.modifierStacks["linear_mod"]);
            Assert.IsEmpty(tree.Tier1.modifierStacks);   // the grant landed on the named ancestor
        }

        [Test]
        public void RemoveModifier_is_the_exact_inverse()
        {
            var tree = new TestTree();
            var linear = TestTree.MakeDefinition<ModifierDefinition>("linear_mod");
            linear.stacking = StackingKind.Linear;
            tree.Ch1Def.modifiers.Add(linear);
            var ctx = tree.Ctx(tree.Tier1);
            var grant = new AddModifier { scope = tree.Ch1Def, modifier = linear };
            var remove = new RemoveModifier { scope = tree.Ch1Def, modifier = linear };

            remove.Execute(ctx);         // absent: no-op, no error
            grant.Execute(ctx);
            grant.Execute(ctx);
            remove.Execute(ctx);         // one stack down
            Assert.AreEqual(1, tree.Ch1.modifierStacks["linear_mod"]);
            remove.Execute(ctx);         // entry deleted at zero
            Assert.IsEmpty(tree.Ch1.modifierStacks);
        }

        [Test]
        public void AddModifier_refuses_a_scope_off_the_chain()
        {
            var tree = new TestTree();

            // Grants live outward, never downward.
            Assert.Throws<System.InvalidOperationException>(
                () => new AddModifier { scope = tree.Tier1Def, modifier = tree.GjTap1 }.Execute(tree.Ctx(tree.Ch1)));
            Assert.IsEmpty(tree.Tier1.modifierStacks);
        }

        [Test]
        public void ResetScope_clears_everything_and_reinitializes()
        {
            var tree = new TestTree();
            // Authored, not loose: nothing links an action the tree was never
            // built over, and the link is what Execute reads.
            var reset = tree.Author(tree.Tier1Def, new ResetScope { scope = tree.Tier1Def });
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit("cash", 300);
            tree.Tier1.flags.Add("fans_revealed");
            tree.Tier1.firedTriggers.Add("some_trigger");
            tree.Tier1.generatorCounts["drummer"] = 2;
            tree.Tier1.barProgress["cover_1"] = 100;

            reset.Execute(ctx);

            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);       // key kept, value zeroed
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.earnedTotals["cash"]);   // gear region re-hides
            Assert.IsEmpty(tree.Tier1.flags);
            Assert.IsEmpty(tree.Tier1.firedTriggers);                           // triggers re-arm
            Assert.IsEmpty(tree.Tier1.generatorCounts);
            Assert.IsEmpty(tree.Tier1.barProgress);
        }

        [Test]
        public void ResetScope_is_downward_closed()
        {
            var tree = new TestTree();
            var reset = tree.Author(tree.Ch1Def, new ResetScope { scope = tree.Ch1Def });
            tree.Ctx(tree.Tier1).Deposit("cash", 100);
            tree.Ctx(tree.Ch1).Deposit("ch1_records", 30);
            tree.Ctx(tree.Root).Deposit("records", 30);

            reset.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);       // reached downward
            Assert.AreEqual((BigNumber)30, tree.Root.balances["records"]);      // never reached upward
        }

        // Reach is settled once, when the tree is built: the link pass resolves
        // the reference and refuses it there, so a peer or an ancestor never
        // reaches the point of having a link to read.
        [Test]
        public void ResetScope_reaches_what_it_encloses_but_never_a_peer()
        {
            var tree = new TestTree();
            var tier2Def = TestTree.MakeTier("tier2");
            tree.Ch1Def.children.Add(tier2Def);

            // A peer is the parent's to clear, so tier1 cannot reach tier2.
            Assert.Throws<InvalidOperationException>(
                () => tree.Author(tree.Tier1Def, new ResetScope { scope = tier2Def }));
        }

        [Test]
        public void ResetScope_never_reaches_an_ancestor()
        {
            var tree = new TestTree();

            Assert.Throws<InvalidOperationException>(
                () => tree.Author(tree.Tier1Def, new ResetScope { scope = tree.Ch1Def }));
        }

        [Test]
        public void ResetScope_refuses_the_root_even_from_a_root_context()
        {
            var tree = new TestTree();

            // A root-declared trigger is a legitimate root acting context; the
            // refusal is structural (12.12: "never the root"), not reach math -
            // and it lands when the reference is resolved, not when it runs.
            Assert.Throws<InvalidOperationException>(
                () => tree.Author(tree.RootDef, new ResetScope { scope = tree.RootDef }));
        }

        [Test]
        public void Clear_replaces_the_facts_payload_wholesale()
        {
            var tree = new TestTree();
            var oldFacts = tree.Tier1.facts;
            tree.Tier1.flags.Add("fans_revealed");

            tree.Tier1.Clear(tree.Now);

            // Clearing is complete by construction because reset swaps the
            // payload - a field added to ScopeFacts next month is cleared
            // because it is there, with no clear method to forget to update.
            Assert.AreNotSame(oldFacts, tree.Tier1.facts);
            Assert.IsEmpty(tree.Tier1.flags);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);   // declared keys re-initialized
        }

        [Test]
        public void Clear_on_the_root_throws_and_changes_nothing()
        {
            var tree = new TestTree();
            tree.Ctx(tree.Root).Deposit("records", 30);
            var facts = tree.Root.facts;

            // The guard lives on the primitive itself - no caller can bypass it.
            Assert.Throws<System.InvalidOperationException>(() => tree.Root.Clear(tree.Now));

            Assert.AreSame(facts, tree.Root.facts);
            Assert.AreEqual((BigNumber)30, tree.Root.balances["records"]);
        }

        [Test]
        public void ExecuteRung_rebases_to_the_target_rung_scope()
        {
            var tree = new TestTree();
            // The release reads tier-owned fans; the capstone acts in ch1, where
            // fans is unreachable - only the rebase makes this legal (12.4).
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions =
                {
                    new AddCurrency
                    {
                        currencies = { tree.Records, tree.Ch1Records },
                        formula = new RootCurveFormula { currency = tree.Fans, divisor = 5, exponent = 0.5 }
                    },
                    new ResetScope { scope = tree.Tier1Def }
                }
            };
            var fire = tree.Author(tree.Ch1Def, new ExecuteRung { tier = tree.Tier1Def });
            tree.Tier1.balances["fans"] = 60;

            fire.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual((BigNumber)3, tree.Root.balances["records"]);
            Assert.AreEqual((BigNumber)3, tree.Ch1.balances["ch1_records"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);       // the run reset
        }

        [Test]
        public void ExecuteRung_noops_on_an_unmet_gate_and_the_run_is_kept_for_its_reset()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions = { new AddCurrency { currencies = { tree.Records }, amount = 99 } }
            };
            var fire = tree.Author(tree.Ch1Def, new ExecuteRung { tier = tree.Tier1Def });
            tree.Tier1.balances["fans"] = 10;

            fire.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Root.balances["records"]);     // no payout without the gate
            Assert.AreEqual((BigNumber)10, tree.Tier1.balances["fans"]);        // untouched; a later reset discards
        }

        [Test]
        public void Rung_with_no_authored_gate_never_offers()
        {
            var tree = new TestTree();
            var rung = new Rung { actions = { new AddCurrency { currencies = { tree.Records }, amount = 1 } } };

            Assert.IsFalse(rung.IsOffered(tree.Ctx(tree.Tier1)));
            Assert.IsFalse(rung.TryExecute(tree.Ctx(tree.Tier1)));
            Assert.AreEqual(BigNumber.Zero, tree.Root.balances["records"]);
        }

        [Test]
        public void Capstone_sequence_banks_the_run_pays_the_roadie_flags_and_resets()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions =
                {
                    new AddCurrency
                    {
                        currencies = { tree.Records, tree.Ch1Records },
                        formula = new RootCurveFormula { currency = tree.Fans, divisor = 5, exponent = 0.5 }
                    },
                    new ResetScope { scope = tree.Tier1Def }
                }
            };
            var capstone = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Ch1Records, threshold = 30 },
                actions =
                {
                    new ExecuteRung { tier = tree.Tier1Def },
                    new AddCurrency { currencies = { tree.Roadies }, amount = 1 },
                    new SetFlag { flagId = "ch1_complete" },
                    new ResetScope { scope = tree.Ch1Def }
                }
            };
            tree.Ch1Def.rung = capstone;
            tree.Rebuild();   // both rungs were authored after construction
            tree.Ch1.balances["ch1_records"] = 29;
            tree.Tier1.balances["fans"] = 60;

            Assert.IsFalse(capstone.TryExecute(tree.Ctx(tree.Ch1)));            // gate unmet at 29

            tree.Ch1.balances["ch1_records"] = 32;
            Assert.IsTrue(capstone.TryExecute(tree.Ctx(tree.Ch1)));

            Assert.AreEqual((BigNumber)3, tree.Root.balances["records"]);       // the final run banked
            Assert.AreEqual(BigNumber.One, tree.Root.balances["roadies"]);
            Assert.IsTrue(tree.Root.flags.Contains("ch1_complete"));
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);  // same gate every replay
            Assert.IsEmpty(tree.Ch1.flags);                                     // album flag re-walked
        }

        [Test]
        public void RestartScope_banks_through_the_rung_gate_and_then_clears()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions = { new AddCurrency { currencies = { tree.Records }, amount = 3 } }
            };
            var restart = tree.Author(tree.Ch1Def, new RestartScope { scope = tree.Tier1Def });
            tree.Tier1.balances["fans"] = 60;
            tree.Tier1.balances["cash"] = 500;

            restart.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual((BigNumber)3, tree.Root.balances["records"]);       // banked
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);       // then cleared
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);
        }

        [Test]
        public void RestartScope_with_an_unmet_gate_clears_with_nothing_banked()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions = { new AddCurrency { currencies = { tree.Records }, amount = 3 } }
            };
            var restart = tree.Author(tree.Ch1Def, new RestartScope { scope = tree.Tier1Def });
            tree.Tier1.balances["fans"] = 10;

            restart.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Root.balances["records"]);     // gate unmet, no payout
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);       // the clear still ran
        }

        [Test]
        public void RestartScope_on_a_scope_with_no_rung_just_clears()
        {
            var tree = new TestTree();
            var restart = tree.Author(tree.Ch1Def, new RestartScope { scope = tree.Tier1Def });
            tree.Ctx(tree.Tier1).Deposit("cash", 300);

            restart.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);
        }

        [Test]
        public void RestartScope_reaches_what_it_encloses_but_never_a_peer()
        {
            var tree = new TestTree();
            var tier2Def = TestTree.MakeTier("tier2");
            tree.Ch1Def.children.Add(tier2Def);

            Assert.Throws<InvalidOperationException>(
                () => tree.Author(tree.Tier1Def, new RestartScope { scope = tier2Def }));
        }

        [Test]
        public void RestartScope_never_reaches_an_ancestor()
        {
            var tree = new TestTree();

            Assert.Throws<InvalidOperationException>(
                () => tree.Author(tree.Tier1Def, new RestartScope { scope = tree.Ch1Def }));
        }

        [Test]
        public void RestartScope_refuses_the_root_even_from_a_root_context()
        {
            var tree = new TestTree();

            Assert.Throws<InvalidOperationException>(
                () => tree.Author(tree.RootDef, new RestartScope { scope = tree.RootDef }));
        }

        // ---- the link, and the reset's own refusal ----

        // Each of the three acts on the node its LINK names, resolved once when
        // the tree was built. The acting scope holds several candidates below
        // it; nothing searches for one at execution time.
        [Test]
        public void The_three_actions_act_on_the_node_the_link_names()
        {
            var tree = new TestTree();
            var tier2Def = TestTree.MakeTier("tier2");
            TestTree.DeclareCurrency(tier2Def, "merch");
            tree.Ch1Def.children.Add(tier2Def);
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 },
                actions = { new AddCurrency { currencies = { tree.Ch1Records }, amount = 7 } }
            };
            var reset = tree.Author(tree.Ch1Def, new ResetScope { scope = tree.Tier1Def });
            var fire = tree.Author(tree.Ch1Def, new ExecuteRung { tier = tree.Tier1Def });
            var restart = tree.Author(tree.Ch1Def, new RestartScope { scope = tree.Tier1Def });
            var tier2 = TestNavigation.Node(tree.Root, tier2Def);
            var ctx = tree.Ctx(tree.Ch1);

            tree.Tier1.balances["fans"] = 60;
            tier2.balances["merch"] = 5;

            fire.Execute(ctx);
            Assert.AreEqual((BigNumber)7, tree.Ch1.balances["ch1_records"], "the rung the link named ran");
            Assert.AreEqual((BigNumber)60, tree.Tier1.balances["fans"], "and it banked without clearing");

            reset.Execute(ctx);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);
            Assert.AreEqual((BigNumber)5, tier2.balances["merch"], "the peer the link never named");

            tree.Tier1.balances["fans"] = 60;
            restart.Execute(ctx);
            Assert.AreEqual((BigNumber)14, tree.Ch1.balances["ch1_records"], "banked again");
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"], "then cleared");
            Assert.AreEqual((BigNumber)5, tier2.balances["merch"]);
        }

        // 12.5: a scope holding an armed, unclaimed reward refuses to be
        // cleared, judged from its own facts, and the refusal comes back up the
        // walk the clear goes down.
        [Test]
        public void A_reset_over_an_armed_reward_is_refused_and_the_refusal_names_the_event()
        {
            var tree = new TestTree();
            var reset = tree.Author(tree.Ch1Def, new ResetScope { scope = tree.Ch1Def });
            tree.Ctx(tree.Tier1).Deposit("cash", 300);
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", goalReached = true };
            var ctx = tree.Ctx(tree.Ch1);

            var refusal = ActionList.Refuses(Only(reset), ctx);
            Assert.IsNotNull(refusal, "the armed reward below refuses the chapter's clear");
            Assert.AreSame(tree.Tier1, refusal.Host, "the refusing scope is the one holding the record");
            Assert.AreSame(tree.Tier1.activeEvent, refusal.Record);
            Assert.AreSame(tree.OpenMic, refusal.Event, "read from that host's own events list");
            Assert.AreEqual("Claim your open_mic reward first", RungFeedback.RefusalText(refusal));

            Assert.IsFalse(ActionList.TryRun(Only(reset), ctx));
            Assert.AreEqual((BigNumber)300, tree.Tier1.balances["cash"], "a refused list moves nothing");
        }

        // Requirement 7: forced past a refusal, both the runner and a reset run
        // outside any list throw. A reset is as fail-closed alone as in a list.
        [Test]
        public void A_forced_reset_over_an_armed_reward_throws()
        {
            var tree = new TestTree();
            var reset = tree.Author(tree.Tier1Def, new ResetScope { scope = tree.Tier1Def });
            tree.Ctx(tree.Tier1).Deposit("cash", 300);
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", goalReached = true };
            var ctx = tree.Ctx(tree.Tier1);

            Assert.Throws<InvalidOperationException>(() => ActionList.Run(Only(reset), ctx));
            Assert.Throws<InvalidOperationException>(() => reset.Execute(ctx));
            Assert.AreEqual((BigNumber)300, tree.Tier1.balances["cash"]);

            // A record with no latch is not a refusal: only an ARMED reward is.
            tree.Tier1.activeEvent.goalReached = false;
            Assert.IsNull(ActionList.Refuses(Only(reset), ctx));
            reset.Execute(ctx);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);
        }

        [Test]
        public void A_restart_over_an_armed_reward_is_refused_the_same_way()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = new Rung
            {
                offerCondition = new Always(),
                actions = { new AddCurrency { currencies = { tree.Ch1Records }, amount = 3 } }
            };
            var restart = tree.Author(tree.Ch1Def, new RestartScope { scope = tree.Tier1Def });
            tree.Tier1.activeEvent = new ActiveEvent { eventId = "open_mic", goalReached = true };
            var ctx = tree.Ctx(tree.Ch1);

            Assert.IsNotNull(ActionList.Refuses(Only(restart), ctx));
            Assert.IsFalse(ActionList.TryRun(Only(restart), ctx));
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"],
                "the bank half never ran either - a list runs whole or not at all");
            Assert.Throws<InvalidOperationException>(() => restart.Execute(ctx));

            EventSystem.Dismiss(tree.Ctx(tree.Tier1), tree.OpenMic);
            Assert.IsTrue(ActionList.TryRun(Only(restart), ctx));
            Assert.AreEqual((BigNumber)3, tree.Ch1.balances["ch1_records"]);
        }
    }
}
