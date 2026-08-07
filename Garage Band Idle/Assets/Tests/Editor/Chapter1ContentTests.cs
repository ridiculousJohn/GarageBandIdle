using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Validates the IMPORTED Chapter 1 assets against docs/chapter-01-garage.json
    // and simulates the generator unlock chain end-to-end. The chapter references
    // its content by id, so these tests resolve ids against the asset folders the
    // importer writes - the editor-test stand-in for the Addressables registries.
    // Requires 'GarageBandIdle > Import Chapter 1 JSON' to have been run.
    public class Chapter1ContentTests
    {
        private const string ChapterPath = "Assets/ScriptableObjects/Chapters/ch01_garage.asset";
        private const string SectionsFolder = "Assets/ScriptableObjects/Sections";
        private const string CurrenciesFolder = "Assets/ScriptableObjects/Currencies";
        private const string ProducersFolder = "Assets/ScriptableObjects/Producers";
        private const string GroupsFolder = "Assets/ScriptableObjects/CurrencyGroups";
        private const string GeneratorsFolder = "Assets/ScriptableObjects/Generators";
        private const string UpgradesFolder = "Assets/ScriptableObjects/Upgrades";
        private const string BarsFolder = "Assets/ScriptableObjects/Bars";
        private const string BarGroupsFolder = "Assets/ScriptableObjects/BarGroups";
        private const string EventsFolder = "Assets/ScriptableObjects/Events";
        private const string RewardsFolder = "Assets/ScriptableObjects/Rewards";
        private const string StoryBeatsFolder = "Assets/ScriptableObjects/StoryBeats";

        // Boot validation, run over the REAL shipped content instead of a
        // fixture: the same four steps GameManager.Awake takes, so anything it
        // would report at boot fails here first.
        //
        // This exists because of how slice 5.7's FanRate generalization got
        // through with a hole in it. ProductionSystem was generalized and
        // ContentValidator was not, so Chapter 1's own band producer would have
        // reported an error on every boot - and nothing caught it, because the
        // importer does not run the validator and every validator test built its
        // own broken fixture. A rule that shipped content is expected to satisfy
        // has to be exercised against shipped content.
        [Test]
        public void RealChapterContent_PassesBootValidation()
        {
            var database = new ContentDatabase();
            var permanent = EconomyContextFactory.BuildPermanentPool(database);

            ChapterDefinition starting = null;
            foreach (var chapter in database.Chapters.All)
            {
                if (starting == null || chapter.Index < starting.Index)
                    starting = chapter;
            }
            Assert.IsNotNull(starting, "no chapter assets - run 'GarageBandIdle > Import Chapter 1 JSON'");

            using var frontier = EconomyContextFactory.Build(starting, database, permanent,
                EconomyRecipe.FrontierChapter);
            Assert.IsNotNull(frontier, "the frontier economy failed to build from shipped content");

            // an unexpected Debug.LogError fails the test, so a clean run IS the
            // assertion: boot validation reports nothing about real content
            ContentValidator.Validate(database, frontier.Conditions, frontier.Rewards);
            LogAssert.NoUnexpectedReceived();
        }

        private static T LoadRequired<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset,
                $"Missing asset at '{path}'. Run 'GarageBandIdle > Import Chapter 1 JSON' first.");
            return asset;
        }

        // a section's module addresses in layout order; the definition ids each
        // entry carries are asserted where they matter
        private static string[] Addresses(SectionDefinition section)
        {
            var addresses = new string[section.Modules.Count];
            for (var i = 0; i < addresses.Length; i++)
                addresses[i] = section.Modules[i].Address;
            return addresses;
        }

        private static T[] LoadAllIn<T>(string folder) where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
            var assets = new T[guids.Length];
            for (var i = 0; i < guids.Length; i++)
                assets[i] = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
            return assets;
        }

        // the importer writes one asset per id, so id resolution in tests is a
        // folder path - the runtime equivalent is the ContentDatabase registry
        private static T LoadById<T>(string folder, string id) where T : Object
            => LoadRequired<T>($"{folder}/{id}.asset");

        private static List<GeneratorDefinition> LoadChapterGenerators(ChapterDefinition chapter)
        {
            var definitions = new List<GeneratorDefinition>();
            foreach (var id in chapter.GeneratorIds)
                definitions.Add(LoadById<GeneratorDefinition>(GeneratorsFolder, id));
            return definitions;
        }

        private static List<UpgradeDefinition> LoadChapterUpgrades(ChapterDefinition chapter)
        {
            var definitions = new List<UpgradeDefinition>();
            foreach (var id in chapter.UpgradeIds)
                definitions.Add(LoadById<UpgradeDefinition>(UpgradesFolder, id));
            return definitions;
        }

        private static CurrencyManager LoadCurrencyManager()
        {
            var groups = LoadAllIn<CurrencyGroupDefinition>(GroupsFolder);
            var currencies = LoadAllIn<CurrencyDefinition>(CurrenciesFolder);
            Assert.IsNotEmpty(groups, $"No CurrencyGroupDefinition assets under '{GroupsFolder}'.");
            Assert.IsNotEmpty(currencies, $"No CurrencyDefinition assets under '{CurrenciesFolder}'.");
            return new CurrencyManager(groups, currencies);
        }

        [Test]
        public void ChapterTuning_MatchesJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            Assert.AreEqual("ch01_garage", chapter.Id);
            Assert.AreEqual(1, chapter.Index);
            Assert.AreEqual(0.02, chapter.RecordBuff.PerRecord, 1e-9);
            CollectionAssert.AreEqual(new[] { "cash" }, chapter.RecordBuff.AffectsCurrencyIds,
                "the Records buff declares exactly the currencies it affects");

            // The chapter gate has ONE authored home now (6.5): the capstone's
            // unlock Condition. The scalar capstoneRecordsGate that used to state
            // the same 30 is deleted rather than kept in step - it was the copy
            // being read while the authored Condition was never imported at all.
            var capstone = chapter.Capstone;
            Assert.IsTrue(capstone.IsAuthored, "Chapter 1 declares a capstone");
            Assert.AreEqual("backyard_party", capstone.Id);
            var gate = capstone.Unlock as RecordsCumulativeCondition;
            Assert.IsNotNull(gate, "the capstone gate is the authored recordsCumulative Condition");
            Assert.AreEqual(30, gate.Value, 1e-9);
            Assert.AreEqual("chapter_2_unlocked", capstone.CompletionFlagId);
        }

        // The capstone's completion payload, as data: one Roadie that can only be
        // paid on acquisition, plus the chapter-advance flag that re-projects safely.
        // Its being a compound is what lets one authored block do both without the
        // capstone growing a bespoke completion handler.
        [Test]
        public void CapstoneOnComplete_GrantsARoadieOnceAndSetsTheAdvanceFlag()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            var payload = chapter.Capstone.OnComplete as CompoundEffect;
            Assert.IsNotNull(payload, "onComplete imports as a compound: a payout plus a flag");
            Assert.IsTrue(payload.ContainsOneShot, "the Roadie grant is a payout");
            Assert.AreEqual(EffectProjection.Projectable, payload.Projection,
                "the compound itself is safe to project - it filters its own one-shot children");

            var roadies = payload.Effects[0] as GrantCurrencyEffect;
            Assert.IsNotNull(roadies, "grantRoadies imports as a currency grant");
            Assert.AreEqual("roadies", roadies.CurrencyId);
            Assert.AreEqual(1.0, roadies.Amount, 1e-9);
            Assert.AreEqual(EffectProjection.OneShot, roadies.Projection,
                "paid once ever - no release, load or reprojection banks a second one");

            var flag = payload.Effects[1] as SetFlagEffect;
            Assert.IsNotNull(flag, "completionFlag imports as an ordinary setFlag");
            Assert.AreEqual("chapter_2_unlocked", flag.FlagId);
            Assert.AreEqual(EffectProjection.Projectable, flag.Projection);
        }

        // Story beats are content: a definition each, listed on the chapter. The
        // prose used to be two inline strings on the chapter asset, which is why
        // beats could not be revealed or listed like anything else.
        [Test]
        public void StoryBeats_AreContentTheChapterLists()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            CollectionAssert.AreEqual(new[] { "beat_open", "beat_capstone" }, chapter.StoryBeatIds);

            var open = LoadById<StoryBeatDefinition>(StoryBeatsFolder, "beat_open");
            Assert.IsTrue(open.Text.StartsWith("It starts in the garage"));
            Assert.IsTrue(string.IsNullOrEmpty(open.ReadFlagId),
                "Ch1 records no read latch, so nothing gates on having read a beat yet");

            var capstoneBeat = LoadById<StoryBeatDefinition>(StoryBeatsFolder, "beat_capstone");
            Assert.IsTrue(capstoneBeat.Text.Contains("first roadie"));
        }

        // Roadies is an ordinary global currency, filed in the same permanent group
        // as Records - no manager, and deliberately NOT in the chapter roster, which
        // both ChapterCurrencies and the factory refuse for a global id.
        [Test]
        public void Roadies_IsAGlobalPermanentCurrency_OutsideTheChapterRoster()
        {
            var roadies = LoadById<CurrencyDefinition>(CurrenciesFolder, "roadies");
            Assert.AreEqual("permanent", roadies.GroupId);
            Assert.AreEqual(0.0, roadies.StartingValue, 1e-9);

            var permanent = LoadById<CurrencyGroupDefinition>(GroupsFolder, "permanent");
            Assert.AreEqual(CurrencyPlacement.Global, permanent.Placement);
            Assert.IsFalse(permanent.ResetsOnAlbumRelease);

            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            CollectionAssert.DoesNotContain(chapter.CurrencyIds, "roadies",
                "a global currency in a chapter roster means two balances for one id");
        }

        [Test]
        public void ChapterFlags_MatchJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            CollectionAssert.AreEqual(new[] { "fans", "covers", "gear", "album", "chapter_2_unlocked" },
                chapter.FlagIds, "the chapter declares exactly the JSON flags array, in order");

            // the second-run flow (design doc section 2): fans, covers and gear
            // clear at every release so their systems re-arm on the re-climb;
            // album survives, so the release region stays taught (its
            // pressability is the album unlock, not the flag)
            Assert.AreEqual(ContentScope.Run, chapter.Flags[0].Scope, "fans re-arms each run");
            Assert.AreEqual(ContentScope.Run, chapter.Flags[1].Scope, "covers re-arms each run");
            Assert.AreEqual(ContentScope.Run, chapter.Flags[2].Scope, "gear re-arms each run");
            Assert.AreEqual(ContentScope.PermanentInChapter, chapter.Flags[3].Scope, "album is knowledge");

            // The capstone's one fact (6.5), and permanent for a sharper reason than
            // album's: run-scoped, the next demo would clear it and re-open a
            // finished chapter. It is both "this chapter is done" and "chapter 2 may
            // open" - nothing in Ch1 can tell those apart, so it is one flag.
            Assert.AreEqual(ContentScope.PermanentInChapter, chapter.Flags[4].Scope,
                "a finished chapter stays finished across demos");
        }

        [TestCase(0, "practice_amp", 60, 0.4)]
        [TestCase(1, "drummer", 500, 3)]
        [TestCase(2, "bassist", 4000, 20)]
        [TestCase(3, "guitarist", 30000, 130)]
        public void GeneratorValues_MatchJson(int index, string id, double baseCost, double baseOutput)
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            Assert.AreEqual(4, chapter.GeneratorIds.Count, "Chapter 1 defines exactly four generators.");
            Assert.AreEqual(id, chapter.GeneratorIds[index], "generator list order matches the JSON");

            var generator = LoadById<GeneratorDefinition>(GeneratorsFolder, id);
            Assert.AreEqual(baseCost, generator.BaseCost, 1e-9);
            Assert.AreEqual(1.15, generator.CostGrowth, 1e-9);
            Assert.AreEqual(baseOutput, generator.BaseOutput, 1e-9);
            Assert.AreEqual("cash", generator.ProducesCurrencyId);
            Assert.AreEqual("cash", generator.CostCurrencyId);
        }

        [Test]
        public void CurrencyGroups_MatchDesign()
        {
            var manager = LoadCurrencyManager();
            var groups = LoadAllIn<CurrencyGroupDefinition>(GroupsFolder);

            // Placement is what decides which POOL holds the balance (design doc
            // section 12, rule 12), and it is asserted here because nothing else
            // can: the group assets are hand-authored, so a placement left at
            // None or filed wrong is not a JSON mistake anyone re-imports away.
            // Records placed Chapter would put permanent progress in the run pool
            // and lose it on the first release.
            foreach (var (currencyId, resets, placement) in new[]
            {
                ("cash", true, CurrencyPlacement.Chapter),
                ("fans", true, CurrencyPlacement.Chapter),
                ("rehearsal", true, CurrencyPlacement.Chapter),
                ("records", false, CurrencyPlacement.Global),
            })
            {
                var definition = manager.GetDefinition(currencyId);
                Assert.IsNotNull(definition, $"currency '{currencyId}' exists");

                var group = System.Array.Find(groups, g => g.Id == definition.GroupId);
                Assert.IsNotNull(group, $"currency '{currencyId}' resolves its group '{definition.GroupId}'");
                Assert.AreEqual(resets, group.ResetsOnAlbumRelease,
                    $"currency '{currencyId}' album-release reset behavior");
                Assert.AreEqual(placement, group.Placement,
                    $"currency '{currencyId}' pool placement");
            }
        }

        [Test]
        public void GeneratorUnlockChain_FiresInOrder()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            var currencies = LoadCurrencyManager();
            var generators = new GeneratorSystem(LoadChapterGenerators(chapter), currencies, new ModifierSystem());
            var context = TestContent.MakeContext(currencies, generators, new FlagSystem());

            var amp = generators.Get("practice_amp");
            var drummer = generators.Get("drummer");
            var bassist = generators.Get("bassist");
            var guitarist = generators.Get("guitarist");

            // stage 0: tap only - nothing revealed below the 100-earned threshold
            currencies.Add("cash", 99);
            Assert.IsFalse(amp.IsUnlocked(context), "amp stays locked at 99 earned cash");

            currencies.Add("cash", 1);
            Assert.IsTrue(amp.IsUnlocked(context), "amp unlocks at exactly 100 earned cash");
            Assert.IsFalse(drummer.IsUnlocked(context));
            Assert.IsFalse(bassist.IsUnlocked(context));
            Assert.IsFalse(guitarist.IsUnlocked(context));

            // spending below the threshold must not re-lock or block anything:
            // the gate is the earned total, not the balance
            TestContent.BuyTimes(amp, currencies, 5);
            Assert.IsTrue(amp.IsUnlocked(context), "amp stays offered after its own cost is spent");
            Assert.IsTrue(drummer.IsUnlocked(context), "drummer unlocks at 5 amps");
            Assert.IsFalse(bassist.IsUnlocked(context));

            TestContent.BuyTimes(drummer, currencies, 5);
            Assert.IsTrue(bassist.IsUnlocked(context), "bassist unlocks at 5 drummers");
            Assert.IsFalse(guitarist.IsUnlocked(context));

            TestContent.BuyTimes(bassist, currencies, 5);
            Assert.IsTrue(guitarist.IsUnlocked(context), "guitarist unlocks at 5 bassists");
        }

        [Test]
        public void FansTuning_MatchesJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            Assert.AreEqual("fans", chapter.Fans.CurrencyId, "which currency is fans comes from the JSON fans block");
            Assert.AreEqual(0.02, chapter.Fans.PerBandmateOwnedBonus, 1e-9);
        }

        // Fan accrual is production like every other flat-rate source (design
        // doc section 12, rule 13), held by a producer with NO module: nothing
        // presents it, and because it is not a generator it can never idle-pay
        // (section 9). The gate names the band directly rather than relying on
        // which upgrade happens to set a flag.
        [Test]
        public void BandProducer_HoldsFanAccrual_GatedOnOwningABandmate()
        {
            var band = LoadById<ProducerDefinition>(ProducersFolder, "band");

            // passive: nothing presents it, and 6.5 made that a DERIVED fact - no
            // section module entry names it - rather than a blank field on the asset
            Assert.IsFalse(band.HasTapConfigs, "a passive producer authors no tap surface");
            Assert.AreEqual(1, band.Production.Count);

            var accrual = band.Production[0];
            Assert.AreEqual("fans", accrual.CurrencyId);
            Assert.AreEqual(0.2, accrual.Amount, 1e-9, "the base fan rate comes from the JSON config");
            Assert.AreEqual(ProductionTrigger.Tick, accrual.Trigger);
            Assert.AreEqual(ModifierTarget.FanRate, accrual.Composes,
                "so cover-bar rewards and the per-bandmate bonus compose through one stack");

            var gate = accrual.Gate as OwnedCountCondition;
            Assert.IsNotNull(gate, "fans accrue only once a bandmate is owned");
            Assert.AreEqual("drummer", gate.GeneratorId);
        }

        // production lives on the producer (design doc section 12, rule 13):
        // the jam producer authors what a tap yields and Rehearsal's trickle,
        // and the chapter lists the producer so production is chapter-owned
        [Test]
        public void JamProducer_MatchesJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            // the chapter's full local roster (design doc section 12, rule 12) -
            // the economy context builds its pool from exactly this list, so a
            // currency missing here has no balance at runtime. Records is absent
            // on purpose: it is placed Global and lives in the startup pool.
            CollectionAssert.AreEqual(new[] { "cash", "fans", "rehearsal" }, chapter.CurrencyIds,
                "if this fails, re-run 'GarageBandIdle > Import Chapter 1 JSON' for the roster");
            CollectionAssert.AreEqual(new[] { "jam", "band" }, chapter.ProducerIds,
                "the jam producer and the passive band producer holding fan accrual - if this fails, re-run 'GarageBandIdle > Import Chapter 1 JSON' for the restructured JSON");

            var jam = LoadById<ProducerDefinition>(ProducersFolder, "jam");
            Assert.IsTrue(jam.HasTapConfigs, "the jam producer is a tap surface");
            Assert.AreEqual(3, jam.Production.Count);

            // Which module presents it lives on the SECTION now (6.5): the producer
            // carries no module of its own, so the binding has exactly one home and
            // boot validation reports a tap producer no section presents.
            var garageFloor = LoadById<SectionDefinition>(SectionsFolder, "garage_floor");
            Assert.AreEqual("jam", garageFloor.Modules[1].DefinitionId);

            var cash = jam.Production[0];
            Assert.AreEqual("cash", cash.CurrencyId);
            Assert.AreEqual(1.0, cash.Amount, 1e-9, "replaces the old constants.tapBaseValue");
            Assert.AreEqual(ProductionTrigger.Tap, cash.Trigger);
            Assert.AreEqual(ModifierTarget.TapValue, cash.Composes, "tap buffs land on the cash yield");
            Assert.IsNull(cash.Gate, "cash per tap is ungated");

            var rehearsalTap = jam.Production[1];
            Assert.AreEqual("rehearsal", rehearsalTap.CurrencyId);
            Assert.AreEqual(2.0, rehearsalTap.Amount, 1e-9);
            Assert.AreEqual(ProductionTrigger.Tap, rehearsalTap.Trigger);
            Assert.AreEqual(ModifierTarget.None, rehearsalTap.Composes, "tap buffs never inflate rehearsal");
            var tapGate = rehearsalTap.Gate as FlagSetCondition;
            Assert.IsNotNull(tapGate, "the rehearsal yield gates on an ordinary Condition");
            Assert.AreEqual("covers", tapGate.FlagId);

            var trickle = jam.Production[2];
            Assert.AreEqual("rehearsal", trickle.CurrencyId);
            Assert.AreEqual(1.0, trickle.Amount, 1e-9);
            Assert.AreEqual(ProductionTrigger.Tick, trickle.Trigger,
                "the passive trickle is module-held, never an innate generator (it must not idle-pay)");
            var trickleGate = trickle.Gate as FlagSetCondition;
            Assert.IsNotNull(trickleGate);
            Assert.AreEqual("covers", trickleGate.FlagId);
        }

        [TestCase("practice_amp", false)]
        [TestCase("drummer", true)]
        [TestCase("bassist", true)]
        [TestCase("guitarist", true)]
        public void BandmateFlags_MatchJson(string id, bool isBandmate)
        {
            var generator = LoadById<GeneratorDefinition>(GeneratorsFolder, id);

            Assert.AreEqual(isBandmate, generator.IsBandmate, $"'{id}' bandmate flag");
        }

        [Test]
        public void PlayForCrowd_UnlocksFansOnFirstDrummer()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            var currencies = LoadCurrencyManager();
            var flags = new FlagSystem(chapter.FlagIds);
            var generators = new GeneratorSystem(LoadChapterGenerators(chapter), currencies, new ModifierSystem());
            var upgrades = new UpgradeSystem(LoadChapterUpgrades(chapter), currencies, flags, new ModifierSystem());
            var context = TestContent.MakeContext(currencies, generators, flags);

            upgrades.EvaluateContentUnlocks(context);
            Assert.IsFalse(flags.IsSet("fans"), "fans locked before the first drummer");

            TestContent.BuyTimes(generators.Get("drummer"), currencies, 1);
            upgrades.EvaluateContentUnlocks(context);

            Assert.IsTrue(flags.IsSet("fans"), "recruiting the first drummer reveals fans");
        }

        [Test]
        public void CutDemoGate_IsCompound_FansAndBarsCompleted()
        {
            var cutDemo = LoadById<UpgradeDefinition>(UpgradesFolder, "cut_demo");

            var payload = cutDemo.Payload as SetFlagEffect;
            Assert.IsNotNull(payload, "cut_demo payload is a setFlag effect");
            Assert.AreEqual("album", payload.FlagId);

            var gate = cutDemo.Gate as CompoundCondition;
            Assert.IsNotNull(gate, "cut_demo gate is a compound condition - if not, re-run the chapter import");
            Assert.AreEqual(2, gate.All.Count);

            var fans = gate.All[0] as CurrencyBalanceCondition;
            Assert.IsNotNull(fans);
            Assert.AreEqual("fans", fans.CurrencyId);
            Assert.AreEqual(50, fans.Value, 1e-9);

            var covers = gate.All[1] as BarsCompletedCondition;
            Assert.IsNotNull(covers);
            Assert.AreEqual("learn_covers", covers.GroupId);
            Assert.AreEqual(1, covers.Value, 1e-9);
        }

        // the second-run flow's unlock lifetimes: the teaching unlocks whose
        // flags re-arm each run are themselves run-scoped (their latches clear,
        // so they re-fire on their own gates), while cut_demo stays permanent -
        // its flag is knowledge, and the release offer re-arms through the
        // album unlock instead
        [TestCase("play_for_crowd", ContentScope.Run)]
        [TestCase("learn_covers", ContentScope.Run)]
        [TestCase("browse_gear", ContentScope.Run)]
        [TestCase("cut_demo", ContentScope.PermanentInChapter)]
        public void ContentUnlockScopes_MatchTheSecondRunFlow(string id, ContentScope expected)
        {
            Assert.AreEqual(expected, LoadById<UpgradeDefinition>(UpgradesFolder, id).Scope);
        }

        // the Ch1 income buff declares what it multiplies instead of implying
        // cash from its effect name, so a chapter whose generators produce
        // something else needs no code change to keep them out of it
        [Test]
        public void TightSetBuff_DeclaresTheCurrenciesItMultiplies()
        {
            var tightSet = LoadById<UpgradeDefinition>(UpgradesFolder, "tight_set");

            var payload = tightSet.Payload as GrantModifierEffect;
            Assert.IsNotNull(payload,
                "tight_set payload grants a modifier - if this fails, re-run 'GarageBandIdle > Import Chapter 1 JSON'");
            Assert.AreEqual(ModifierTarget.CurrencyProduction, payload.Target,
                "the friendly currencyPerSecMultiplier maps onto currency production");
            Assert.AreEqual(ModifierOperation.Multiply, payload.Operation);
            Assert.AreEqual(1.5, payload.Value, 1e-9);
            CollectionAssert.AreEqual(new[] { "cash" }, payload.Qualifiers,
                "the buff multiplies only the currencies the JSON names");

            // the gate is this upgrade's whole point in Ch1: the same Condition
            // shape as the Cash-gated buffs, just a different currency id
            var gate = tightSet.Gate as CurrencyBalanceCondition;
            Assert.IsNotNull(gate, "tight_set gates on a currency balance");
            Assert.AreEqual("fans", gate.CurrencyId);
            Assert.AreEqual(30, gate.Value, 1e-9);
        }

        [Test]
        public void Sections_MatchJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            CollectionAssert.AreEqual(new[] { "garage_floor", "the_band", "the_gear", "rehearsal_space", "the_release" },
                chapter.SectionIds);

            var garageFloor = LoadById<SectionDefinition>(SectionsFolder, "garage_floor");
            Assert.IsNull(garageFloor.VisibleWhen, "garage_floor is visible from chapter start");
            CollectionAssert.AreEqual(new[] { "module/currency-header", "module/tap" }, Addresses(garageFloor));

            // The Jam button names the producer it fires (6.5). Before that a tap
            // fired every tap config in the chapter, so this binding existed in the
            // JSON and nowhere in the runtime.
            var tapEntry = garageFloor.Modules[1];
            Assert.AreEqual("module/tap", tapEntry.Address);
            Assert.AreEqual("jam", tapEntry.DefinitionId, "the tap module presents the jam producer");
            Assert.IsTrue(string.IsNullOrEmpty(garageFloor.Modules[0].DefinitionId),
                "the currency header renders a roster, so it names no single definition");

            var theBand = LoadById<SectionDefinition>(SectionsFolder, "the_band");
            var visibleWhen = theBand.VisibleWhen as CurrencyEarnedTotalCondition;
            Assert.IsNotNull(visibleWhen, "the_band reveals on an earned-total condition");
            Assert.AreEqual("cash", visibleWhen.CurrencyId);
            Assert.AreEqual(100, visibleWhen.Value, 1e-9);

            // the buff list shows while the gear flag is set: a balance gate
            // here would strobe with every purchase, so the threshold moment is
            // latched as STATE (browse_gear sets the run-scoped flag at 250
            // Cash) and the section reads the flag live
            var theGear = LoadById<SectionDefinition>(SectionsFolder, "the_gear");
            var gearGate = theGear.VisibleWhen as FlagSetCondition;
            Assert.IsNotNull(gearGate, "the_gear shows on a flag condition");
            Assert.AreEqual("gear", gearGate.FlagId);
            CollectionAssert.AreEqual(new[] { "module/upgrade-list" }, Addresses(theGear));

            var rehearsalSpace = LoadById<SectionDefinition>(SectionsFolder, "rehearsal_space");
            var coversGate = rehearsalSpace.VisibleWhen as FlagSetCondition;
            Assert.IsNotNull(coversGate, "rehearsal_space reveals on a flag condition");
            Assert.AreEqual("covers", coversGate.FlagId);
            CollectionAssert.AreEqual(new[] { "module/bar-list" }, Addresses(rehearsalSpace));

            // the prestige button reveals through its section's visibleWhen like
            // every other module (5.6 deleted album.revealFlag so slice 6 could
            // do exactly this) - the flag cut_demo latches at 50 Fans + 1 cover
            var theRelease = LoadById<SectionDefinition>(SectionsFolder, "the_release");
            var albumGate = theRelease.VisibleWhen as FlagSetCondition;
            Assert.IsNotNull(albumGate, "the_release reveals on a flag condition");
            Assert.AreEqual("album", albumGate.FlagId);
            CollectionAssert.AreEqual(new[] { "module/release" }, Addresses(theRelease));

            // No section carries a lifetime of its own: visibility is a live
            // function of visibleWhen, and each section's persistence comes
            // from what its condition reads - the run-scoped gear and covers
            // flags reset with the demo, the permanent album flag doesn't, and
            // the_band's earned-total is monotonic so it can never strobe.
        }

        // The release offer's gate (design doc section 5): the JSON album
        // block's unlock, imported as an ordinary Condition. Its inputs are run
        // facts - a fans balance and a bar completion - so the offer disarms at
        // every release and re-arms on the re-climb, cover re-learned included.
        [Test]
        public void AlbumUnlock_MatchesJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            var unlock = chapter.Album.ReleaseWhen as CompoundCondition;
            Assert.IsNotNull(unlock, "the album unlock is the JSON's compound condition");
            Assert.AreEqual(2, unlock.All.Count);
            Assert.AreEqual(0, unlock.Any.Count);

            var fans = unlock.All[0] as CurrencyBalanceCondition;
            Assert.IsNotNull(fans, "first leg: the fans balance");
            Assert.AreEqual("fans", fans.CurrencyId);
            Assert.AreEqual(50, fans.Value, 1e-9);

            var cover = unlock.All[1] as BarsCompletedCondition;
            Assert.IsNotNull(cover, "second leg: a learned cover");
            Assert.AreEqual("learn_covers", cover.GroupId);
            Assert.AreEqual(1, cover.Value, 1e-9);
        }

        [Test]
        public void LearnCoversBars_MatchJson_AndReferenceThePoolById()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            CollectionAssert.AreEqual(new[] { "learn_covers" }, chapter.BarGroupIds);

            var group = LoadById<BarGroupDefinition>(BarGroupsFolder, "learn_covers");
            var groupGate = group.VisibleWhen as FlagSetCondition;
            Assert.IsNotNull(groupGate, "learn_covers reveals on a flag condition");
            Assert.AreEqual("covers", groupGate.FlagId);
            // the concrete behavior type is the fill mode; the importer maps
            // the JSON's (fillMode, delivery) pair onto it
            Assert.IsInstanceOf<PerBarContinuousFill>(group.FillBehavior);
            Assert.AreEqual(ContentScope.Run, group.Scope);

            // the fill currency is pure state (its accrual lives on the jam
            // producer); bars reference it by id
            var rehearsal = LoadById<CurrencyDefinition>(CurrenciesFolder, "rehearsal");
            Assert.AreEqual("run", rehearsal.GroupId);
            CollectionAssert.AreEqual(new[] { "cover_1", "cover_2", "cover_3" }, group.BarIds);

            foreach (var (barId, requirement, rewardId) in new[]
                { ("cover_1", 120.0, "fan_rate_x1_15"), ("cover_2", 300.0, "fan_rate_x1_15"), ("cover_3", 600.0, "fan_rate_x1_20") })
            {
                var bar = LoadById<BarDefinition>(BarsFolder, barId);
                Assert.AreEqual("rehearsal", bar.FillCurrencyId, $"bar '{barId}' fills from rehearsal");
                Assert.AreEqual(requirement, bar.FillRequirement, 1e-9);
                Assert.AreEqual(rewardId, bar.RewardId, $"bar '{barId}' names its reward from the shared pool");

                var reward = LoadById<RewardDefinition>(RewardsFolder, rewardId);
                // the reward asset declares no lifetime at all - the group's Scope
                // asserted above is the one declaration, so a cover's boost clears
                // with the bars it came from
                var effect = reward.Effect as GrantModifierEffect;
                Assert.IsNotNull(effect, $"reward '{rewardId}' grants a modifier");
                Assert.AreEqual(ModifierTarget.FanRate, effect.Target);
            }
        }

        [TestCase(0, "tap_value_x1_25", 1.25)]
        [TestCase(1, "tap_value_x1_50", 1.50)]
        [TestCase(2, "tap_value_x2", 2.0)]
        public void GarageJamTierRewards_ResolveFromThePool(int tierIndex, string rewardId, double value)
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            CollectionAssert.AreEqual(new[] { "garage_jam" }, chapter.EventIds);

            var gameEvent = LoadById<Events.EventDefinition>(EventsFolder, "garage_jam");
            var tier = gameEvent.Tiers[tierIndex];

            Assert.AreEqual(rewardId, tier.RewardId);
            var reward = LoadById<RewardDefinition>(RewardsFolder, rewardId);
            var effect = reward.Effect as GrantModifierEffect;
            Assert.IsNotNull(effect, $"reward '{rewardId}' grants a modifier");
            Assert.AreEqual(ModifierTarget.TapValue, effect.Target);
            Assert.AreEqual(value, effect.Value, 1e-9);

            // the tier's own clear state is what carries a lifetime; the grant
            // projects from it and inherits one, so the shared reward needs none
            Assert.AreEqual(ContentScope.PermanentInChapter, tier.Scope,
                "an event ladder is not re-climbed after an album release");

            var goal = tier.Goal as CurrencyBalanceCondition;
            Assert.IsNotNull(goal, "tier goals are currency conditions");
            Assert.AreEqual("cash", goal.CurrencyId);

            Assert.IsInstanceOf<Events.AutomationDisabledDebuff>(tier.Debuff,
                "every garage_jam tier is tap-only");
        }

        [Test]
        public void GarageJamAvailability_IsRecordsCumulative()
        {
            var gameEvent = LoadById<Events.EventDefinition>(EventsFolder, "garage_jam");

            var availableWhen = gameEvent.AvailableWhen as RecordsCumulativeCondition;
            Assert.IsNotNull(availableWhen, "event availability uses the same recordsCumulative type as the capstone");
            Assert.AreEqual(1, availableWhen.Value, 1e-9);
        }

        [Test]
        public void SecondAmpCosts69_PerTheCurve()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            var currencies = LoadCurrencyManager();
            var generators = new GeneratorSystem(LoadChapterGenerators(chapter), currencies, new ModifierSystem());
            var amp = generators.Get("practice_amp");

            TestContent.BuyTimes(amp, currencies, 1);

            Assert.AreEqual(69.0, amp.NextCost.ToDouble(), 1e-6);
        }
    }
}
