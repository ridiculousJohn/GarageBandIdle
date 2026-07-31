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
