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
                currencyIds = { "fans", "records" },
                formula = new RootCurveFormula { currencyId = "fans", divisor = 1, exponent = 1 }
            };

            action.Execute(tree.Ctx(tree.Tier1));

            Assert.AreEqual((BigNumber)100, tree.Tier1.balances["fans"]);
            Assert.AreEqual((BigNumber)50, tree.Root.balances["records"]);
        }

        [Test]
        public void AddCurrency_without_formula_uses_the_constant()
        {
            var tree = new TestTree();

            new AddCurrency { currencyIds = { "roadies" }, amount = 1 }.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.One, tree.Root.balances["roadies"]);
        }

        [Test]
        public void AddModifier_stacking_semantics_come_from_the_definition()
        {
            var tree = new TestTree();
            tree.Defs.Add(TestTree.MakeDefinition<ModifierDefinition>("replace_mod"));
            var linear = TestTree.MakeDefinition<ModifierDefinition>("linear_mod");
            linear.stacking = StackingKind.Linear;
            tree.Defs.Add(linear);
            var ctx = tree.Ctx(tree.Tier1);

            var grantReplace = new AddModifier { scopeId = "ch1", modifierId = "replace_mod" };
            grantReplace.Execute(ctx);
            grantReplace.Execute(ctx);   // re-grant keeps count at 1

            var grantLinear = new AddModifier { scopeId = "ch1", modifierId = "linear_mod" };
            grantLinear.Execute(ctx);
            grantLinear.Execute(ctx);    // re-grant increments

            Assert.AreEqual(1, tree.Ch1.activeModifiers.Find(e => e.modifierId == "replace_mod").count);
            Assert.AreEqual(2, tree.Ch1.activeModifiers.Find(e => e.modifierId == "linear_mod").count);
            Assert.IsEmpty(tree.Tier1.activeModifiers);   // the grant landed on the named ancestor
        }

        [Test]
        public void RemoveModifier_is_the_exact_inverse()
        {
            var tree = new TestTree();
            var linear = TestTree.MakeDefinition<ModifierDefinition>("linear_mod");
            linear.stacking = StackingKind.Linear;
            tree.Defs.Add(linear);
            var ctx = tree.Ctx(tree.Tier1);
            var grant = new AddModifier { scopeId = "ch1", modifierId = "linear_mod" };
            var remove = new RemoveModifier { scopeId = "ch1", modifierId = "linear_mod" };

            remove.Execute(ctx);         // absent: no-op, no error
            grant.Execute(ctx);
            grant.Execute(ctx);
            remove.Execute(ctx);         // one stack down
            Assert.AreEqual(1, tree.Ch1.activeModifiers.Find(e => e.modifierId == "linear_mod").count);
            remove.Execute(ctx);         // entry deleted at zero
            Assert.IsEmpty(tree.Ch1.activeModifiers);
        }

        [Test]
        public void AddModifier_refuses_a_scope_off_the_chain()
        {
            var tree = new TestTree();

            // Grants live outward, never downward.
            Assert.Throws<System.InvalidOperationException>(
                () => new AddModifier { scopeId = "tier1", modifierId = "m" }.Execute(tree.Ctx(tree.Ch1)));
            Assert.IsEmpty(tree.Tier1.activeModifiers);
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

            new ResetScope { scopeId = "tier1" }.Execute(ctx);

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

            new ResetScope { scopeId = "ch1" }.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"]);       // reached downward
            Assert.AreEqual((BigNumber)30, tree.Root.balances["records"]);      // never reached upward
        }

        [Test]
        public void ResetScope_reaches_a_sibling_but_never_an_ancestor()
        {
            var tree = new TestTree();
            var tier2Def = TestTree.MakeScope("tier2");
            tree.Defs.Add(TestTree.DeclareCurrency(tier2Def, "merch"));
            tree.Ch1Def.children.Add(tier2Def);
            var root = ScopeState.Build(tree.RootDef);   // rebuild with the sibling
            var tier1 = root.FindInSubtree("tier1");
            var tier2 = root.FindInSubtree("tier2");
            tier2.balances["merch"] = 5;

            new ResetScope { scopeId = "tier2" }.Execute(new GameContext(tier1, tree.Defs, tree.Now));
            Assert.AreEqual(BigNumber.Zero, tier2.balances["merch"]);

            Assert.Throws<System.InvalidOperationException>(
                () => new ResetScope { scopeId = "ch1" }.Execute(new GameContext(tier1, tree.Defs, tree.Now)));
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
                () => new ResetScope { scopeId = "root" }.Execute(tree.Ctx(tree.Root)));

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
                offerCondition = new CurrencyAtLeast { currencyId = "fans", threshold = 50 },
                actions =
                {
                    new AddCurrency
                    {
                        currencyIds = { "records", "ch1_records" },
                        formula = new RootCurveFormula { currencyId = "fans", divisor = 5, exponent = 0.5 }
                    },
                    new ResetScope { scopeId = "tier1" }
                }
            };
            tree.Tier1.balances["fans"] = 60;

            new ExecuteRung { tierId = "tier1" }.Execute(tree.Ctx(tree.Ch1));

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
                offerCondition = new CurrencyAtLeast { currencyId = "fans", threshold = 50 },
                actions = { new AddCurrency { currencyIds = { "records" }, amount = 99 } }
            };
            tree.Tier1.balances["fans"] = 10;

            new ExecuteRung { tierId = "tier1" }.Execute(tree.Ctx(tree.Ch1));

            Assert.AreEqual(BigNumber.Zero, tree.Root.balances["records"]);     // no payout without the gate
            Assert.AreEqual((BigNumber)10, tree.Tier1.balances["fans"]);        // untouched; a later reset discards
        }

        [Test]
        public void Rung_with_no_authored_gate_never_offers()
        {
            var tree = new TestTree();
            var rung = new Rung { actions = { new AddCurrency { currencyIds = { "records" }, amount = 1 } } };

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
                offerCondition = new CurrencyAtLeast { currencyId = "fans", threshold = 50 },
                actions =
                {
                    new AddCurrency
                    {
                        currencyIds = { "records", "ch1_records" },
                        formula = new RootCurveFormula { currencyId = "fans", divisor = 5, exponent = 0.5 }
                    },
                    new ResetScope { scopeId = "tier1" }
                }
            };
            var capstone = new Rung
            {
                offerCondition = new CurrencyAtLeast { currencyId = "ch1_records", threshold = 30 },
                actions =
                {
                    new ExecuteRung { tierId = "tier1" },
                    new AddCurrency { currencyIds = { "roadies" }, amount = 1 },
                    new SetFlag { flagId = "ch1_complete" },
                    new ResetScope { scopeId = "ch1" }
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
    }
}
