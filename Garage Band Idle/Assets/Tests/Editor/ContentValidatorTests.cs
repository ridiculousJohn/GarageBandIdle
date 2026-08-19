using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // A finding-free content set shaped like Chapter 1: root/ch1/tier1 plus a
    // rungless sibling tier, the album and capstone rungs, and a trigger
    // granting a modifier. The fixture validating clean is the keystone test;
    // every other test breaks exactly one thing and asserts the finding.
    public class ValidatorFixture
    {
        public readonly FakeDefs Defs = new();
        public readonly ScopeDefinition Root;
        public readonly ScopeDefinition Ch1;
        public readonly ScopeDefinition Tier1;
        public readonly ScopeDefinition Tier1b;
        public readonly Rung Album;
        public readonly Rung Capstone;
        public readonly TriggerDefinition Trigger;
        public readonly ModifierDefinition Boost;

        public ValidatorFixture()
        {
            Root = TestTree.MakeScope("root");
            Ch1 = TestTree.MakeScope("ch1");
            Tier1 = TestTree.MakeScope("tier1");
            Tier1b = TestTree.MakeScope("tier1b");
            Root.children.Add(Ch1);
            Ch1.children.Add(Tier1);
            Ch1.children.Add(Tier1b);

            Root.declaredCurrencyIds.Add("records");
            Root.declaredFlags.Add("ch1_complete");
            Ch1.declaredCurrencyIds.Add("ch1_records");
            Ch1.declaredFlags.Add("album");
            Tier1.declaredCurrencyIds.AddRange(new[] { "cash", "fans" });

            Album = new Rung
            {
                offerCondition = new All
                {
                    conditions =
                    {
                        new CurrencyAtLeast { currencyId = "fans", threshold = 100, uiText = "Need 100 fans" },
                        new BarsCompleted { groupId = "covers", count = 1 },
                    }
                },
                actions =
                {
                    new AddCurrency
                    {
                        currencyIds = { "records", "ch1_records" },
                        formula = new RootCurveFormula { currencyId = "fans", divisor = 5, exponent = 0.5 },
                    },
                    new SetFlag { flagId = "album" },
                    new ResetScope { scopeId = "tier1" },
                }
            };
            Tier1.rung = Album;

            Capstone = new Rung
            {
                offerCondition = new CurrencyAtLeast { currencyId = "ch1_records", threshold = 30 },
                actions =
                {
                    new ExecuteRung { tierId = "tier1" }, // cash the album before the reset, like the authored capstone
                    new AddCurrency { currencyIds = { "records" }, amount = 1 },
                    new SetFlag { flagId = "ch1_complete" },
                    new ResetScope { scopeId = "ch1" },
                }
            };
            Ch1.rung = Capstone;

            Boost = TestTree.MakeDefinition<ModifierDefinition>("boost");
            Boost.effects.Add(new Effect { target = "cash", multiplier = 2 });
            Boost.effects.Add(new Effect { target = "income", multiplier = 1.5 });

            Trigger = TestTree.MakeDefinition<TriggerDefinition>("boost_trigger");
            Trigger.condition = new FlagSet { flagId = "album" };
            Trigger.actions.Add(new AddModifier { scopeId = "tier1", modifierId = "boost" });
            Trigger.actions.Add(new RemoveModifier { scopeId = "tier1", modifierId = "boost" });
            Tier1.triggers.Add(Trigger);

            Defs.Add(Root).Add(Ch1).Add(Tier1).Add(Tier1b)
                .Add(TestTree.MakeDefinition<CurrencyDefinition>("records"))
                .Add(TestTree.MakeDefinition<CurrencyDefinition>("ch1_records"))
                .Add(TestTree.MakeDefinition<CurrencyDefinition>("cash", "income"))
                .Add(TestTree.MakeDefinition<CurrencyDefinition>("fans"))
                .Add(TestTree.MakeDefinition<BarGroupDefinition>("covers"))
                .Add(Boost)
                .Add(Trigger);
        }

        public ValidationReport Run() => ContentValidator.Validate(Defs);
    }

    public class ContentValidatorTests
    {
        private static string Dump(ValidationReport report) =>
            report.Findings.Count == 0 ? "(no findings)" : string.Join("\n", report.Findings);

        private static void AssertFinding(ValidationReport report, ValidationSeverity severity, ValidationCheck check, string fragment)
        {
            Assert.IsTrue(
                report.Findings.Any(f => f.Severity == severity && f.Check == check && f.Message.Contains(fragment)),
                $"expected [{severity}] {check} containing '{fragment}'; got:\n{Dump(report)}");
        }

        private static void AssertNoFinding(ValidationReport report, ValidationCheck check)
        {
            Assert.IsFalse(report.OfCheck(check).Any(),
                $"expected no {check} findings; got:\n{Dump(report)}");
        }

        private static void AssertClean(ValidationReport report) =>
            Assert.AreEqual(0, report.Findings.Count, Dump(report));

        // ---- keystone ----

        [Test]
        public void ValidFixture_NoFindings() => AssertClean(new ValidatorFixture().Run());

        // ---- id space ----

        [Test]
        public void DuplicateDefinitionId_Error()
        {
            var f = new ValidatorFixture();
            f.Defs.Add(TestTree.MakeDefinition<CurrencyDefinition>("cash"));
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateId, "'cash'");
        }

        [Test]
        public void FlagCollidingWithDefinitionId_Error()
        {
            var f = new ValidatorFixture();
            f.Ch1.declaredFlags.Add("cash");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateId, "flag declared at scope 'ch1'");
        }

        [Test]
        public void FlagDeclaredInTwoScopes_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1.declaredFlags.Add("album");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateHome, "flag 'album'");
        }

        [Test]
        public void CurrencyDeclaredInTwoScopes_Error()
        {
            var f = new ValidatorFixture();
            f.Ch1.declaredCurrencyIds.Add("cash");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateHome, "a currency has one home");
        }

        [Test]
        public void TriggerDeclaredInTwoScopes_Error()
        {
            var f = new ValidatorFixture();
            f.Ch1.triggers.Add(f.Trigger);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateHome, "a trigger has one home");
        }

        [Test]
        public void TagCollidingWithId_Error()
        {
            var f = new ValidatorFixture();
            f.Defs.Add(TestTree.MakeDefinition<CurrencyDefinition>("extra", "cash"));
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.TagIdCollision, "tag 'cash'");
        }

        // ---- scope graph ----

        [Test]
        public void TwoRoots_Error()
        {
            var f = new ValidatorFixture();
            f.Defs.Add(TestTree.MakeScope("stray"));
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeGraph, "multiple root scopes");
        }

        [Test]
        public void ScopeUnderTwoParents_Error()
        {
            var f = new ValidatorFixture();
            f.Root.children.Add(f.Tier1);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeGraph, "child of both");
        }

        [Test]
        public void UnreachableScopes_Error()
        {
            var f = new ValidatorFixture();
            var a = TestTree.MakeScope("a");
            var b = TestTree.MakeScope("b");
            a.children.Add(b);
            b.children.Add(a); // a children cycle, detached from the tree
            f.Defs.Add(a).Add(b);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeGraph, "not reachable");
        }

        [Test]
        public void UnknownDeclaredCurrency_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1.declaredCurrencyIds.Add("ghost");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "has no CurrencyDefinition");
        }

        [Test]
        public void RungOnRoot_Error()
        {
            var f = new ValidatorFixture();
            f.Root.rung = new Rung();
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.RungOnRoot, "'root'");
        }

        // ---- ResetScope reach ----

        [Test]
        public void ResetScope_AncestorTarget_Error()
        {
            var f = new ValidatorFixture();
            ((ResetScope)f.Album.actions[2]).scopeId = "ch1";
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "none of these from 'tier1'");
        }

        [Test]
        public void ResetScope_RootTarget_Error()
        {
            var f = new ValidatorFixture();
            ((ResetScope)f.Capstone.actions[3]).scopeId = "root";
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "never resettable");
        }

        [Test]
        public void ResetScope_UnrelatedSubtree_Error()
        {
            var f = new ValidatorFixture();
            var ch2 = TestTree.MakeScope("ch2");
            var tier2 = TestTree.MakeScope("tier2");
            ch2.children.Add(tier2);
            f.Root.children.Add(ch2);
            f.Defs.Add(ch2).Add(tier2);
            ((ResetScope)f.Album.actions[2]).scopeId = "tier2";
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "none of these from 'tier1'");
        }

        [Test]
        public void ResetScope_SiblingTarget_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Add(new ResetScope { scopeId = "tier1b" });
            AssertClean(f.Run());
        }

        [Test]
        public void ResetScope_UnknownScope_Error()
        {
            var f = new ValidatorFixture();
            ((ResetScope)f.Album.actions[2]).scopeId = "ghost";
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "unknown scope 'ghost'");
        }

        // ---- ExecuteRung reach and cycles ----

        [Test]
        public void ExecuteRung_OutsideSubtree_Error()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new ExecuteRung { tierId = "ch1" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "outside 'tier1'");
        }

        [Test]
        public void ExecuteRung_TargetWithoutRung_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.Add(new ExecuteRung { tierId = "tier1b" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "declares no rung");
        }

        [Test]
        public void ExecuteRung_SelfInvocation_CycleError()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Add(new ExecuteRung { tierId = "tier1" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ReferenceCycle, "rung invocation cycle");
        }

        [Test]
        public void ExecuteRung_DownwardChain_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.Add(new ExecuteRung { tierId = "tier1" });
            AssertClean(f.Run());
        }

        // ---- modifier grants ----

        [Test]
        public void AddModifier_UnknownModifier_Error()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new AddModifier { scopeId = "tier1", modifierId = "ghost" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "unknown modifier 'ghost'");
        }

        [Test]
        public void AddModifier_OffChainTarget_Error()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new AddModifier { scopeId = "tier1b", modifierId = "boost" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "grants live outward");
        }

        [Test]
        public void AddModifier_AncestorTarget_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new AddModifier { scopeId = "ch1", modifierId = "boost" });
            AssertClean(f.Run());
        }

        [Test]
        public void RemoveModifier_NothingGrantsThere_Warning()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Clear();
            f.Trigger.actions.Add(new RemoveModifier { scopeId = "tier1", modifierId = "boost" });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.RemoveWithoutGrant, "nothing grants it");
        }

        // ---- flags ----

        [Test]
        public void SetFlag_UndeclaredFlag_Error()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Add(new SetFlag { flagId = "ghost" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "flag 'ghost'");
        }

        [Test]
        public void SetFlag_CrossTreeHome_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1b.declaredFlags.Add("side");
            f.Album.actions.Add(new SetFlag { flagId = "side" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "flag 'side'");
        }

        [Test]
        public void Flag_NoSetter_Warning()
        {
            var f = new ValidatorFixture();
            f.Ch1.declaredFlags.Add("lonely");
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.FlagNoSetter, "flag 'lonely'");
        }

        // A setter acting from an ancestor of the flag's home writes outward and
        // never reaches it - the per-flag warn, not a per-site error (12.12).
        [Test]
        public void Flag_SettersMoreDurable_Warning()
        {
            var f = new ValidatorFixture();
            f.Tier1.declaredFlags.Add("deep");
            f.Capstone.actions.Add(new SetFlag { flagId = "deep" });
            var report = f.Run();
            AssertFinding(report, ValidationSeverity.Warning, ValidationCheck.FlagSettersMoreDurable, "flag 'deep'");
            AssertNoFinding(report, ValidationCheck.ChainReach);
        }

        // ---- ordinary reads and writes address only the acting chain ----

        [Test]
        public void AddCurrency_HomeBelowActing_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.Add(new AddCurrency { currencyIds = { "cash" }, amount = 5 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "AddCurrency");
        }

        [Test]
        public void CurrencyCondition_HomeBelowActing_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.offerCondition = new CurrencyAtLeast { currencyId = "cash", threshold = 1 };
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "CurrencyAtLeast");
        }

        [Test]
        public void CurrencyCondition_UnknownCurrency_Error()
        {
            var f = new ValidatorFixture();
            f.Album.offerCondition = new CurrencyAtLeast { currencyId = "ghost", threshold = 1 };
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "unknown currency 'ghost'");
        }

        [Test]
        public void BarsCompleted_UnknownGroup_Error()
        {
            var f = new ValidatorFixture();
            ((BarsCompleted)((All)f.Album.offerCondition).conditions[1]).groupId = "ghost";
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "unknown bar group 'ghost'");
        }

        [Test]
        public void RootCurve_HomeOffActingChain_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions[1] = new AddCurrency
            {
                currencyIds = { "records" },
                formula = new RootCurveFormula { currencyId = "cash", divisor = 1, exponent = 1 },
            };
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "RootCurveFormula");
        }

        [Test]
        public void RootCurve_UnknownCurrency_Error()
        {
            var f = new ValidatorFixture();
            ((RootCurveFormula)((AddCurrency)f.Album.actions[0]).formula).currencyId = "ghost";
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "unknown currency 'ghost'");
        }

        // ---- list-order checks ----

        [Test]
        public void SetThenWiped_FlagInsideResetClosure_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1.declaredFlags.Add("temp");
            f.Album.actions.Insert(2, new SetFlag { flagId = "temp" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.SetThenWiped, "flag 'temp'");
        }

        [Test]
        public void SetThenWiped_CurrencyInsideResetClosure_Error()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Insert(0, new AddCurrency { currencyIds = { "cash" }, amount = 10 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.SetThenWiped, "currency 'cash'");
        }

        [Test]
        public void StrandedValue_ResetOverUninvokedPayoutRung_Warning()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.RemoveAt(0); // drop the ExecuteRung(tier1)
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.StrandedValue, "payout rung at 'tier1'");
        }

        [Test]
        public void StrandedValue_RungAfterReset_StillWarns()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.RemoveAt(0);
            f.Capstone.actions.Add(new ExecuteRung { tierId = "tier1" }); // after the reset - too late
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.StrandedValue, "payout rung at 'tier1'");
        }

        // A nested ladder cashes transitively: the capstone rungs the album,
        // whose own list rungs the inner payout - nothing is stranded even
        // though the capstone never names the inner rung directly.
        [Test]
        public void StrandedValue_NestedLadder_TransitiveRung_NoFindings()
        {
            var f = new ValidatorFixture();
            var inner = TestTree.MakeScope("tier_inner");
            inner.rung = new Rung
            {
                offerCondition = new CurrencyAtLeast { currencyId = "fans", threshold = 1 },
                actions = { new AddCurrency { currencyIds = { "records" }, amount = 1 } },
            };
            f.Tier1.children.Add(inner);
            f.Defs.Add(inner);
            f.Album.actions.Insert(0, new ExecuteRung { tierId = "tier_inner" });
            AssertClean(f.Run());
        }

        [Test]
        public void FormulaAfterReset_ReadsZeros_Warning()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Clear();
            f.Album.actions.Add(new ResetScope { scopeId = "tier1" });
            f.Album.actions.Add(new AddCurrency
            {
                currencyIds = { "records", "ch1_records" },
                formula = new RootCurveFormula { currencyId = "fans", divisor = 5, exponent = 0.5 },
            });
            f.Album.actions.Add(new SetFlag { flagId = "album" });
            var report = f.Run();
            AssertFinding(report, ValidationSeverity.Warning, ValidationCheck.FormulaReadsCleared, "reads zeros");
            AssertNoFinding(report, ValidationCheck.SetThenWiped);
        }

        // ---- effects ----

        [Test]
        public void EffectReach_GrantBelowCurrencyHome_Error()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "ch1_records", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.EffectReach, "'ch1_records'");
        }

        [Test]
        public void EffectTag_NoMemberInGrantSubtree_Warning()
        {
            var f = new ValidatorFixture();
            f.Defs.Add(TestTree.MakeDefinition<CurrencyDefinition>("merch", "gear"));
            f.Root.declaredCurrencyIds.Add("merch");
            f.Boost.effects.Add(new Effect { target = "gear", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "matches no member within 'tier1'");
        }

        // A tag living only on a scope or trigger is vocabulary, not a target -
        // no multiplier ever resolves against those kinds.
        [Test]
        public void EffectTag_OnlyOnNonTargetableKinds_Warning()
        {
            var f = new ValidatorFixture();
            f.Tier1.EditorInit("tier1", "gearish"); // the tag exists, but only on the scope itself
            f.Boost.effects.Add(new Effect { target = "gearish", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "matches no member within 'tier1'");
        }

        [Test]
        public void EffectTarget_MatchesNothing_Warning()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "nonsense", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "matches no id and no tag");
        }

        [Test]
        public void EffectTarget_WrongDefinitionKind_Warning()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "tier1", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "not an effect target kind");
        }

        [Test]
        public void Effect_UnknownNarrowingCurrency_Error()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "cash", currencyId = "ghost", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "narrows to unknown currency 'ghost'");
        }

        // Reference resolution is unconditional - a modifier nothing grants
        // still has every reference checked.
        [Test]
        public void UngrantedModifier_ReferencesStillValidated()
        {
            var f = new ValidatorFixture();
            var orphan = TestTree.MakeDefinition<ModifierDefinition>("orphan");
            orphan.effects.Add(new Effect { target = "nonsense", multiplier = 2 });
            orphan.effects.Add(new Effect { target = "cash", currencyId = "ghost", multiplier = 2 });
            f.Defs.Add(orphan);
            var report = f.Run();
            AssertFinding(report, ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "modifier 'orphan' effects[0]");
            AssertFinding(report, ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "narrows to unknown currency 'ghost'");
        }

        // ---- null slots ----

        [Test]
        public void NullListEntries_Error()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Add(null);
            ((All)f.Album.offerCondition).conditions.Add(null);
            f.Ch1.children.Add(null);
            f.Tier1.triggers.Add(null);
            var report = f.Run();
            AssertFinding(report, ValidationSeverity.Error, ValidationCheck.NullEntry, "null action entry");
            AssertFinding(report, ValidationSeverity.Error, ValidationCheck.NullEntry, "All has a null conditions[2]");
            AssertFinding(report, ValidationSeverity.Error, ValidationCheck.NullEntry, "children[2] is null");
            AssertFinding(report, ValidationSeverity.Error, ValidationCheck.NullEntry, "triggers[1] is null");
        }

        [Test]
        public void Not_NullOperand_Error()
        {
            var f = new ValidatorFixture();
            f.Trigger.condition = new Not();
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "Not has no operand");
        }
    }
}
