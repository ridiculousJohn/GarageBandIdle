using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
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
            var context = new ConditionContext(TestContent.MakeEconomy(), null,
                new FlagSystem(chapter.FlagIds), database: database);

            // "resets on whose release?" has no answer - a global currency is
            // held by a pool no release touches, so one of the two declarations
            // is a mistake and nothing can tell which
            LogAssert.Expect(LogType.Error,
                "ContentValidator: currency group 'roadies' is placed Global and also resets on album release - a global currency is held by a pool no release touches, so the two cannot both be true.");

            ContentValidator.Validate(database, context, NoRewards);
        }

        [Test]
        public void GroupWithNoPlacement_IsReported()
        {
            var chapter = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            // replaces the standard 'run' group rather than standing alone: every
            // standard currency still needs its group to resolve, or this fixture
            // reports unknown-group errors it is not about
            var unmigrated = TestContent.MakeGroup("run", true, CurrencyPlacement.None);
            var database = TestContent.MakeDatabase(chapters: new[] { chapter },
                currencyGroups: new[] { unmigrated, TestContent.MakeGroup("permanent", false, CurrencyPlacement.Global) });
            var context = new ConditionContext(TestContent.MakeEconomy(), null,
                new FlagSystem(chapter.FlagIds), database: database);

            // the un-migrated field: its currencies land in no pool at all, so
            // every balance in the group silently reads zero
            LogAssert.Expect(LogType.Error,
                "ContentValidator: currency group 'run' has no placement set (None) - its currencies would land in no pool and every balance would read zero. Set it to Chapter or Global.");

            ContentValidator.Validate(database, context, NoRewards);
        }

        [Test]
        public void Flags_ValidateAgainstTheOwningChapter_NotTheActiveOne()
        {
            var currencies = TestContent.MakeEconomy();
            var s1 = TestContent.MakeSection("s1", new FlagSetCondition("one"));
            var s2 = TestContent.MakeSection("s2", new FlagSetCondition("two"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                sectionIds: new List<string> { "s1" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" },
                sectionIds: new List<string> { "s2" }, index: 2);
            var database = TestContent.MakeDatabase(
                chapters: new[] { ch1, ch2 }, sections: new[] { s1, s2 });

            // ch1 plays the active chapter (its flags are the live context);
            // ch2's content must still validate against ch2's own declarations
            // - the pass reports nothing at all
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
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" }, index: 2);
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 }, sections: new[] { poached });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            // "two" exists somewhere (ch2 declares it), but ch1 owns the
            // section - a flag another chapter declares can never be set while
            // ch1's FlagSystem is live, so this is a content error
            LogAssert.Expect(LogType.Error,
                "Condition: Section 'poached' (visibleWhen) references flag 'two', which the chapter does not declare.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // The currency half of the same guarantee. A chapter's roster says which
        // currencies its economy owns, so a later chapter's local currency is
        // valid content the ACTIVE chapter's pool cannot resolve - and resolving
        // it there is what made every non-frontier chapter's currencies look
        // broken while only one chapter existed to hide it.
        [Test]
        public void Currencies_ValidateAgainstTheOwningChapter_NotTheActiveOne()
        {
            var currencies = TestContent.MakeEconomy();
            var merch = TestContent.MakeCurrency("merch", "run");
            var stall = TestContent.MakeGenerator("stall", "merch", 10, 1.1, 1, costCurrency: "merch");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "merch" },
                generatorIds: new List<string> { "stall" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 },
                generators: new[] { stall },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { merch });

            // ch1 is the active chapter and has never heard of merch; ch2's
            // generator both costs and produces it, and the pass says nothing
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);
            ContentValidator.Validate(database, context, NoRewards);
        }

        [Test]
        public void CurrencyFromAnotherChaptersRoster_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var merch = TestContent.MakeCurrency("merch", "run");
            var poached = TestContent.MakeGenerator("poached", "cash", 10, 1.1, 1, costCurrency: "merch");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                generatorIds: new List<string> { "poached" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 },
                generators: new[] { poached },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { merch });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            // merch is a real currency, and ch1's generator still cannot charge
            // in it: the balance would live in ch2's pool, which ch1's economy
            // never routes to. Reported as undeclared, not as unknown.
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Generator 'poached' (cost currency) references currency id 'merch', which chapter 'ch1' does not declare - add it to the chapter's currency roster, or reference a currency the chapter owns (globals are reachable from every chapter).");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // A roster is only read when an economy is CONSTRUCTED from it, and only
        // the frontier chapter is ever constructed - so a later chapter could
        // declare a roster the factory would refuse and boot validation would
        // pass it. Both checks live in ChapterCurrencies now, which is what the
        // factory fills its pool from, so the two cannot disagree.
        [Test]
        public void LaterChapterRosteringAGlobalCurrency_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "records" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            // records lives in the startup pool; a chapter rostering it asks for
            // a second balance, and every read would pick one arbitrarily
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: chapter 'ch2' roster names currency 'records', whose group 'permanent' is placed Global - it is held by the startup pool and must not be in a chapter roster.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        [Test]
        public void LaterChapterRosteringAnUnknownCurrency_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" }, index: 2,
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: chapter 'ch2' roster names unknown currency id 'merch'. Re-run the chapter import.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // both of a currency's lifetime facts come from its group, so a group
        // reference resolving to nothing leaves it in no pool and surviving every
        // release. CurrencyManager reports it only for currencies it was built
        // with - the frontier pool and the permanent one.
        [Test]
        public void CurrencyWithAnUnknownGroup_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var stray = TestContent.MakeCurrency("merch", "ghost_group");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { stray });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: currency 'merch' references unknown group id 'ghost_group' - placement and the album-release reset both come from the group, so it would land in no pool and survive every release.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // What a generator PAYS INTO was checked only by GeneratorSystem, which
        // sees one chapter's generators - so this escaped the boot pass entirely
        // for an orphan or a later chapter's generator.
        [Test]
        public void GeneratorProducingAnUnknownCurrency_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var orphan = TestContent.MakeGenerator("ghost_output", "merch", 10, 1.1, 1);
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, generators: new[] { orphan });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Generator 'ghost_output' (produces) references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // a chapter-listed producer's config gate validates against the OWNING
        // chapter - another chapter declaring the same flag id must not make
        // it pass, because flag ids are chapter-local and may repeat
        [Test]
        public void ProducerGateFlag_ValidatesAgainstTheOwningChapter()
        {
            var currencies = TestContent.MakeEconomy();
            var poached = TestContent.MakeProducer("busk", new List<ProductionConfig>
            {
                new("cash", 1, ProductionTrigger.Tap, new FlagSetCondition("two"), ModifierTarget.None),
            });
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "one" },
                producerIds: new List<string> { "busk" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans", "two" }, index: 2);
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 }, producers: new[] { poached });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "Condition: Producer 'busk' (config for 'cash') (gate) references flag 'two', which the chapter does not declare.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // negative tuning drains or dead-ends instead of earning - runtime
        // fails closed on it, so validation must say why the systems look dead
        [Test]
        public void NegativeRecordBuffTuning_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                recordBuffPerRecord: -0.02);
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' has a negative recordBuff perRecord (-0.02).");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // A producer IS its production configs, and every field is trusted per
        // firing: broken tuning must say why the runtime (which fails closed -
        // skipped configs, zeroed compositions) looks mysteriously dead. The
        // importer refuses to write any of these, so reaching them means a
        // stale or hand-built asset.
        [Test]
        public void BrokenProducerConfigs_AreReported()
        {
            var currencies = TestContent.MakeEconomy();
            var broken = TestContent.MakeProducer("broken", new List<ProductionConfig>
            {
                new("cash", -1, ProductionTrigger.Tap, null, ModifierTarget.None),
                new("merch", 1, ProductionTrigger.None, null, ModifierTarget.None),
                // FanRate is authorable since 5.7 (fan accrual is a config); what
                // a config still cannot compose is a QUALIFIED target - composed
                // through Global(...) it would read an empty bucket and scale by
                // nothing, which looks like it worked
                new("cash", 1, ProductionTrigger.Tick, null, ModifierTarget.GeneratorOutput),
            });
            var hollow = TestContent.MakeProducer("hollow", new List<ProductionConfig>());
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                producerIds: new List<string> { "broken", "hollow" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, producers: new[] { broken, hollow });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'broken' (config for 'cash') has a negative amount (-1).");
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Producer 'broken' (config for 'merch') references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'broken' (config for 'merch') has trigger None (uninitialized) - it would never fire.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'broken' (config for 'cash') declares composition 'GeneratorOutput', which a config cannot compose - it must be a defined target that composes globally (a qualified target like GeneratorOutput would read an empty bucket).");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Producer 'hollow' has no production configs - it would produce nothing.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // stale/unlisted definitions keep every structural check; only the
        // flag-known checks are skipped - no chapter's declaration list
        // governs an orphan
        [Test]
        public void OrphanedContent_KeepsStructuralChecks_WithoutFlagFalsePositives()
        {
            var currencies = TestContent.MakeEconomy();
            var stale = TestContent.MakeGenerator("stale", "cash", -5, 1.15, 1, new FlagSetCondition("ghost"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, generators: new[] { stale });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            // the broken cost is reported; the undeclared 'ghost' flag is not
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Generator 'stale' has a non-positive base cost (-5) - it would be free to buy.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // the importer refuses to write either of these, so reaching them means
        // a stale asset from before the payload declared its currencies - the
        // same belt-and-braces the reward and generator value checks get
        [Test]
        public void PerSecMultiplierPayload_UnappliableTuning_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var empty = TestContent.MakeUpgrade("empty_affects", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(ModifierTarget.CurrencyProduction, ModifierOperation.Multiply, 1.5, new List<string>()), costAmount: 100);
            var zeroed = TestContent.MakeUpgrade("zeroed", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(ModifierTarget.CurrencyProduction, ModifierOperation.Multiply, 0, new List<string> { "cash" }), costAmount: 100);
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "empty_affects", "zeroed" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { empty, zeroed });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'empty_affects' (payload) targets CurrencyProduction but names nothing to affect - the modifier could never apply.");
            LogAssert.Expect(LogType.Error,
                "GameEffect: Upgrade 'zeroed' (payload) has a non-positive multiplier (0).");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // a buff is bought, so it must cost something; a content unlock is
        // applied when its gate holds, so costing nothing is right for it - the
        // check reads the upgrade's type, never its id
        [Test]
        public void BuffWithNoCost_IsReported_AndAFreeContentUnlockIsNot()
        {
            var currencies = TestContent.MakeEconomy();
            var free = TestContent.MakeUpgrade("free_buff", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1));
            var reveal = TestContent.MakeUpgrade("reveal", UpgradeType.ContentUnlock, ContentScope.PermanentInChapter,
                null, new SetFlagEffect("fans"));
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "free_buff", "reveal" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { free, reveal });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            // only the buff reports - an unexpected second error would fail here
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Upgrade 'free_buff' is a buff with no cost - it would be free to buy.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // a buff must be payable, which takes two checks: an amount with no
        // currency to charge is free in practice, and a currency it does name
        // has to resolve. A content unlock charges nothing and needs neither.
        [Test]
        public void UpgradeCostCurrency_MustBeNamedAndResolve_WhenTheUpgradeCostsAnything()
        {
            var currencies = TestContent.MakeEconomy();
            var unnamed = TestContent.MakeUpgrade("unnamed_currency", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costCurrencyId: "", costAmount: 250);
            var ghost = TestContent.MakeUpgrade("ghost_cost_currency", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, 1), costCurrencyId: "merch", costAmount: 250);
            var free = TestContent.MakeUpgrade("free_reveal", UpgradeType.ContentUnlock,
                ContentScope.PermanentInChapter, null, new SetFlagEffect("fans"), costCurrencyId: "");
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "unnamed_currency", "ghost_cost_currency", "free_reveal" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { unnamed, ghost, free });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Upgrade 'unnamed_currency' costs 250 but names no cost currency - the purchase would charge nothing.");
            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Upgrade 'ghost_cost_currency' (cost currency) references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            // the free content unlock reports nothing - an unexpected third
            // error would fail here
            ContentValidator.Validate(database, context, NoRewards);
        }

        // The permanent income buff and every capstone gate read the cumulative
        // Records total, and the balance is what the player reads as permanent
        // progress. Filing Records in a resetting group makes those disagree, and
        // the "derived modifiers carry no scope" rule rests on this group flag
        // being right - nothing else checks that the asset was filed correctly.
        [Test]
        public void RecordsInAResettingGroup_IsReported()
        {
            // the misfiling lives in the DATABASE, because that is what boot
            // validation reads: a currency's group is content, so a pool built
            // beside it would be describing the scenario somewhere nothing looks
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
            var context = new ConditionContext(new CurrencyManager(groups, currencies), null,
                new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Records currency 'records' is in a currency group that resets on album release - permanent progress would return to zero every release.");
            ContentValidator.Validate(database, context, NoRewards);
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
            var context = new ConditionContext(new CurrencyManager(groups, currencies), null,
                new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' fans currency 'fans' is in a currency group that survives an album release - fans would compound across runs and inflate the Records payout.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // Before 5.7 this was impossible rather than checked: fan accrual was its
        // own system and only ever composed FanRate, so no income multiplier could
        // reach it. Fan production is an ordinary config now, so this list is the
        // only thing keeping Records off the fan rate - and Records inflating fans
        // would let time away shortcut the Records payout (design doc section 11),
        // the same failure the reset-on-release check guards from the other side.
        [Test]
        public void FansCurrencyInRecordBuffAffects_IsRefused()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                recordBuffAffects: new List<string> { "cash", "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' lists its fans currency 'fans' in recordBuff affects - the Records multiplier must never reach the fan rate, or time away shortcuts the Records payout (design doc section 11).");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // an unresolvable fans currency reports once, as a bad reference - the group
        // question is only asked of a currency that exists
        [Test]
        public void UnknownFansCurrency_ReportsOnlyTheBadReference()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" }, fansCurrencyId: "ghost");
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Chapter 'ch1' (fans currency) references currency id 'ghost', which resolves to no CurrencyDefinition asset.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // a negative starting value puts the currency in debt at boot and again
        // after every release, which resets balances back to it
        [Test]
        public void NegativeStartingValue_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var indebted = TestContent.MakeCurrency("merch", "run", startingValue: -5);
            // added to the standard economy rather than replacing it: the chapter
            // still needs a cash for its recordBuff and a fans for its fans
            // config, and those cannot be the same currency as each other (rule
            // 11) let alone both be merch
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                currencyIds: new List<string> { "cash", "fans", "merch" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 },
                currencies: new List<CurrencyDefinition>(TestContent.StandardCurrencies()) { indebted });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Currency 'merch' has a negative starting value (-5) - it would start in debt at boot and after every album release.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // the module list is instantiated in order with no de-duplication, so a
        // repeat is two of the same module wired to the same systems
        [Test]
        public void SectionListingAModuleTwice_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var doubled = TestContent.MakeSection("doubled", null,
                new List<string> { "module/tap", "module/tap" });
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                sectionIds: new List<string> { "doubled" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { doubled });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'doubled' lists module 'module/tap' more than once - it would be instantiated twice.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // the starting chapter is the lowest index, so an index is an ordinal:
        // sharing one makes which chapter starts arbitrary
        [Test]
        public void ChaptersSharingAnIndex_AreReported()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" });
            var ch2 = TestContent.MakeChapter("ch2", new List<string> { "fans" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1, ch2 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapters 'ch1' and 'ch2' share index 1 - which one starts would be arbitrary.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // the capstone gate is the primary pacing knob (design doc section 11): at
        // zero the chapter has no length, and the declared flag list is the
        // chapter's whole reveal vocabulary, so a blank or repeated entry is a slip
        [Test]
        public void ZeroCapstoneGate_AndFlagListSlips_AreReported()
        {
            var currencies = TestContent.MakeEconomy();
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans", "covers", "covers", "" },
                capstoneRecordsGate: 0);
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' has a non-positive capstoneRecordsGate (0) - the capstone would unlock before play starts.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' declares flag 'covers' more than once.");
            LogAssert.Expect(LogType.Error,
                "ContentValidator: Chapter 'ch1' declares an empty flag id.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // a group with no bars reveals an empty region and can never satisfy a
        // barsCompleted gate, so cut_demo's leg would wait forever
        [Test]
        public void BarGroupWithNoBars_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var group = TestContent.MakeBarGroup("learn_covers", new FlagSetCondition("fans"), new List<string>());
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                barGroupIds: new List<string> { "learn_covers" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, barGroups: new[] { group });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Bar group 'learn_covers' has no bars - it can never complete one.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // the importer reports a module-less section but still writes it, so boot
        // validation is what catches it in loaded content - the same pairing the
        // empty bar group and every other content check here has
        [Test]
        public void SectionWithNoModules_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var hollow = TestContent.MakeSection("hollow", null, new List<string>());
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                sectionIds: new List<string> { "hollow" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, sections: new[] { hollow });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Section 'hollow' has no modules - its reveal would show an empty region.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // slice 8's runtime will trust every tier field, so a tier that cannot be
        // failed, cannot be won, or pays nothing has to report before it exists:
        // only timed tiers can fail (design doc section 6.1), a null goal is won on
        // entry, and an event's reward magnitude is the dial that makes it worth
        // entering at all
        [Test]
        public void IncoherentEventTiers_AreReported()
        {
            var currencies = TestContent.MakeEconomy();
            var reward = TestContent.MakeTapValueReward("tap_x2", 2);
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
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

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
            ContentValidator.Validate(database, context, rewards);
        }

        // A tier's scope is how long its clear lasts, and whatever it pays projects
        // from that clear - so an unscoped tier leaves the grant with no lifetime and
        // no reset path, which ModifierSystem would then refuse at runtime. Zero is
        // the uninitialized state a hand-built or un-migrated asset lands on.
        [Test]
        public void EventTierWithNoScope_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var rewards = new RewardManager(new[] { TestContent.MakeTapValueReward("tap_x2", 2) });
            var unscoped = TestContent.MakeEvent("unscoped", new List<EventTier>
            {
                TestContent.MakeTier(1, "tap_x2", new CurrencyBalanceCondition("cash", 500),
                    scope: ContentScope.None),
            });
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                eventIds: new List<string> { "unscoped" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, events: new[] { unscoped });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'unscoped' tier 1 has scope None (uninitialized) - a tier clear needs a declared lifetime for anything to project from.");
            ContentValidator.Validate(database, context, rewards);
        }

        // an event with no tiers has nothing to enter at all
        [Test]
        public void EventWithNoTiers_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var empty = TestContent.MakeEvent("empty", new List<EventTier>());
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                eventIds: new List<string> { "empty" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, events: new[] { empty });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ContentValidator: Event 'empty' has no tiers - there would be nothing to enter.");
            ContentValidator.Validate(database, context, NoRewards);
        }

        // a declared currency that resolves to no asset would silently multiply
        // nothing; it reports through the same reference check every other
        // currency id goes through
        [Test]
        public void PerSecMultiplierPayload_UnknownAffectedCurrency_IsReported()
        {
            var currencies = TestContent.MakeEconomy();
            var upgrade = TestContent.MakeUpgrade("ghost_currency", UpgradeType.Buff, ContentScope.Run,
                null, new GrantModifierEffect(ModifierTarget.CurrencyProduction, ModifierOperation.Multiply, 1.5, new List<string> { "cash", "merch" }), costAmount: 100);
            var ch1 = TestContent.MakeChapter("ch1", new List<string> { "fans" },
                upgradeIds: new List<string> { "ghost_currency" });
            var database = TestContent.MakeDatabase(chapters: new[] { ch1 }, upgrades: new[] { upgrade });
            var context = new ConditionContext(currencies, null, new FlagSystem(ch1.FlagIds), database: database);

            LogAssert.Expect(LogType.Error,
                "ChapterCurrencies: Upgrade 'ghost_currency' (payload) references currency id 'merch', which resolves to no CurrencyDefinition asset.");
            ContentValidator.Validate(database, context, NoRewards);
        }
    }
}
