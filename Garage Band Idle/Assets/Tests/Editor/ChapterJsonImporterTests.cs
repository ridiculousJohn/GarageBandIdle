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
                            { ""type"": ""currency"", ""currency"": ""cash"", ""amount"": 100 }
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
