using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class GameActionTests
    {
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
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit("cash", 300);
            tree.Tier1.flags.Add("fans_revealed");
            tree.Tier1.firedTriggers.Add("some_trigger");
            tree.Tier1.generatorCounts["drummer"] = 2;
            tree.Tier1.barProgress["cover_1"] = 100;

            new ResetScope { scope = tree.Tier1Def }.Execute(ctx);

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
            tree.Ctx(tree.Tier1).Deposit("cash", 100);
            tree.Ctx(tree.Ch1).Deposit("ch1_records", 30);
            tree.Ctx(tree.Root).Deposit("records", 30);

            new ResetScope { scope = tree.Ch1Def }.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);       // reached downward
            Assert.AreEqual((BigNumber)30, tree.Root.balances["records"]);      // never reached upward
        }

        [Test]
        public void ResetScope_reaches_what_it_encloses_but_never_a_peer_or_an_ancestor()
        {
            var tree = new TestTree();
            var tier2Def = TestTree.MakeTier("tier2");
            tree.Ch1Def.children.Add(tier2Def);
            var root = ScopeState.Build(tree.Content);   // rebuild with the sibling
            var tier1 = root.FindInSubtree(tree.Tier1Def);
            var tier2 = root.FindInSubtree(tier2Def);
            tier2.balances["merch"] = 5;

            // A peer is the parent's to clear, so tier1 cannot reach tier2.
            Assert.Throws<System.InvalidOperationException>(
                () => new ResetScope { scope = tier2Def }.Execute(new GameContext(tier1, tree.Now)));
            Assert.AreEqual((BigNumber)5, tier2.balances["merch"]);

            Assert.Throws<System.InvalidOperationException>(
                () => new ResetScope { scope = tree.Ch1Def }.Execute(new GameContext(tier1, tree.Now)));
        }

        [Test]
        public void ResetScope_refuses_the_root_even_from_a_root_context()
        {
            var tree = new TestTree();
            tree.Ctx(tree.Root).Deposit("records", 30);
            tree.Root.flags.Add("ch1_complete");

            // A root-declared trigger is a legitimate root acting context; the
            // refusal is structural (12.12: "never the root"), not reach math.
            Assert.Throws<System.InvalidOperationException>(
                () => new ResetScope { scope = tree.RootDef }.Execute(tree.Ctx(tree.Root)));

            Assert.AreEqual((BigNumber)30, tree.Root.balances["records"]);
            Assert.IsTrue(tree.Root.flags.Contains("ch1_complete"));
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
            tree.Tier1.balances["fans"] = 60;

            new ExecuteRung { tier = tree.Tier1Def }.Execute(tree.Ctx(tree.Ch1));

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
            tree.Tier1.balances["fans"] = 10;

            new ExecuteRung { tier = tree.Tier1Def }.Execute(tree.Ctx(tree.Ch1));

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
            tree.Tier1.balances["fans"] = 60;
            tree.Tier1.balances["cash"] = 500;

            new RestartScope { scope = tree.Tier1Def }.Execute(tree.Ctx(tree.Ch1));

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
            tree.Tier1.balances["fans"] = 10;

            new RestartScope { scope = tree.Tier1Def }.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Root.balances["records"]);     // gate unmet, no payout
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);       // the clear still ran
        }

        [Test]
        public void RestartScope_on_a_scope_with_no_rung_just_clears()
        {
            var tree = new TestTree();
            tree.Ctx(tree.Tier1).Deposit("cash", 300);

            new RestartScope { scope = tree.Tier1Def }.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);
        }

        [Test]
        public void RestartScope_reaches_what_it_encloses_but_never_a_peer_or_an_ancestor()
        {
            var tree = new TestTree();
            var tier2Def = TestTree.MakeTier("tier2");
            tree.Ch1Def.children.Add(tier2Def);
            var root = ScopeState.Build(tree.Content);   // rebuild with the sibling
            var tier1 = root.FindInSubtree(tree.Tier1Def);
            var tier2 = root.FindInSubtree(tier2Def);
            tier2.balances["merch"] = 5;

            Assert.Throws<System.InvalidOperationException>(
                () => new RestartScope { scope = tier2Def }.Execute(new GameContext(tier1, tree.Now)));
            Assert.AreEqual((BigNumber)5, tier2.balances["merch"]);

            Assert.Throws<System.InvalidOperationException>(
                () => new RestartScope { scope = tree.Ch1Def }.Execute(new GameContext(tier1, tree.Now)));
        }

        [Test]
        public void RestartScope_refuses_the_root_even_from_a_root_context()
        {
            var tree = new TestTree();
            tree.Ctx(tree.Root).Deposit("records", 30);

            Assert.Throws<System.InvalidOperationException>(
                () => new RestartScope { scope = tree.RootDef }.Execute(tree.Ctx(tree.Root)));

            Assert.AreEqual((BigNumber)30, tree.Root.balances["records"]);
        }
    }
}
