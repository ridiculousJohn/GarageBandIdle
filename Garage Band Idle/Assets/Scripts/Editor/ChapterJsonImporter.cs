using System;
using System.Collections.Generic;
using System.IO;
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
    // by label, so no asset lives in Resources.
    //
    // The JSON's varied gate shapes ({currency,amount}, cashEarnedTotal,
    // ownedCount, recordsAtLeast, compound) all normalize into GateCondition
    // lists with generic types — e.g. cashEarnedTotal becomes
    // currencyEarnedTotal + currencyId "cash" — so runtime handlers never
    // hardcode a currency.
    public static class ChapterJsonImporter
    {
        private const string ChaptersFolder = "Assets/ScriptableObjects/Chapters";
        private const string GeneratorsFolder = "Assets/ScriptableObjects/Generators";
        private const string UpgradesFolder = "Assets/ScriptableObjects/Upgrades";
        private const string CoversFolder = "Assets/ScriptableObjects/Covers";
        private const string EventsFolder = "Assets/ScriptableObjects/Events";

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

            var generators = new List<GeneratorDefinition>();
            foreach (var block in data.generators ?? Array.Empty<GeneratorBlock>())
            {
                var asset = LoadOrCreate<GeneratorDefinition>($"{GeneratorsFolder}/{block.id}.asset");
                asset.EditorInitialize(block.id, block.name, block.produces,
                    block.baseCost, block.costGrowth, block.baseOutput, ToConditions(block.unlock));
                EditorUtility.SetDirty(asset);
                generators.Add(asset);
            }

            var upgrades = new List<UpgradeDefinition>();
            foreach (var block in data.upgrades ?? Array.Empty<UpgradeBlock>())
            {
                var asset = LoadOrCreate<UpgradeDefinition>($"{UpgradesFolder}/{block.id}.asset");
                asset.EditorInitialize(block.id, block.name, block.type, block.scope,
                    block.cost?.currency, block.cost?.amount ?? 0, ToConditions(block.gate),
                    new UpgradePayload(block.payload?.effect, block.payload?.value ?? 0,
                        block.payload?.generator, block.payload?.system));
                EditorUtility.SetDirty(asset);
                upgrades.Add(asset);
            }

            var covers = new List<CoverDefinition>();
            foreach (var bar in data.covers?.bars ?? Array.Empty<CoverBarBlock>())
            {
                var asset = LoadOrCreate<CoverDefinition>($"{CoversFolder}/{bar.id}.asset");
                asset.EditorInitialize(bar.id, bar.name, bar.fillRequirement,
                    bar.reward?.effect, bar.reward?.value ?? 0);
                EditorUtility.SetDirty(asset);
                covers.Add(asset);
            }

            var events = new List<EventDefinition>();
            foreach (var block in data.events ?? Array.Empty<EventBlock>())
            {
                var tiers = new List<EventTier>();
                foreach (var tier in block.tiers ?? Array.Empty<TierBlock>())
                {
                    tiers.Add(new EventTier(tier.tier, tier.debuff?.effect,
                        tier.goal?.currency, tier.goal?.amount ?? 0, tier.timerSeconds, tier.failable,
                        tier.reward?.effect, tier.reward?.value ?? 0, tier.reward?.scope));
                }

                var asset = LoadOrCreate<EventDefinition>($"{EventsFolder}/{block.id}.asset");
                asset.EditorInitialize(block.id, block.name, block.type,
                    ToConditions(block.availableWhen), block.baselineReset, tiers);
                EditorUtility.SetDirty(asset);
                events.Add(asset);
            }

            var sections = new List<ChapterSection>();
            foreach (var block in data.sections ?? Array.Empty<SectionBlock>())
            {
                sections.Add(new ChapterSection(block.id, block.name,
                    new List<string>(block.modules ?? Array.Empty<string>()), ToConditions(block.visibleWhen)));
            }

            var chapterAsset = LoadOrCreate<ChapterDefinition>($"{ChaptersFolder}/{data.chapter.id}.asset");
            chapterAsset.EditorInitialize(data.chapter.id, data.chapter.index, data.chapter.name,
                data.chapter.theme, data.chapter.storyBeatOpen, data.chapter.storyBeatCapstone,
                data.chapter.capstoneRecordsGate,
                data.constants?.tapBaseValue ?? 1, data.constants?.recordBuffPerRecord ?? 0,
                new FansConfig(data.fans?.baseFansPerSec ?? 0, data.fans?.perBandmateOwnedBonus ?? 0),
                new RehearsalConfig(data.covers?.rehearsal?.pointsPerSec ?? 0, data.covers?.rehearsal?.pointsPerTap ?? 0),
                sections, generators, upgrades, covers, events);
            EditorUtility.SetDirty(chapterAsset);

            MarkAllContentAddressable();

            AssetDatabase.SaveAssets();
            var summary = $"Imported '{data.chapter.id}' — {generators.Count} generators, " +
                $"{upgrades.Count} upgrades, {covers.Count} covers, {events.Count} events. All content marked addressable.";
            Debug.Log($"ChapterJsonImporter: {summary}");
            EditorUtility.DisplayDialog("Chapter import", summary, "OK");
        }

        // Sweeps every definition asset in the project (including hand-authored
        // currencies/groups) into Addressables: address "<label>/<asset name>",
        // one label per type. Safe to re-run; entries are created or updated.
        [MenuItem("GarageBandIdle/Mark Content Addressable")]
        public static void MarkAllContentAddressable()
        {
            // creates Assets/AddressableAssetsData + settings on first use
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

            int count = 0;
            count += MarkType<CurrencyDefinition>(settings, ContentLabels.Currency);
            count += MarkType<CurrencyGroupDefinition>(settings, ContentLabels.CurrencyGroup);
            count += MarkType<ChapterDefinition>(settings, ContentLabels.Chapter);
            count += MarkType<GeneratorDefinition>(settings, ContentLabels.Generator);
            count += MarkType<UpgradeDefinition>(settings, ContentLabels.Upgrade);
            count += MarkType<CoverDefinition>(settings, ContentLabels.Cover);
            count += MarkType<EventDefinition>(settings, ContentLabels.Event);
            count += MarkModulePrefabs(settings);

            AssetDatabase.SaveAssets();
            Debug.Log($"ChapterJsonImporter: {count} definition assets marked addressable.");
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

        // Normalizes every gate/unlock shape in the JSON into a flat all-must-hold
        // condition list (compound gates flatten; single gates become one entry).
        private static List<GateCondition> ToConditions(GateBlock gate)
        {
            var conditions = new List<GateCondition>();
            if (gate == null)
                return conditions;

            if (gate.type == "compound")
            {
                foreach (var leaf in gate.all ?? Array.Empty<GateLeafBlock>())
                {
                    if (leaf.coversCompleted > 0)
                        conditions.Add(new GateCondition(GateCondition.TypeCoversCompleted, null, null, null, leaf.coversCompleted));
                    else if (!string.IsNullOrEmpty(leaf.flag))
                        conditions.Add(new GateCondition(GateCondition.TypeFlagSet, null, null, leaf.flag, 0));
                    else if (!string.IsNullOrEmpty(leaf.currency))
                        conditions.Add(new GateCondition(GateCondition.TypeCurrencyBalance, leaf.currency, null, null, leaf.amount));
                    else
                        Debug.LogWarning("ChapterJsonImporter: compound gate has an empty leaf; skipping it.");
                }
                return conditions;
            }

            switch (gate.type)
            {
                case null:
                case "":
                    // A bare { currency, amount } gate — but note JsonUtility
                    // materializes ABSENT gate objects as empty instances, so a
                    // typeless gate with no currency means "no gate at all".
                    if (!string.IsNullOrEmpty(gate.currency))
                        conditions.Add(new GateCondition(GateCondition.TypeCurrencyBalance, gate.currency, null, null, gate.amount));
                    break;
                case "cashEarnedTotal":
                    conditions.Add(new GateCondition(GateCondition.TypeCurrencyEarnedTotal, "cash", null, null, gate.value));
                    break;
                case "recordsAtLeast":
                    // Records are never spent, so cumulative == earned total
                    conditions.Add(new GateCondition(GateCondition.TypeCurrencyEarnedTotal, "records", null, null, gate.value));
                    break;
                case "ownedCount":
                    conditions.Add(new GateCondition(GateCondition.TypeOwnedCount, null, gate.generator, null, gate.value));
                    break;
                case "flagSet":
                    conditions.Add(new GateCondition(GateCondition.TypeFlagSet, null, null, gate.flag, 0));
                    break;
                default:
                    Debug.LogWarning($"ChapterJsonImporter: gate type '{gate.type}' has no normalization; importing it verbatim.");
                    conditions.Add(new GateCondition(gate.type, gate.currency, gate.generator, gate.flag, gate.value != 0 ? gate.value : gate.amount));
                    break;
            }
            return conditions;
        }

        private static void EnsureFolders()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var folder in new[] { ChaptersFolder, GeneratorsFolder, UpgradesFolder, CoversFolder, EventsFolder })
                Directory.CreateDirectory(Path.Combine(projectRoot, folder));
            AssetDatabase.Refresh();
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
            public SectionBlock[] sections;
            public GeneratorBlock[] generators;
            public UpgradeBlock[] upgrades;
            public CoversBlock covers;
            public FansBlock fans;
            public EventBlock[] events;
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
            public double recordBuffPerRecord;
            public double tapBaseValue;
        }

        [Serializable]
        private class SectionBlock
        {
            public string id;
            public string name;
            public string[] modules;
            public GateBlock visibleWhen;
        }

        [Serializable]
        private class GateBlock
        {
            public string type;
            public string currency;
            public double amount;
            public string generator;
            public string flag;
            public double value;
            public GateLeafBlock[] all;
        }

        [Serializable]
        private class GateLeafBlock
        {
            public string currency;
            public double amount;
            public string flag;
            public double coversCompleted;
        }

        [Serializable]
        private class GeneratorBlock
        {
            public string id;
            public string name;
            public string produces;
            public double baseCost;
            public double costGrowth;
            public double baseOutput;
            public GateBlock unlock;
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
            public string system;
        }

        [Serializable]
        private class UpgradeBlock
        {
            public string id;
            public string name;
            public string type;
            public string scope;
            public CostBlock cost;
            public GateBlock gate;
            public PayloadBlock payload;
        }

        [Serializable]
        private class CoversBlock
        {
            public RehearsalBlock rehearsal;
            public CoverBarBlock[] bars;
        }

        [Serializable]
        private class RehearsalBlock
        {
            public double pointsPerSec;
            public double pointsPerTap;
        }

        [Serializable]
        private class CoverBarBlock
        {
            public string id;
            public string name;
            public double fillRequirement;
            public RewardBlock reward;
        }

        [Serializable]
        private class RewardBlock
        {
            public string effect;
            public double value;
            public string scope;
        }

        [Serializable]
        private class FansBlock
        {
            public double baseFansPerSec;
            public double perBandmateOwnedBonus;
        }

        [Serializable]
        private class EventBlock
        {
            public string id;
            public string name;
            public string type;
            public GateBlock availableWhen;
            public bool baselineReset;
            public TierBlock[] tiers;
        }

        [Serializable]
        private class TierBlock
        {
            public int tier;
            public DebuffBlock debuff;
            public CostBlock goal;
            public double timerSeconds;
            public bool failable;
            public RewardBlock reward;
        }

        [Serializable]
        private class DebuffBlock
        {
            public string effect;
        }
#pragma warning restore 0649
    }
}
