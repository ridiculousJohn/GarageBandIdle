using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Boot validation is chapter-scoped: content validates against the chapter
    // that OWNS it, never against whichever chapter happens to be active. The
    // load-bearing claims: a second chapter's flags are legal for its own
    // content, a flag from another chapter's declaration list is a reported
    // content error, and definitions no chapter lists still get every
    // structural check without flag false-positives.
    public class ContentValidatorTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static RewardManager NoRewards => new(Array.Empty<RewardDefinition>());

        [Test]
        public void Flags_ValidateAgainstTheOwningChapter_NotTheActiveOne()
        {
            var currencies = TestContent.MakeEconomy();
            var s1 = TestContent.MakeSection("s1", new FlagSetCondition("one"));
            var s2 = TestContent.MakeSection("s2", new FlagSetCondition("two"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                sectionIds: new List<string> { "s1" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" },
                sectionIds: new List<string> { "s2" });
            var database = new ContentDatabase(
                chapters: new[] { ch1, ch2 }, sections: new[] { s1, s2 });

            // ch1 plays the active chapter (its flags are the live context);
            // ch2's content must still validate against ch2's own declarations
            // — the pass reports nothing at all
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);
            ContentValidator.Validate(database, context, NoRewards);
        }

        [Test]
        public void FlagFromAnotherChaptersList_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var poached = TestContent.MakeSection("poached", new FlagSetCondition("two"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                sectionIds: new List<string> { "poached" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" });
            var database = new ContentDatabase(chapters: new[] { ch1, ch2 }, sections: new[] { poached });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            // "two" exists somewhere (ch2 declares it), but ch1 owns the
            // section — a flag another chapter declares can never be set while
            // ch1's FlagSystem is live, so this is a content error
            LogAssert.Expect(LogType.Error,
                "Condition: Section 'poached' (visibleWhen) references flag 'two', which the chapter does not declare.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // a chapter-listed currency's earn flag validates against the OWNING
        // chapter — another chapter declaring the same flag id must not make
        // it pass, because flag ids are chapter-local and may repeat
        [Test]
        public void CurrencyEarnFlag_ValidatesAgainstTheOwningChapter()
        {
            var currencies = TestContent.MakeEconomy();
            var poached = TestContent.MakeCurrency("stagecraft", "run", earnRevealFlag: "two", earnPerSec: 1);
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                currencyIds: new List<string> { "stagecraft" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" });
            var database = new ContentDatabase(chapters: new[] { ch1, ch2 }, currencies: new[] { poached });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Currency 'stagecraft' (earn revealFlag) references flag 'two', which the chapter does not declare.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // negative tuning drains or dead-ends instead of earning — runtime
        // fails closed on it, so validation must say why the systems look dead
        [Test]
        public void NegativeTapAndRecordBuffTuning_AreReported()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                tapBaseValue: -1, recordBuffPerRecord: -0.02);
            var database = new ContentDatabase(chapters: new[] { ch1 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' has a negative tapBaseValue (-1) — every Jam would drain cash.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' has a negative recordBuff perRecord (-0.02).");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // stale/unlisted definitions keep every structural check; only the
        // flag-known checks are skipped — no chapter's declaration list
        // governs an orphan
        [Test]
        public void OrphanedContent_KeepsStructuralChecks_WithoutFlagFalsePositives()
        {
            var currencies = TestContent.MakeEconomy();
            var stale = TestContent.MakeGenerator("stale", "cash", -5, 1.15, 1, new FlagSetCondition("ghost"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = new ContentDatabase(chapters: new[] { ch1 }, generators: new[] { stale });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            // the broken cost is reported; the undeclared 'ghost' flag is not
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Generator 'stale' has a non-positive base cost (-5) — it would be free to buy.");
            ContentValidator.Validate(database, context, NoRewards);
        }
    }
}
