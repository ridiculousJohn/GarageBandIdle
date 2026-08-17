using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Loop;
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

        private const string RecordsId = GameManager.RecordsCurrencyId;

        // A producer with yield contributions needs some section module presenting
        // it, or boot validation reports a surface nobody can press. Fixtures about
        // a producer's CONTRIBUTIONS author the presenting section so they stay
        // coherent content rather than tripping an unrelated rule.
        private static SectionDefinition TapSectionFor(string producerId)
            => TestContent.MakeSection($"presents_{producerId}", null,
                modules: new List<SectionModule> { new("module/tap", producerId) });

        // Currency group placement (design doc section 12, rule 12) is checked
        // here and nowhere else: group assets are hand-authored, never generated
        // from the chapter JSON, so there is no importer to refuse a bad
        // combination at. Both cases describe an asset that already exists.
        [Test]
        public void GlobalGroupThatAlsoResetsOnRelease_IsReported()
        {
            var chapter = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            // a group id no standard currency is filed under, so this fixture
            // reports the placement mistake and nothing else - the Records and
            // fans group checks read the same definitions and would otherwise
            // fire on whatever landed in an incoherent group
            var incoherent = TestContent.MakeGroup("roadies", true, CurrencyPlacement.Global);
            // alongside the standard groups, not instead of them: the Records and
            // fans checks read their currency's group from this same list, and a
            // group that resolves to nothing answers "does not reset" - which
            // would fire the fans check on a fixture that is not about fans
            var database = TestContent.MakeDatabase(chapters: new[] { chapter },
                currencyGroups: new List<CurrencyGroupDefinition>(TestContent.StandardGroups()) { incoherent });

            // "resets on whose release?" has no answer - a global currency is
            // held by a pool no release touches, so one of the two declarations
            // is a mistake and nothing can tell which
            LogAssert.Expect(LogType.Error,
                "ContentValidator: currency group 'roadies' is placed Global and also resets on album release - a global currency is held by a pool no release touches, so the two cannot both be true.");

            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        [Test]
        public void GroupWithNoPlacement_IsReported()
        {
            var chapter = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            // stands in for the standard 'run' group rather than beside it: every
            // standard currency still needs its group to resolve, or this fixture
            // reports unknown-group errors it is not about
            var unmigrated = TestContent.MakeGroup("run", true, CurrencyPlacement.None);
            var database = TestContent.MakeDatabase(chapters: new[] { chapter },
                currencyGroups: new[] { unmigrated, TestContent.MakeGroup("permanent", false, CurrencyPlacement.Global) });

            // the un-migrated field: its currencies land in no pool at all, so
            // every balance in the group silently reads zero
            LogAssert.Expect(LogType.Error,
                "ContentValidator: currency group 'run' has no placement set (None) - its currencies would land in no pool and every balance would read zero. Set it to Chapter or Global.");

            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        [Test]
        public void Flags_ValidateAgainstTheOwningChapter_NotTheActiveOne()
        {
            var s1 = TestContent.MakeSection("s1", new FlagSetCondition("one"));
            var s2 = TestContent.MakeSection("s2", new FlagSetCondition("two"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                sectionIds: new List<string> { "s1" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" },
                sectionIds: new List<string> { "s2" }, index: 2);
            var database = TestContent.MakeDatabase(
                chapters: new[] { ch1, ch2 }, sections: new[] { s1, s2 });

            // ch2's content validates against ch2's own declarations, whichever
            // chapter is being played - the pass reports nothing at all
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        [Test]
        public void FlagFromAnotherChaptersList_IsReported()
        {
            var poached = TestContent.MakeSection("poached", new FlagSetCondition("two"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                sectionIds: new List<string> { "poached" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" }, index: 2);
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 }, sections: new[] { poached });

            // "two" exists somewhere (ch2 declares it), but ch1 owns the
            // section - a flag another chapter declares can never be set while
            // ch1's FlagSystem is live, so this is a content error
            LogAssert.Expect(LogType.Error,
                "Condition: Section 'poached' (visibleWhen) references flag 'two', which no scope in reach declares.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The currency half of the same guarantee. A chapter's roster says which
        // currencies its economy owns, so a later chapter's local currency is
        // valid content the ACTIVE chapter's pool cannot resolve - and resolving
        // it there is what made every non-frontier chapter's currencies look
        // broken while only one chapter existed to hide it.
        [Test]
        public void Currencies_ValidateAgainstTheOwningChapter_NotTheActiveOne()
        {
            var merch = TestContent.MakeCurrency("merch", "run");
            var stall = TestContent.MakeGenerator("stall", "merch", 10, 1.1, 1, costCurrency: "merch");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "merch" },
                generatorIds: new List<string> { "stall" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 },
                generators: new[] { stall },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { merch });

            // ch1 has never heard of merch; ch2's generator both costs and
            // produces it, and the pass says nothing
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        [Test]
        public void CurrencyFromAnotherChaptersRoster_IsReported()
        {
            var merch = TestContent.MakeCurrency("merch", "run");
            var poached = TestContent.MakeGenerator("poached", "cash", 10, 1.1, 1, costCurrency: "merch");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                generatorIds: new List<string> { "poached" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 },
                generators: new[] { poached },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { merch });

            // merch is a real currency, and ch1's generator still cannot charge
            // in it: the balance would live in ch2's pool, which ch1's economy
            // never routes to. Reported as undeclared, not as unknown.
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Generator 'poached' (cost currency) references currency id 'merch', which chapter 'ch1' does not declare - add it to the chapter's currency roster, or reference a currency the chapter owns (globals are reachable from every chapter).");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // A roster is only read when an economy is CONSTRUCTED from it, and only
        // the frontier chapter is ever constructed - so a later chapter could
        // declare a roster the factory would refuse and boot validation would
        // pass it. Both checks live in ChapterCurrencies now, which is what the
        // factory fills its pool from, so the two cannot disagree.
        [Test]
        public void LaterChapterRosteringAGlobalCurrency_IsReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "records" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 });

            // records lives in the startup pool; a chapter rostering it asks for
            // a second balance, and every read would pick one arbitrarily
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: chapter 'ch2' roster names currency 'records', whose group 'permanent' is placed Global - it is held by the startup pool and must not be in a chapter roster.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        [Test]
        public void LaterChapterRosteringAnUnknownCurrency_IsReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 });

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: chapter 'ch2' roster names unknown currency id 'merch'. Re-run the chapter import.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // both of a currency's lifetime facts come from its group, so a group
        // reference resolving to nothing leaves it in no pool and surviving every
        // release. CurrencyManager reports it only for currencies it was built
        // with - the frontier pool and the permanent one.
        [Test]
        public void CurrencyWithAnUnknownGroup_IsReported()
        {
            var stray = TestContent.MakeCurrency("merch", "ghost_group");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { stray });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: currency 'merch' references unknown group id 'ghost_group' - placement and the album-release reset both come from the group, so it would land in no pool and survive every release.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // What a generator PAYS INTO was checked only by GeneratorSystem, which
        // sees one chapter's generators - so this escaped the boot pass entirely
        // for an orphan or a later chapter's generator.
        [Test]
        public void GeneratorProducingAnUnknownCurrency_IsReported()
        {
            var orphan = TestContent.MakeGenerator("ghost_output", "merch", 10, 1.1, 1);
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, generators: new[] { orphan });

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Generator 'ghost_output' (contribution 'ghost_output_merch' for 'merch') references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // a chapter-listed producer's config gate validates against the OWNING
        // chapter - another chapter declaring the same flag id must not make
        // it pass, because flag ids are chapter-local and may repeat
        [Test]
        public void ProducerGateFlag_ValidatesAgainstTheOwningChapter()
        {
            var poached = TestContent.MakeProducer("busk",
                ("cash", 1, ProductionFeed.Yield, new FlagSetCondition("two")));
            var section = TapSectionFor("busk");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                producerIds: new List<string> { "busk" },
                sectionIds: new List<string> { section.Id });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" }, index: 2);
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 }, producers: new[] { poached },
                sections: new[] { section });

            LogAssert.Expect(LogType.Error,
                "Condition: Producer 'busk' (contribution 'busk_cash' for 'cash') (gate) references flag 'two', which no scope in reach declares.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // negative tuning drains or dead-ends instead of earning - runtime
        // fails closed on it, so validation must say why the systems look dead
        [Test]
        public void NegativeRecordBuffTuning_IsReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                recordBuffPerRecord: -0.02);
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' has a negative recordBuff perRecord (-0.02).");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // A producer IS its contributions, and every field is trusted per
        // composition: broken tuning must say why the runtime (which fails closed -
        // dropped lines, zeroed compositions) looks mysteriously dead. The
        // importer refuses to write any of these, so reaching them means a
        // stale or hand-built asset.
        [Test]
        public void BrokenProducerContributions_AreReported()
        {
            var broken = TestContent.MakeProducer("broken", new List<ProductionContribution>
            {
                TestContent.Line("broken", "cash", -1, ProductionFeed.Yield),
                // an unnamed line is unreachable by any selector naming it, which
                // is what rule 11 makes reportable rather than merely odd
                TestContent.Line("broken", "merch", 1, ProductionFeed.Rate, id: ""),
                // Feeds is the one enum left on a line, so the two states a
                // serialized int can reach - uninitialized and undefined - are
                // still worth naming
                TestContent.Line("broken", "fans", 1, ProductionFeed.None),
                TestContent.Line("broken", "records", 1, (ProductionFeed)99),
            });
            var hollow = TestContent.MakeProducer("hollow", new List<ProductionContribution>());
            var section = TapSectionFor("broken");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                producerIds: new List<string> { "broken", "hollow" },
                sectionIds: new List<string> { section.Id });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, producers: new[] { broken, hollow },
                sections: new[] { section });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'broken' (contribution 'broken_cash' for 'cash') has a negative amount (-1).");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'broken' has a contribution with no id - a modifiable number is named.");
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Producer 'broken' (contribution '' for 'merch') references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'broken' (contribution 'broken_fans' for 'fans') feeds '0', which names neither of a producer's two numbers - a contribution is a rate (per second) or a yield (per firing).");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'broken' (contribution 'broken_records' for 'records') feeds '99', which names neither of a producer's two numbers - a contribution is a rate (per second) or a yield (per firing).");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'hollow' has no contributions - it would produce nothing.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // stale/unlisted definitions keep every structural check; only the
        // flag-known checks are skipped - no chapter's declaration list
        // governs an orphan
        [Test]
        public void OrphanedContent_KeepsStructuralChecks_WithoutFlagFalsePositives()
        {
            var stale = TestContent.MakeGenerator("stale", "cash", -5, 1.15, 1, new FlagSetCondition("ghost"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, generators: new[] { stale });

            // the broken cost is reported; the undeclared 'ghost' flag is not
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Generator 'stale' has a non-positive base cost (-5) - it would be free to buy.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // Story beats are a registry like any other, so the orphan pass owes them the
        // same treatment: an empty beat shows an empty card whoever lists it. The read
        // flag is the half that is skipped, for the reason every other orphan skips its
        // flag checks - no chapter's declaration list governs a beat no chapter lists.
        [Test]
        public void OrphanStoryBeat_KeepsItsTextCheck_WithoutFlagFalsePositives()
        {
            var stale = TestContent.MakeStoryBeat("beat_stale", "", "ghost");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, storyBeats: new[] { stale });

            // the empty text is reported; the undeclared 'ghost' read flag is not
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Story beat 'beat_stale' has no text - its card would show nothing.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // A non-positive multiplier is refused; an EMPTY term list is not, because
        // rule 11 makes it mean every number in scope rather than nothing at all,
        // which is how "raise every rate" is authored. What IS reported is a term
        // nothing in the content set answers to, the one addressing mistake an open
        // vocabulary leaves.
        [Test]
        public void RatePayload_ReportsWhatCannotApply_AndAllowsAnUnqualifiedReachAll()
        {
            var reachAll = TestContent.MakeUpgrade("reach_all", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(ModifierSelector.Everything, ModifierOperation.Multiply, 1.5), costAmount: 100);
            var zeroed = TestContent.MakeUpgrade("zeroed", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("cash_rate"), ModifierOperation.Multiply, 0), costAmount: 100);
            var unresolvable = TestContent.MakeUpgrade("unresolvable", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("ch01_garage"), ModifierOperation.Multiply, 2), costAmount: 100);
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "reach_all", "zeroed", "unresolvable" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                upgrades: new[] { reachAll, zeroed, unresolvable });

            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'zeroed' (payload) has a non-positive multiplier (0).");
            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'unresolvable' (payload) targets 'ch01_garage', which no definition id, tag or produced number answers to.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // a buff is bought, so it must cost something; a content unlock is
        // applied when its gate holds, so costing nothing is right for it - the
        // check reads the upgrade's type, never its id
        [Test]
        public void BuffWithNoCost_IsReported_AndAFreeContentUnlockIsNot()
        {
            var free = TestContent.MakeUpgrade("free_buff", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1));
            var reveal = TestContent.MakeUpgrade("reveal", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                null, new SetFlagEffect("fans"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "free_buff", "reveal" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { free, reveal });

            // only the buff reports - an unexpected second error would fail here
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Upgrade 'free_buff' is a buff with no cost - it would be free to buy.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // a buff must be payable, which takes two checks: an amount with no
        // currency to charge is free in practice, and a currency it does name
        // has to resolve. A content unlock charges nothing and needs neither.
        [Test]
        public void UpgradeCostCurrency_MustBeNamedAndResolve_WhenTheUpgradeCostsAnything()
        {
            var unnamed = TestContent.MakeUpgrade("unnamed_currency", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1), costCurrencyId: "", costAmount: 250);
            var ghost = TestContent.MakeUpgrade("ghost_cost_currency", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1), costCurrencyId: "merch", costAmount: 250);
            var free = TestContent.MakeUpgrade("free_reveal", UpgradeType.ContentUnlock,
                ContentScope.PermanentInChapter, null, new SetFlagEffect("fans"), costCurrencyId: "");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "unnamed_currency", "ghost_cost_currency", "free_reveal" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { unnamed, ghost, free });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Upgrade 'unnamed_currency' costs 250 but names no cost currency - the purchase would charge nothing.");
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Upgrade 'ghost_cost_currency' (cost currency) references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            // the free content unlock reports nothing - an unexpected third
            // error would fail here
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The permanent income buff and every capstone gate read the cumulative
        // Records total, and the balance is what the player reads as permanent
        // progress. Filing Records in a resetting group makes those disagree, and
        // the "derived modifiers carry no scope" rule rests on this group flag
        // being right - nothing else checks that the asset was filed correctly.
        [Test]
        public void RecordsInAResettingGroup_IsReported()
        {
            // the misfiling lives in the DATABASE, because that is all boot
            // validation reads: a currency's group is content
            var groups = new[] { TestContent.MakeGroup("run", true) };
            var currencies = new[]
            {
                TestContent.MakeCurrency("cash", "run"),
                TestContent.MakeCurrency("fans", "run"),
                TestContent.MakeCurrency("records", "run"),
            };
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                currencies: currencies, currencyGroups: groups);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Records currency 'records' is in a currency group that resets on album release - permanent progress would return to zero every release.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The mirror of the Records check, and the reason both exist: Fans are the
        // run's performance meter feeding the Records payout, so they MUST reset
        // where Records must not. Kept across a release, fans compound and every
        // fans gate stays satisfied after the first demo - still playable, which is
        // what makes it worth reporting.
        [Test]
        public void FansSurvivingARelease_IsReported()
        {
            // in the database for the same reason as the Records case above
            var groups = new[] { TestContent.MakeGroup("permanent", false) };
            var currencies = new[]
            {
                TestContent.MakeCurrency("cash", "permanent"),
                TestContent.MakeCurrency("fans", "permanent"),
                TestContent.MakeCurrency("records", "permanent"),
            };
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                currencies: currencies, currencyGroups: groups);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' fans currency 'fans' is in a currency group that survives an album release - fans would compound across runs and inflate the Records payout.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // Fan accrual is ordinary production, reachable by any modifier that names
        // it, so this list is the only thing keeping Records off the fan rate - and
        // Records inflating fans
        // would let time away shortcut the Records payout (design doc section 11),
        // the same failure the reset-on-release check guards from the other side.
        [Test]
        public void FansCurrencyInRecordBuffAffects_IsRefused()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                recordBuffAffects: new List<string> { "cash", "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' lists its fans currency 'fans' in recordBuff affects - the Records multiplier must never reach the fan rate, or time away shortcuts the Records payout (design doc section 11).");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // an unresolvable fans currency reports once, as a bad reference - the group
        // question is only asked of a currency that exists
        [Test]
        public void UnknownFansCurrency_ReportsOnlyTheBadReference()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" }, fansCurrencyId: "ghost");
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Chapter 'ch1' (fans currency) references currency id 'ghost', which resolves to no CurrencyDefinition asset.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // a negative starting value puts the currency in debt at boot and again
        // after every release, which resets balances back to it
        [Test]
        public void NegativeStartingValue_IsReported()
        {
            var indebted = TestContent.MakeCurrency("merch", "run", startingValue: -5);
            // added to the standard economy rather than replacing it: the chapter
            // still needs a cash for its recordBuff and a fans for its fans
            // config, and those cannot be the same currency as each other (rule
            // 11) let alone both be merch
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { indebted });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Currency 'merch' has a negative starting value (-5) - it would start in debt at boot and after every album release.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // the module list is instantiated in order with no de-duplication, so a
        // repeat is two of the same module wired to the same systems
        [Test]
        public void SectionListingAModuleTwice_IsReported()
        {
            // a roster module twice: two generator lists in one region, each wired to
            // the same systems. Repeating an ADDRESS is legitimate now (two beat cards
            // are one prefab presenting two beats), so the key is address + id, and
            // this is the case where they match.
            var doubled = TestContent.MakeSection("doubled", null,
                new List<string> { "module/generator-list", "module/generator-list" });
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                sectionIds: new List<string> { "doubled" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { doubled });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'doubled' lists module 'module/generator-list' more than once - it would be instantiated twice.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The flag-lifetime checks run at BOTH layers: the importer lints the
        // authored JSON for early feedback, and this pass covers the loaded
        // assets - a stale or hand-edited asset can disagree with the file, and
        // boot is what sees what the game actually runs. The setters surface
        // through SetFlagEffect's own Validate (the context's listener), so no
        // code outside the family walks a payload.

        // a run-scoped flag whose only setter is a permanent fact: the release
        // clears the flag and the projection re-asserts it from the surviving
        // latch in the same operation, so the declared scope does nothing
        [Test]
        public void RunScopedFlagWithOnlyPermanentSetters_IsReported()
        {
            var setter = TestContent.MakeUpgrade("teach", UpgradeType.ContentUnlock,
                ContentScope.PermanentInChapter, null, new SetFlagEffect("covers"));
            var ch1 = TestContent.MakeChapter("ch1", null,
                flags: new List<FlagDeclaration> { new("covers", ContentScope.Run) },
                upgradeIds: new List<string> { "teach" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                upgrades: new[] { setter });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' flag 'covers' is run-scoped but every setter is permanent - the release clears it and the projection re-asserts it in the same operation, so the scope has no effect.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // a flag no content sets is a warning, not an error: everything gated
        // on it silently never appears, but a flag set from code alone is
        // legitimate and invisible to the sweep
        [Test]
        public void FlagNoContentSets_IsWarnedAbout()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "orphan" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Warning,
                "ContentValidator: Chapter 'ch1' declares flag 'orphan' but no content sets it - unless code sets it, every flagSet gate on it stays closed and the content behind them can never appear.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // the run-scope rule is satisfied from any setter kind that resets with
        // the run, so coherent authoring stays silent
        [Test]
        public void RunScopedFlagWithARunScopedSetter_IsSilent()
        {
            var setter = TestContent.MakeUpgrade("teach", UpgradeType.ContentUnlock,
                ContentScope.Run, null, new SetFlagEffect("covers"));
            var ch1 = TestContent.MakeChapter("ch1", null,
                flags: new List<FlagDeclaration> { new("covers", ContentScope.Run) },
                upgradeIds: new List<string> { "teach" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                upgrades: new[] { setter });

            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // a setFlag REWARD is a setter through whatever names the reward - a bar
        // carries its group's scope - and the reward's flags are collected once
        // at its validation, then paired per reference. Silence proves both the
        // run-scope rule and the no-content-sets warning saw the reward setter.
        [Test]
        public void RunScopedFlagSetByARunScopedBarGroupsReward_IsSilent()
        {
            var reward = TestContent.MakeReward("open_backroom", new SetFlagEffect("backroom"));
            var bar = TestContent.MakeBar("cover_1", "cash", 100, "open_backroom");
            var group = TestContent.MakeBarGroup("learn_covers", null, new List<string> { "cover_1" },
                scope: ContentScope.Run);
            var ch1 = TestContent.MakeChapter("ch1", null,
                flags: new List<FlagDeclaration> { new("backroom", ContentScope.Run) },
                barGroupIds: new List<string> { "learn_covers" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                bars: new[] { bar }, barGroups: new[] { group }, rewards: new[] { reward });

            ContentValidator.Validate(database, RecordsId, new RewardManager(new[] { reward }));
            LogAssert.NoUnexpectedReceived();
        }

        // the starting chapter is the lowest index, so an index is an ordinal:
        // sharing one makes which chapter starts arbitrary
        [Test]
        public void ChaptersSharingAnIndex_AreReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapters 'ch1' and 'ch2' share index 1 - which one starts would be arbitrary.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // the declared flag list is the chapter's whole reveal vocabulary, so a
        // blank or repeated entry is a slip
        [Test]
        public void FlagListSlips_AreReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "covers", "covers", "" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' declares flag 'covers' more than once.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' declares an empty flag id.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The capstone gate is the primary pacing knob (design doc section 11), and
        // its authored Condition is the only home for it. A NULL unlock is the
        // one case ordinary condition validation cannot report: by this codebase's
        // convention a null Condition means "no gate" and is always met, so the
        // chapter would end before it started. A non-positive THRESHOLD needs nothing
        // bespoke - every threshold condition already reports one and ThresholdIsMet
        // fails closed - so this is the only capstone-specific check.
        [Test]
        public void CapstoneWithNoUnlockCondition_IsReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", null,
                flags: new List<FlagDeclaration> { new("done") },
                capstone: new CapstoneConfig("backyard", "Backyard Party", null, "done",
                    onComplete: null));
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' capstone 'backyard' has no unlock condition - a null gate is always met, so the capstone would be offered at boot.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The completion flag IS the chapter boundary, so it has to outlive a
        // release: run-scoped, the next demo clears it and a finished chapter
        // re-opens. The second error is the lifetime sweep catching the same
        // declaration from the other side - the capstone counts as a permanent
        // setter (the operation latches the flag), so a run-scoped flag with
        // only that setter has a scope that does nothing.
        [Test]
        public void RunScopedCapstoneCompletionFlag_IsReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", null,
                flags: new List<FlagDeclaration> { new("done", ContentScope.Run) },
                capstone: new CapstoneConfig("backyard", "Backyard Party",
                    new RecordsCumulativeCondition(30), "done", onComplete: null));
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' capstone completion flag 'done' is declared Run - a chapter boundary must be permanent-in-chapter, or the next release clears it and re-opens a finished chapter.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' flag 'done' is run-scoped but every setter is permanent - the release clears it and the projection re-asserts it in the same operation, so the scope has no effect.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // Actions execute on purchase and a content unlock is never bought, so an
        // award authored on one would silently never pay - which reads as a tuning
        // problem rather than the authoring mistake it is. (The opposite danger, an
        // award PAID repeatedly by the auto-apply path, needs no check: that path
        // executes no actions, so it is not expressible.)
        [TestCase(ContentScope.Run)]
        [TestCase(ContentScope.PermanentInChapter)]
        public void ActionsOnAContentUnlock_AreReported(ContentScope scope)
        {
            var upgrade = TestContent.MakeUpgrade("payday", UpgradeType.ContentUnlock, scope,
                null, new SetFlagEffect("fans"),
                actions: new List<GameAction> { new GrantCurrencyAction("cash", 100) });
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "payday" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                upgrades: new[] { upgrade });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Upgrade 'payday' is a content unlock carrying actions - actions execute on purchase, and a content unlock is never bought, so its award would never pay. Move it to a bought buff, an event tier, or the capstone.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // A BOUGHT buff may carry awards: TryBuy charges the cost again, so
        // re-buying and re-paying is coherent rather than free. Its action ids
        // still validate like any other reference.
        [Test]
        public void ActionsOnABoughtBuff_AreAllowed_AndTheirReferencesChecked()
        {
            var upgrade = TestContent.MakeUpgrade("advance", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1),
                costAmount: 250,
                actions: new List<GameAction> { new GrantCurrencyAction("cash", 100) });
            var ch1 = TestContent.MakeChapter("ch1", null, upgradeIds: new List<string> { "advance" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { upgrade });

            // a clean pass IS the assertion
            ContentValidator.Validate(database, RecordsId, NoRewards);
            LogAssert.NoUnexpectedReceived();
        }

        // and a broken reference inside an action reports like any other id
        [Test]
        public void ActionWithAnUnknownCurrency_IsReported()
        {
            var upgrade = TestContent.MakeUpgrade("advance", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("cash_yield"), ModifierOperation.Multiply, 1),
                costAmount: 250,
                actions: new List<GameAction> { new GrantCurrencyAction("merch", 100) });
            var ch1 = TestContent.MakeChapter("ch1", null, upgradeIds: new List<string> { "advance" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { upgrade });

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Upgrade 'advance' (actions) references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The capstone's OnComplete effects are setters too, heard by the same
        // collector and recorded permanent - a chapter boundary's grants are not
        // facts a release takes back. Silence proves the capstone's payload
        // validated with the listener installed; without it, 'stagecraft' would
        // falsely warn as a flag nothing sets.
        [Test]
        public void FlagSetByTheCapstonesOnComplete_CountsAsAPermanentSetter()
        {
            var ch1 = TestContent.MakeChapter("ch1", null,
                flags: new List<FlagDeclaration> { new("done"), new("stagecraft") },
                capstone: new CapstoneConfig("backyard", "Backyard Party",
                    new RecordsCumulativeCondition(30), "done",
                    new SetFlagEffect("stagecraft")));
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            ContentValidator.Validate(database, RecordsId, NoRewards);
            LogAssert.NoUnexpectedReceived();
        }

        // A completion flag with no declared lifetime is no more a chapter boundary
        // than a run-scoped one: None is the un-migrated value a hand-edited
        // declaration holds, so the check compares for equality with
        // PermanentInChapter rather than merely excluding Run.
        [Test]
        public void CapstoneCompletionFlagWithNoScope_IsReported()
        {
            var ch1 = TestContent.MakeChapter("ch1", null,
                flags: new List<FlagDeclaration> { new("done", ContentScope.None) },
                capstone: new CapstoneConfig("backyard", "Backyard Party",
                    new RecordsCumulativeCondition(30), "done", onComplete: null));
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' capstone completion flag 'done' is declared None - a chapter boundary must be permanent-in-chapter, or the next release clears it and re-opens a finished chapter.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // A module entry's definitionId is the binding the runtime fires on, so an id
        // the chapter does not declare is a module presenting nothing - a dead button
        // or a blank card, which reads as a tuning problem rather than a typo.
        [Test]
        public void ModuleEntryNamingSomethingTheChapterDoesNotDeclare_IsReported()
        {
            var section = TestContent.MakeSection("floor", null,
                modules: new List<SectionModule> { new("module/tap", "no_such_producer") });
            var ch1 = TestContent.MakeChapter("ch1", null, sectionIds: new List<string> { "floor" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { section });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'floor' module 'module/tap' presents producer 'no_such_producer', which chapter 'ch1' does not declare - the module would present nothing.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // Membership in ANY of the chapter's content lists is not proof the id belongs
        // to this module's family. Swap a producer id and a story-beat id across two
        // entries and a family-blind check passes both - the jam producer even counts
        // as presented, by the card - while the Jam button is dead. So the module
        // declares what it needs (IChapterModule.RequiredDefinition) and the id is
        // resolved against exactly that.
        [Test]
        public void TapModulePresentingSomethingThatIsNotAProducer_IsReported()
        {
            var jam = TestContent.MakeProducer("jam",
                ("cash", 1, ProductionFeed.Yield, null));
            var beat = TestContent.MakeStoryBeat("beat_open", "It starts in the garage.");
            var section = TestContent.MakeSection("floor", null,
                modules: new List<SectionModule> { new("module/tap", "beat_open") });
            var ch1 = TestContent.MakeChapter("ch1", null,
                sectionIds: new List<string> { "floor" },
                producerIds: new List<string> { "jam" },
                storyBeatIds: new List<string> { "beat_open" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { section },
                producers: new[] { jam }, storyBeats: new[] { beat });

            // the beat IS declared by the chapter, which membership alone would
            // accept
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'floor' module 'module/tap' presents producer 'beat_open', which chapter 'ch1' does not declare - the module would present nothing.");
            // and the consequence the swap hides: nothing presents the real surface
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' producer 'jam' has yield contributions but no section module presents it - firing names one producer, so nothing could ever fire this one.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // A roster module resolves what it shows from the chapter, so an id on its
        // entry is read by nobody - it looks like a binding and is not.
        [Test]
        public void RosterModuleCarryingADefinitionId_IsReported()
        {
            var section = TestContent.MakeSection("floor", null,
                modules: new List<SectionModule> { new("module/generator-list", "drummer") });
            var ch1 = TestContent.MakeChapter("ch1", null, sectionIds: new List<string> { "floor" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { section });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'floor' module 'module/generator-list' names definition 'drummer', but that module presents a whole roster and reads no definition id.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // Who presents a producer is DERIVED from the section entries naming it, so
        // there is no second declaration to disagree with - and what is worth
        // reporting is not a missing string but the consequence, a fireable surface
        // the player cannot reach.
        [Test]
        public void FireableProducerNoSectionPresents_IsReported()
        {
            var orphaned = TestContent.MakeProducer("busk",
                ("cash", 1, ProductionFeed.Yield, null));
            var ch1 = TestContent.MakeChapter("ch1", null, producerIds: new List<string> { "busk" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, producers: new[] { orphaned });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' producer 'busk' has yield contributions but no section module presents it - firing names one producer, so nothing could ever fire this one.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // A passive producer needs no surface at all - that is what fan accrual is -
        // so the rule keys on having YIELD contributions rather than on being presented.
        [Test]
        public void PassiveProducerNoSectionPresents_IsAllowed()
        {
            var band = TestContent.MakeProducer("band",
                ("fans", 0.2, ProductionFeed.Rate, null));
            var ch1 = TestContent.MakeChapter("ch1", null, producerIds: new List<string> { "band" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, producers: new[] { band });

            ContentValidator.Validate(database, RecordsId, NoRewards);
            LogAssert.NoUnexpectedReceived();
        }

        // The presented-producer sweep has to ask the same family question the binding
        // check asks. Reading an id off ANY entry counts a module that presents no
        // producer at all as presenting one: the roster entry below is already a
        // reported mistake, and taking its id as a fireable surface forgave the consequence -
        // a Jam button no section renders. Two errors, not one.
        //
        // A roster module rather than a story card because no module declares StoryBeat
        // yet; the same hole, reachable through the family that exists today.
        [Test]
        public void ProducerIdOnAModuleThatPresentsNoProducer_DoesNotCountAsPresented()
        {
            var jam = TestContent.MakeProducer("jam",
                ("cash", 1, ProductionFeed.Yield, null));
            var section = TestContent.MakeSection("floor", null,
                modules: new List<SectionModule> { new("module/generator-list", "jam") });
            var ch1 = TestContent.MakeChapter("ch1", null,
                sectionIds: new List<string> { "floor" },
                producerIds: new List<string> { "jam" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { section },
                producers: new[] { jam });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'floor' module 'module/generator-list' names definition 'jam', but that module presents a whole roster and reads no definition id.");
            // the consequence a family-blind sweep swallowed
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' producer 'jam' has yield contributions but no section module presents it - firing names one producer, so nothing could ever fire this one.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // An entry with no address names no prefab, so it satisfies the "a section is
        // its modules" check while every check that needs the prefab is skipped. Boot is
        // where the section naming it is still in hand - ChapterScreen's instantiate
        // failure knows only the empty key it was handed.
        [Test]
        public void ModuleEntryWithNoAddress_IsReported()
        {
            var section = TestContent.MakeSection("floor", null,
                modules: new List<SectionModule> { new("", "jam") });
            var ch1 = TestContent.MakeChapter("ch1", null, sectionIds: new List<string> { "floor" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { section });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'floor' has a module entry for 'jam' with no address - there is no prefab to instantiate at reveal time.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // The bar half of the payout rule needs no check: a reward holds a
        // GameEffect, awards are GameActions, and no reward can hold one - a
        // re-completed bar re-paying is unauthorable rather than reported.

        // a group with no bars reveals an empty region and can never satisfy a
        // barsCompleted gate, so cut_demo's leg would wait forever
        [Test]
        public void BarGroupWithNoBars_IsReported()
        {
            var group = TestContent.MakeBarGroup("learn_covers", new FlagSetCondition("fans"), new List<string>());
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                barGroupIds: new List<string> { "learn_covers" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, barGroups: new[] { group });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Bar group 'learn_covers' has no bars - it can never complete one.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // the importer reports a module-less section but still writes it, so boot
        // validation is what catches it in loaded content - the same pairing the
        // empty bar group and every other content check here has
        [Test]
        public void SectionWithNoModules_IsReported()
        {
            var hollow = TestContent.MakeSection("hollow", null, new List<string>());
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                sectionIds: new List<string> { "hollow" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { hollow });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'hollow' has no modules - its reveal would show an empty region.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // slice 8's runtime will trust every tier field, so a tier that cannot be
        // failed, cannot be won, or pays nothing has to report before it exists:
        // only timed tiers can fail (design doc section 6.1), a null goal is won on
        // entry, and an event's reward magnitude is the dial that makes it worth
        // entering at all
        [Test]
        public void IncoherentEventTiers_AreReported()
        {
            var reward = TestContent.MakeCashYieldReward("tap_x2", 2);
            var rewards = new RewardManager(new RewardDefinition[] { reward });
            var goal = new CurrencyBalanceCondition("cash", 500);
            var broken = TestContent.MakeEvent("broken", new List<EventTier>
            {
                TestContent.MakeTier(1, "tap_x2", goal, timerSeconds: 0),
                TestContent.MakeTier(2, "tap_x2", null, failable: false),
                TestContent.MakeTier(2, "", goal),
            });
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                eventIds: new List<string> { "broken" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, events: new[] { broken });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'broken' tier 1 is failable but has no timer (0s) - only timed tiers can fail.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'broken' tier 2 has no goal - the tier would be won on entry.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'broken' tier 2 has a 60s timer but is not failable - the timer could never end the tier.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'broken' tier 2 has no reward - clearing it would grant nothing.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'broken' has tier number 2 following 2 - tier numbers ascend with list order, starting at 1.");
            ContentValidator.Validate(database, RecordsId, rewards);
        }

        // A tier's scope is how long its clear lasts, and whatever it pays projects
        // from that clear - so an unscoped tier leaves the grant with no lifetime and
        // no reset path, which ModifierSystem would then refuse at runtime. Zero is
        // the uninitialized state a hand-built or un-migrated asset lands on.
        [Test]
        public void EventTierWithNoScope_IsReported()
        {
            var rewards = new RewardManager(new[] { TestContent.MakeCashYieldReward("tap_x2", 2) });
            var unscoped = TestContent.MakeEvent("unscoped", new List<EventTier>
            {
                TestContent.MakeTier(1, "tap_x2", new CurrencyBalanceCondition("cash", 500),
                    scope: ContentScope.None),
            });
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                eventIds: new List<string> { "unscoped" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, events: new[] { unscoped });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'unscoped' tier 1 has scope None (uninitialized) - a tier clear needs a declared lifetime for anything to project from.");
            ContentValidator.Validate(database, RecordsId, rewards);
        }

        // an event with no tiers has nothing to enter at all
        [Test]
        public void EventWithNoTiers_IsReported()
        {
            var empty = TestContent.MakeEvent("empty", new List<EventTier>());
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                eventIds: new List<string> { "empty" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, events: new[] { empty });

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'empty' has no tiers - there would be nothing to enter.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }

        // a declared currency that resolves to no asset would silently multiply
        // nothing; it reports through the same reference check every other
        // currency id goes through
        [Test]
        public void PerSecMultiplierPayload_UnknownAffectedCurrency_IsReported()
        {
            var upgrade = TestContent.MakeUpgrade("ghost_currency", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(TestContent.Sel("cash_rate", "merch_rate"), ModifierOperation.Multiply, 1.5), costAmount: 100);
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "ghost_currency" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { upgrade });

            // A term is resolved against the WHOLE content set rather than against
            // one family (rule 11), so an unknown one is reported as a term nothing
            // answers to rather than as a bad currency id: a term does not say which
            // family it belongs to, and requiring it to would leave a buff unable to
            // name one of a generator's two output lines.
            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'ghost_currency' (payload) targets 'merch_rate', which no definition id, tag or produced number answers to.");
            ContentValidator.Validate(database, RecordsId, NoRewards);
        }
    }
}
