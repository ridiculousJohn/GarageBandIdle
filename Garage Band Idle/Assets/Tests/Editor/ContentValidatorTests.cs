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
        public readonly ScopeDefinition Root;
        public readonly ScopeDefinition Ch1;
        public readonly ScopeDefinition Tier1;
        public readonly ScopeDefinition Tier1b;
        public readonly Rung Album;
        public readonly Rung Capstone;
        public readonly TriggerDefinition Trigger;
        public readonly ModifierDefinition Boost;
        public readonly ProducerDefinition Tap;
        public readonly GeneratorDefinition Amp;
        public readonly UpgradeDefinition StagePresence;
        public readonly UpgradeDefinition AmpStrings;
        public readonly CareerEffectDefinition RecordsIncome;
        public readonly CurrencyDefinition Cash;
        public readonly CurrencyDefinition Fans;
        public readonly CurrencyDefinition Records;
        public readonly CurrencyDefinition Ch1Records;
        public readonly BarGroupDefinition Covers;

        public ValidatorFixture()
        {
            Root = TestTree.MakeScope("root");
            Ch1 = TestTree.MakeScope("ch1");
            Tier1 = TestTree.MakeScope("tier1");
            Tier1b = TestTree.MakeScope("tier1b");
            Root.children.Add(Ch1);
            Ch1.children.Add(Tier1);
            Ch1.children.Add(Tier1b);

            Records = TestTree.DeclareCurrency(Root, "records");
            Root.declaredFlags.Add("ch1_complete");
            Ch1Records = TestTree.DeclareCurrency(Ch1, "ch1_records");
            Ch1.declaredFlags.Add("album");
            Cash = TestTree.DeclareCurrency(Tier1, "cash", "income");
            Fans = TestTree.DeclareCurrency(Tier1, "fans");

            Covers = TestTree.MakeDefinition<BarGroupDefinition>("covers");
            Tier1.barGroups.Add(Covers);

            Album = new Rung
            {
                offerCondition = new All
                {
                    conditions =
                    {
                        new CurrencyAtLeast { currency = Fans, threshold = 100, uiText = "Need 100 fans" },
                        new BarsCompleted { group = Covers, count = 1 },
                    }
                },
                actions =
                {
                    new AddCurrency
                    {
                        currencies = { Records, Ch1Records },
                        formula = new RootCurveFormula { currency = Fans, divisor = 5, exponent = 0.5 },
                    },
                    new SetFlag { flagId = "album" },
                    new ResetScope { scope = Tier1 },
                }
            };
            Tier1.rung = Album;

            Capstone = new Rung
            {
                offerCondition = new CurrencyAtLeast { currency = Ch1Records, threshold = 30 },
                actions =
                {
                    new ExecuteRung { tier = Tier1 }, // cash the album before the reset, like the authored capstone
                    new AddCurrency { currencies = { Records }, amount = 1 },
                    new SetFlag { flagId = "ch1_complete" },
                    new ResetScope { scope = Ch1 },
                }
            };
            Ch1.rung = Capstone;

            Boost = TestTree.MakeDefinition<ModifierDefinition>("boost");
            Boost.effects.Add(new Effect { target = "cash", multiplier = 2 });
            Boost.effects.Add(new Effect { target = "income", multiplier = 1.5 });

            Trigger = TestTree.MakeDefinition<TriggerDefinition>("boost_trigger");
            Trigger.condition = new FlagSet { flagId = "album" };
            Trigger.actions.Add(new AddModifier { scope = Tier1, modifier = Boost });
            Trigger.actions.Add(new RemoveModifier { scope = Tier1, modifier = Boost });
            Tier1.triggers.Add(Trigger);

            // The economy declarations: a producer reading an upgrade latch, a
            // tagged generator with a cost curve, an upgrade whose effect targets
            // that generator, and the career effect on the income tag.
            Tap = TestTree.MakeDefinition<ProducerDefinition>("tap_producer");
            Tap.produces.Add(TestTree.Entry(Cash, Stat.Yield, 1));
            Tier1.producers.Add(Tap);

            Amp = TestTree.MakeDefinition<GeneratorDefinition>("practice_amp", "gear");
            Amp.availableWhen = new EarnedTotalAtLeast { currency = Cash, threshold = 100 };
            Amp.costCurrency = Cash;
            Amp.baseCost = 60;
            Amp.growth = 1.15;
            Amp.produces.Add(TestTree.Entry(Cash, Stat.Rate, 0.5));
            Tier1.generators.Add(Amp);

            StagePresence = TestTree.MakeDefinition<UpgradeDefinition>("stage_presence");
            StagePresence.gate = new EarnedTotalAtLeast { currency = Cash, threshold = 250 };
            StagePresence.costCurrency = Cash;
            StagePresence.cost = 250;

            AmpStrings = TestTree.MakeDefinition<UpgradeDefinition>("amp_strings");
            AmpStrings.gate = new EarnedTotalAtLeast { currency = Cash, threshold = 500 };
            AmpStrings.costCurrency = Cash;
            AmpStrings.cost = 500;
            AmpStrings.effects.Add(new Effect { target = "practice_amp", multiplier = 2 });
            Tier1.upgrades.Add(StagePresence);
            Tier1.upgrades.Add(AmpStrings);
            Tap.produces.Add(TestTree.Entry(Cash, Stat.Yield, 1, new UpgradePurchased { upgrade = StagePresence }));

            RecordsIncome = TestTree.MakeDefinition<CareerEffectDefinition>("records_income");
            RecordsIncome.target = "income";
            RecordsIncome.formula = new LinearOnBalance { currency = Records, coefficient = 0.02 };
            Root.careerEffects.Add(RecordsIncome);

            Ch1.modifiers.Add(Boost);
        }

        public ValidationReport Run() => ContentValidator.Validate(Root);

        // A second chapter under the same root, deliberately reusing chapter
        // one's ids. Sibling subtrees cannot see each other, so every reuse
        // here is legal content and the validator has to answer from where the
        // question is asked rather than from a tree-wide map.
        public class Sibling
        {
            public ScopeDefinition Ch2;
            public ScopeDefinition Tier2;
            public CurrencyDefinition Cash;   // tier1's id, a different asset
        }

        public Sibling AddSiblingChapter()
        {
            var sibling = new Sibling { Ch2 = TestTree.MakeScope("ch2"), Tier2 = TestTree.MakeScope("tier2") };
            Root.children.Add(sibling.Ch2);
            sibling.Ch2.children.Add(sibling.Tier2);
            sibling.Cash = TestTree.DeclareCurrency(sibling.Tier2, "cash", "income");
            return sibling;
        }
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
        public void DuplicateIdOnAChain_Error()
        {
            var f = new ValidatorFixture();
            TestTree.DeclareCurrency(f.Ch1, "cash");   // tier1 already declares one, and ch1 is on its chain
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateId, "'cash'");
        }

        [Test]
        public void FlagCollidingWithDefinitionId_Error()
        {
            var f = new ValidatorFixture();
            f.Ch1.declaredFlags.Add("cash");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateId, "flag at 'ch1'");
        }

        [Test]
        public void FlagDeclaredInTwoScopes_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1.declaredFlags.Add("album");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateHome, "'album' is declared twice on the chain");
        }

        [Test]
        public void CurrencyDeclaredInTwoScopes_Error()
        {
            var f = new ValidatorFixture();
            f.Ch1.declaredCurrencies.Add(f.Cash);
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
            TestTree.DeclareCurrency(f.Tier1, "extra", "cash");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.TagIdCollision, "tag 'cash'");
        }

        // ---- scope graph ----


        [Test]
        public void ScopeUnderTwoParents_Error()
        {
            var f = new ValidatorFixture();
            f.Root.children.Add(f.Tier1);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeGraph, "child of both");
        }



        [Test]
        public void NullDeclaredCurrency_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1.declaredCurrencies.Add(null);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "declaredCurrencies[");
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
            ((ResetScope)f.Album.actions[2]).scope = f.Ch1;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "is neither from 'tier1'");
        }

        [Test]
        public void ResetScope_RootTarget_Error()
        {
            var f = new ValidatorFixture();
            ((ResetScope)f.Capstone.actions[3]).scope = f.Root;
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
            ((ResetScope)f.Album.actions[2]).scope = tier2;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "is neither from 'tier1'");
        }

        [Test]
        public void ResetScope_SiblingTarget_Error()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Add(new ResetScope { scope = f.Tier1b });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "is neither from 'tier1'");
        }

        [Test]
        public void ResetScope_NoScope_Error()
        {
            var f = new ValidatorFixture();
            ((ResetScope)f.Album.actions[2]).scope = null;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "ResetScope names no scope");
        }

        // ---- ExecuteRung reach and cycles ----

        [Test]
        public void ExecuteRung_OutsideSubtree_Error()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new ExecuteRung { tier = f.Ch1 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "outside 'tier1'");
        }

        [Test]
        public void ExecuteRung_TargetWithoutRung_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.Add(new ExecuteRung { tier = f.Tier1b });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "declares no rung");
        }

        [Test]
        public void ExecuteRung_SelfInvocation_CycleError()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Add(new ExecuteRung { tier = f.Tier1 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ReferenceCycle, "rung invocation cycle");
        }

        [Test]
        public void ExecuteRung_DownwardChain_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.Add(new ExecuteRung { tier = f.Tier1 });
            AssertClean(f.Run());
        }

        // ---- modifier grants ----

        [Test]
        public void AddModifier_NoModifier_Error()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new AddModifier { scope = f.Tier1, modifier = null });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "AddModifier names no modifier");
        }

        [Test]
        public void AddModifier_OffChainTarget_Error()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new AddModifier { scope = f.Tier1b, modifier = f.Boost });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeReach, "grants live outward");
        }

        [Test]
        public void AddModifier_AncestorTarget_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Add(new AddModifier { scope = f.Ch1, modifier = f.Boost });
            AssertClean(f.Run());
        }

        [Test]
        public void RemoveModifier_NothingGrantsThere_Warning()
        {
            var f = new ValidatorFixture();
            f.Trigger.actions.Clear();
            f.Trigger.actions.Add(new RemoveModifier { scope = f.Tier1, modifier = f.Boost });
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
        // never reaches it - and could not read it either, so it is an error
        // like any other off-chain write (12.12).
        [Test]
        public void Flag_SetterAboveTheHome_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1.declaredFlags.Add("deep");
            f.Capstone.actions.Add(new SetFlag { flagId = "deep" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "flag 'deep' homed at 'tier1'");
        }

        // ---- ordinary reads and writes address only the acting chain ----

        [Test]
        public void AddCurrency_HomeBelowActing_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions.Add(new AddCurrency { currencies = { f.Cash }, amount = 5 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "AddCurrency");
        }

        [Test]
        public void CurrencyCondition_HomeBelowActing_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.offerCondition = new CurrencyAtLeast { currency = f.Cash, threshold = 1 };
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "CurrencyAtLeast");
        }

        [Test]
        public void CurrencyCondition_NoCurrency_Error()
        {
            var f = new ValidatorFixture();
            f.Album.offerCondition = new CurrencyAtLeast { currency = null, threshold = 1 };
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "CurrencyAtLeast names nothing");
        }

        [Test]
        public void BarsCompleted_NoGroup_Error()
        {
            var f = new ValidatorFixture();
            ((BarsCompleted)((All)f.Album.offerCondition).conditions[1]).group = null;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "BarsCompleted names no bar group");
        }

        [Test]
        public void RootCurve_HomeOffActingChain_Error()
        {
            var f = new ValidatorFixture();
            f.Capstone.actions[1] = new AddCurrency
            {
                currencies = { f.Records },
                formula = new RootCurveFormula { currency = f.Cash, divisor = 1, exponent = 1 },
            };
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "RootCurveFormula");
        }

        [Test]
        public void RootCurve_UnknownCurrency_Error()
        {
            var f = new ValidatorFixture();
            ((RootCurveFormula)((AddCurrency)f.Album.actions[0]).formula).currency = null;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "RootCurveFormula names nothing");
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
            f.Album.actions.Insert(0, new AddCurrency { currencies = { f.Cash }, amount = 10 });
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
            f.Capstone.actions.Add(new ExecuteRung { tier = f.Tier1 }); // after the reset - too late
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
                offerCondition = new CurrencyAtLeast { currency = f.Fans, threshold = 1 },
                actions = { new AddCurrency { currencies = { f.Records }, amount = 1 } },
            };
            f.Tier1.children.Add(inner);
            f.Album.actions.Insert(0, new ExecuteRung { tier = inner });
            AssertClean(f.Run());
        }

        [Test]
        public void FormulaAfterReset_ReadsZeros_Warning()
        {
            var f = new ValidatorFixture();
            f.Album.actions.Clear();
            f.Album.actions.Add(new ResetScope { scope = f.Tier1 });
            f.Album.actions.Add(new AddCurrency
            {
                currencies = { f.Records, f.Ch1Records },
                formula = new RootCurveFormula { currency = f.Fans, divisor = 5, exponent = 0.5 },
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
            TestTree.DeclareCurrency(f.Root, "merch", "collectible");   // the tag exists, but not inside tier1
            f.Boost.effects.Add(new Effect { target = "collectible", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "matches no member within 'tier1'");
        }

        // Tag membership reaches the sources a scope declares, not just its
        // currencies: the gear tag lives on tier1's generator.
        [Test]
        public void EffectTag_MatchedByADeclaredGenerator_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "gear", multiplier = 2 });
            AssertNoFinding(f.Run(), ValidationCheck.EffectTargetUnmatched);
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
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "narrows to 'ghost', which is no currency id and no tag any currency carries");
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
            f.Ch1.modifiers.Add(orphan);   // declared but never granted
            var report = f.Run();
            AssertFinding(report, ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "modifier 'orphan' effects[0]");
            AssertFinding(report, ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "narrows to 'ghost', which is no currency id and no tag any currency carries");
        }

        // The currency coordinate matches an entry's CURRENCY, so a tag only a
        // producer carries narrows to nothing at runtime - it must not validate.
        [Test]
        public void Effect_NarrowingByACurrencyTag_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "practice_amp", currencyId = "income", multiplier = 2 });
            AssertNoFinding(f.Run(), ValidationCheck.UnresolvedReference);
        }

        [Test]
        public void Effect_NarrowingByANonCurrencyTag_Error()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "practice_amp", currencyId = "gear", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference,
                "narrows to 'gear', which is no currency id and no tag any currency carries");
        }

        // ---- producers, generators, upgrades ----

        [Test]
        public void ProducesEntry_CurrencyOffActingChain_Error()
        {
            var f = new ValidatorFixture();
            var merch = TestTree.DeclareCurrency(f.Tier1b, "merch");
            f.Tap.produces.Add(TestTree.Entry(merch, Stat.Rate, 1));
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "a produces entry addresses 'merch' declared at 'tier1b'");
        }

        [Test]
        public void ProducesEntry_UnconsumedStat_Warning()
        {
            var f = new ValidatorFixture();
            f.Tap.produces.Add(TestTree.Entry(f.Cash, "Rate", 1));   // no system consumes "Rate"
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.UnconsumedStat, "stat 'Rate'");
        }

        [Test]
        public void ProducesEntry_MissingStat_Warning()
        {
            var f = new ValidatorFixture();
            f.Tap.produces.Add(TestTree.Entry(f.Cash, null, 1));
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.UnconsumedStat, "names no stat");
        }

        [Test]
        public void ProducesEntry_NegativeValue_Error()
        {
            var f = new ValidatorFixture();
            f.Tap.produces.Add(TestTree.Entry(f.Cash, Stat.Yield, -1));
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "never subtracts");
        }

        [Test]
        public void ProducesEntry_ConditionValidatedInTheDeclaringScope_Error()
        {
            var f = new ValidatorFixture();
            f.Tap.produces.Add(TestTree.Entry(f.Cash, Stat.Yield, 1, new UpgradePurchased { upgrade = null }));
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "UpgradePurchased names nothing");
        }

        [Test]
        public void Generator_FreeBaseCost_Error()
        {
            var f = new ValidatorFixture();
            f.Amp.baseCost = 0;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "unbounded rate printer");
        }

        [Test]
        public void Generator_NonpositiveGrowth_Error()
        {
            var f = new ValidatorFixture();
            f.Amp.growth = 0;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "positive ratio");
        }

        [Test]
        public void Generator_CostCurrencyOffActingChain_Error()
        {
            var f = new ValidatorFixture();
            f.Amp.costCurrency = f.Ch1Records;
            AssertNoFinding(f.Run(), ValidationCheck.ChainReach);    // an ancestor's currency is on the chain

            f.Amp.costCurrency = TestTree.DeclareCurrency(f.Tier1b, "merch");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "generator cost addresses 'merch' declared at 'tier1b'");
        }

        // An unauthored gate refuses the buy, so the content is inert rather
        // than broken - the same species of finding as a flag nothing sets.
        [Test]
        public void NullPurchaseGate_Warning()
        {
            var f = new ValidatorFixture();
            f.Amp.availableWhen = null;
            f.AmpStrings.gate = null;
            var report = f.Run();
            AssertFinding(report, ValidationSeverity.Warning, ValidationCheck.NullEntry, "generator 'practice_amp': availableWhen is unauthored");
            AssertFinding(report, ValidationSeverity.Warning, ValidationCheck.NullEntry, "upgrade 'amp_strings': gate is unauthored");
        }

        [Test]
        public void Upgrade_NegativeCost_Error()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.cost = -1;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "never pays out");
        }

        [Test]
        public void Upgrade_ZeroCost_NoFindings()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.cost = 0;                                   // cut_demo is authored at 0
            AssertNoFinding(f.Run(), ValidationCheck.NumericRange);
        }

        // The purchase latch is a fact write BEFORE actions[0], so a payload
        // resetting the latch's own scope would make the upgrade repeatable.
        [Test]
        public void Upgrade_PayloadResettingItsOwnScope_SetThenWiped_Error()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.actions.Add(new ResetScope { scope = f.Tier1 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.SetThenWiped, "purchase latch of upgrade 'amp_strings'");
        }

        [Test]
        public void Upgrade_ActionListParticipatesInTheSharedLedgers_Error()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.actions.Add(new SetFlag { flagId = "ghost_flag" });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.UnresolvedReference, "SetFlag names flag 'ghost_flag'");
        }

        [Test]
        public void UpgradeEffect_TargetingASiblingsSource_EffectReach_Error()
        {
            var f = new ValidatorFixture();
            var sibling = TestTree.MakeDefinition<GeneratorDefinition>("merch_stand");
            sibling.availableWhen = new CurrencyAtLeast { currency = f.Ch1Records, threshold = 1 };
            sibling.costCurrency = f.Ch1Records;
            sibling.baseCost = 5;
            sibling.produces.Add(TestTree.Entry(f.Ch1Records, Stat.Rate, 1));
            f.Tier1b.generators.Add(sibling);

            // tier1's upgrade cannot reach a source declared in tier1b: the
            // source's own outward walk never visits tier1.
            f.AmpStrings.effects.Add(new Effect { target = "merch_stand", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.EffectReach, "targets 'merch_stand' declared at 'tier1b'");
        }

        [Test]
        public void Effect_NegativeMultiplier_Error()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.effects.Add(new Effect { target = "practice_amp", multiplier = -1 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "never flips a number's sign");
        }

        [Test]
        public void Effect_ZeroMultiplier_NoFindings()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.effects.Add(new Effect { target = "practice_amp", multiplier = 0 });   // an event handicap
            AssertNoFinding(f.Run(), ValidationCheck.NumericRange);
        }

        [Test]
        public void Effect_UnconsumedStatNarrowing_Warning()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "cash", stat = "tick", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.UnconsumedStat, "stat narrowing names stat 'tick'");
        }

        [Test]
        public void DeclaringTheSameSourceTwice_Error()
        {
            var f = new ValidatorFixture();
            f.Tier1b.generators.Add(f.Amp);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.DuplicateHome, "declaration is ownership");
        }

        [Test]
        public void GeneratorReference_OffActingChain_Error()
        {
            var f = new ValidatorFixture();
            var sibling = TestTree.MakeDefinition<GeneratorDefinition>("merch_stand");
            sibling.availableWhen = new CurrencyAtLeast { currency = f.Ch1Records, threshold = 1 };
            sibling.costCurrency = f.Ch1Records;
            sibling.baseCost = 5;
            f.Tier1b.generators.Add(sibling);

            // tier1's rung cannot read a count stored in tier1b.
            ((All)f.Album.offerCondition).conditions.Add(new OwnedCountAtLeast { generator = sibling, count = 1 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "OwnedCountAtLeast addresses 'merch_stand' declared at 'tier1b'");
        }

        [Test]
        public void GeneratorReference_Missing_Error()
        {
            var f = new ValidatorFixture();
            ((All)f.Album.offerCondition).conditions.Add(new OwnedCountAtLeast { generator = null, count = 1 });
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "OwnedCountAtLeast names nothing");
        }

        [Test]
        public void UpgradeReference_OffActingChain_Error()
        {
            var f = new ValidatorFixture();
            var sibling = TestTree.MakeDefinition<UpgradeDefinition>("merch_deal");
            sibling.gate = new CurrencyAtLeast { currency = f.Ch1Records, threshold = 1 };
            sibling.costCurrency = f.Ch1Records;
            f.Tier1b.upgrades.Add(sibling);

            f.Trigger.condition = new UpgradePurchased { upgrade = sibling };
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ChainReach, "UpgradePurchased addresses 'merch_deal' declared at 'tier1b'");
        }

        // ---- career effects ----

        [Test]
        public void CareerEffect_NoFormula_Error()
        {
            var f = new ValidatorFixture();
            f.RecordsIncome.formula = null;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NullEntry, "career effect 'records_income': no formula");
        }

        [Test]
        public void CareerEffect_TargetOutOfReach_Error()
        {
            var f = new ValidatorFixture();
            var local = TestTree.MakeDefinition<CareerEffectDefinition>("tier_career");
            local.target = "records";                                // homed at the root, declared at tier1
            local.formula = new LinearOnBalance { currency = f.Ch1Records, coefficient = 1 };
            f.Tier1.careerEffects.Add(local);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.EffectReach, "targets currency 'records' homed at 'root'");
        }

        [Test]
        public void CareerFormula_NegativeCoefficient_Error()
        {
            var f = new ValidatorFixture();
            ((LinearOnBalance)f.RecordsIncome.formula).coefficient = -0.02;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "never shrinks");
        }

        [Test]
        public void RoadieBoost_NegativePerRoadie_Error()
        {
            var f = new ValidatorFixture();
            var roadie = TestTree.MakeDefinition<CareerEffectDefinition>("roadie_total");
            roadie.target = "income";
            roadie.formula = new RoadieTotalBoost { perRoadie = -0.05 };
            f.Root.careerEffects.Add(roadie);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "never shrinks");
        }




        // ---- numeric range across every authored double ----

        [Test]
        public void NonFiniteDoubles_Error()
        {
            var f = new ValidatorFixture();
            f.Amp.growth = double.NaN;
            ((LinearOnBalance)f.RecordsIncome.formula).coefficient = double.NaN;
            ((RootCurveFormula)((AddCurrency)f.Album.actions[0]).formula).exponent = double.PositiveInfinity;
            f.Boost.effects.Add(new Effect { target = "cash", multiplier = double.NaN });

            var report = f.Run();
            var findings = report.OfCheck(ValidationCheck.NumericRange).Count();
            Assert.AreEqual(4, findings, Dump(report));
            AssertFinding(report, ValidationSeverity.Error, ValidationCheck.NumericRange, "must be finite");
        }

        [Test]
        public void RootCurve_NonpositiveDivisor_Error()
        {
            var f = new ValidatorFixture();
            ((RootCurveFormula)((AddCurrency)f.Album.actions[0]).formula).divisor = 0;
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.NumericRange, "infinite or undefined");
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

        // ---- sibling chains: an id is unique where it is VISIBLE, not globally ----

        [Test]
        public void SiblingChapters_MayEachDeclareTheSameCurrencyId_NoFindings()
        {
            var f = new ValidatorFixture();
            f.AddSiblingChapter();          // tier2 declares its own 'cash' asset
            AssertClean(f.Run());
        }

        // The setter in each chapter resolves the name outward to its OWN
        // chapter's declaration: neither is off-chain, and neither declaration
        // is left without a setter.
        [Test]
        public void SiblingChapters_MayEachDeclareTheSameFlag_NoFindings()
        {
            var f = new ValidatorFixture();
            var sibling = f.AddSiblingChapter();
            sibling.Ch2.declaredFlags.Add("album");     // ch1 declares one of its own
            sibling.Tier2.rung = new Rung { actions = { new SetFlag { flagId = "album" } } };
            AssertClean(f.Run());
        }

        // A grant names the modifier ASSET, so two chapters may each declare a
        // 'boost' and each grant its own - the effects are judged at the grant
        // site, against the currencies that site can reach.
        [Test]
        public void SiblingChapters_MayEachDeclareTheSameModifierId_NoFindings()
        {
            var f = new ValidatorFixture();
            var sibling = f.AddSiblingChapter();
            var boost2 = TestTree.MakeDefinition<ModifierDefinition>("boost");
            boost2.effects.Add(new Effect { target = "cash", multiplier = 2 });
            sibling.Ch2.modifiers.Add(boost2);

            var trigger2 = TestTree.MakeDefinition<TriggerDefinition>("boost_trigger");
            trigger2.actions.Add(new AddModifier { scope = sibling.Tier2, modifier = boost2 });
            sibling.Tier2.triggers.Add(trigger2);
            AssertClean(f.Run());
        }

        [Test]
        public void SiblingChapters_EffectResolvesToItsOwnChaptersCurrency_NoFindings()
        {
            var f = new ValidatorFixture();
            var sibling = f.AddSiblingChapter();
            var upgrade = TestTree.MakeDefinition<UpgradeDefinition>("tight_set");
            upgrade.gate = new EarnedTotalAtLeast { currency = sibling.Cash, threshold = 10 };
            upgrade.costCurrency = sibling.Cash;
            upgrade.cost = 10;
            upgrade.effects.Add(new Effect { target = "cash", multiplier = 1.5 });
            sibling.Tier2.upgrades.Add(upgrade);
            AssertClean(f.Run());
        }

        // Both cycle shapes REPORT rather than recurse: the chain and subtree
        // walks run only after the graph is known to be a tree, so reaching the
        // assertion at all is half of what these two prove.

        [Test]
        public void RootBackEdge_ReportsTheCycle()
        {
            var f = new ValidatorFixture();
            f.Tier1.children.Add(f.Root);
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeGraph, "children cycle");
        }

        [Test]
        public void MidTreeBackEdge_ReportsTheCycle()
        {
            var f = new ValidatorFixture();
            f.Tier1.children.Add(f.Ch1);    // ch1 becomes a child of its own descendant
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.ScopeGraph, "child of both");
        }

        // Tags travel down a chain with ids, so a collision is caught where both
        // are visible even though neither scope declares both halves.
        [Test]
        public void AncestorTagCollidingWithADescendantId_Error()
        {
            var f = new ValidatorFixture();
            TestTree.DeclareCurrency(f.Root, "merch", "encore");
            TestTree.DeclareCurrency(f.Tier1, "encore");
            AssertFinding(f.Run(), ValidationSeverity.Error, ValidationCheck.TagIdCollision, "collides with tag 'encore'");
        }

        // ---- the coordinates address one entry TOGETHER (12.2) ----

        // The currency stage evaluates with owner == currency, so a currency
        // target narrowed to a DIFFERENT currency can never match anything.
        [Test]
        public void EffectCoordinates_CurrencyTargetNarrowedToAnotherCurrency_Warning()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "cash", currencyId = "fans", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "never select an entry together");
        }

        [Test]
        public void EffectCoordinates_SourceNarrowedToACurrencyItNeverProduces_Warning()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.effects.Add(new Effect { target = "practice_amp", currencyId = "fans", multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "pairs with currency 'fans'");
        }

        [Test]
        public void EffectCoordinates_SourceNarrowedToAStatItNeverProduces_Warning()
        {
            var f = new ValidatorFixture();
            f.AmpStrings.effects.Add(new Effect { target = "practice_amp", stat = Stat.Yield, multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "pairs with stat 'yield'");
        }

        // The currency stage is only ever asked for a stat something pays the
        // currency with, so nothing paying fans a rate makes the pair inert.
        [Test]
        public void EffectCoordinates_CurrencyNarrowedToAStatNothingPaysIt_Warning()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "fans", stat = Stat.Rate, multiplier = 2 });
            AssertFinding(f.Run(), ValidationSeverity.Warning, ValidationCheck.EffectTargetUnmatched, "pairs with stat 'rate'");
        }

        [Test]
        public void EffectCoordinates_CurrencyNarrowedToAStatSomethingPays_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Boost.effects.Add(new Effect { target = "cash", stat = Stat.Yield, multiplier = 2 });   // the tap pays it
            AssertNoFinding(f.Run(), ValidationCheck.EffectTargetUnmatched);
        }

        // A tag names many owners on purpose: the pair holds when ONE of them
        // can pay it, not when the first one can.
        [Test]
        public void EffectCoordinates_TagTargetSatisfiedBySomeOwner_NoFindings()
        {
            var f = new ValidatorFixture();
            f.Tap.EditorInit("tap_producer", "gear");   // the amp already carries it, with rate entries
            f.AmpStrings.effects.Add(new Effect { target = "gear", stat = Stat.Yield, multiplier = 2 });
            AssertNoFinding(f.Run(), ValidationCheck.EffectTargetUnmatched);
        }
    }
}
