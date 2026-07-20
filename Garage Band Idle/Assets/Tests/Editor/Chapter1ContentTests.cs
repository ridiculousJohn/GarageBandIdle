using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Validates the IMPORTED Chapter 1 assets against docs/chapter-01-garage.json
    // and simulates the generator unlock chain end-to-end — the repeatable form
    // of build-prompt slice 2's "costs match the JSON; unlock gates fire" check.
    // Requires 'GarageBandIdle → Import Chapter 1 JSON' to have been run.
    public class Chapter1ContentTests
    {
        private const string ChapterPath = "Assets/ScriptableObjects/Chapters/ch01_garage.asset";
        private const string CurrenciesFolder = "Assets/ScriptableObjects/Currencies";
        private const string GroupsFolder = "Assets/ScriptableObjects/CurrencyGroups";

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

        [TestCase(0, "practice_amp", 60, 0.4)]
        [TestCase(1, "drummer", 500, 3)]
        [TestCase(2, "bassist", 4000, 20)]
        [TestCase(3, "guitarist", 30000, 130)]
        public void GeneratorValues_MatchJson(int index, string id, double baseCost, double baseOutput)
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            Assert.AreEqual(4, chapter.Generators.Count, "Chapter 1 defines exactly four generators.");

            var generator = chapter.Generators[index];
            Assert.AreEqual(id, generator.Id, "generator list order matches the JSON");
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

            foreach (var (currencyId, resets) in new[] { ("cash", true), ("fans", true), ("records", false) })
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
            var generators = new GeneratorSystem(chapter.Generators, currencies, new FlagSystem());

            var amp = generators.Get("practice_amp");
            var drummer = generators.Get("drummer");
            var bassist = generators.Get("bassist");
            var guitarist = generators.Get("guitarist");

            // stage 0: tap only — nothing revealed below the 100-earned threshold
            currencies.Add("cash", 99);
            generators.EvaluateUnlocks();
            Assert.IsFalse(amp.Unlocked, "amp stays locked at 99 lifetime cash");

            currencies.Add("cash", 1);
            generators.EvaluateUnlocks();
            Assert.IsTrue(amp.Unlocked, "amp unlocks at exactly 100 lifetime cash");
            Assert.IsFalse(drummer.Unlocked);
            Assert.IsFalse(bassist.Unlocked);
            Assert.IsFalse(guitarist.Unlocked);

            // spending below the threshold must not re-lock or block anything:
            // the gate is lifetime-earned, not balance
            TestContent.BuyTimes(amp, currencies, 5);
            generators.EvaluateUnlocks();
            Assert.IsTrue(drummer.Unlocked, "drummer unlocks at 5 amps");
            Assert.IsFalse(bassist.Unlocked);

            TestContent.BuyTimes(drummer, currencies, 5);
            generators.EvaluateUnlocks();
            Assert.IsTrue(bassist.Unlocked, "bassist unlocks at 5 drummers");
            Assert.IsFalse(guitarist.Unlocked);

            TestContent.BuyTimes(bassist, currencies, 5);
            generators.EvaluateUnlocks();
            Assert.IsTrue(guitarist.Unlocked, "guitarist unlocks at 5 bassists");
        }

        [Test]
        public void SecondAmpCosts69_PerTheCurve()
        {
            var chapter = LoadRequired<ChapterDefinition>(ChapterPath);
            var currencies = LoadCurrencyManager();
            var generators = new GeneratorSystem(chapter.Generators, currencies, new FlagSystem());
            var amp = generators.Get("practice_amp");

            TestContent.BuyTimes(amp, currencies, 1);

            Assert.AreEqual(69.0, amp.NextCost.ToDouble(), 1e-6);
        }
    }
}
