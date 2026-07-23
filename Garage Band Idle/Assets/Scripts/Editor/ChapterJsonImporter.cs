using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.EditorTools
{
    // Reads a chapter content JSON (Docs/chapter-XX-*.json) and generates the
    // corresponding definition assets, so the JSON stays the source of truth.
    // Re-running updates existing assets in place (stable paths keyed by id).
    // Every definition asset in the project is then marked addressable
    // (address "<label>/<id>", one label per type) — runtime discovery loads
    // by label, so no asset lives in Resources and no chapter holds a direct
    // asset reference: content links by string id, resolved at load.
    //
    // Every gate/unlock/visibility/availability rule in the JSON is one
    // discriminated Condition shape ({ "type": ... }), mapped 1:1 onto the
    // Condition subclass family — no bespoke gate shapes survive import.
    public static class ChapterJsonImporter
    {
        private const string ChaptersFolder = "Assets/ScriptableObjects/Chapters";
        private const string SectionsFolder = "Assets/ScriptableObjects/Sections";
        private const string CurrenciesFolder = "Assets/ScriptableObjects/Currencies";
        private const string GeneratorsFolder = "Assets/ScriptableObjects/Generators";
        private const string UpgradesFolder = "Assets/ScriptableObjects/Upgrades";
        private const string BarsFolder = "Assets/ScriptableObjects/Bars";
        private const string BarGroupsFolder = "Assets/ScriptableObjects/BarGroups";
        private const string EventsFolder = "Assets/ScriptableObjects/Events";
        private const string RewardsFolder = "Assets/ScriptableObjects/Rewards";

        [MenuItem("GarageBandIdle/Import Chapter 1 JSON")]
        public static void ImportChapter1()
        {
            // Assets/../.. is the repo root, where Docs lives beside the Unity project
            var defaultPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "chapter-01-garage.json"));
            var path = File.Exists(defaultPath)
                ? defaultPath
                : EditorUtility.OpenFilePanel("Select chapter JSON", Path.GetDirectoryName(defaultPath), "json");
            if (string.IsNullOrEmpty(path))
                return;

            Import(path);
        }

        private static void Import(string jsonPath)
        {
            var data = JsonUtility.FromJson<ChapterFile>(File.ReadAllText(jsonPath));
            if (data?.chapter == null || string.IsNullOrEmpty(data.chapter.id))
            {
                Debug.LogError($"ChapterJsonImporter: '{jsonPath}' has no chapter block with an id. Nothing imported.");
                return;
            }

            EnsureFolders();

            // flags: the chapter's declared reveal registry
            var flagIds = new List<string>();
            foreach (var flag in data.flags ?? Array.Empty<FlagBlock>())
            {
                if (string.IsNullOrEmpty(flag.id))
                    Debug.LogError("ChapterJsonImporter: flags array contains an entry with an empty id. Skipping it.");
                else if (flagIds.Contains(flag.id))
                    Debug.LogError($"ChapterJsonImporter: duplicate flag id '{flag.id}'. Keeping the first.");
                else
                    flagIds.Add(flag.id);
            }

            // a chapter can declare a fill currency (rehearsal); generate it like
            // any other content so bars' fillCurrency ids resolve on load
            if (!string.IsNullOrEmpty(data.rehearsal?.currency))
            {
                var currencyAsset = LoadOrCreate<CurrencyDefinition>($"{CurrenciesFolder}/{data.rehearsal.currency}.asset");
                ApplyIfChanged(currencyAsset, asset => asset.EditorInitialize(data.rehearsal.currency,
                    ToDisplayName(data.rehearsal.currency), data.rehearsal.group));
            }

            // rewards first: bars and event tiers reference the pool by id, so
            // report a missing reward against the content that names it
            var rewardIds = new List<string>();
            foreach (var block in data.rewards ?? Array.Empty<RewardEntryBlock>())
            {
                if (rewardIds.Contains(block.id))
                {
                    Debug.LogError($"ChapterJsonImporter: duplicate reward id '{block.id}'. Keeping the first.");
                    continue;
                }

                var path = $"{RewardsFolder}/{block.id}.asset";
                switch (block.type)
                {
                    case "fanRateMultiplier":
                    {
                        var reward = LoadOrCreateReward<FanRateMultiplierReward>(path);
                        var scope = ToScope(block.scope, $"reward '{block.id}'");
                        ApplyIfChanged(reward, asset => asset.EditorInitialize(block.id, block.name, block.value, scope));
                        break;
                    }
                    case "tapValueMultiplier":
                    {
                        var reward = LoadOrCreateReward<TapValueMultiplierReward>(path);
                        var scope = ToScope(block.scope, $"reward '{block.id}'");
                        ApplyIfChanged(reward, asset => asset.EditorInitialize(block.id, block.name, block.value, scope));
                        break;
                    }
                    case "setFlag":
                    {
                        var reward = LoadOrCreateReward<SetFlagReward>(path);
                        ApplyIfChanged(reward, asset => asset.EditorInitialize(block.id, block.name, block.flag));
                        break;
                    }
                    default:
                        Debug.LogError($"ChapterJsonImporter: reward '{block.id}' has unknown type '{block.type}' — no RewardDefinition subclass maps to it. Skipping it.");
                        continue;
                }

                rewardIds.Add(block.id);
            }

            var sectionIds = new List<string>();
            foreach (var block in data.sections ?? Array.Empty<SectionBlock>())
            {
                var asset = LoadOrCreate<SectionDefinition>($"{SectionsFolder}/{block.id}.asset");
                var modules = new List<string>(block.modules ?? Array.Empty<string>());
                var visibleWhen = ToCondition(block.visibleWhen);
                ApplyIfChanged(asset, section => section.EditorInitialize(block.id, block.name, modules, visibleWhen));
                sectionIds.Add(block.id);
            }

            var generatorIds = new List<string>();
            foreach (var block in data.generators ?? Array.Empty<GeneratorBlock>())
            {
                // a missing/invalid cost would import as zeros — never write
                // that state: the asset is not created/updated and the chapter
                // does not list the generator. Growth < 1 (shrinking costs) is
                // legal; growth <= 0 breaks the curve.
                if (block.cost == null || string.IsNullOrEmpty(block.cost.currency)
                    || block.cost.amount <= 0 || block.cost.growth <= 0)
                {
                    Debug.LogError($"ChapterJsonImporter: generator '{block.id}' has a missing or invalid cost block (needs currency, amount > 0, growth > 0). Skipping it — fix the JSON and re-import.");
                    continue;
                }

                var asset = LoadOrCreate<GeneratorDefinition>($"{GeneratorsFolder}/{block.id}.asset");
                var unlock = ToCondition(block.unlock);
                ApplyIfChanged(asset, generator => generator.EditorInitialize(block.id, block.name, block.produces,
                    block.isBandmate, block.cost?.currency, block.cost?.amount ?? 0, block.cost?.growth ?? 0,
                    block.baseOutput, unlock));
                generatorIds.Add(block.id);
            }

            var upgradeIds = new List<string>();
            foreach (var block in data.upgrades ?? Array.Empty<UpgradeBlock>())
            {
                var asset = LoadOrCreate<UpgradeDefinition>($"{UpgradesFolder}/{block.id}.asset");
                var type = ToUpgradeType(block.type, $"upgrade '{block.id}'");
                var scope = ToScope(block.scope, $"upgrade '{block.id}'");
                var gate = ToCondition(block.gate);
                var payload = ToPayload(block.payload, $"upgrade '{block.id}'");
                ApplyIfChanged(asset, upgrade => upgrade.EditorInitialize(block.id, block.name, type, scope,
                    block.cost?.currency, block.cost?.amount ?? 0, gate, payload));
                upgradeIds.Add(block.id);
            }

            var barGroupIds = new List<string>();
            var barCount = 0;
            foreach (var group in data.bars?.groups ?? Array.Empty<BarGroupBlock>())
            {
                var barIds = new List<string>();
                foreach (var bar in group.bars ?? Array.Empty<BarBlock>())
                {
                    var barAsset = LoadOrCreate<BarDefinition>($"{BarsFolder}/{bar.id}.asset");
                    ApplyIfChanged(barAsset, asset => asset.EditorInitialize(bar.id, bar.name,
                        bar.fillCurrency, bar.fillRequirement, bar.reward));
                    barIds.Add(bar.id);
                    barCount++;
                }

                var groupAsset = LoadOrCreate<BarGroupDefinition>($"{BarGroupsFolder}/{group.id}.asset");
                var fillMode = ToFillMode(group.fillMode, $"bar group '{group.id}'");
                var delivery = ToDelivery(group.delivery, $"bar group '{group.id}'");
                var groupScope = ToScope(data.bars.scope, $"bar group '{group.id}'");
                ApplyIfChanged(groupAsset, asset => asset.EditorInitialize(group.id, group.name, group.revealFlag,
                    fillMode, delivery, groupScope, barIds));
                barGroupIds.Add(group.id);
            }

            var eventIds = new List<string>();
            foreach (var block in data.events ?? Array.Empty<EventBlock>())
            {
                var tiers = new List<EventTier>();
                foreach (var tier in block.tiers ?? Array.Empty<TierBlock>())
                {
                    tiers.Add(new EventTier(tier.tier,
                        ToDebuff(tier.debuff, $"event '{block.id}' tier {tier.tier}"),
                        ToCondition(tier.goal), tier.timerSeconds, tier.failable, tier.reward));
                }

                var asset = LoadOrCreate<EventDefinition>($"{EventsFolder}/{block.id}.asset");
                var availableWhen = ToCondition(block.availableWhen);
                ApplyIfChanged(asset, gameEvent => gameEvent.EditorInitialize(block.id, block.name,
                    availableWhen, block.baselineReset, tiers));
                eventIds.Add(block.id);
            }

            var chapterAsset = LoadOrCreate<ChapterDefinition>($"{ChaptersFolder}/{data.chapter.id}.asset");
            var recordBuff = new RecordBuffConfig(data.constants?.recordBuff?.perRecord ?? 0,
                new List<string>(data.constants?.recordBuff?.affects ?? Array.Empty<string>()));
            var fans = new FansConfig(data.fans?.currency, data.fans?.revealFlag,
                data.fans?.baseFansPerSec ?? 0, data.fans?.perBandmateOwnedBonus ?? 0);
            var rehearsal = new RehearsalConfig(data.rehearsal?.currency, data.rehearsal?.revealFlag,
                data.rehearsal?.perSec ?? 0, data.rehearsal?.perTap ?? 0);
            ApplyIfChanged(chapterAsset, chapter => chapter.EditorInitialize(data.chapter.id, data.chapter.index,
                data.chapter.name, data.chapter.theme, data.chapter.storyBeatOpen, data.chapter.storyBeatCapstone,
                data.chapter.capstoneRecordsGate,
                data.constants?.tapBaseValue ?? 1, recordBuff,
                fans, rehearsal, flagIds, sectionIds, generatorIds, upgradeIds, barGroupIds, eventIds));

            MarkAllContentAddressable();

            AssetDatabase.SaveAssets();
            var summary = $"Imported '{data.chapter.id}' — {flagIds.Count} flags, {sectionIds.Count} sections, " +
                $"{generatorIds.Count} generators, {upgradeIds.Count} upgrades, {barGroupIds.Count} bar groups " +
                $"({barCount} bars), {eventIds.Count} events, {rewardIds.Count} rewards. All content marked addressable.";
            Debug.Log($"ChapterJsonImporter: {summary}");
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Chapter import", summary, "OK");
        }

        // Sweeps every definition asset in the project (including hand-authored
        // currencies/groups) into Addressables: address "<label>/<asset name>",
        // one label per type. Safe to re-run; entries are created or updated,
        // and entries whose asset was deleted are removed.
        [MenuItem("GarageBandIdle/Mark Content Addressable")]
        public static void MarkAllContentAddressable()
        {
            // creates Assets/AddressableAssetsData + settings on first use
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

            RemoveStaleEntries(settings);

            int count = 0;
            count += MarkType<CurrencyDefinition>(settings, ContentLabels.Currency);
            count += MarkType<CurrencyGroupDefinition>(settings, ContentLabels.CurrencyGroup);
            count += MarkType<ChapterDefinition>(settings, ContentLabels.Chapter);
            count += MarkType<SectionDefinition>(settings, ContentLabels.Section);
            count += MarkType<GeneratorDefinition>(settings, ContentLabels.Generator);
            count += MarkType<UpgradeDefinition>(settings, ContentLabels.Upgrade);
            count += MarkType<BarDefinition>(settings, ContentLabels.Bar);
            count += MarkType<BarGroupDefinition>(settings, ContentLabels.BarGroup);
            count += MarkType<EventDefinition>(settings, ContentLabels.Event);
            count += MarkType<RewardDefinition>(settings, ContentLabels.Reward);
            count += MarkModulePrefabs(settings);

            AssetDatabase.SaveAssets();
            Debug.Log($"ChapterJsonImporter: {count} definition assets marked addressable.");
        }

        // Deleted assets leave dangling Addressables entries behind (they show as
        // Missing in the Groups window); drop them, then drop any label no entry
        // uses and no code loads, so retired content types don't linger.
        private static void RemoveStaleEntries(AddressableAssetSettings settings)
        {
            var dangling = new List<AddressableAssetEntry>();
            foreach (var group in settings.groups)
            {
                if (group == null)
                    continue;
                foreach (var entry in group.entries)
                {
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.guid)))
                        dangling.Add(entry);
                }
            }
            foreach (var entry in dangling)
            {
                Debug.Log($"ChapterJsonImporter: removing stale addressable entry '{entry.address}' (asset deleted).");
                settings.RemoveAssetEntry(entry.guid);
            }

            var knownLabels = new HashSet<string>
            {
                ContentLabels.Currency, ContentLabels.CurrencyGroup, ContentLabels.Chapter,
                ContentLabels.Section, ContentLabels.Generator, ContentLabels.Upgrade,
                ContentLabels.Bar, ContentLabels.BarGroup, ContentLabels.Event,
                ContentLabels.Reward, ContentLabels.Module,
            };
            var usedLabels = new HashSet<string>();
            foreach (var group in settings.groups)
            {
                if (group == null)
                    continue;
                foreach (var entry in group.entries)
                    usedLabels.UnionWith(entry.labels);
            }
            foreach (var label in new List<string>(settings.GetLabels()))
            {
                if (!knownLabels.Contains(label) && !usedLabels.Contains(label))
                {
                    Debug.Log($"ChapterJsonImporter: removing unused addressable label '{label}'.");
                    settings.RemoveLabel(label);
                }
            }
        }

        // Module prefabs live under Assets/Prefabs/Modules; the file name is the
        // address suffix (module/<name>), matching how sections reference them.
        private static int MarkModulePrefabs(AddressableAssetSettings settings)
        {
            const string modulesFolder = "Assets/Prefabs/Modules";
            if (!AssetDatabase.IsValidFolder(modulesFolder))
                return 0;

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { modulesFolder });
            foreach (var guid in guids)
            {
                var name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
                entry.address = $"{ContentLabels.Module}/{name}";
                entry.SetLabel(ContentLabels.Module, true, true);
            }
            return guids.Length;
        }

        private static int MarkType<T>(AddressableAssetSettings settings, string label) where T : ScriptableObject
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
                entry.address = $"{label}/{asset.name}";
                // force: adds the label to the settings if it doesn't exist yet
                entry.SetLabel(label, true, true);
            }
            return guids.Length;
        }

        // Maps a JSON condition ({ "type": ... }) onto the Condition subclass
        // family. An absent gate means no gate: JsonUtility materializes absent
        // objects as empty instances, so an empty type returns null (always met).
        private static Condition ToCondition(ConditionBlock block)
        {
            if (block == null || string.IsNullOrEmpty(block.type))
                return null;

            if (block.type == "compound")
            {
                var all = ToConditionList(block.all);
                var any = ToConditionList(block.any);
                if (all.Count == 0 && any.Count == 0)
                {
                    Debug.LogError("ChapterJsonImporter: compound condition has no children. Importing no gate.");
                    return null;
                }
                return new CompoundCondition(all, any);
            }

            return ToSimpleCondition(block.type, block.currency, block.amount,
                block.generator, block.flag, block.group, block.value);
        }

        private static List<Condition> ToConditionList(ConditionLeafBlock[] blocks)
        {
            var conditions = new List<Condition>();
            foreach (var block in blocks ?? Array.Empty<ConditionLeafBlock>())
            {
                if (string.IsNullOrEmpty(block.type))
                {
                    Debug.LogError("ChapterJsonImporter: compound condition has a child with no type. Skipping it.");
                    continue;
                }
                if (block.type == "compound")
                {
                    // JsonUtility cannot express recursive DTOs; extend the leaf
                    // shape if a chapter ever needs deeper nesting
                    Debug.LogError("ChapterJsonImporter: nested compound conditions are not supported by the importer. Skipping it.");
                    continue;
                }

                var condition = ToSimpleCondition(block.type, block.currency, block.amount,
                    block.generator, block.flag, block.group, block.value);
                if (condition != null)
                    conditions.Add(condition);
            }
            return conditions;
        }

        private static Condition ToSimpleCondition(string type, string currency, double amount,
            string generator, string flag, string group, double value)
        {
            switch (type)
            {
                case "currency":
                    return new CurrencyBalanceCondition(currency, amount);
                case "currencyEarnedTotal":
                    return new CurrencyEarnedTotalCondition(currency, value);
                case "ownedCount":
                    return new OwnedCountCondition(generator, value);
                case "flagSet":
                    return new FlagSetCondition(flag);
                case "barsCompleted":
                    return new BarsCompletedCondition(group, value);
                case "recordsCumulative":
                    return new RecordsCumulativeCondition(value);
                default:
                    Debug.LogError($"ChapterJsonImporter: condition type '{type}' maps to no Condition subclass. Importing no gate.");
                    return null;
            }
        }

        // Scope is a closed, code-defined set (ContentScope); the strings here
        // are the JSON spellings, and anything else is a content error.
        private static ContentScope ToScope(string scope, string context)
        {
            switch (scope)
            {
                case "run":
                    return ContentScope.Run;
                case "permanentInChapter":
                    return ContentScope.PermanentInChapter;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} has unknown scope '{scope}'. Defaulting to run.");
                    return ContentScope.Run;
            }
        }

        // Maps a JSON payload ({ "effect": ... }) onto the UpgradePayload
        // subclass family. Every upgrade must grant something, so an absent or
        // unknown effect is a content error.
        private static UpgradePayload ToPayload(PayloadBlock block, string context)
        {
            switch (block?.effect)
            {
                case "setFlag":
                    return new SetFlagPayload(block.flag);
                case "tapValueAdd":
                    return new TapValueAddPayload(block.value);
                case "generatorOutputMultiplier":
                    return new GeneratorOutputMultiplierPayload(block.generator, block.value);
                case "allCashPerSecMultiplier":
                    return new AllCashPerSecMultiplierPayload(block.value);
                case null:
                case "":
                    Debug.LogError($"ChapterJsonImporter: {context} has no payload effect. Importing no payload.");
                    return null;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} payload effect '{block.effect}' maps to no UpgradePayload subclass. Importing no payload.");
                    return null;
            }
        }

        // A tier with no debuff block is legal content (the plain loop, design
        // doc section 6.1); an unknown effect is a content error.
        private static Debuff ToDebuff(DebuffBlock block, string context)
        {
            switch (block?.effect)
            {
                case "automationDisabled":
                    return new AutomationDisabledDebuff();
                case null:
                case "":
                    return null;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} debuff effect '{block.effect}' maps to no Debuff subclass. Importing no debuff.");
                    return null;
            }
        }

        private static UpgradeType ToUpgradeType(string type, string context)
        {
            switch (type)
            {
                case "buff":
                    return UpgradeType.Buff;
                case "contentUnlock":
                    return UpgradeType.ContentUnlock;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} has unknown type '{type}'. Defaulting to buff.");
                    return UpgradeType.Buff;
            }
        }

        private static BarFillMode ToFillMode(string fillMode, string context)
        {
            switch (fillMode)
            {
                case "perBar":
                    return BarFillMode.PerBar;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} has unknown fillMode '{fillMode}'. Defaulting to perBar.");
                    return BarFillMode.PerBar;
            }
        }

        private static BarFillDelivery ToDelivery(string delivery, string context)
        {
            switch (delivery)
            {
                case "continuous":
                    return BarFillDelivery.Continuous;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} has unknown delivery '{delivery}'. Defaulting to continuous.");
                    return BarFillDelivery.Continuous;
            }
        }

        // "rehearsal" to "Rehearsal" — display names for currencies the JSON
        // declares by id only
        private static string ToDisplayName(string id)
            => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id.Substring(1);

        private static void EnsureFolders()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var folder in new[] { ChaptersFolder, SectionsFolder, CurrenciesFolder, GeneratorsFolder,
                UpgradesFolder, BarsFolder, BarGroupsFolder, EventsFolder, RewardsFolder })
                Directory.CreateDirectory(Path.Combine(projectRoot, folder));
            AssetDatabase.Refresh();
        }

        // Initializes an asset only when the result would differ from what is
        // already saved: the init runs on a scratch instance first and the two
        // serialized forms are compared. Unity assigns fresh managed-reference
        // ids (rid) to every new [SerializeReference] instance, so blindly
        // re-initializing rewrites every gate/payload holder with id churn even
        // when nothing changed — re-importing an unchanged JSON must leave the
        // working tree clean.
        private static void ApplyIfChanged<T>(T asset, Action<T> initialize) where T : ScriptableObject
        {
            var candidate = (T)ScriptableObject.CreateInstance(asset.GetType());
            candidate.name = asset.name;
            initialize(candidate);

            var changed = NormalizeReferenceIds(EditorJsonUtility.ToJson(asset))
                != NormalizeReferenceIds(EditorJsonUtility.ToJson(candidate));
            UnityEngine.Object.DestroyImmediate(candidate);
            if (!changed)
                return;

            initialize(asset);
            EditorUtility.SetDirty(asset);
        }

        // managed-reference ids are per-instance, so two structurally identical
        // objects serialize differently; map each distinct rid to its
        // first-appearance order before comparing
        private static string NormalizeReferenceIds(string json)
        {
            var order = new Dictionary<string, string>();
            return Regex.Replace(json, "\"rid\":(-?\\d+)", match =>
            {
                var rid = match.Groups[1].Value;
                if (!order.TryGetValue(rid, out var stable))
                {
                    stable = order.Count.ToString();
                    order.Add(rid, stable);
                }
                return $"\"rid\":{stable}";
            });
        }

        // like LoadOrCreate, but a reward id whose type changed in the JSON needs
        // its asset recreated as the new subclass
        private static T LoadOrCreateReward<T>(string assetPath) where T : RewardDefinition
        {
            var existing = AssetDatabase.LoadAssetAtPath<RewardDefinition>(assetPath);
            if (existing is T match)
                return match;
            if (existing != null)
                AssetDatabase.DeleteAsset(assetPath);

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        // DTOs mirroring the JSON for JsonUtility; unknown JSON fields (notes,
        // _meta, capstone, progression, balanceTargets) are simply skipped.
#pragma warning disable 0649 // fields are assigned by JsonUtility
        [Serializable]
        private class ChapterFile
        {
            public ChapterBlock chapter;
            public ConstantsBlock constants;
            public FlagBlock[] flags;
            public SectionBlock[] sections;
            public GeneratorBlock[] generators;
            public UpgradeBlock[] upgrades;
            public RewardEntryBlock[] rewards;
            public RehearsalBlock rehearsal;
            public BarsBlock bars;
            public FansBlock fans;
            public EventBlock[] events;
        }

        [Serializable]
        private class FlagBlock
        {
            public string id;
        }

        // one entry in the shared reward pool; which fields matter depends on type
        [Serializable]
        private class RewardEntryBlock
        {
            public string id;
            public string name;
            public string type;
            public double value;
            public string scope;
            public string flag;
        }

        [Serializable]
        private class ChapterBlock
        {
            public string id;
            public int index;
            public string name;
            public string theme;
            public string storyBeatOpen;
            public string storyBeatCapstone;
            public int capstoneRecordsGate;
        }

        [Serializable]
        private class ConstantsBlock
        {
            public RecordBuffBlock recordBuff;
            public double tapBaseValue;
        }

        // a multiplier declares the currencies it affects (plural); production
        // of anything it doesn't name is untouched
        [Serializable]
        private class RecordBuffBlock
        {
            public double perRecord;
            public string[] affects;
        }

        [Serializable]
        private class SectionBlock
        {
            public string id;
            public string name;
            public string[] modules;
            public ConditionBlock visibleWhen;
        }

        // the discriminated Condition shape; which fields matter depends on type
        [Serializable]
        private class ConditionBlock
        {
            public string type;
            public string currency;
            public double amount;
            public string generator;
            public string flag;
            public string group;
            public double value;
            public ConditionLeafBlock[] all;
            public ConditionLeafBlock[] any;
        }

        // compound children: the same shape minus nesting (JsonUtility cannot
        // express recursive DTOs)
        [Serializable]
        private class ConditionLeafBlock
        {
            public string type;
            public string currency;
            public double amount;
            public string generator;
            public string flag;
            public string group;
            public double value;
        }

        [Serializable]
        private class GeneratorBlock
        {
            public string id;
            public string name;
            public string produces;
            public bool isBandmate;
            public GeneratorCostBlock cost;
            public double baseOutput;
            public ConditionBlock unlock;
        }

        // a generator's cost declares its currency, independent of `produces`
        [Serializable]
        private class GeneratorCostBlock
        {
            public string currency;
            public double amount;
            public double growth;
        }

        [Serializable]
        private class CostBlock
        {
            public string currency;
            public double amount;
        }

        [Serializable]
        private class PayloadBlock
        {
            public string effect;
            public double value;
            public string generator;
            public string flag;
        }

        [Serializable]
        private class UpgradeBlock
        {
            public string id;
            public string name;
            public string type;
            public string scope;
            public CostBlock cost;
            public ConditionBlock gate;
            public PayloadBlock payload;
        }

        [Serializable]
        private class RehearsalBlock
        {
            public string currency;
            public string group; // CurrencyGroupDefinition id, e.g. "run"
            public string revealFlag;
            public double perSec;
            public double perTap;
        }

        [Serializable]
        private class BarsBlock
        {
            public BarGroupBlock[] groups;
            public string scope;
        }

        [Serializable]
        private class BarGroupBlock
        {
            public string id;
            public string name;
            public string revealFlag;
            public string fillMode;
            public string delivery;
            public BarBlock[] bars;
        }

        [Serializable]
        private class BarBlock
        {
            public string id;
            public string name;
            public string fillCurrency;
            public double fillRequirement;
            public string reward; // reward pool id
        }

        [Serializable]
        private class FansBlock
        {
            public string currency;
            public string revealFlag;
            public double baseFansPerSec;
            public double perBandmateOwnedBonus;
        }

        [Serializable]
        private class EventBlock
        {
            public string id;
            public string name;
            public ConditionBlock availableWhen;
            public bool baselineReset;
            public TierBlock[] tiers;
        }

        [Serializable]
        private class TierBlock
        {
            public int tier;
            public DebuffBlock debuff;
            public ConditionBlock goal;
            public double timerSeconds;
            public bool failable;
            public string reward; // reward pool id
        }

        [Serializable]
        private class DebuffBlock
        {
            public string effect;
        }
#pragma warning restore 0649
    }
}
