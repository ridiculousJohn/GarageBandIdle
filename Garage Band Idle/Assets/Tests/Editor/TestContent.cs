using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
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

        public static CurrencyDefinition MakeCurrency(string id, string groupId, double startingValue = 0,
            string earnRevealFlag = null, double earnPerSec = 0, double earnPerTap = 0)
        {
            var definition = Track(ScriptableObject.CreateInstance<CurrencyDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = id;
            serialized.FindProperty("_groupId").stringValue = groupId;
            serialized.FindProperty("_startingValue").doubleValue = startingValue;
            serialized.FindProperty("_earn._revealFlagId").stringValue = earnRevealFlag ?? "";
            serialized.FindProperty("_earn._perSec").doubleValue = earnPerSec;
            serialized.FindProperty("_earn._perTap").doubleValue = earnPerTap;
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
            Condition gate, GameEffect payload,
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
            List<string> barIds, BarFillBehavior fillBehavior = null,
            ContentScope scope = ContentScope.Run)
        {
            var definition = Track(ScriptableObject.CreateInstance<BarGroupDefinition>());
            definition.EditorInitialize(id, id, revealFlagId,
                fillBehavior ?? new PerBarContinuousFill(), scope, barIds);
            return definition;
        }

        // Defaults to one real module address: a section with none is a reported
        // content error, so a fixture that only cares about visibility still has to
        // be a coherent section. The address has to resolve through Addressables
        // like the running game's would.
        public static SectionDefinition MakeSection(string id, Condition visibleWhen = null,
            List<string> moduleAddresses = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<SectionDefinition>());
            definition.EditorInitialize(id, id,
                moduleAddresses ?? new List<string> { "module/tap" }, visibleWhen);
            return definition;
        }

        // a minimal coherent chapter: declared flags plus the id lists that
        // form its content closure. The fans config uses the standard economy's
        // currency and must reveal on a declared flag, so include
        // fansRevealFlagId (default "fans") in flagIds.
        public static ChapterDefinition MakeChapter(string id, List<string> flagIds,
            List<string> sectionIds = null, List<string> generatorIds = null,
            List<string> upgradeIds = null, List<string> barGroupIds = null,
            List<string> eventIds = null, List<string> currencyIds = null,
            string fansRevealFlagId = "fans", double tapBaseValue = 1, double recordBuffPerRecord = 0.02,
            int index = 1, int capstoneRecordsGate = 30, string fansCurrencyId = "fans")
        {
            var definition = Track(ScriptableObject.CreateInstance<ChapterDefinition>());
            definition.EditorInitialize(id, index, id, "", "", "", capstoneRecordsGate, tapBaseValue,
                new RecordBuffConfig(recordBuffPerRecord, new List<string> { "cash" }),
                new FansConfig(fansCurrencyId, fansRevealFlagId, 0.2, 0.02),
                flagIds, currencyIds ?? new List<string>(), sectionIds ?? new List<string>(),
                generatorIds ?? new List<string>(), upgradeIds ?? new List<string>(),
                barGroupIds ?? new List<string>(), eventIds ?? new List<string>());
            return definition;
        }

        public static EventDefinition MakeEvent(string id, List<EventTier> tiers,
            Condition availableWhen = null, bool baselineReset = true)
        {
            var definition = Track(ScriptableObject.CreateInstance<EventDefinition>());
            definition.EditorInitialize(id, id, availableWhen, baselineReset, tiers);
            return definition;
        }

        // One tier, defaulting to the coherent shape: timed and failable together,
        // paying a reward. Goal is required rather than defaulted, because "no
        // goal" is one of the states a tier fixture needs to be able to express.
        public static EventTier MakeTier(int tier, string rewardId, Condition goal,
            double timerSeconds = 60, bool failable = true,
            ContentScope scope = ContentScope.PermanentInChapter)
            => new(tier, new AutomationDisabledDebuff(), goal, timerSeconds, failable, rewardId, scope);

        // Reward fixtures carry no scope: the content applying one declares the
        // lifetime, so a bar group's Scope or a tier's Scope supplies it at Apply.
        // The named helpers are just the friendly effect vocabulary, same as the
        // importer's - one reward type underneath.
        public static RewardDefinition MakeFanRateReward(string id, double value)
            => MakeReward(id, new GrantModifierEffect(ModifierTarget.FanRate, ModifierOperation.Multiply, value));

        public static RewardDefinition MakeTapValueReward(string id, double value)
            => MakeReward(id, new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Multiply, value));

        public static RewardDefinition MakeSetFlagReward(string id, string flagId)
            => MakeReward(id, new SetFlagEffect(flagId));

        public static RewardDefinition MakeReward(string id, GameEffect effect)
        {
            var definition = Track(ScriptableObject.CreateInstance<RewardDefinition>());
            definition.EditorInitialize(id, id, effect);
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
