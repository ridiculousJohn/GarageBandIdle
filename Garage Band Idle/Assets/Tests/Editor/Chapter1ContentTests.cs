using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Validates the IMPORTED Chapter 1 assets against docs/chapter-01-garage.json
    // and simulates the generator unlock chain end-to-end. The chapter references
    // its content by id, so these tests resolve ids against the asset folders the
    // importer writes — the editor-test stand-in for the Addressables registries.
    // Requires 'GarageBandIdle → Import Chapter 1 JSON' to have been run.
    public class Chapter1ContentTests
    {
        private const string ChapterPath = "Assets/ScriptableObjects/Chapters/ch01_garage.asset";
        private const string SectionsFolder = "Assets/ScriptableObjects/Sections";
        private const string CurrenciesFolder = "Assets/ScriptableObjects/Currencies";
        private const string GroupsFolder = "Assets/ScriptableObjects/CurrencyGroups";
        private const string GeneratorsFolder = "Assets/ScriptableObjects/Generators";
        private const string UpgradesFolder = "Assets/ScriptableObjects/Upgrades";
        private const string BarsFolder = "Assets/ScriptableObjects/Bars";
        private const string BarGroupsFolder = "Assets/ScriptableObjects/BarGroups";
        private const string EventsFolder = "Assets/ScriptableObjects/Events";
        private const string RewardsFolder = "Assets/ScriptableObjects/Rewards";

        private static T LoadRequired<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset,
                $"Missing asset at '{path}'. Run 'GarageBandIdle → Import Chapter 1 JSON' first.");
            return asset;
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
        // folder path — the runtime equivalent is the ContentDatabase registry
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
            Assert.AreEqual(1.0, chapter.TapBaseValue, 1e-9);
            Assert.AreEqual(0.02, chapter.RecordBuffPerRecord, 1e-9);
            Assert.AreEqual(30, chapter.CapstoneRecordsGate);
        }

        [Test]
        public void ChapterFlags_MatchJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            CollectionAssert.AreEqual(new[] { "fans", "covers", "album" }, chapter.FlagIds,
                "the chapter declares exactly the JSON flags array, in order");
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
        }

        [Test]
        public void CurrencyGroups_MatchDesign()
        {
            var manager = LoadCurrencyManager();
            var groups = LoadAllIn<CurrencyGroupDefinition>(GroupsFolder);

            foreach (var (currencyId, resets) in new[]
                { ("cash", true), ("fans", true), ("rehearsal", true), ("records", false) })
            {
                var definition = manager.GetDefinition(currencyId);
                Assert.IsNotNull(definition, $"currency '{currencyId}' exists");

                var group = System.Array.Find(groups, g => g.Id == definition.GroupId);
                Assert.IsNotNull(group, $"currency '{currencyId}' resolves its group '{definition.GroupId}'");
                Assert.AreEqual(resets, group.ResetsOnAlbumRelease,
                    $"currency '{currencyId}' album-release reset behavior");
            }
        }

        [Test]
        public void GeneratorUnlockChain_FiresInOrder()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            var currencies = LoadCurrencyManager();
            var generators = new GeneratorSystem(LoadChapterGenerators(chapter), currencies);
            var context = TestContent.MakeContext(currencies, generators, new FlagSystem());

            var amp = generators.Get("practice_amp");
            var drummer = generators.Get("drummer");
            var bassist = generators.Get("bassist");
            var guitarist = generators.Get("guitarist");

            // stage 0: tap only — nothing revealed below the 100-earned threshold
            currencies.Add("cash", 99);
            generators.EvaluateUnlocks(context);
            Assert.IsFalse(amp.Unlocked, "amp stays locked at 99 lifetime cash");

            currencies.Add("cash", 1);
            generators.EvaluateUnlocks(context);
            Assert.IsTrue(amp.Unlocked, "amp unlocks at exactly 100 lifetime cash");
            Assert.IsFalse(drummer.Unlocked);
            Assert.IsFalse(bassist.Unlocked);
            Assert.IsFalse(guitarist.Unlocked);

            // spending below the threshold must not re-lock or block anything:
            // the gate is lifetime-earned, not balance
            TestContent.BuyTimes(amp, currencies, 5);
            generators.EvaluateUnlocks(context);
            Assert.IsTrue(drummer.Unlocked, "drummer unlocks at 5 amps");
            Assert.IsFalse(bassist.Unlocked);

            TestContent.BuyTimes(drummer, currencies, 5);
            generators.EvaluateUnlocks(context);
            Assert.IsTrue(bassist.Unlocked, "bassist unlocks at 5 drummers");
            Assert.IsFalse(guitarist.Unlocked);

            TestContent.BuyTimes(bassist, currencies, 5);
            generators.EvaluateUnlocks(context);
            Assert.IsTrue(guitarist.Unlocked, "guitarist unlocks at 5 bassists");
        }

        [Test]
        public void FansTuning_MatchesJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            Assert.AreEqual(0.2, chapter.Fans.BaseFansPerSec, 1e-9);
            Assert.AreEqual(0.02, chapter.Fans.PerBandmateOwnedBonus, 1e-9);
        }

        [Test]
        public void RehearsalTuning_MatchesJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);

            Assert.AreEqual(1.0, chapter.Rehearsal.PointsPerSec, 1e-9,
                "rehearsal perSec — if this fails, re-run 'GarageBandIdle → Import Chapter 1 JSON' for the restructured JSON");
            Assert.AreEqual(2.0, chapter.Rehearsal.PointsPerTap, 1e-9);
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
            var generators = new GeneratorSystem(LoadChapterGenerators(chapter), currencies);
            var upgrades = new UpgradeSystem(LoadChapterUpgrades(chapter), currencies, flags);
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

            Assert.AreEqual(UpgradePayload.EffectSetFlag, cutDemo.Payload.Effect);
            Assert.AreEqual("album", cutDemo.Payload.FlagId);

            var gate = cutDemo.Gate as CompoundCondition;
            Assert.IsNotNull(gate, "cut_demo gate is a compound condition — if not, re-run the chapter import");
            Assert.AreEqual(2, gate.All.Count);

            var fans = gate.All[0] as CurrencyBalanceCondition;
            Assert.IsNotNull(fans);
            Assert.AreEqual("fans", fans.CurrencyId);
            Assert.AreEqual(50, fans.Amount, 1e-9);

            var covers = gate.All[1] as BarsCompletedCondition;
            Assert.IsNotNull(covers);
            Assert.AreEqual("learn_covers", covers.GroupId);
            Assert.AreEqual(1, covers.Value, 1e-9);
        }

        [Test]
        public void Sections_MatchJson()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            CollectionAssert.AreEqual(new[] { "garage_floor", "the_band" }, chapter.SectionIds);

            var garageFloor = LoadById<SectionDefinition>(SectionsFolder, "garage_floor");
            Assert.IsNull(garageFloor.VisibleWhen, "garage_floor is visible from chapter start");
            CollectionAssert.AreEqual(new[] { "module/currency-header", "module/tap" }, garageFloor.ModuleAddresses);

            var theBand = LoadById<SectionDefinition>(SectionsFolder, "the_band");
            var visibleWhen = theBand.VisibleWhen as CurrencyEarnedTotalCondition;
            Assert.IsNotNull(visibleWhen, "the_band reveals on an earned-total condition");
            Assert.AreEqual("cash", visibleWhen.CurrencyId);
            Assert.AreEqual(100, visibleWhen.Value, 1e-9);
        }

        [Test]
        public void LearnCoversBars_MatchJson_AndReferenceThePoolById()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            CollectionAssert.AreEqual(new[] { "learn_covers" }, chapter.BarGroupIds);

            var group = LoadById<BarGroupDefinition>(BarGroupsFolder, "learn_covers");
            Assert.AreEqual("covers", group.RevealFlagId);
            Assert.AreEqual(BarFillMode.PerBar, group.FillMode);
            Assert.AreEqual(ContentScope.Run, group.Scope);
            CollectionAssert.AreEqual(new[] { "cover_1", "cover_2", "cover_3" }, group.BarIds);

            foreach (var (barId, requirement, rewardId) in new[]
                { ("cover_1", 120.0, "fan_rate_x1_15"), ("cover_2", 300.0, "fan_rate_x1_15"), ("cover_3", 600.0, "fan_rate_x1_20") })
            {
                var bar = LoadById<BarDefinition>(BarsFolder, barId);
                Assert.AreEqual("rehearsal", bar.FillCurrencyId, $"bar '{barId}' fills from rehearsal");
                Assert.AreEqual(requirement, bar.FillRequirement, 1e-9);
                Assert.AreEqual(rewardId, bar.RewardId, $"bar '{barId}' names its reward from the shared pool");

                var reward = LoadById<RewardDefinition>(RewardsFolder, rewardId);
                Assert.IsInstanceOf<FanRateMultiplierReward>(reward);
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
            Assert.IsInstanceOf<TapValueMultiplierReward>(reward);
            Assert.AreEqual(value, ((TapValueMultiplierReward)reward).Value, 1e-9);

            var goal = tier.Goal as CurrencyBalanceCondition;
            Assert.IsNotNull(goal, "tier goals are currency conditions");
            Assert.AreEqual("cash", goal.CurrencyId);
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
            var generators = new GeneratorSystem(LoadChapterGenerators(chapter), currencies);
            var amp = generators.Get("practice_amp");

            TestContent.BuyTimes(amp, currencies, 1);

            Assert.AreEqual(69.0, amp.NextCost.ToDouble(), 1e-6);
        }
    }
}
