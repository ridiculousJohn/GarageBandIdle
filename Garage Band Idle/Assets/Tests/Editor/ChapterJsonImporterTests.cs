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

        [Test]
        public void Condition_CompoundChildWithNoType_IsReportedAndSkipped()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var context = TestContent.MakeContext(currencies, flags: flags);

            LogAssert.Expect(LogType.Error, "ChapterJsonImporter: compound condition has a child with no type. Skipping it.");
            var condition = ChapterJsonImporter.ParseCondition(
                @"{ ""type"": ""compound"", ""all"": [ {}, { ""type"": ""flagSet"", ""flag"": ""fans"" } ] }");

            Assert.IsFalse(condition.Evaluate(context));
            flags.Set("fans");
            Assert.IsTrue(condition.Evaluate(context), "the surviving child governs the gate");
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
        [TestCase(@"{ ""id"": ""jam"", ""production"": [ { ""currency"": ""cash"", ""amount"": 1, ""trigger"": ""tap"", ""composes"": ""fanRate"" } ] }",
            "ChapterJsonImporter: producer 'jam' production for 'cash' has unknown composes 'fanRate' - a module-held config composes 'tapValue' or nothing. Skipping the producer - fix the JSON and re-import.")]
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

        // a per-sec multiplier carries the currencies it affects as data, so the
        // payload can name any number of them without a code change - the same
        // contract constants.recordBuff.affects follows
        [Test]
        public void Payload_CurrencyPerSecMultiplier_CarriesItsAffectedCurrencies()
        {
            var payload = ChapterJsonImporter.ParsePayload(
                @"{ ""effect"": ""currencyPerSecMultiplier"", ""affects"": [""cash"", ""merch""], ""value"": 1.5 }",
                "upgrade 'tight_set'") as CurrencyPerSecMultiplierPayload;

            Assert.IsNotNull(payload, "the effect maps onto the currency-declaring payload");
            Assert.AreEqual(1.5, payload.Value, 1e-9);
            CollectionAssert.AreEqual(new[] { "cash", "merch" }, payload.AffectsCurrencyIds);
        }

        // a multiplier naming nothing could never apply, and a non-positive one
        // would zero or negate the production stack it lands in - neither is
        // written, and the upgrade's absent payload is what boot validation
        // then reports
        [TestCase(@"{ ""effect"": ""currencyPerSecMultiplier"", ""value"": 1.5 }",
            "ChapterJsonImporter: upgrade 'x' currencyPerSecMultiplier names no affected currencies - the multiplier could never apply. Importing no payload - fix the JSON and re-import.")]
        [TestCase(@"{ ""effect"": ""currencyPerSecMultiplier"", ""affects"": [""cash""], ""value"": 0 }",
            "ChapterJsonImporter: upgrade 'x' has a non-positive currencyPerSecMultiplier (0). Importing no payload - fix the JSON and re-import.")]
        public void Payload_CurrencyPerSecMultiplier_RefusesWhatCouldNeverApply(string json, string expectedError)
        {
            LogAssert.Expect(LogType.Error, expectedError);

            Assert.IsNull(ChapterJsonImporter.ParsePayload(json, "upgrade 'x'"));
        }
    }
}
