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

        // Placement defaults to Chapter because that is what almost every
        // fixture wants: one flat pool standing in for a chapter's economy.
        // A fixture testing placement itself passes it explicitly.
        public static CurrencyGroupDefinition MakeGroup(string id, bool resetsOnAlbumRelease,
            CurrencyPlacement placement = CurrencyPlacement.Chapter)
        {
            var definition = Track(ScriptableObject.CreateInstance<CurrencyGroupDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = id;
            serialized.FindProperty("_resetsOnAlbumRelease").boolValue = resetsOnAlbumRelease;
            // intValue, not enumValueIndex: the serialized form is the enum's
            // VALUE (a save contract, explicitly numbered), and index-vs-value
            // only coincide while no value is skipped
            serialized.FindProperty("_placement").intValue = (int)placement;
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

        public static ProducerDefinition MakeProducer(string id, List<ProductionConfig> production,
            string moduleAddress = "module/tap")
        {
            var definition = Track(ScriptableObject.CreateInstance<ProducerDefinition>());
            definition.EditorInitialize(id, moduleAddress, production);
            return definition;
        }

        // a jam producer whose single tap config composes TapValue - the probe
        // for the tap-value modifier stack (the shape TapSystem was)
        public static ProductionSystem MakeTapProduction(double baseAmount, ModifierSystem modifiers,
            CurrencyManager currencies = null, FlagSystem flags = null)
        {
            currencies ??= MakeEconomy();
            var producer = MakeProducer("jam", new List<ProductionConfig>
            {
                new("cash", baseAmount, ProductionTrigger.Tap, null, ModifierTarget.TapValue),
            });
            return new ProductionSystem(new[] { producer }, currencies, modifiers,
                MakeContext(currencies, flags: flags));
        }

        // The fan-accrual path as the game authors it: a passive producer (no
        // module) holding one tick config that composes FanRate, plus the derived
        // per-bandmate Add registered on the modifier stack. Both halves together,
        // because either alone is not the fan rate - the composed value is
        // (base + perBandmate x bandmates) x rewards.
        public static ProductionSystem MakeFanProduction(ModifierSystem modifiers,
            GeneratorSystem generators, ICurrencies currencies, ConditionContext conditions,
            Condition gate = null, double baseFansPerSec = 0.2, double perBandmateOwnedBonus = 0.02)
        {
            var producer = MakeProducer("band", new List<ProductionConfig>
            {
                new("fans", baseFansPerSec, ProductionTrigger.Tick, gate, ModifierTarget.FanRate),
            }, moduleAddress: null);
            modifiers.AddDerived(new BandmateFanRateModifier(generators, perBandmateOwnedBonus));
            return new ProductionSystem(new[] { producer }, currencies, modifiers, conditions);
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

        public static BarGroupDefinition MakeBarGroup(string id, Condition visibleWhen,
            List<string> barIds, BarFillBehavior fillBehavior = null,
            ContentScope scope = ContentScope.Run)
        {
            var definition = Track(ScriptableObject.CreateInstance<BarGroupDefinition>());
            definition.EditorInitialize(id, id, visibleWhen,
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
        // form its content closure. Fan accrual itself is a production config on
        // a producer, so a chapter fixture declares only which currency is fans
        // and the per-bandmate tuning - see MakeFanProduction for the accrual.
        public static ChapterDefinition MakeChapter(string id, List<string> flagIds,
            List<string> sectionIds = null, List<string> generatorIds = null,
            List<string> upgradeIds = null, List<string> barGroupIds = null,
            List<string> eventIds = null, List<string> currencyIds = null,
            List<string> producerIds = null,
            double perBandmateOwnedBonus = 0.02, double recordBuffPerRecord = 0.02,
            int index = 1, int capstoneRecordsGate = 30, string fansCurrencyId = "fans",
            List<string> recordBuffAffects = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<ChapterDefinition>());
            definition.EditorInitialize(id, index, id, "", "", "", capstoneRecordsGate,
                new RecordBuffConfig(recordBuffPerRecord, recordBuffAffects ?? new List<string> { "cash" }),
                new FansConfig(fansCurrencyId, perBandmateOwnedBonus),
                // the chapter-local half of the standard economy; records is
                // global, so no chapter declares it
                flagIds, currencyIds ?? new List<string> { "cash", "fans" }, producerIds ?? new List<string>(),
                sectionIds ?? new List<string>(), generatorIds ?? new List<string>(),
                upgradeIds ?? new List<string>(), barGroupIds ?? new List<string>(),
                eventIds ?? new List<string>());
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

        // The standard two-group, three-currency economy, as DEFINITIONS. Records
        // is placed Global like the real game's: it lives in the permanent pool,
        // which every economy routes to, so it is reachable from every chapter
        // without appearing in any chapter's roster.
        public static CurrencyGroupDefinition[] StandardGroups()
            => new[] { MakeGroup("run", true), MakeGroup("permanent", false, CurrencyPlacement.Global) };

        public static CurrencyDefinition[] StandardCurrencies()
            => new[]
            {
                MakeCurrency("cash", "run"),
                MakeCurrency("fans", "run"),
                MakeCurrency("records", "permanent"),
            };

        // the standard two-group, three-currency economy most fixtures need
        public static CurrencyManager MakeEconomy()
            => new(StandardGroups(), StandardCurrencies());

        // A ContentDatabase for fixtures: the injection constructor, except the
        // currencies and their groups default to the standard set instead of to
        // empty. Boot validation resolves a chapter's currency references against
        // the DATABASE (ChapterCurrencies), so a fixture registering currencies
        // only in a CurrencyManager describes content where nothing resolves -
        // and every test would drown in that rather than in what it is asserting.
        public static ContentDatabase MakeDatabase(
            IEnumerable<ChapterDefinition> chapters = null,
            IEnumerable<SectionDefinition> sections = null,
            IEnumerable<GeneratorDefinition> generators = null,
            IEnumerable<UpgradeDefinition> upgrades = null,
            IEnumerable<BarDefinition> bars = null,
            IEnumerable<BarGroupDefinition> barGroups = null,
            IEnumerable<EventDefinition> events = null,
            IEnumerable<RewardDefinition> rewards = null,
            IEnumerable<CurrencyDefinition> currencies = null,
            IEnumerable<CurrencyGroupDefinition> currencyGroups = null,
            IEnumerable<ProducerDefinition> producers = null)
            => new(chapters, sections, generators, upgrades, bars, barGroups, events, rewards,
                currencies ?? StandardCurrencies(), currencyGroups ?? StandardGroups(), producers);

        // evaluation context over live test systems; no ContentDatabase, which
        // makes Validate fall back to the systems themselves
        public static ConditionContext MakeContext(ICurrencies currencies,
            GeneratorSystem generators = null, FlagSystem flags = null)
            => new(currencies, generators, flags);

        // The run reset exactly as slice 6's release will perform it (design doc
        // section 12, rule 6): reset the FACTS that declare a run lifetime, then
        // rebuild the modifier store by re-projecting from whatever facts
        // survived. Nothing filters the store - a run-scoped effect disappears
        // because the fact behind it is gone, not because anything went looking
        // for grants to remove.
        //
        // This is the shape every test asserting "what a release keeps" now
        // uses, so the tests exercise the mechanism the release will use rather
        // than a reset call that only existed for them.
        public static void RunReset(ModifierSystem modifiers, UpgradeSystem upgrades = null,
            BarSystem bars = null, GeneratorSystem generators = null)
        {
            // facts first, all of them, before the store is touched: a
            // projection that ran against half-reset facts would rebuild
            // effects the release is in the middle of removing
            upgrades?.ResetRunScoped();
            bars?.ResetRunScopedGroups();
            generators?.ResetOwned();

            modifiers.ResetGranted();
            upgrades?.ProjectModifiers();
            bars?.ProjectModifiers();
        }

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
