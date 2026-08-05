using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    // (address "<label>/<id>", one label per type) - runtime discovery loads
    // by label, so no asset lives in Resources and no chapter holds a direct
    // asset reference: content links by string id, resolved at load.
    //
    // Every gate/unlock/visibility/availability rule in the JSON is one
    // discriminated Condition shape ({ "type": ... }), mapped 1:1 onto the
    // Condition subclass family - no bespoke gate shapes survive import.
    public static class ChapterJsonImporter
    {
        private const string ChaptersFolder = "Assets/ScriptableObjects/Chapters";
        private const string SectionsFolder = "Assets/ScriptableObjects/Sections";
        private const string CurrenciesFolder = "Assets/ScriptableObjects/Currencies";
        private const string ProducersFolder = "Assets/ScriptableObjects/Producers";
        private const string GeneratorsFolder = "Assets/ScriptableObjects/Generators";
        private const string UpgradesFolder = "Assets/ScriptableObjects/Upgrades";
        private const string BarsFolder = "Assets/ScriptableObjects/Bars";
        private const string BarGroupsFolder = "Assets/ScriptableObjects/BarGroups";
        private const string EventsFolder = "Assets/ScriptableObjects/Events";
        private const string RewardsFolder = "Assets/ScriptableObjects/Rewards";

        // an explicit null in the JSON behaves exactly like an absent field:
        // the member keeps its DTO initializer, the single source of "absent"
        // semantics
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

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
            var data = JsonConvert.DeserializeObject<ChapterFile>(File.ReadAllText(jsonPath), JsonSettings);
            if (data?.chapter == null || string.IsNullOrEmpty(data.chapter.id))
            {
                Debug.LogError($"ChapterJsonImporter: '{jsonPath}' has no chapter block with an id. Nothing imported.");
                return;
            }

            // A malformed condition aborts the whole import (design doc section
            // 12, rules 8 and 9). A condition that cannot convert becomes null,
            // and null means "no gate" - so dropping one silently UNGATES its
            // content: a generator revealed at boot, a buff with nothing to meet,
            // a section shown from the first frame. Boot validation cannot catch
            // it either, because a null Condition is legal content everywhere.
            //
            // Aborting rather than skipping the affected asset: a skip leaves the
            // previous import's asset on disk (ApplyIfChanged never runs) while
            // the chapter list drops its id, so the content silently vanishes
            // from the game and boot validation sees a healthy-looking orphan.
            // An import is one manual action over one file, so refusing all of it
            // costs a re-run and leaves no partial state. This runs BEFORE
            // EnsureFolders and every LoadOrCreate, which is what makes "nothing
            // was written" true rather than approximately true.
            var conditionFaults = CollectConditionFaults(data);
            if (conditionFaults.Count > 0)
            {
                foreach (var fault in conditionFaults)
                    Debug.LogError($"ChapterJsonImporter: {fault}");
                // one grep-able line: batchmode exits 0 either way, so the
                // headless verify loop needs a token of its own to check
                Debug.LogError($"ChapterJsonImporter: IMPORT ABORTED - {conditionFaults.Count} malformed condition(s); nothing was written. Fix the JSON and re-import.");
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

            // The chapter's currency roster (design doc section 12, rule 12):
            // every currency the chapter's economy owns, as pure state
            // ({id, group}) - how a currency is earned lives on producers (rule
            // 13), never here. The context builds its pool from this list, so a
            // currency missing from it has no balance at runtime no matter how
            // many producers name it.
            //
            // Records is deliberately absent: it is placed Global, held by the
            // startup pool, and naming it in a chapter roster is refused at
            // construction.
            var currencyIds = new List<string>();
            foreach (var block in data.currencies ?? Array.Empty<CurrencyEntryBlock>())
            {
                if (!IsImportableCurrencyEntry(block))
                    continue;

                var currencyAsset = LoadOrCreateCurrency(block.id);
                ApplyIfChanged(currencyAsset, asset => asset.EditorInitialize(block.id,
                    ToDisplayName(block.id), block.group));
                currencyIds.Add(block.id);
            }

            // producers: the module-held production sources (design doc
            // section 12, rule 13) - what the Jam button yields per tap, plus
            // Rehearsal's passive trickle. Config gates are ordinary
            // Conditions; an invalid entry skips the whole producer, because a
            // producer missing one of its yields is not the authored producer.
            var producerIds = new List<string>();
            foreach (var block in data.producers ?? Array.Empty<ProducerBlock>())
            {
                if (string.IsNullOrEmpty(block.id))
                {
                    Debug.LogError("ChapterJsonImporter: producers array contains an entry with an empty id. Skipping it.");
                    continue;
                }
                if (producerIds.Contains(block.id))
                {
                    Debug.LogError($"ChapterJsonImporter: duplicate producer id '{block.id}'. Keeping the first.");
                    continue;
                }
                // a producer with no module is a PASSIVE source: nothing presents
                // it and the player never touches it, which is how fan accrual
                // is authored. It is still module-held in the sense section 9
                // means - not a generator, so it never idle-pays. What it must
                // have is production, which ToProductionConfigs refuses without.
                var configs = ToProductionConfigs(block);
                if (configs == null)
                    continue;

                var producerAsset = LoadOrCreate<ProducerDefinition>($"{ProducersFolder}/{block.id}.asset");
                ApplyIfChanged(producerAsset, asset => asset.EditorInitialize(block.id, block.module, configs));
                producerIds.Add(block.id);
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

                // a reward carries no scope: whatever applies it declares the
                // lifetime (a bar group, an event tier), so the same asset can be a
                // run payoff in one place and a permanent one in another
                var effect = ToRewardEffect(block, $"reward '{block.id}'");
                if (effect == null)
                    continue;

                var reward = LoadOrCreateReward($"{RewardsFolder}/{block.id}.asset");
                ApplyIfChanged(reward, asset => asset.EditorInitialize(block.id, block.name, effect));
                rewardIds.Add(block.id);
            }

            var sectionIds = new List<string>();
            foreach (var block in data.sections ?? Array.Empty<SectionBlock>())
            {
                var asset = LoadOrCreate<SectionDefinition>($"{SectionsFolder}/{block.id}.asset");
                var modules = new List<string>(block.modules ?? Array.Empty<string>());
                // a section IS its modules: one with none reveals an empty region
                // when its visibleWhen holds. Written anyway (an empty region is
                // inert, not wrong) so the rest of the chapter still imports.
                if (modules.Count == 0)
                    Debug.LogError($"ChapterJsonImporter: section '{block.id}' names no modules - it would reveal an empty region.");
                var visibleWhen = ToCondition(block.visibleWhen, $"section '{block.id}' (visibleWhen)");
                ApplyIfChanged(asset, section => section.EditorInitialize(block.id, block.name, modules, visibleWhen));
                sectionIds.Add(block.id);
            }

            var generatorIds = new List<string>();
            foreach (var block in data.generators ?? Array.Empty<GeneratorBlock>())
            {
                // a missing/invalid cost would import as zeros - never write
                // that state: the asset is not created/updated and the chapter
                // does not list the generator. Growth < 1 (shrinking costs) is
                // legal; growth <= 0 breaks the curve.
                if (block.cost == null || string.IsNullOrEmpty(block.cost.currency)
                    || block.cost.amount <= 0 || block.cost.growth <= 0)
                {
                    Debug.LogError($"ChapterJsonImporter: generator '{block.id}' has a missing or invalid cost block (needs currency, amount > 0, growth > 0). Skipping it - fix the JSON and re-import.");
                    continue;
                }

                // production must never drain; zero output stays legal (a pure
                // fan-rate bandmate is coherent)
                if (block.baseOutput < 0)
                {
                    Debug.LogError($"ChapterJsonImporter: generator '{block.id}' has a negative baseOutput ({block.baseOutput}). Skipping it - fix the JSON and re-import.");
                    continue;
                }

                var asset = LoadOrCreate<GeneratorDefinition>($"{GeneratorsFolder}/{block.id}.asset");
                var unlock = ToCondition(block.unlock, $"generator '{block.id}' (unlock)");
                ApplyIfChanged(asset, generator => generator.EditorInitialize(block.id, block.name, block.produces,
                    block.isBandmate, block.cost?.currency, block.cost?.amount ?? 0, block.cost?.growth ?? 0,
                    block.baseOutput, unlock));
                generatorIds.Add(block.id);
            }

            var upgradeIds = new List<string>();
            foreach (var block in data.upgrades ?? Array.Empty<UpgradeBlock>())
            {
                var type = ToUpgradeType(block.type, $"upgrade '{block.id}'");

                // a negative cost would GRANT currency when the buff purchase
                // flow lands - never write that state
                if ((block.cost?.amount ?? 0) < 0)
                {
                    Debug.LogError($"ChapterJsonImporter: upgrade '{block.id}' has a negative cost amount ({block.cost.amount}). Skipping it - fix the JSON and re-import.");
                    continue;
                }

                // a buff is bought, so it must cost something: a zero cost is
                // an endless free purchase, the same failure a non-positive
                // generator cost would be. Content unlocks legitimately cost
                // nothing - their gate is the price.
                if (type == UpgradeType.Buff && (block.cost?.amount ?? 0) == 0)
                {
                    Debug.LogError($"ChapterJsonImporter: upgrade '{block.id}' is a buff with no cost - it would be free to buy. Skipping it - fix the JSON and re-import.");
                    continue;
                }

                // an amount with no currency to charge is the same free purchase
                // from the other side - never write it. Whether a named currency
                // resolves is a database question, so boot validation owns that.
                if ((block.cost?.amount ?? 0) > 0 && string.IsNullOrEmpty(block.cost?.currency))
                {
                    Debug.LogError($"ChapterJsonImporter: upgrade '{block.id}' has a cost amount ({block.cost.amount}) but names no cost currency. Skipping it - fix the JSON and re-import.");
                    continue;
                }

                var asset = LoadOrCreate<UpgradeDefinition>($"{UpgradesFolder}/{block.id}.asset");
                var scope = ToScope(block.scope, $"upgrade '{block.id}'");
                var gate = ToCondition(block.gate, $"upgrade '{block.id}' (gate)");
                var payload = ToPayload(block.payload, $"upgrade '{block.id}'");
                ApplyIfChanged(asset, upgrade => upgrade.EditorInitialize(block.id, block.name, type, scope,
                    block.cost?.currency, block.cost?.amount ?? 0, gate, payload));
                upgradeIds.Add(block.id);
            }

            var barGroupIds = new List<string>();
            var barCount = 0;
            foreach (var group in data.bars?.groups ?? Array.Empty<BarGroupBlock>())
            {
                if (!IsImportableBarGroup(group))
                    continue;

                var barIds = new List<string>();
                foreach (var bar in group.bars ?? Array.Empty<BarBlock>())
                {
                    // a non-positive requirement can never be legitimately
                    // filled - never write that state: the asset is not
                    // created/updated and the group does not list the bar
                    if (bar.fillRequirement <= 0)
                    {
                        Debug.LogError($"ChapterJsonImporter: bar '{bar.id}' has a non-positive fillRequirement ({bar.fillRequirement}). Skipping it - fix the JSON and re-import.");
                        continue;
                    }

                    var barAsset = LoadOrCreate<BarDefinition>($"{BarsFolder}/{bar.id}.asset");
                    ApplyIfChanged(barAsset, asset => asset.EditorInitialize(bar.id, bar.name,
                        bar.fillCurrency, bar.fillRequirement, bar.reward));
                    barIds.Add(bar.id);
                    barCount++;
                }

                var groupAsset = LoadOrCreate<BarGroupDefinition>($"{BarGroupsFolder}/{group.id}.asset");
                var fillBehavior = ToFillBehavior(group.fillMode, group.delivery, $"bar group '{group.id}'");
                var groupScope = ToScope(data.bars.scope, $"bar group '{group.id}'");
                var visibleWhen = ToCondition(group.visibleWhen, $"bar group '{group.id}' (visibleWhen)");
                ApplyIfChanged(groupAsset, asset => asset.EditorInitialize(group.id, group.name, visibleWhen,
                    fillBehavior, groupScope, barIds));
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
                        ToCondition(tier.goal, $"event '{block.id}' tier {tier.tier} (goal)"),
                        tier.timerSeconds, tier.failable, tier.reward,
                        ToScope(tier.scope, $"event '{block.id}' tier {tier.tier}")));
                }

                var asset = LoadOrCreate<EventDefinition>($"{EventsFolder}/{block.id}.asset");
                var availableWhen = ToCondition(block.availableWhen, $"event '{block.id}' (availableWhen)");
                ApplyIfChanged(asset, gameEvent => gameEvent.EditorInitialize(block.id, block.name,
                    availableWhen, block.baselineReset, tiers));
                eventIds.Add(block.id);
            }

            // negative tuning drains or dead-ends instead of earning; the
            // chapter still imports (config is not skippable content) - boot
            // validation reports it too
            if ((data.fans?.perBandmateOwnedBonus ?? 0) < 0)
                Debug.LogError("ChapterJsonImporter: fans block has a negative perBandmateOwnedBonus. Fix the JSON and re-import.");
            // Three pre-5.7 keys, all now production: the base rate and its gate
            // are a config on a producer (design doc section 12, rule 13), which
            // is what keeps fan accrual out of the idle payout by construction
            // (section 9). Refused rather than ignored, the same fail-closed rule
            // the currency 'earn' block and the bar group's 'revealFlag' get. The
            // chapter still imports - a fans config is not skippable content - and
            // boot validation reports what the missing production leaves behind.
            ReportStaleFansKeys(data.fans);
            // the pre-5.4 schema put the Jam yield in constants; a leftover
            // tapBaseValue would silently disagree with the jam producer's
            // cash config, so its presence is refused rather than dropped
            if (data.constants?.tapBaseValue != null)
                Debug.LogError("ChapterJsonImporter: constants block still carries tapBaseValue - the Jam yield lives on the jam producer's cash config (design doc section 12, rule 13). Fix the JSON and re-import.");
            if ((data.constants?.recordBuff?.perRecord ?? 0) < 0)
                Debug.LogError("ChapterJsonImporter: recordBuff block has a negative perRecord. Fix the JSON and re-import.");

            var chapterAsset = LoadOrCreate<ChapterDefinition>($"{ChaptersFolder}/{data.chapter.id}.asset");
            var recordBuff = new RecordBuffConfig(data.constants?.recordBuff?.perRecord ?? 0,
                new List<string>(data.constants?.recordBuff?.affects ?? Array.Empty<string>()));
            var fans = new FansConfig(data.fans?.currency, data.fans?.perBandmateOwnedBonus ?? 0);
            ApplyIfChanged(chapterAsset, chapter => chapter.EditorInitialize(data.chapter.id, data.chapter.index,
                data.chapter.name, data.chapter.theme, data.chapter.storyBeatOpen, data.chapter.storyBeatCapstone,
                data.chapter.capstoneRecordsGate, recordBuff,
                fans, flagIds, currencyIds, producerIds, sectionIds, generatorIds, upgradeIds, barGroupIds, eventIds));

            MarkAllContentAddressable();

            AssetDatabase.SaveAssets();
            var summary = $"Imported '{data.chapter.id}' - {flagIds.Count} flags, {producerIds.Count} producers, " +
                $"{sectionIds.Count} sections, {generatorIds.Count} generators, {upgradeIds.Count} upgrades, " +
                $"{barGroupIds.Count} bar groups ({barCount} bars), {eventIds.Count} events, {rewardIds.Count} rewards. " +
                "All content marked addressable.";
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
            count += MarkType<ProducerDefinition>(settings, ContentLabels.Producer);
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
                ContentLabels.Currency, ContentLabels.CurrencyGroup, ContentLabels.Producer,
                ContentLabels.Chapter, ContentLabels.Section, ContentLabels.Generator,
                ContentLabels.Upgrade, ContentLabels.Bar, ContentLabels.BarGroup,
                ContentLabels.Event, ContentLabels.Reward, ContentLabels.Module,
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

        // ---- the condition pre-pass ------------------------------------------

        // Every condition block in the file, walked before a single asset is
        // touched. The seven sites below are the complete set of places a
        // Condition is authored, and a new one has to be added here - that is the
        // cost of the guarantee. Checking at the conversion sites instead is what
        // fell open in the first place: a conversion that answers "no gate" for
        // bad input reads as success at every call site, so no caller could tell
        // an absent gate from an unconvertible one.
        private static List<string> CollectConditionFaults(ChapterFile data)
        {
            var faults = new List<string>();

            foreach (var block in data.sections ?? Array.Empty<SectionBlock>())
                CollectConditionFaults(block.visibleWhen, $"section '{block.id}' (visibleWhen)", faults);

            foreach (var block in data.generators ?? Array.Empty<GeneratorBlock>())
                CollectConditionFaults(block.unlock, $"generator '{block.id}' (unlock)", faults);

            foreach (var block in data.upgrades ?? Array.Empty<UpgradeBlock>())
                CollectConditionFaults(block.gate, $"upgrade '{block.id}' (gate)", faults);

            foreach (var group in data.bars?.groups ?? Array.Empty<BarGroupBlock>())
                CollectConditionFaults(group.visibleWhen, $"bar group '{group.id}' (visibleWhen)", faults);

            foreach (var block in data.producers ?? Array.Empty<ProducerBlock>())
            {
                foreach (var entry in block.production ?? Array.Empty<ProductionEntryBlock>())
                    CollectConditionFaults(entry.gate, $"producer '{block.id}' production for '{entry.currency}' (gate)", faults);
            }

            foreach (var block in data.events ?? Array.Empty<EventBlock>())
            {
                CollectConditionFaults(block.availableWhen, $"event '{block.id}' (availableWhen)", faults);
                foreach (var tier in block.tiers ?? Array.Empty<TierBlock>())
                    CollectConditionFaults(tier.goal, $"event '{block.id}' tier {tier.tier} (goal)", faults);
            }

            return faults;
        }

        // One condition block and everything nested under it. An absent block is
        // NOT a fault: the DTO materializes a missing gate as an empty instance,
        // and no gate is legal content at every site above. The fault is
        // authored-but-unconvertible - the only case where the null that reaches
        // an asset means something other than what the author wrote.
        private static void CollectConditionFaults(ConditionBlock block, string context, List<string> faults)
        {
            if (block == null)
                return;

            if (string.IsNullOrEmpty(block.type))
            {
                // An absent gate materializes as the DTO's DEFAULT instance, so
                // "no type" alone does not mean "no gate" - it means "no gate"
                // only when nothing else was authored either. A block carrying
                // anything at all had a gate intended for it, and `type` is the
                // one key whose misspelling the unrecognized-key check cannot
                // report: ToCondition returns on the empty type before
                // ValidateThreshold ever runs, so `"typ": "flagSet"` would
                // otherwise import as content with no gate at all.
                if (IsAuthored(block))
                    faults.Add($"{context} has a condition object with no 'type'{DescribeKeys(block)} - a condition is identified by its 'type', so this would import as no gate.");
                return;
            }

            if (block.type != "compound")
            {
                if (!IsKnownConditionType(block.type))
                    faults.Add($"{context} has condition type '{block.type}', which maps to no Condition subclass.");
                return;
            }

            var all = block.all ?? Array.Empty<ConditionBlock>();
            var any = block.any ?? Array.Empty<ConditionBlock>();

            // Counted on the RAW arrays, never on the children that converted: a
            // compound authored empty and one whose every child is malformed are
            // different mistakes, and reporting them identically sends the author
            // looking in the wrong place.
            if (all.Length == 0 && any.Length == 0)
                faults.Add($"{context} is a compound condition with no children.");

            CollectChildFaults(all, $"{context} all", faults);
            CollectChildFaults(any, $"{context} any", faults);
        }

        // Whether anything was written inside the block - the test that separates
        // "no gate authored" from "a gate authored wrongly". PRESENCE, not
        // contents: the reference fields carry no initializers, so non-null means
        // the key was in the JSON whatever it held. Testing contents instead
        // would wave through `{"type": ""}` and `{"flag": ""}`, which are the
        // spellings a half-finished gate leaves behind - and the ones least
        // likely to be caught by eye.
        //
        // The extension bucket counts too: a key that matched no field is still
        // something the author typed. `value` is the documented exception (see
        // ConditionBlock) - a plain double cannot report its own absence, so a
        // bare `{"value": 0}` still reads as an absent gate.
        private static bool IsAuthored(ConditionBlock block)
            => (block.unrecognized != null && block.unrecognized.Count > 0)
               || block.type != null
               || block.currency != null
               || block.generator != null
               || block.flag != null
               || block.group != null
               || block.all != null
               || block.any != null
               || block.value != 0;

        // names the misspelled keys when there are any, since that is the edit
        private static string DescribeKeys(ConditionBlock block)
            => block.unrecognized != null && block.unrecognized.Count > 0
                ? $" (unrecognized key(s): {string.Join(", ", block.unrecognized.Keys)})"
                : string.Empty;

        // A child with no type is a fault, not a skip. CompoundCondition already
        // fails closed on a null child in `all` and reports it at boot, so
        // preserving the null would be safe there - but a child dropped from
        // `any` leaves a compound quietly WEAKER rather than closed, and one rule
        // for both lists is what keeps that asymmetry from mattering.
        private static void CollectChildFaults(ConditionBlock[] children, string context, List<string> faults)
        {
            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] == null || string.IsNullOrEmpty(children[i].type))
                {
                    faults.Add($"{context}[{i}] is a compound child with no type.");
                    continue;
                }

                CollectConditionFaults(children[i], $"{context}[{i}]", faults);
            }
        }

        // Maps a JSON condition ({ "type": ... }) onto the Condition subclass
        // family. An absent gate means no gate: the DTO initializers materialize
        // absent objects as empty instances, so an empty type returns null
        // (always met).
        //
        // Every OTHER null this can return is unconvertible input, which
        // CollectConditionFaults has already aborted the import over - so by the
        // time anything calls this, null means exactly one thing.
        private static Condition ToCondition(ConditionBlock block, string context)
        {
            if (block == null || string.IsNullOrEmpty(block.type))
                return null;

            if (block.type == "compound")
            {
                var all = ToConditionList(block.all, $"{context} all");
                var any = ToConditionList(block.any, $"{context} any");
                if (all.Count == 0 && any.Count == 0)
                {
                    // unreachable: the pre-pass aborts on an empty compound before
                    // anything is written, so reaching this says the pre-pass
                    // missed a site rather than that the JSON is bad
                    Debug.LogError($"ChapterJsonImporter: {context} is a compound condition with no children - the condition pre-pass should have aborted the import. Importing no gate.");
                    return null;
                }
                return new CompoundCondition(all, any);
            }

            ValidateThreshold(block, context);
            return ToSimpleCondition(block.type, block.currency,
                block.generator, block.flag, block.group, block.value);
        }

        private static List<Condition> ToConditionList(ConditionBlock[] blocks, string context)
        {
            var conditions = new List<Condition>();
            var children = blocks ?? Array.Empty<ConditionBlock>();
            for (var i = 0; i < children.Length; i++)
            {
                var block = children[i];
                // unreachable for the same reason, and still a skip rather than a
                // refusal: the abort IS the refusal, and this caller has no way
                // to perform one
                if (block == null || string.IsNullOrEmpty(block.type))
                {
                    Debug.LogError($"ChapterJsonImporter: {context}[{i}] is a compound child with no type - the condition pre-pass should have aborted the import. Skipping it.");
                    continue;
                }

                var condition = ToCondition(block, $"{context}[{i}]");
                if (condition != null)
                    conditions.Add(condition);
            }
            return conditions;
        }

        // Every condition states its threshold as `value`, because a condition
        // compares against one; `amount` is a cost block's key, where the number is
        // a price. A key the DTO does not define is the importer's to catch: only
        // it sees the raw JSON, since the asset keeps just the keys that were read.
        //
        // The condition is still written when the threshold is bad. Dropping it
        // would mean "no gate", which is the very always-open failure being
        // reported, so the faithful asset plus Condition's fail-closed evaluation
        // is the safe pair.
        private static void ValidateThreshold(ConditionBlock block, string context)
        {
            if (block.unrecognized != null)
            {
                foreach (var key in block.unrecognized.Keys)
                    Debug.LogError($"ChapterJsonImporter: {context} carries unrecognized key '{key}' - a condition's threshold is 'value' ('amount' is a cost block's price). Fix the JSON and re-import.");
            }

            // flagSet compares against nothing, and an unknown type is reported by
            // the subclass mapping itself
            if (!IsThresholdType(block.type))
                return;
            if (block.value > 0)
                return;

            Debug.LogError($"ChapterJsonImporter: {context} has a non-positive value ({block.value}) - the gate would be met before play starts. Fix the JSON and re-import.");
        }

        // flagSet carries no threshold; every other non-compound type does
        private static bool IsThresholdType(string type)
            => type is "currency" or "currencyEarnedTotal" or "ownedCount"
                or "barsCompleted" or "recordsCumulative";

        // The types ToSimpleCondition maps, for the pre-pass to test before any
        // asset exists. Two spellings of one list that must agree - the same
        // duplication IsThresholdType above already carries for five of the six
        // names, which is why this sits beside it rather than somewhere tidier.
        // ToSimpleCondition's default case is the backstop that says they drifted.
        private static bool IsKnownConditionType(string type)
            => type is "currency" or "currencyEarnedTotal" or "ownedCount"
                or "flagSet" or "barsCompleted" or "recordsCumulative";

        // one currency entry's import decision: pure state ({id, group}) only -
        // the pre-5.4 schema put engagement earn on the currency, and an earn
        // block is stale JSON that used to mean something, so it is refused
        // loudly rather than silently dropped
        private static bool IsImportableCurrencyEntry(CurrencyEntryBlock block)
        {
            if (string.IsNullOrEmpty(block.id))
            {
                Debug.LogError("ChapterJsonImporter: currencies array contains an entry with an empty id. Skipping it.");
                return false;
            }
            if (block.earn != null)
            {
                Debug.LogError($"ChapterJsonImporter: currency '{block.id}' carries an 'earn' block - currencies are pure state, production lives on producers (design doc section 12, rule 13). Skipping it - fix the JSON and re-import.");
                return false;
            }
            return true;
        }

        // The three pre-5.7 fans keys, all now production: the base rate and its
        // gate are a config on a producer (design doc section 12, rule 13), which
        // is what keeps fan accrual out of the idle payout by construction
        // (section 9). Refused rather than ignored, the same fail-closed rule the
        // currency 'earn' block and the bar group's 'revealFlag' get. The chapter
        // still imports - a fans config is not skippable content - and boot
        // validation reports what the missing production leaves behind.
        //
        // Each test is PRESENCE, never contents: `"activeWhen": {}` and
        // `"revealFlag": ""` are stale keys just as much as filled-in ones, and a
        // contents test would wave through exactly the spellings least likely to
        // be noticed by hand.
        private static void ReportStaleFansKeys(FansBlock block)
        {
            if (block == null)
                return;

            if (block.baseFansPerSec != null)
                Debug.LogError("ChapterJsonImporter: fans block still carries 'baseFansPerSec' - the base fan rate is a production config on a producer (design doc section 12, rule 13). Fix the JSON and re-import.");
            if (block.revealFlag != null)
                Debug.LogError("ChapterJsonImporter: fans block still carries a 'revealFlag' key - accrual is gated by the production config's gate (design doc section 12, rules 8, 9 and 13). Fix the JSON and re-import.");
            if (block.activeWhen != null)
                Debug.LogError("ChapterJsonImporter: fans block still carries 'activeWhen' - the accrual gate moved onto the production config's 'gate' (design doc section 12, rule 13). Fix the JSON and re-import.");
        }

        // the fans-block parse path, exposed like ParseCondition: tests cover that
        // every stale key is refused on presence, including its empty spelling
        internal static void ParseFansBlockStaleKeys(string json)
            => ReportStaleFansKeys(JsonConvert.DeserializeObject<FansBlock>(json, JsonSettings));

        // one bar group's import decision. The pre-5.6 schema revealed a group
        // by bare flag id; reveal is a Condition now (design doc section 12,
        // rules 8 and 9), and a stale `revealFlag` is JSON that used to mean
        // something, so it is refused loudly rather than silently ignored. The
        // group is skipped rather than imported gateless: a group whose gate
        // was dropped shows from the first frame, which is the one failure the
        // reveal registry exists to prevent.
        //
        // Tested on PRESENCE, never contents: `"revealFlag": ""` is a stale key
        // just as much as a filled-in one, and a contents test waves through
        // exactly the spelling least likely to be caught by eye. That is why the
        // DTO field carries no initializer - null is the only way to say absent.
        private static bool IsImportableBarGroup(BarGroupBlock block)
        {
            if (block.revealFlag == null)
                return true;

            Debug.LogError($"ChapterJsonImporter: bar group '{block.id}' carries a 'revealFlag' key - reveal is a Condition under 'visibleWhen' (design doc section 12, rules 8 and 9). Skipping it - fix the JSON and re-import.");
            return false;
        }

        // Converts a producer's production entries, or null when any entry is
        // invalid - a producer missing one of its yields is not the authored
        // producer, so the whole block is skipped rather than half-written.
        private static List<ProductionConfig> ToProductionConfigs(ProducerBlock block)
        {
            if (block.production.Length == 0)
            {
                Debug.LogError($"ChapterJsonImporter: producer '{block.id}' has no production entries - it would produce nothing. Skipping it - fix the JSON and re-import.");
                return null;
            }

            var configs = new List<ProductionConfig>();
            foreach (var entry in block.production)
            {
                var context = $"producer '{block.id}' production for '{entry.currency}'";
                if (string.IsNullOrEmpty(entry.currency))
                {
                    Debug.LogError($"ChapterJsonImporter: producer '{block.id}' has a production entry with no currency. Skipping the producer - fix the JSON and re-import.");
                    return null;
                }
                // production must never drain - never write that state
                if (entry.amount < 0)
                {
                    Debug.LogError($"ChapterJsonImporter: {context} has a negative amount ({entry.amount}). Skipping the producer - fix the JSON and re-import.");
                    return null;
                }

                var trigger = ToTrigger(entry.trigger, context);
                var composes = ToComposes(entry.composes, context);
                if (trigger == ProductionTrigger.None || composes == null)
                    return null;

                var gate = ToCondition(entry.gate, $"{context} (gate)");
                configs.Add(new ProductionConfig(entry.currency, entry.amount, trigger, gate, composes.Value));
            }
            return configs;
        }

        // the condition parse path (real DTO shape + conversion), exposed so
        // EditMode tests can cover nesting depth without an asset-writing import
        internal static Condition ParseCondition(string json, string context = "condition")
            => ToCondition(JsonConvert.DeserializeObject<ConditionBlock>(json, JsonSettings), context);

        // the condition pre-pass over one block, exposed for the same reason:
        // tests cover which spellings abort an import - and, just as importantly,
        // which do NOT - without writing a single asset
        internal static List<string> ParseConditionFaults(string json, string context = "condition")
        {
            var faults = new List<string>();
            CollectConditionFaults(JsonConvert.DeserializeObject<ConditionBlock>(json, JsonSettings), context, faults);
            return faults;
        }

        // the producer parse path, exposed like ParseCondition: tests cover
        // the trigger/composes/gate mapping and its refusals without an
        // asset-writing import
        internal static List<ProductionConfig> ParseProducerProduction(string json)
            => ToProductionConfigs(JsonConvert.DeserializeObject<ProducerBlock>(json, JsonSettings));

        // the currency-entry parse path: tests cover that the pre-5.4 earn
        // schema is refused rather than silently dropped
        internal static bool ParseCurrencyEntryIsImportable(string json)
            => IsImportableCurrencyEntry(JsonConvert.DeserializeObject<CurrencyEntryBlock>(json, JsonSettings));

        // the bar-group parse path, for the same reason: tests cover that the
        // pre-5.6 revealFlag schema is refused rather than silently ignored
        internal static bool ParseBarGroupIsImportable(string json)
            => IsImportableBarGroup(JsonConvert.DeserializeObject<BarGroupBlock>(json, JsonSettings));

        // the payload parse path, exposed for the same reason as ParseCondition:
        // tests cover which values the importer refuses to write without one
        internal static GameEffect ParsePayload(string json, string context)
            => ToPayload(JsonConvert.DeserializeObject<PayloadBlock>(json, JsonSettings), context);

        // the reward entry's parse path, exposed so tests can prove the two
        // authoring sites really are one vocabulary rather than two that happen to
        // overlap - the same effect name authored either way builds the same effect
        internal static GameEffect ParseRewardEffect(string json, string context)
            => ToRewardEffect(JsonConvert.DeserializeObject<RewardEntryBlock>(json, JsonSettings), context);

        private static Condition ToSimpleCondition(string type, string currency,
            string generator, string flag, string group, double value)
        {
            switch (type)
            {
                case "currency":
                    return new CurrencyBalanceCondition(currency, value);
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
                    // unreachable: the pre-pass refuses an unknown type before
                    // any asset is written, so reaching this means the two
                    // spellings of the type list disagree (IsKnownConditionType)
                    Debug.LogError($"ChapterJsonImporter: condition type '{type}' maps to no Condition subclass - the condition pre-pass should have aborted the import. Importing no gate.");
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

        // Trigger is a closed, code-defined set (ProductionTrigger); "tick"
        // and "tap" are the JSON spellings, and anything else is a content
        // error that skips the producer - a config that never fires is not
        // the authored config.
        private static ProductionTrigger ToTrigger(string trigger, string context)
        {
            switch (trigger)
            {
                case "tick":
                    return ProductionTrigger.Tick;
                case "tap":
                    return ProductionTrigger.Tap;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} has unknown trigger '{trigger}' - a production config fires on 'tick' or 'tap'. Skipping the producer - fix the JSON and re-import.");
                    return ProductionTrigger.None;
            }
        }

        // Which modifier target a config's output composes through. Any target
        // the family defines is authorable - the old tapValue-only restriction
        // was a fossil of TapValue being the only composing target that existed
        // in 5.4. Null signals an unknown spelling, which skips the producer.
        private static ModifierTarget? ToComposes(string composes, string context)
        {
            switch (composes)
            {
                case null:
                case "":
                    return ModifierTarget.None;
                case "tapValue":
                    return ModifierTarget.TapValue;
                case "fanRate":
                    return ModifierTarget.FanRate;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} has unknown composes '{composes}' - a config composes 'tapValue', 'fanRate' or nothing. Skipping the producer - fix the JSON and re-import.");
                    return null;
            }
        }

        // Every authored effect name, mapped in one place onto the GameEffect
        // family. The JSON keeps friendly names rather than spelling out a modifier
        // target and operation, because the importer is where authored vocabulary
        // meets classes - the same way fillMode + delivery maps onto a
        // BarFillBehavior.
        //
        // Both authoring sites feed this: an upgrade's `payload.effect` and a reward
        // entry's `type` are two JSON keys over ONE vocabulary. Neither restricts
        // which names it accepts, and that is deliberate - the old split
        // (multipliers for rewards, flat adds and per-generator targets for payloads)
        // was a fossil of the two class families rather than a rule. A reward paying
        // a flat tap bonus and a buff raising fan rate are both coherent content, so
        // the check worth keeping is whether the family knows the name at all.
        //
        // Returns null on refusal, having reported why; what a refusal MEANS belongs
        // to the caller - a reward entry is skipped, while an upgrade imports with no
        // payload and boot validation reports the gap.
        private static GameEffect ToEffect(string kind, double value, string flag, string generator,
            string[] affects, string context)
        {
            switch (kind)
            {
                case "setFlag":
                    return new SetFlagEffect(flag);
                case "tapValueAdd":
                    // a negative add is left to boot validation: unlike a multiplier
                    // it cannot poison a whole stack, so the asset is worth keeping
                    // around to be reported by name
                    return new GrantModifierEffect(ModifierTarget.TapValue, ModifierOperation.Add, value);
                case "tapValueMultiplier":
                    return ToMultiplier(ModifierTarget.TapValue, value, context, kind);
                case "fanRateMultiplier":
                    return ToMultiplier(ModifierTarget.FanRate, value, context, kind);
                case "generatorOutputMultiplier":
                    return ToMultiplier(ModifierTarget.GeneratorOutput, value, context, kind,
                        new List<string> { generator });
                case "currencyPerSecMultiplier":
                {
                    // an empty affects list could never apply, so the effect is never
                    // written and the content naming it reports instead
                    if (affects == null || affects.Length == 0)
                    {
                        Debug.LogError($"ChapterJsonImporter: {context} currencyPerSecMultiplier names no affected currencies - the multiplier could never apply. Refusing it - fix the JSON and re-import.");
                        return null;
                    }
                    return ToMultiplier(ModifierTarget.CurrencyProduction, value, context, kind,
                        new List<string>(affects));
                }
                case null:
                case "":
                    Debug.LogError($"ChapterJsonImporter: {context} names no effect.");
                    return null;
                default:
                    Debug.LogError($"ChapterJsonImporter: {context} names unknown effect '{kind}' - no GameEffect maps to it.");
                    return null;
            }
        }

        // a reward entry authors its effect name under `type`
        private static GameEffect ToRewardEffect(RewardEntryBlock block, string context)
            => ToEffect(block.type, block.value, block.flag, block.generator, block.affects, context);

        // never write a multiplier that would zero or negate the product it lands
        // in: the effect is refused here and the content naming it reports loudly,
        // rather than importing a value the registry refuses at runtime anyway
        private static GameEffect ToMultiplier(ModifierTarget target, double value, string context,
            string effectName, List<string> qualifiers = null)
        {
            if (value <= 0)
            {
                Debug.LogError($"ChapterJsonImporter: {context} has a non-positive {effectName} ({value}). Refusing it - fix the JSON and re-import.");
                return null;
            }

            return new GrantModifierEffect(target, ModifierOperation.Multiply, value, qualifiers);
        }

        // an upgrade authors its effect name under `payload.effect`; every upgrade
        // must grant something, so an absent payload is a content error the boot
        // validation pass reports against the upgrade
        private static GameEffect ToPayload(PayloadBlock block, string context)
            => ToEffect(block?.effect, block?.value ?? 0, block?.flag, block?.generator,
                block?.affects, context);

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

        // Maps the JSON (fillMode, delivery) pair onto the BarFillBehavior
        // subclass family. The pair must name an implemented behavior; anything
        // else imports no behavior, which boot validation reports - a mode can
        // never be authored without its handler.
        private static BarFillBehavior ToFillBehavior(string fillMode, string delivery, string context)
        {
            if (fillMode == "perBar" && delivery == "continuous")
                return new PerBarContinuousFill();

            Debug.LogError($"ChapterJsonImporter: {context} fillMode '{fillMode}' + delivery '{delivery}' maps to no BarFillBehavior subclass. Importing no fill behavior.");
            return null;
        }

        // "rehearsal" to "Rehearsal" - display names for currencies the JSON
        // declares by id only
        private static string ToDisplayName(string id)
            => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id.Substring(1);

        private static void EnsureFolders()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var folder in new[] { ChaptersFolder, SectionsFolder, CurrenciesFolder, ProducersFolder,
                GeneratorsFolder, UpgradesFolder, BarsFolder, BarGroupsFolder, EventsFolder, RewardsFolder })
                Directory.CreateDirectory(Path.Combine(projectRoot, folder));
            AssetDatabase.Refresh();
        }

        // Initializes an asset only when the result would differ from what is
        // already saved: the init runs on a scratch instance first and the two
        // serialized forms are compared. Unity assigns fresh managed-reference
        // ids (rid) to every new [SerializeReference] instance, so blindly
        // re-initializing rewrites every gate/payload holder with id churn even
        // when nothing changed - re-importing an unchanged JSON must leave the
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

        // like LoadOrCreate, but an asset written by an older schema - back when a
        // reward kind was its own RewardDefinition subclass - no longer loads as a
        // RewardDefinition at all, so the file is replaced rather than collided with
        private static RewardDefinition LoadOrCreateReward(string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<RewardDefinition>(assetPath);
            if (existing != null)
                return existing;

            AssetDatabase.DeleteAsset(assetPath);
            var asset = ScriptableObject.CreateInstance<RewardDefinition>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        // A currency asset is found by its ID, not by its filename. Every other
        // content kind was generated by this importer, so filename and id have
        // always matched by construction; the currencies predate it - Cash.asset
        // and Fans.asset were authored by hand before the roster existed, and
        // they carry a symbol, decimal count and starting value that no JSON
        // field expresses. Resolving on `{id}.asset` would have looked for
        // `cash.asset`, and on a case-sensitive filesystem it would have created
        // a SECOND asset with id 'cash': a duplicate-id error at load, and the
        // one that won would have had no `$` and no decimals.
        //
        // EditorInitialize writes only id, display name and group, so the
        // inspector-authored formatting fields survive an import untouched. That
        // is the division: the JSON owns identity and placement, the asset owns
        // presentation.
        private static CurrencyDefinition LoadOrCreateCurrency(string id)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(CurrencyDefinition)}", new[] { CurrenciesFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var existing = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>(path);
                if (existing != null && existing.Id == id)
                    return existing;
            }

            return LoadOrCreate<CurrencyDefinition>($"{CurrenciesFolder}/{id}.asset");
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

        // DTOs mirroring the JSON for Newtonsoft; unknown JSON fields (notes,
        // _meta, capstone, progression, balanceTargets) are simply skipped.
        // Field initializers stand in for absent fields (and explicit nulls,
        // via JsonSettings), so a block or array is never null and an absent
        // object is an empty instance.
#pragma warning disable 0649 // fields are assigned by Newtonsoft via reflection
        private class ChapterFile
        {
            public ChapterBlock chapter = new();
            public ConstantsBlock constants = new();
            public FlagBlock[] flags = Array.Empty<FlagBlock>();
            public SectionBlock[] sections = Array.Empty<SectionBlock>();
            public GeneratorBlock[] generators = Array.Empty<GeneratorBlock>();
            public UpgradeBlock[] upgrades = Array.Empty<UpgradeBlock>();
            public RewardEntryBlock[] rewards = Array.Empty<RewardEntryBlock>();
            public CurrencyEntryBlock[] currencies = Array.Empty<CurrencyEntryBlock>();
            public ProducerBlock[] producers = Array.Empty<ProducerBlock>();
            public BarsBlock bars = new();
            public FansBlock fans = new();
            public EventBlock[] events = Array.Empty<EventBlock>();
        }

        private class FlagBlock
        {
            public string id = "";
        }

        // one entry in the shared reward pool; which fields matter depends on type
        private class RewardEntryBlock
        {
            public string id = "";
            public string name = "";
            public string type = "";
            public double value;
            public string flag = "";

            // a reward authors from the same effect vocabulary an upgrade payload
            // does, so it carries the same parameter fields; which ones matter
            // depends on the effect named by `type`
            public string generator = "";
            public string[] affects = Array.Empty<string>();
        }

        private class ChapterBlock
        {
            public string id = "";
            public int index;
            public string name = "";
            public string theme = "";
            public string storyBeatOpen = "";
            public string storyBeatCapstone = "";
            public int capstoneRecordsGate;
        }

        private class ConstantsBlock
        {
            public RecordBuffBlock recordBuff = new();

            // detection only: the pre-5.4 schema's Jam yield, refused when
            // present (it lives on the jam producer's cash config now)
            public JToken tapBaseValue;
        }

        // a multiplier declares the currencies it affects (plural); production
        // of anything it doesn't name is untouched
        private class RecordBuffBlock
        {
            public double perRecord;
            public string[] affects = Array.Empty<string>();
        }

        private class SectionBlock
        {
            public string id = "";
            public string name = "";
            public string[] modules = Array.Empty<string>();
            public ConditionBlock visibleWhen = new();
        }

        // the discriminated Condition shape; which fields matter depends on
        // type. all/any children are this same shape, so compounds nest to
        // any depth - matching the recursive CompoundCondition family.
        private class ConditionBlock
        {
            // Every reference-type field is left WITHOUT an initializer on
            // purpose, the same rule FansBlock's retired keys follow: null means
            // "the key is absent", so an authored-empty value stays
            // distinguishable from omission. IsAuthored rests on exactly this -
            // `{}` is a legitimate absent gate, while `{"type": ""}`,
            // `{"flag": ""}` and `{"all": []}` are gates someone wrote and got
            // wrong, and importing those as "no gate" is the silent ungating the
            // preflight exists to stop.
            public string type;
            public string currency;
            public string generator;
            public string flag;
            public string group;

            // The one field that stays a plain double, and so the one authored
            // spelling the preflight cannot see: a bare `{"value": 0}` is
            // identical to omission after deserialization. Left as a known
            // exception rather than made nullable, which would spread `?? 0`
            // through the conversion for a block that names no type, no currency
            // and no threshold - it says nothing on its own. Any other key
            // alongside it IS caught, and once a type is present a zero value is
            // reported by ValidateThreshold and fails closed at evaluation.
            public double value;

            // Any key that matches no field above lands here instead of being
            // dropped. A condition object carries nothing but its own keys (notes
            // sit on the content around it), so anything collected here is a
            // misspelling - `amount` copied from the cost block beside it, say -
            // and a misspelled threshold would otherwise import as zero: a gate
            // met before play starts.
            [JsonExtensionData]
            public IDictionary<string, JToken> unrecognized;
            public ConditionBlock[] all;
            public ConditionBlock[] any;
        }

        private class GeneratorBlock
        {
            public string id = "";
            public string name = "";
            public string produces = "";
            public bool isBandmate;
            public GeneratorCostBlock cost = new();
            public double baseOutput;
            public ConditionBlock unlock = new();
        }

        // a generator's cost declares its currency, independent of `produces`
        private class GeneratorCostBlock
        {
            public string currency = "";
            public double amount;
            public double growth;
        }

        private class CostBlock
        {
            public string currency = "";
            public double amount;
        }

        private class PayloadBlock
        {
            public string effect = "";
            public double value;
            public string generator = "";
            public string flag = "";
            public string[] affects = Array.Empty<string>();
        }

        private class UpgradeBlock
        {
            public string id = "";
            public string name = "";
            public string type = "";
            public string scope = "";
            public CostBlock cost = new();
            public ConditionBlock gate = new();
            public PayloadBlock payload = new();
        }

        // one chapter-declared currency: pure state ({id, group})
        private class CurrencyEntryBlock
        {
            public string id = "";
            public string group = ""; // CurrencyGroupDefinition id, e.g. "run"

            // detection only: the pre-5.4 schema's engagement earn, refused
            // when present (production lives on producers now)
            public JToken earn;
        }

        // a module-held production source: the module prefab presenting it
        // plus the production configs it fires (design doc section 12, rule 13)
        private class ProducerBlock
        {
            public string id = "";
            public string module = "";
            public ProductionEntryBlock[] production = Array.Empty<ProductionEntryBlock>();
        }

        // one flat-rate source: amount is per second for tick, per tap for
        // tap; the gate is the discriminated Condition shape like every other
        // rule, and composes names the modifier target that scales the output
        private class ProductionEntryBlock
        {
            public string currency = "";
            public double amount;
            public string trigger = "";
            public string composes = "";
            public ConditionBlock gate = new();
        }

        private class BarsBlock
        {
            public BarGroupBlock[] groups = Array.Empty<BarGroupBlock>();
            public string scope = "";
        }

        private class BarGroupBlock
        {
            public string id = "";
            public string name = "";
            public ConditionBlock visibleWhen = new();

            // pre-5.6 schema: reveal was a bare flag id. Kept as a field ONLY so
            // its presence can be refused - see IsImportableBarGroup. Left
            // WITHOUT an initializer on purpose, the same as FansBlock's retired
            // keys: null means "the key is absent", so any authored value is
            // detectable including the one that reads as empty ("").
            public string revealFlag;
            public string fillMode = "";
            public string delivery = "";
            public BarBlock[] bars = Array.Empty<BarBlock>();
        }

        private class BarBlock
        {
            public string id = "";
            public string name = "";
            public string fillCurrency = "";
            public double fillRequirement;
            public string reward = ""; // reward pool id
        }

        private class FansBlock
        {
            public string currency = "";
            public double perBandmateOwnedBonus;

            // pre-5.7 schema, kept only to be refused - see ReportStaleFansKeys.
            // All three are production now: the base rate and its gate live on a
            // producer's config. Every one of them is left WITHOUT an initializer
            // on purpose, so null means "the key is absent" and any authored value
            // is detectable - including the ones that read as empty: 0, {} and "".
            // A refusal that tests the contents rather than the presence lets the
            // emptiest spelling of a stale key through silently, which is the one
            // case a fail-closed rule exists for.
            public double? baseFansPerSec;
            public ConditionBlock activeWhen;
            public string revealFlag;
        }

        private class EventBlock
        {
            public string id = "";
            public string name = "";
            public ConditionBlock availableWhen = new();
            public bool baselineReset;
            public TierBlock[] tiers = Array.Empty<TierBlock>();
        }

        private class TierBlock
        {
            public int tier;
            public DebuffBlock debuff = new();
            public ConditionBlock goal = new();
            public double timerSeconds;
            public bool failable;
            public string reward = ""; // reward pool id
            public string scope = ""; // how long a cleared tier stays cleared
        }

        private class DebuffBlock
        {
            public string effect = "";
        }
#pragma warning restore 0649
    }
}
