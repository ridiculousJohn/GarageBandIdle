using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.EditorTools;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The importer's condition parse path (real DTO shape + conversion, no
    // asset writes). The load-bearing claim: compound conditions map onto the
    // recursive CompoundCondition family at any nesting depth - the Condition
    // contract declares all/any as arrays of nested Condition.
    public class ChapterJsonImporterTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        [Test]
        public void Condition_CompoundsNestToAnyDepth()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var context = TestContent.MakeContext(currencies, flags: flags);
            // all[fans, any[covers, all[album, cash >= 100]]] - three levels
            var condition = ChapterJsonImporter.ParseCondition(@"{
                ""type"": ""compound"",
                ""all"": [
                    { ""type"": ""flagSet"", ""flag"": ""fans"" },
                    { ""type"": ""compound"", ""any"": [
                        { ""type"": ""flagSet"", ""flag"": ""covers"" },
                        { ""type"": ""compound"", ""all"": [
                            { ""type"": ""flagSet"", ""flag"": ""album"" },
                            { ""type"": ""currency"", ""currency"": ""cash"", ""value"": 100 }
                        ] }
                    ] }
                ]
            }");

            Assert.IsFalse(condition.Evaluate(context), "nothing met");

            flags.Set("fans");
            Assert.IsFalse(condition.Evaluate(context), "the nested any is unmet");

            flags.Set("album");
            Assert.IsFalse(condition.Evaluate(context), "the innermost all is only half met");

            currencies.Add("cash", 100);
            Assert.IsTrue(condition.Evaluate(context), "the innermost all satisfies the nested any");
        }

        // The conversion backstop, not the refusal: the pre-pass now aborts the
        // whole import on a child with no type (see ConditionFaults_* below), so
        // reaching this in production means the pre-pass missed a site. Kept
        // covered because the message is the only thing that would say so.
        [Test]
        public void Condition_CompoundChildWithNoType_IsReportedAndSkipped()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var context = TestContent.MakeContext(currencies, flags: flags);

            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: condition all[0] is a compound child with no type - the condition pre-pass should have aborted the import. Skipping it.");
            var condition = ChapterJsonImporter.ParseCondition(
                @"{ ""type"": ""compound"", ""all"": [ {}, { ""type"": ""flagSet"", ""flag"": ""fans"" } ] }");

            Assert.IsFalse(condition.Evaluate(context));
            flags.Set("fans");
            Assert.IsTrue(condition.Evaluate(context), "the surviving child governs the gate");
        }

        // The condition pre-pass (design doc section 12, rules 8 and 9). An
        // unconvertible condition becomes null, null means "no gate", and boot
        // validation cannot object because a null Condition is legal content
        // everywhere - so the import refuses the whole file rather than writing an
        // asset whose gate silently stands open.
        [TestCase(@"{ ""type"": ""flagset"", ""flag"": ""covers"" }",
            "condition has condition type 'flagset', which maps to no Condition subclass.",
            TestName = "UnknownType_IncludingWrongCasing")]
        [TestCase(@"{ ""type"": ""compound"" }",
            "condition is a compound condition with no children.",
            TestName = "CompoundWithNoChildren")]
        // `type` is the one key whose misspelling nothing else can catch: the
        // unrecognized-key report lives past the empty-type return, so this
        // would otherwise import as content with no gate at all
        [TestCase(@"{ ""typ"": ""flagSet"", ""flag"": ""covers"" }",
            "condition has a condition object with no 'type' (unrecognized key(s): typ) - a condition is identified by its 'type', so this would import as no gate.",
            TestName = "MisspelledTypeKey")]
        [TestCase(@"{ ""flag"": ""covers"" }",
            "condition has a condition object with no 'type' - a condition is identified by its 'type', so this would import as no gate.",
            TestName = "AuthoredFieldsButNoTypeAtAll")]
        // Presence, not contents: these three are what a half-finished gate leaves
        // behind, and after deserialization they are indistinguishable from an
        // absent key UNLESS the DTO field carries no initializer.
        [TestCase(@"{ ""type"": """" }",
            "condition has a condition object with no 'type' - a condition is identified by its 'type', so this would import as no gate.",
            TestName = "TypeKeyPresentButEmpty")]
        [TestCase(@"{ ""flag"": """" }",
            "condition has a condition object with no 'type' - a condition is identified by its 'type', so this would import as no gate.",
            TestName = "FieldKeyPresentButEmpty")]
        [TestCase(@"{ ""all"": [] }",
            "condition has a condition object with no 'type' - a condition is identified by its 'type', so this would import as no gate.",
            TestName = "CompoundListPresentButEmpty")]
        [TestCase(@"{ ""type"": ""compound"", ""all"": [ {} ] }",
            "condition all[0] is a compound child with no type.",
            TestName = "CompoundChildWithNoType")]
        [TestCase(@"{ ""type"": ""compound"", ""all"": [ { ""type"": ""compound"", ""any"": [ { ""type"": ""flagSet"", ""flag"": ""fans"" }, {} ] } ] }",
            "condition all[0] any[1] is a compound child with no type.",
            TestName = "NestedChild_FaultCarriesThePath")]
        public void ConditionFaults_MalformedInputIsAFault(string json, string expectedFault)
        {
            // exactly one fault: a compound with a bad child must not ALSO claim it
            // has no children - those are different mistakes, so the count is taken
            // on the raw arrays rather than on the children that survived
            CollectionAssert.AreEqual(new[] { expectedFault }, ChapterJsonImporter.ParseConditionFaults(json));
        }

        // The regression that matters most: a false positive here aborts EVERY
        // import. No gate is legal content at all seven authoring sites, and the
        // DTO materializes an absent block as an empty instance, so "no type" has
        // to stay indistinguishable from "no gate authored".
        [TestCase("{}", TestName = "AbsentGate")]
        [TestCase("null", TestName = "ExplicitNullGate")]
        [TestCase(@"{ ""type"": ""currency"", ""currency"": ""cash"", ""value"": 250 }", TestName = "currency")]
        [TestCase(@"{ ""type"": ""currencyEarnedTotal"", ""currency"": ""cash"", ""value"": 100 }", TestName = "currencyEarnedTotal")]
        [TestCase(@"{ ""type"": ""ownedCount"", ""generator"": ""drummer"", ""value"": 1 }", TestName = "ownedCount")]
        [TestCase(@"{ ""type"": ""flagSet"", ""flag"": ""covers"" }", TestName = "flagSet")]
        [TestCase(@"{ ""type"": ""barsCompleted"", ""group"": ""learn_covers"", ""value"": 3 }", TestName = "barsCompleted")]
        [TestCase(@"{ ""type"": ""recordsCumulative"", ""value"": 5 }", TestName = "recordsCumulative")]
        [TestCase(@"{ ""type"": ""compound"", ""all"": [ { ""type"": ""flagSet"", ""flag"": ""covers"" } ] }", TestName = "compound")]
        public void ConditionFaults_ValidAndAbsentGatesAreNotFaults(string json)
        {
            Assert.IsEmpty(ChapterJsonImporter.ParseConditionFaults(json));
        }

        // The one authored spelling presence-testing cannot reach, recorded as a
        // test so it is a known exception rather than a surprise. `value` is a
        // plain double, so it cannot report its own absence, and making it
        // nullable would spread `?? 0` through the conversion to catch a block
        // naming no type, no currency and no threshold - it declares nothing.
        // Anything alongside it IS caught, which is what keeps the hole this
        // narrow.
        [Test]
        public void ConditionFaults_BareZeroValue_IsTheKnownExceptionAndReadsAsAbsent()
        {
            Assert.IsEmpty(ChapterJsonImporter.ParseConditionFaults(@"{ ""value"": 0 }"),
                "a plain double cannot distinguish an authored 0 from omission");

            // one more key beside it and presence-testing sees the block again
            CollectionAssert.AreEqual(
                new[] { "condition has a condition object with no 'type' - a condition is identified by its 'type', so this would import as no gate." },
                ChapterJsonImporter.ParseConditionFaults(@"{ ""value"": 0, ""currency"": """" }"));
        }

        // Every fault, not the first: an author fixing a chapter wants the whole
        // list, not one import round trip per typo.
        [Test]
        public void ConditionFaults_ReportsEveryFaultRatherThanStoppingAtTheFirst()
        {
            var faults = ChapterJsonImporter.ParseConditionFaults(@"{
                ""type"": ""compound"",
                ""all"": [ { ""type"": ""flagset"" }, {} ],
                ""any"": [ { ""type"": ""compound"" } ]
            }");

            CollectionAssert.AreEqual(new[]
            {
                "condition all[0] has condition type 'flagset', which maps to no Condition subclass.",
                "condition all[1] is a compound child with no type.",
                "condition any[0] is a compound condition with no children.",
            }, faults);
        }

        // absent and explicit-null gates both mean "no gate" - the Newtonsoft
        // swap must keep JsonUtility's absent-field semantics
        [Test]
        public void Condition_AbsentOrNullGate_ImportsNoGate()
        {
            Assert.IsNull(ChapterJsonImporter.ParseCondition("{}"), "an empty block is no gate");
            Assert.IsNull(ChapterJsonImporter.ParseCondition("null"), "an explicit null is no gate");
        }

        // A key the DTO does not define would otherwise be dropped, leaving the
        // threshold at zero - a gate met before play starts. `amount` copied from
        // the cost block beside the gate is the likely one, but the check is on the
        // key not matching rather than on any particular spelling, so a plain typo
        // reports the same way. Only the importer sees this: the asset keeps just
        // the keys that were read.
        [Test]
        public void Condition_UnrecognizedKey_IsReportedRatherThanDropped()
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: upgrade 'x' (gate) carries unrecognized key 'amount' - a condition's threshold is 'value' ('amount' is a cost block's price). Fix the JSON and re-import.");
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: upgrade 'x' (gate) has a non-positive value (0) - the gate would be met before play starts. Fix the JSON and re-import.");
            ChapterJsonImporter.ParseCondition(
                @"{ ""type"": ""currency"", ""currency"": ""cash"", ""amount"": 250 }", "upgrade 'x' (gate)");

            // any misspelling, not one hand-picked wrong key
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: generator 'g' (unlock) carries unrecognized key 'valeu' - a condition's threshold is 'value' ('amount' is a cost block's price). Fix the JSON and re-import.");
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: generator 'g' (unlock) has a non-positive value (0) - the gate would be met before play starts. Fix the JSON and re-import.");
            ChapterJsonImporter.ParseCondition(
                @"{ ""type"": ""currencyEarnedTotal"", ""currency"": ""cash"", ""valeu"": 100 }", "generator 'g' (unlock)");
        }

        // a genuinely zero threshold reports plainly, and the condition is still
        // written: dropping it would mean "no gate", which is the same always-open
        // failure the report is about
        [Test]
        public void Condition_NonPositiveThreshold_IsReportedAndStillWritten()
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: upgrade 'x' (gate) has a non-positive value (0) - the gate would be met before play starts. Fix the JSON and re-import.");

            Assert.IsInstanceOf<CurrencyBalanceCondition>(ChapterJsonImporter.ParseCondition(
                @"{ ""type"": ""currency"", ""currency"": ""cash"" }", "upgrade 'x' (gate)"));
        }

        // the unified key is what the importer reads: a currency gate's threshold
        // arrives under `value`, the same key every other condition uses
        [Test]
        public void Condition_CurrencyGate_ReadsItsThresholdFromValue()
        {
            var condition = ChapterJsonImporter.ParseCondition(
                @"{ ""type"": ""currency"", ""currency"": ""cash"", ""value"": 250 }",
                "upgrade 'x' (gate)") as CurrencyBalanceCondition;

            Assert.IsNotNull(condition);
            Assert.AreEqual("cash", condition.CurrencyId);
            Assert.AreEqual(250, condition.Value, 1e-9);
        }

        // flagSet reads neither numeric key, so a stray one must not be read as a
        // missing threshold
        [Test]
        public void Condition_FlagSet_CarriesNoThresholdToReport()
        {
            Assert.IsInstanceOf<FlagSetCondition>(ChapterJsonImporter.ParseCondition(
                @"{ ""type"": ""flagSet"", ""flag"": ""fans"" }", "section 's' (visibleWhen)"));
        }

        // the producer parse path (design doc section 12, rule 13): trigger and
        // composes vocabularies map onto their closed enums, the gate onto the
        // Condition family, and an absent gate means always-on
        [Test]
        public void Producer_ProductionEntries_MapOntoConfigs()
        {
            var configs = ChapterJsonImporter.ParseProducerProduction(@"{
                ""id"": ""jam"", ""module"": ""module/tap"",
                ""production"": [
                    { ""currency"": ""cash"", ""amount"": 1, ""trigger"": ""tap"", ""composes"": ""tapValue"" },
                    { ""currency"": ""rehearsal"", ""amount"": 2, ""trigger"": ""tap"", ""gate"": { ""type"": ""flagSet"", ""flag"": ""covers"" } },
                    { ""currency"": ""rehearsal"", ""amount"": 1, ""trigger"": ""tick"", ""gate"": { ""type"": ""flagSet"", ""flag"": ""covers"" } }
                ]
            }");

            Assert.AreEqual(3, configs.Count);

            Assert.AreEqual("cash", configs[0].CurrencyId);
            Assert.AreEqual(1.0, configs[0].Amount, 1e-9);
            Assert.AreEqual(ProductionTrigger.Tap, configs[0].Trigger);
            Assert.AreEqual(ModifierTarget.TapValue, configs[0].Composes);
            Assert.IsNull(configs[0].Gate, "no gate = always on");

            Assert.AreEqual(ModifierTarget.None, configs[1].Composes, "absent composes = the raw amount");
            var gate = configs[1].Gate as FlagSetCondition;
            Assert.IsNotNull(gate, "the gate is an ordinary Condition");
            Assert.AreEqual("covers", gate.FlagId);

            Assert.AreEqual(ProductionTrigger.Tick, configs[2].Trigger);
        }

        // an invalid entry skips the whole producer - a producer missing one of
        // its yields is not the authored producer - and each refusal says why
        [TestCase(@"{ ""id"": ""jam"", ""production"": [ { ""currency"": ""cash"", ""amount"": 1, ""trigger"": ""hold"" } ] }",
            "ChapterJsonImporter: producer 'jam' production for 'cash' has unknown trigger 'hold' - a production config fires on 'tick' or 'tap'. Skipping the producer - fix the JSON and re-import.")]
        // 'fanRate' is authorable since 5.7 (fan accrual is a config); what stays
        // refused is a spelling the family does not define, casing included
        [TestCase(@"{ ""id"": ""jam"", ""production"": [ { ""currency"": ""cash"", ""amount"": 1, ""trigger"": ""tap"", ""composes"": ""fanrate"" } ] }",
            "ChapterJsonImporter: producer 'jam' production for 'cash' has unknown composes 'fanrate' - a config composes 'tapValue', 'fanRate' or nothing. Skipping the producer - fix the JSON and re-import.")]
        [TestCase(@"{ ""id"": ""jam"", ""production"": [ { ""currency"": ""cash"", ""amount"": -1, ""trigger"": ""tap"" } ] }",
            "ChapterJsonImporter: producer 'jam' production for 'cash' has a negative amount (-1). Skipping the producer - fix the JSON and re-import.")]
        [TestCase(@"{ ""id"": ""jam"", ""production"": [ { ""amount"": 1, ""trigger"": ""tap"" } ] }",
            "ChapterJsonImporter: producer 'jam' has a production entry with no currency. Skipping the producer - fix the JSON and re-import.")]
        [TestCase(@"{ ""id"": ""jam"", ""production"": [] }",
            "ChapterJsonImporter: producer 'jam' has no production entries - it would produce nothing. Skipping it - fix the JSON and re-import.")]
        public void Producer_RefusesWhatCouldNeverFireAsAuthored(string json, string expectedError)
        {
            LogAssert.Expect(LogType.Error, expectedError);

            Assert.IsNull(ChapterJsonImporter.ParseProducerProduction(json));
        }

        // the pre-5.4 schema put engagement earn on the currency; an earn block
        // is stale JSON that used to mean something, so it refuses rather than
        // silently dropping it (a bare {id, group} entry imports fine)
        [Test]
        public void CurrencyEntry_WithAnEarnBlock_IsRefused()
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: currency 'rehearsal' carries an 'earn' block - currencies are pure state, production lives on producers (design doc section 12, rule 13). Skipping it - fix the JSON and re-import.");
            Assert.IsFalse(ChapterJsonImporter.ParseCurrencyEntryIsImportable(
                @"{ ""id"": ""rehearsal"", ""group"": ""run"", ""earn"": { ""revealFlag"": ""covers"", ""perSec"": 1, ""perTap"": 2 } }"));

            Assert.IsTrue(ChapterJsonImporter.ParseCurrencyEntryIsImportable(
                @"{ ""id"": ""rehearsal"", ""group"": ""run"" }"));
        }

        // the pre-5.6 schema revealed a bar group by bare flag id; reveal is a
        // Condition now, so a leftover revealFlag is refused rather than
        // silently ignored - ignoring it would import the group with no gate,
        // showing it from the first frame.
        //
        // Refused on PRESENCE, like the fans keys below: `""` is a stale key just
        // as much as a filled-in one, and it is the spelling a contents test lets
        // through - which is what makes it the one worth naming.
        [TestCase(@"{ ""id"": ""learn_covers"", ""revealFlag"": ""covers"", ""fillMode"": ""perBar"" }",
            TestName = "FilledIn")]
        [TestCase(@"{ ""id"": ""learn_covers"", ""revealFlag"": """", ""fillMode"": ""perBar"" }",
            TestName = "EmptySpelling")]
        public void BarGroup_WithARevealFlagKey_IsRefused_EvenItsEmptySpelling(string json)
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: bar group 'learn_covers' carries a 'revealFlag' key - reveal is a Condition under 'visibleWhen' (design doc section 12, rules 8 and 9). Skipping it - fix the JSON and re-import.");

            Assert.IsFalse(ChapterJsonImporter.ParseBarGroupIsImportable(json));
        }

        // The other half of a presence test, and the reason the DTO field lost its
        // `= ""` initializer in the same change: with the initializer still there,
        // an absent key would read as `""` and refuse EVERY bar group.
        [Test]
        public void BarGroup_WithNoRevealFlagKey_Imports()
        {
            Assert.IsTrue(ChapterJsonImporter.ParseBarGroupIsImportable(
                @"{ ""id"": ""learn_covers"", ""visibleWhen"": { ""type"": ""flagSet"", ""flag"": ""covers"" }, ""fillMode"": ""perBar"" }"));
        }

        // fan accrual is production since 5.7, so the three keys that used to
        // describe it on the chapter are stale JSON. Each is refused on PRESENCE,
        // which is what these empty spellings prove: a contents test would let
        // `{}` and `""` through silently, and the emptiest form of a stale key is
        // the one least likely to be spotted by eye.
        [TestCase(@"{ ""currency"": ""fans"", ""baseFansPerSec"": 0 }",
            "ChapterJsonImporter: fans block still carries 'baseFansPerSec' - the base fan rate is a production config on a producer (design doc section 12, rule 13). Fix the JSON and re-import.")]
        [TestCase(@"{ ""currency"": ""fans"", ""revealFlag"": """" }",
            "ChapterJsonImporter: fans block still carries a 'revealFlag' key - accrual is gated by the production config's gate (design doc section 12, rules 8, 9 and 13). Fix the JSON and re-import.")]
        [TestCase(@"{ ""currency"": ""fans"", ""activeWhen"": {} }",
            "ChapterJsonImporter: fans block still carries 'activeWhen' - the accrual gate moved onto the production config's 'gate' (design doc section 12, rule 13). Fix the JSON and re-import.")]
        public void FansBlock_RefusesEveryStaleKey_EvenItsEmptySpelling(string json, string expectedError)
        {
            LogAssert.Expect(LogType.Error, expectedError);
            ChapterJsonImporter.ParseFansBlockStaleKeys(json);
        }

        // the fans block as 5.7 leaves it: currency plus the per-bandmate tuning,
        // nothing about production
        [Test]
        public void FansBlock_WithNoStaleKeys_ReportsNothing()
        {
            ChapterJsonImporter.ParseFansBlockStaleKeys(
                @"{ ""currency"": ""fans"", ""perBandmateOwnedBonus"": 0.02 }");
        }

        // the other half of making fanRate authorable: it has to MAP, not merely
        // stop being refused
        [Test]
        public void ProductionConfig_ComposesFanRate_MapsToTheFanRateTarget()
        {
            var configs = ChapterJsonImporter.ParseProducerProduction(
                @"{ ""id"": ""band"", ""production"": [ { ""currency"": ""fans"", ""amount"": 0.2, ""trigger"": ""tick"", ""composes"": ""fanRate"" } ] }");

            Assert.IsNotNull(configs);
            Assert.AreEqual(1, configs.Count);
            Assert.AreEqual(ModifierTarget.FanRate, configs[0].Composes);
            Assert.AreEqual(ProductionTrigger.Tick, configs[0].Trigger);
            Assert.AreEqual("fans", configs[0].CurrencyId);
        }

        // a per-sec multiplier carries the currencies it affects as data, so the
        // payload can name any number of them without a code change - the same
        // contract constants.recordBuff.affects follows
        [Test]
        public void Payload_CurrencyPerSecMultiplier_CarriesItsAffectedCurrencies()
        {
            var payload = ChapterJsonImporter.ParsePayload(
                @"{ ""effect"": ""currencyPerSecMultiplier"", ""affects"": [""cash"", ""merch""], ""value"": 1.5 }",
                "upgrade 'tight_set'") as GrantModifierEffect;

            Assert.IsNotNull(payload, "the effect maps onto a currency-production modifier");
            Assert.AreEqual(ModifierTarget.CurrencyProduction, payload.Target);
            Assert.AreEqual(ModifierOperation.Multiply, payload.Operation);
            Assert.AreEqual(1.5, payload.Value, 1e-9);
            CollectionAssert.AreEqual(new[] { "cash", "merch" }, payload.Qualifiers);
        }

        // One vocabulary, two JSON keys. A reward's `type` and an upgrade's
        // `payload.effect` route through the same translator, so an effect name
        // authored either way builds the same object - including the names the old
        // two-family split happened to keep on one side. A reward paying a flat tap
        // bonus is coherent content, and nothing should have refused it but the
        // accident of which class family used to own the handler.
        [TestCase(@"{ ""type"": ""tapValueAdd"", ""value"": 3 }",
            @"{ ""effect"": ""tapValueAdd"", ""value"": 3 }")]
        [TestCase(@"{ ""type"": ""generatorOutputMultiplier"", ""generator"": ""practice_amp"", ""value"": 2 }",
            @"{ ""effect"": ""generatorOutputMultiplier"", ""generator"": ""practice_amp"", ""value"": 2 }")]
        [TestCase(@"{ ""type"": ""fanRateMultiplier"", ""value"": 1.15 }",
            @"{ ""effect"": ""fanRateMultiplier"", ""value"": 1.15 }")]
        public void RewardAndPayload_AuthorTheSameEffectVocabulary(string rewardJson, string payloadJson)
        {
            var asReward = (GrantModifierEffect)ChapterJsonImporter.ParseRewardEffect(rewardJson, "reward 'r'");
            var asPayload = (GrantModifierEffect)ChapterJsonImporter.ParsePayload(payloadJson, "upgrade 'u'");

            Assert.IsNotNull(asReward, "the reward site accepts the name");
            Assert.AreEqual(asPayload.Target, asReward.Target);
            Assert.AreEqual(asPayload.Operation, asReward.Operation);
            Assert.AreEqual(asPayload.Value, asReward.Value, 1e-9);
            CollectionAssert.AreEqual(asPayload.Qualifiers, asReward.Qualifiers);
        }

        // a name the family does not know is the one check either site still makes
        [Test]
        public void RewardEffect_WithAnUnknownName_IsRefused()
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: reward 'r' names unknown effect 'teleport' - no GameEffect maps to it.");

            Assert.IsNull(ChapterJsonImporter.ParseRewardEffect(
                @"{ ""type"": ""teleport"", ""value"": 1 }", "reward 'r'"));
        }

        // a multiplier naming nothing could never apply, and a non-positive one
        // would zero or negate the production stack it lands in - neither is
        // written, and the upgrade's absent payload is what boot validation
        // then reports
        [TestCase(@"{ ""effect"": ""currencyPerSecMultiplier"", ""value"": 1.5 }",
            "ChapterJsonImporter: upgrade 'x' currencyPerSecMultiplier names no affected currencies - the multiplier could never apply. Refusing it - fix the JSON and re-import.")]
        [TestCase(@"{ ""effect"": ""currencyPerSecMultiplier"", ""affects"": [""cash""], ""value"": 0 }",
            "ChapterJsonImporter: upgrade 'x' has a non-positive currencyPerSecMultiplier (0). Refusing it - fix the JSON and re-import.")]
        public void Payload_CurrencyPerSecMultiplier_RefusesWhatCouldNeverApply(string json, string expectedError)
        {
            LogAssert.Expect(LogType.Error, expectedError);

            Assert.IsNull(ChapterJsonImporter.ParsePayload(json, "upgrade 'x'"));
        }
    }
}
