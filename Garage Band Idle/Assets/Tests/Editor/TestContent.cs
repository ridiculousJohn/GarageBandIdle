using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Builders for in-memory definition instances so unit tests don't depend on
    // imported assets. Everything created here is tracked and torn down via
    // DestroyAll from a fixture's [OneTimeTearDown].
    internal static class TestContent
    {
        private static readonly List<Object> Created = new();

        public static void DestroyAll()
        {
            foreach (var created in Created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            Created.Clear();
        }

        public static CurrencyGroupDefinition MakeGroup(string id, bool resetsOnAlbumRelease)
        {
            var definition = Track(ScriptableObject.CreateInstance<CurrencyGroupDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = id;
            serialized.FindProperty("_resetsOnAlbumRelease").boolValue = resetsOnAlbumRelease;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        public static CurrencyDefinition MakeCurrency(string id, string groupId, double startingValue = 0)
        {
            var definition = Track(ScriptableObject.CreateInstance<CurrencyDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = id;
            serialized.FindProperty("_groupId").stringValue = groupId;
            serialized.FindProperty("_startingValue").doubleValue = startingValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        public static GeneratorDefinition MakeGenerator(string id, string produces,
            double baseCost, double costGrowth, double baseOutput, Condition unlock = null,
            bool isBandmate = false, string costCurrency = "cash")
        {
            var definition = Track(ScriptableObject.CreateInstance<GeneratorDefinition>());
            definition.EditorInitialize(id, id, produces, isBandmate, costCurrency, baseCost, costGrowth, baseOutput, unlock);
            return definition;
        }

        public static UpgradeDefinition MakeUpgrade(string id, UpgradeType type, ContentScope scope,
            Condition gate, UpgradePayload payload,
            string costCurrencyId = "cash", double costAmount = 0)
        {
            var definition = Track(ScriptableObject.CreateInstance<UpgradeDefinition>());
            definition.EditorInitialize(id, id, type, scope, costCurrencyId, costAmount,
                gate, payload);
            return definition;
        }

        public static BarDefinition MakeBar(string id, string fillCurrencyId,
            double fillRequirement, string rewardId = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<BarDefinition>());
            definition.EditorInitialize(id, id, fillCurrencyId, fillRequirement, rewardId);
            return definition;
        }

        public static BarGroupDefinition MakeBarGroup(string id, string revealFlagId,
            List<string> barIds, BarFillMode fillMode = BarFillMode.PerBar,
            BarFillDelivery delivery = BarFillDelivery.Continuous,
            ContentScope scope = ContentScope.Run)
        {
            var definition = Track(ScriptableObject.CreateInstance<BarGroupDefinition>());
            definition.EditorInitialize(id, id, revealFlagId, fillMode, delivery, scope, barIds);
            return definition;
        }

        public static SectionDefinition MakeSection(string id, Condition visibleWhen = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<SectionDefinition>());
            definition.EditorInitialize(id, id, new List<string>(), visibleWhen);
            return definition;
        }

        // a minimal coherent chapter: declared flags plus the id lists that
        // form its content closure. The fans config uses the standard economy's
        // currency and must reveal on a declared flag, so include
        // fansRevealFlagId (default "fans") in flagIds.
        public static ChapterDefinition MakeChapter(string id, List<string> flagIds,
            List<string> sectionIds = null, List<string> generatorIds = null,
            List<string> upgradeIds = null, List<string> barGroupIds = null,
            List<string> eventIds = null, string fansRevealFlagId = "fans")
        {
            var definition = Track(ScriptableObject.CreateInstance<ChapterDefinition>());
            definition.EditorInitialize(id, 1, id, "", "", "", 100, 1,
                new RecordBuffConfig(0.02, new List<string> { "cash" }),
                new FansConfig("fans", fansRevealFlagId, 0.2, 0.02),
                new RehearsalConfig(null, null, 0, 0),
                flagIds, sectionIds ?? new List<string>(), generatorIds ?? new List<string>(),
                upgradeIds ?? new List<string>(), barGroupIds ?? new List<string>(), eventIds ?? new List<string>());
            return definition;
        }

        public static FanRateMultiplierReward MakeFanRateReward(string id, double value, ContentScope scope = ContentScope.Run)
        {
            var definition = Track(ScriptableObject.CreateInstance<FanRateMultiplierReward>());
            definition.EditorInitialize(id, id, value, scope);
            return definition;
        }

        public static SetFlagReward MakeSetFlagReward(string id, string flagId)
        {
            var definition = Track(ScriptableObject.CreateInstance<SetFlagReward>());
            definition.EditorInitialize(id, id, flagId);
            return definition;
        }

        // the standard two-group, three-currency economy most fixtures need
        public static CurrencyManager MakeEconomy()
        {
            var groups = new[] { MakeGroup("run", true), MakeGroup("permanent", false) };
            var currencies = new[]
            {
                MakeCurrency("cash", "run"),
                MakeCurrency("fans", "run"),
                MakeCurrency("records", "permanent"),
            };
            return new CurrencyManager(groups, currencies);
        }

        // evaluation context over live test systems; no ContentDatabase, which
        // makes Validate fall back to the systems themselves
        public static ConditionContext MakeContext(CurrencyManager currencies,
            GeneratorSystem generators = null, FlagSystem flags = null)
            => new(currencies, generators, flags);

        // grants exactly enough of the cost currency for each purchase so tests
        // control balances
        public static void BuyTimes(Generator generator, CurrencyManager currencies, int times)
        {
            for (var i = 0; i < times; i++)
            {
                currencies.Add(generator.Definition.CostCurrencyId, generator.NextCost);
                Assert.IsTrue(generator.TryBuy(currencies),
                    $"TryBuy failed for '{generator.Definition.Id}' at owned {generator.Owned}.");
            }
        }

        private static T Track<T>(T created) where T : Object
        {
            created.hideFlags = HideFlags.HideAndDontSave;
            Created.Add(created);
            return created;
        }
    }
}
