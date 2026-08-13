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

        // the contribution parse path (design doc section 12, rule 13): `feeds`
        // maps onto the quantity enum, the gate onto the Condition family, an
        // absent gate means always-on, and the id is carried through because a
        // modifiable number is named (rule 11)
        [Test]
        public void Producer_Contributions_MapOntoLines()
        {
            var lines = ChapterJsonImporter.ParseProducerContributions(@"{
                ""id"": ""jam"",
                ""contributions"": [
                    { ""id"": ""jam_cash"", ""currency"": ""cash"", ""amount"": 1, ""feeds"": ""yield"" },
                    { ""id"": ""jam_rehearsal_press"", ""currency"": ""rehearsal"", ""amount"": 2, ""feeds"": ""yield"", ""gate"": { ""type"": ""flagSet"", ""flag"": ""covers"" } },
                    { ""id"": ""jam_rehearsal_trickle"", ""currency"": ""rehearsal"", ""amount"": 1, ""feeds"": ""rate"", ""tags"": [ ""trickle"" ], ""gate"": { ""type"": ""flagSet"", ""flag"": ""covers"" } }
                ]
            }");

            Assert.AreEqual(3, lines.Count);

            Assert.AreEqual("jam_cash", lines[0].Id);
            Assert.AreEqual("cash", lines[0].CurrencyId);
            Assert.AreEqual(1.0, lines[0].Amount, 1e-9);
            Assert.AreEqual(ProductionFeed.Yield, lines[0].Feeds);
            Assert.IsNull(lines[0].Gate, "no gate = always on");

            var gate = lines[1].Gate as FlagSetCondition;
            Assert.IsNotNull(gate, "the gate is an ordinary Condition");
            Assert.AreEqual("covers", gate.FlagId);

            Assert.AreEqual(ProductionFeed.Rate, lines[2].Feeds);
            CollectionAssert.AreEqual(new[] { "trickle" }, lines[2].Tags,
                "a line carries tags like any other definition, so a buff can name a set of them");
        }

        // an invalid line skips the whole producer - a producer missing one of its
        // lines is not the authored producer - and each refusal says why
        [TestCase(@"{ ""id"": ""jam"", ""contributions"": [ { ""id"": ""jam_cash"", ""currency"": ""cash"", ""amount"": 1, ""feeds"": ""tap"" } ] }",
            "ChapterJsonImporter: producer 'jam' contribution 'jam_cash' has unknown feeds 'tap' - a contribution is a 'rate' (per second) or a 'yield' (per firing). Skipping it - fix the JSON and re-import.")]
        // an absent `feeds` is refused rather than guessed into a rate: the two
        // quantities are not defaults of each other
        [TestCase(@"{ ""id"": ""jam"", ""contributions"": [ { ""id"": ""jam_cash"", ""currency"": ""cash"", ""amount"": 1 } ] }",
            "ChapterJsonImporter: producer 'jam' contribution 'jam_cash' declares no feeds - a contribution is a 'rate' (per second) or a 'yield' (per firing). Skipping it - fix the JSON and re-import.")]
        [TestCase(@"{ ""id"": ""jam"", ""contributions"": [ { ""id"": ""jam_cash"", ""currency"": ""cash"", ""amount"": -1, ""feeds"": ""yield"" } ] }",
            "ChapterJsonImporter: producer 'jam' contribution 'jam_cash' has a negative amount (-1). Skipping it - fix the JSON and re-import.")]
        [TestCase(@"{ ""id"": ""jam"", ""contributions"": [ { ""id"": ""jam_cash"", ""amount"": 1, ""feeds"": ""yield"" } ] }",
            "ChapterJsonImporter: producer 'jam' contribution 'jam_cash' names no currency. Skipping it - fix the JSON and re-import.")]
        // an unnamed line is unreachable by any buff naming it, including one
        // authored later against the id it was meant to have
        [TestCase(@"{ ""id"": ""jam"", ""contributions"": [ { ""currency"": ""cash"", ""amount"": 1, ""feeds"": ""yield"" } ] }",
            "ChapterJsonImporter: producer 'jam' has a contribution with no id - a modifiable number is named (design doc rule 11). Skipping it - fix the JSON and re-import.")]
        [TestCase(@"{ ""id"": ""jam"", ""contributions"": [] }",
            "ChapterJsonImporter: producer 'jam' has no contributions - it would produce nothing. Skipping it - fix the JSON and re-import.")]
        public void Producer_RefusesWhatCouldNeverPayAsAuthored(string json, string expectedError)
        {
            LogAssert.Expect(LogType.Error, expectedError);

            Assert.IsNull(ChapterJsonImporter.ParseProducerContributions(json));
        }

        // a currency never declares how it is earned, so an `earn` block on one is
        // refused rather than silently dropped (a bare {id, group} entry imports
        // fine)
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

        // reveal is a Condition, so a `revealFlag` naming a bare flag id is refused
        // rather than silently ignored - ignoring it would import the group with no
        // gate, showing it from the first frame.
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

        // A flag latch's lifetime: absence is the permanent default (every
        // pre-scope flag keeps exactly its old behavior), the spellings map,
        // and a typo never opts a flag into resetting. Sections deliberately
        // have no scope key - visibility is a live function of visibleWhen.
        [TestCase(@"{ ""id"": ""album"" }", ContentScope.PermanentInChapter, TestName = "Absent")]
        [TestCase(@"{ ""id"": ""covers"", ""scope"": ""run"" }", ContentScope.Run, TestName = "Run")]
        [TestCase(@"{ ""id"": ""album"", ""scope"": ""permanentInChapter"" }", ContentScope.PermanentInChapter, TestName = "Permanent")]
        public void FlagScope_MapsTheAuthoredSpellings(string json, ContentScope expected)
        {
            Assert.AreEqual(expected, ChapterJsonImporter.ParseFlagScope(json));
        }

        [Test]
        public void FlagScope_UnknownSpellingReportsAndDefaultsToThePermanentLatch()
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: flag has unknown scope 'Run' - a flag latch is 'run' or 'permanentInChapter'. Defaulting to permanentInChapter.");

            Assert.AreEqual(ContentScope.PermanentInChapter,
                ChapterJsonImporter.ParseFlagScope(@"{ ""id"": ""covers"", ""scope"": ""Run"" }"));
        }

        // fan accrual is production, so the three keys that would describe it on
        // the chapter are refused instead. Each is refused on PRESENCE,
        // which is what these empty spellings prove: a contents test would let
        // `{}` and `""` through silently, and the emptiest form of a stale key is
        // the one least likely to be spotted by eye.
        [TestCase(@"{ ""currency"": ""fans"", ""baseFansPerSec"": 0 }",
            "ChapterJsonImporter: fans block still carries 'baseFansPerSec' - the base fan rate is a contribution on a producer (design doc section 12, rule 13). Fix the JSON and re-import.")]
        [TestCase(@"{ ""currency"": ""fans"", ""revealFlag"": """" }",
            "ChapterJsonImporter: fans block still carries a 'revealFlag' key - accrual is gated by the contribution's gate (design doc section 12, rules 8, 9 and 13). Fix the JSON and re-import.")]
        [TestCase(@"{ ""currency"": ""fans"", ""activeWhen"": {} }",
            "ChapterJsonImporter: fans block still carries 'activeWhen' - the accrual gate moved onto the contribution's 'gate' (design doc section 12, rule 13). Fix the JSON and re-import.")]
        // and the fourth: the per-bandmate bonus is each bandmate's own fans
        // contribution, not a chapter constant added onto the rate
        [TestCase(@"{ ""currency"": ""fans"", ""perBandmateOwnedBonus"": 0 }",
            "ChapterJsonImporter: fans block still carries 'perBandmateOwnedBonus' - band size raises the fan rate because each bandmate generator CONTRIBUTES a fans line (design doc section 12, rules 11 and 13), not because a chapter constant is added onto the rate. Fix the JSON and re-import.")]
        public void FansBlock_RefusesEveryStaleKey_EvenItsEmptySpelling(string json, string expectedError)
        {
            LogAssert.Expect(LogType.Error, expectedError);
            ChapterJsonImporter.ParseFansBlockStaleKeys(json);
        }

        // the whole fans block: which currency is fans, and nothing else - accrual
        // and the band bonus are both contributions
        [Test]
        public void FansBlock_WithNoStaleKeys_ReportsNothing()
        {
            ChapterJsonImporter.ParseFansBlockStaleKeys(@"{ ""currency"": ""fans"" }");
        }

        // fan accrual is an ordinary rate line on a passive producer, and it has to
        // MAP, not merely stop being refused
        [Test]
        public void BandProducer_FanAccrual_MapsToARateLine()
        {
            var lines = ChapterJsonImporter.ParseProducerContributions(
                @"{ ""id"": ""band"", ""contributions"": [ { ""id"": ""band_fans"", ""currency"": ""fans"", ""amount"": 0.2, ""feeds"": ""rate"" } ] }");

            Assert.IsNotNull(lines);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("band_fans", lines[0].Id);
            Assert.AreEqual(ProductionFeed.Rate, lines[0].Feeds);
            Assert.AreEqual("fans", lines[0].CurrencyId);
        }

        // A multiplier carries the terms it reaches as data, so the payload can
        // name any number of them without a code change. The effect name is the
        // OPERATION now - which numbers it hits is entirely `targets`.
        [Test]
        public void Payload_Multiplier_CarriesTheTermsItReaches()
        {
            var payload = ChapterJsonImporter.ParsePayload(
                @"{ ""effect"": ""multiplier"", ""targets"": [""cash_rate""], ""value"": 1.5 }",
                "upgrade 'tight_set'") as GrantModifierEffect;

            Assert.IsNotNull(payload, "the effect maps onto a modifier");
            Assert.AreEqual(ModifierOperation.Multiply, payload.Operation);
            Assert.AreEqual(1.5, payload.Value, 1e-9);
            Assert.IsTrue(payload.Selector.Matches(TestContent.RateOf("cash")));
            Assert.IsFalse(payload.Selector.Matches(TestContent.YieldOf("cash")),
                "cash_rate is the id of ONE number - the yield is a different number with a different id");
            Assert.IsFalse(payload.Selector.Matches(new ModifierSubject("cash")),
                "and naming the number does not name the currency");
        }

        // Absent and empty are different: an empty list is the deliberate way to
        // reach every number in scope (rule 11), while a forgotten key would
        // otherwise silently buff the whole game, so it is refused.
        [Test]
        public void Payload_RefusesAnEffectThatDeclaresNoTargets()
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: upgrade 'oops' declares no targets. Author an explicit empty list to reach everything in scope, or name ids and tags.");

            var payload = ChapterJsonImporter.ParsePayload(
                @"{ ""effect"": ""multiplier"", ""value"": 1.5 }", "upgrade 'oops'") as GrantModifierEffect;

            Assert.IsNotNull(payload, "the effect still imports - the refusal is reported, not fatal");
            Assert.IsTrue(payload.Selector.ReachesEverything);
        }

        [Test]
        public void Payload_AnEmptyTargetList_ReachesEverything()
        {
            var payload = ChapterJsonImporter.ParsePayload(
                @"{ ""effect"": ""multiplier"", ""targets"": [], ""value"": 1.5 }",
                "upgrade 'everything'") as GrantModifierEffect;

            Assert.IsNotNull(payload);
            Assert.IsTrue(payload.Selector.ReachesEverything);
            Assert.IsTrue(payload.Selector.Matches(TestContent.Num("anything")));
        }

        // One vocabulary, two JSON keys. A reward's `type` and an upgrade's
        // `payload.effect` route through the same translator, so an effect name
        // authored either way builds the same object. A reward multiplying a rate is
        // coherent content, and neither site restricts which names it accepts.
        [TestCase(@"{ ""type"": ""multiplier"", ""targets"": [""cash_yield""], ""value"": 3 }",
            @"{ ""effect"": ""multiplier"", ""targets"": [""cash_yield""], ""value"": 3 }")]
        [TestCase(@"{ ""type"": ""multiplier"", ""targets"": [""practice_amp""], ""value"": 2 }",
            @"{ ""effect"": ""multiplier"", ""targets"": [""practice_amp""], ""value"": 2 }")]
        [TestCase(@"{ ""type"": ""multiplier"", ""targets"": [], ""value"": 1.15 }",
            @"{ ""effect"": ""multiplier"", ""targets"": [], ""value"": 1.15 }")]
        public void RewardAndPayload_AuthorTheSameEffectVocabulary(string rewardJson, string payloadJson)
        {
            var asReward = (GrantModifierEffect)ChapterJsonImporter.ParseRewardEffect(rewardJson, "reward 'r'");
            var asPayload = (GrantModifierEffect)ChapterJsonImporter.ParsePayload(payloadJson, "upgrade 'u'");

            Assert.IsNotNull(asReward, "the reward site accepts the name");
            Assert.AreEqual(asPayload.Selector, asReward.Selector);
            Assert.AreEqual(asPayload.Operation, asReward.Operation);
            Assert.AreEqual(asPayload.Value, asReward.Value, 1e-9);
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

        // a non-positive multiplier would zero or negate the production stack it
        // lands in, so it is never written and the upgrade's absent payload is
        // what boot validation then reports
        [TestCase(@"{ ""effect"": ""multiplier"", ""targets"": [""cash"", ""rate""], ""value"": 0 }",
            "ChapterJsonImporter: upgrade 'x' has a non-positive multiplier (0). Refusing it - fix the JSON and re-import.")]
        public void Payload_CurrencyRateMultiplier_RefusesWhatCouldNeverApply(string json, string expectedError)
        {
            LogAssert.Expect(LogType.Error, expectedError);

            Assert.IsNull(ChapterJsonImporter.ParsePayload(json, "upgrade 'x'"));
        }

        // ---- the flag-lifetime lint --------------------------------------------
        // The authoring-time half: the same two rules boot validation runs over
        // the loaded assets run here over the flat JSON, so the author hears
        // about a dead or scope-less flag at import rather than at the next play.

        // a run-scoped flag whose only setter is a permanent fact: the release
        // clears the flag and the rebuild re-asserts it from the surviving latch
        // in the same operation, so the declared scope does nothing
        [Test]
        public void FlagLint_RunScopedFlagWithOnlyPermanentSetters_IsReported()
        {
            LogAssert.Expect(LogType.Error,
                "ChapterJsonImporter: flag 'covers' is run-scoped but every setter is permanent - the release clears it and the rebuild re-asserts it in the same operation, so the scope has no effect.");
            ChapterJsonImporter.LintFlagLifetimes(@"{
                ""flags"": [ { ""id"": ""covers"", ""scope"": ""run"" } ],
                ""upgrades"": [ { ""id"": ""teach"", ""type"": ""contentUnlock"", ""scope"": ""permanentInChapter"",
                    ""payload"": { ""effect"": ""setFlag"", ""flag"": ""covers"" } } ]
            }");
        }

        // a flag no content sets is a warning, not an error: everything gated on
        // it silently never appears, but a flag set from code alone is legitimate
        // and invisible to the sweep
        [Test]
        public void FlagLint_FlagNoContentSets_IsWarnedAbout()
        {
            LogAssert.Expect(LogType.Warning,
                "ChapterJsonImporter: chapter declares flag 'orphan' but no content sets it - unless code sets it, every flagSet gate on it stays closed and the content behind them can never appear.");
            ChapterJsonImporter.LintFlagLifetimes(@"{ ""flags"": [ { ""id"": ""orphan"" } ] }");
        }

        // the run-scope rule is satisfied from any setter whose own fact resets
        // with the run, so coherent authoring stays silent
        [Test]
        public void FlagLint_RunScopedFlagWithARunScopedSetter_IsSilent()
        {
            ChapterJsonImporter.LintFlagLifetimes(@"{
                ""flags"": [ { ""id"": ""covers"", ""scope"": ""run"" } ],
                ""upgrades"": [ { ""id"": ""teach"", ""type"": ""contentUnlock"", ""scope"": ""run"",
                    ""payload"": { ""effect"": ""setFlag"", ""flag"": ""covers"" } } ]
            }");
            LogAssert.NoUnexpectedReceived();
        }

        // the capstone's declared completion flag counts as a permanent setter -
        // the completion operation latches it from the declaration, so a correct
        // chapter must not warn its boundary flag as dead
        [Test]
        public void FlagLint_CapstoneCompletionFlag_CountsAsASetter()
        {
            ChapterJsonImporter.LintFlagLifetimes(@"{
                ""flags"": [ { ""id"": ""chapter_2_unlocked"" } ],
                ""capstone"": { ""id"": ""backyard"", ""onComplete"": { ""completionFlag"": ""chapter_2_unlocked"" } }
            }");
            LogAssert.NoUnexpectedReceived();
        }

        // a setFlag REWARD is a setter through whatever names the reward - a bar
        // carries its group's scope, so a run-scoped group satisfies the rule
        [Test]
        public void FlagLint_SetFlagRewardOnARunScopedBarGroup_CountsAsARunSetter()
        {
            ChapterJsonImporter.LintFlagLifetimes(@"{
                ""flags"": [ { ""id"": ""backroom"", ""scope"": ""run"" } ],
                ""rewards"": [ { ""id"": ""open_backroom"", ""type"": ""setFlag"", ""flag"": ""backroom"" } ],
                ""bars"": { ""scope"": ""run"", ""groups"": [ { ""id"": ""learn_covers"",
                    ""bars"": [ { ""id"": ""cover_1"", ""reward"": ""open_backroom"" } ] } ] }
            }");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
