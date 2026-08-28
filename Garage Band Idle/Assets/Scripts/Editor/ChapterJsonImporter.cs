using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;

namespace RidiculousGaming.GarageBandIdle.Editor
{
    // The authoring importer (design doc 12.14.5): JSON documents in,
    // ScriptableObject assets out, wired into the scope tree by direct
    // reference because a declaration IS a reference and there are no labels to
    // assign beyond the roster's one.
    //
    // The contract is preflight the UNION, write the union, never half. Every
    // command parses, resolves, and composes EVERY document into transient
    // instances and validates that one assembly; only a clean assembly earns
    // the persistent writes. All-transient is load-bearing: mixing a transient
    // document with persisted neighbours would put two generations of one
    // declaration in one tree, and identity-based ownership rightly refuses to
    // call those the same asset.
    public static class ChapterJsonImporter
    {
        public const string ContentPath = "Assets/Content";
        public const string AssetRootPath = "Assets/ScriptableObjects";
        public const string GroupName = "Content";

        // Ids become path segments, so the grammar is what keeps separators,
        // "..", and case-games out of the filesystem.
        private static readonly Regex IdGrammar = new("^[a-z0-9_]+$", RegexOptions.Compiled);

        // The grammar alone does not keep RESERVED names out: Windows refuses
        // these as filenames even with an extension, so `aux.asset` passes every
        // check and then fails at CreateAsset, mid-write, after earlier assets
        // were already mutated.
        private static readonly HashSet<string> ReservedSegments = new()
        {
            "con", "prn", "aux", "nul",
            "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
        };

        internal class Options
        {
            public string ContentDirectory = ContentPath;
            public string AssetRoot = AssetRootPath;
            public string GroupName = ChapterJsonImporter.GroupName;
        }

        [MenuItem("Garage Band Idle/Import Content")]
        public static void ImportMenuItem()
        {
            try
            {
                ImportAll();
                Debug.Log("Content import: done.");
            }
            catch (ContentImportException e) when (!e.AssetsMutated)
            {
                Debug.LogError($"Content import FAILED in preflight - assets left untouched. {e.Message}");
            }
            catch (Exception e)
            {
                // Everything past the first write: the addressables pass, the
                // orphan report, the post-write validation. Assets HAVE changed
                // by then, and saying otherwise hides a half-written tree.
                Debug.LogError($"Content import FAILED after writing began - the assets on disk may be partially updated. {e.Message}");
            }
        }

        // The -executeMethod entry point. It THROWS on any preflight failure and
        // on a post-write report with ERRORS, so the batchmode process exits
        // nonzero: an import that aborted by logging would leave yesterday's
        // assets testing green.
        public static void ImportAll() => Import(new Options());

        internal static void Import(Options options)
        {
            var documents = ParseAll(options.ContentDirectory);
            if (documents.Count == 0)
                throw new ContentImportException($"no JSON documents under '{options.ContentDirectory}'.");

            // ---- preflight: the whole union, all transient ----
            // The build is created HERE and filled in place: an abort inside
            // BuildUnion - unknown kind, duplicate id, unresolved reference -
            // still leaves every instance it made reachable by the cleanup.
            var transient = new Build();
            try
            {
                BuildUnion(transient, documents, options, (type, _) => (Definition)ScriptableObject.CreateInstance(type));
                LintPaths(transient, options);
                var preflight = ContentValidator.Validate(transient.Content);
                preflight.LogAll();
                if (preflight.HasErrors)
                    throw new ContentImportException(
                        "content validation failed on the assembled documents - nothing was written (12.12).");
            }
            finally
            {
                // Native objects: without this, repeated imports and the
                // importer tests accumulate them until a domain reload.
                foreach (var definition in transient.Created)
                    UnityEngine.Object.DestroyImmediate(definition);
            }

            // ---- the writes ----
            // The first write is a PHASE boundary, not a property of any one
            // throw: everything Write does can leave assets changed, so every
            // failure out of it is re-raised as mutating rather than each site
            // remembering to say so. A caller reporting "nothing was written"
            // has to be right about that, and a step added inside Write
            // inherits the answer.
            try
            {
                Write(documents, options);
            }
            catch (Exception e)
            {
                if (e is ContentImportException marked && marked.AssetsMutated)
                    throw;
                throw new ContentImportException(e.Message, true, e);
            }
        }

        // Everything that can change what is on disk, in one place: this
        // method's body IS the mutating phase the caller's catch marks.
        private static void Write(List<Document> documents, Options options)
        {
            // The same build as the preflight, against persisted assets.
            var written = new Build();
            BuildUnion(written, documents, options, (type, path) => LoadOrCreate(type, path));
            // Every asset was mutated in place, and Unity serializes only what
            // is dirty - without this the wiring lives in memory until the
            // domain reloads over it, and the files on disk stay empty.
            foreach (var definition in written.Created)
                EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();

            AssignAddressables(written, options);
            ReportOrphans(written, options);

            // Re-read every file, so the pass below judges what is ON DISK
            // rather than the instances just wired in memory - LoadAssetAtPath
            // hands back the live object either way, and a write that never
            // reached the file would otherwise validate clean.
            foreach (var path in written.Paths.Values)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // What boot will load, composed through the same seam. The preflight
            // already gated the writes, so this disagreeing means the writer is
            // broken - exactly worth failing on.
            var loaded = LoadPersisted(options);
            var report = ContentValidator.Validate(loaded);
            report.LogAll();
            if (report.HasErrors)
                throw new ContentImportException(
                    "the WRITTEN content fails validation - the writer disagreed with the preflight (12.14.5).", true);
        }

        // ---- parsing ----

        private class Document
        {
            public string Path;
            public ScopeDto Dto;
            public string Id => Dto.id;
        }

        private static JsonSerializerSettings Settings() => new()
        {
            // The typo guard the old importer hand-rolled: `amount` where a
            // condition wants `value`, and every misspelling, is an abort.
            MissingMemberHandling = MissingMemberHandling.Error,
            // One source of "absent": an explicit null behaves as omitted.
            NullValueHandling = NullValueHandling.Ignore,
            Converters =
            {
                new KindConverter<ConditionDto>(KindRegistry.Conditions, "condition"),
                new KindConverter<ActionDto>(KindRegistry.Actions, "action"),
                new KindConverter<PayoutFormulaDto>(KindRegistry.PayoutFormulas, "payout formula"),
                new KindConverter<MultiplierFormulaDto>(KindRegistry.MultiplierFormulas, "multiplier formula"),
                new BigNumberConverter(),
                new StrictEnumConverter(),
            },
        };

        private static List<Document> ParseAll(string directory)
        {
            var documents = new List<Document>();
            if (!Directory.Exists(directory))
                return documents;
            foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                ScopeDto dto;
                try
                {
                    dto = JsonConvert.DeserializeObject<ScopeDto>(File.ReadAllText(path), Settings());
                }
                catch (ContentImportException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new ContentImportException($"{Path.GetFileName(path)}: {e.Message}");
                }
                if (dto == null)
                    throw new ContentImportException($"{Path.GetFileName(path)}: empty document.");
                documents.Add(new Document { Path = path, Dto = dto });
            }
            return documents;
        }

        // ---- the build ----

        // How a definition instance is obtained. The preflight makes transient
        // ones; the write pass loads or creates the asset at its stable path,
        // so GUIDs survive and every direct reference stays intact.
        private delegate Definition Materialize(Type type, string assetPath);

        private class Build
        {
            public ComposedContent Content;
            public readonly List<Definition> Created = new();
            public readonly Dictionary<Definition, string> Paths = new();
            public readonly Dictionary<ScopeDefinition, ScopeDefinition> Parents = new();
            public readonly Dictionary<ScopeDefinition, Dictionary<string, Definition>> Declared = new();
            public readonly Dictionary<string, ScopeDefinition> ScopesById = new();
            public readonly List<ChapterDefinition> Chapters = new();
            // The instance each DTO produced, so the wiring pass finds it
            // without threading a second map through every call.
            public readonly Dictionary<object, Definition> Built = new();
            public RootDefinition Root;
        }

        private static void BuildUnion(Build build, List<Document> documents, Options options, Materialize materialize)
        {
            // Pass one: materialize every scope and every declaration, and index
            // what each scope declares. The index is the outward walk's input -
            // per scope, never a catalogue.
            foreach (var document in documents)
                MaterializeScope(build, document.Dto, null, document, options, materialize);

            if (build.Root == null)
                throw new ContentImportException("no document declares a RootDefinition - the root document is required.");
            build.Content = ComposedContent.Compose(build.Root, build.Chapters);

            // Pass two: wire every list and reference, now that everything the
            // references can name exists.
            foreach (var document in documents)
                WireScope(build, document.Dto);

            LintChainIds(build);
        }

        private static void MaterializeScope(Build build, ScopeDto dto, ScopeDefinition parent,
                                             Document document, Options options, Materialize materialize)
        {
            if (!KindRegistry.Scopes.TryGetValue(dto.type ?? string.Empty, out var scopeType))
                throw new ContentImportException(
                    $"{Path.GetFileName(document.Path)}: '{dto.type}' is not a scope kind ({string.Join(", ", KindRegistry.Scopes.Keys)}).");
            RequireId(dto.id, "scope");

            var isRoot = scopeType == typeof(RootDefinition);
            if (isRoot && parent != null)
                throw new ContentImportException($"scope '{dto.id}' is a root declared as a child (12.3).");
            // A document is the root document or one chapter's (12.14.5), so a
            // tier at the top has no chain to resolve outward along.
            if (parent == null && !isRoot && scopeType != typeof(ChapterDefinition))
                throw new ContentImportException(
                    $"document '{Path.GetFileName(document.Path)}' declares a {dto.type} at the top; a document is the root or a chapter (12.14.5).");

            var scope = (ScopeDefinition)materialize(scopeType, ScopeAssetPath(options, document.Id, dto.id));
            Init(build, scope, dto.id, dto.tags, ScopeAssetPath(options, document.Id, dto.id));
            if (build.ScopesById.ContainsKey(dto.id))
                throw new ContentImportException($"two scopes share the id '{dto.id}'; scope ids are tree-wide unique (12.3).");
            build.ScopesById[dto.id] = scope;
            build.Declared[scope] = new Dictionary<string, Definition>();
            if (parent != null)
                build.Parents[scope] = parent;

            if (isRoot)
            {
                if (build.Root != null)
                    throw new ContentImportException("two documents declare a root scope; there is exactly one (12.3).");
                build.Root = (RootDefinition)scope;
            }
            else if (parent == null)
            {
                build.Chapters.Add((ChapterDefinition)scope);
            }

            // Root's serialized child list stays EMPTY by contract (12.14.5) -
            // the chapter DOCUMENTS are the roster. Caught here rather than at
            // Compose, which runs before the wiring that would populate the list
            // and would therefore see an empty one and pass.
            if (isRoot && dto.children.Count > 0)
                throw new ContentImportException(
                    $"the root document authors {dto.children.Count} children; a chapter is its own document, and the label is the roster (12.14.5).");

            if (scope is not InteriorDefinition)
            {
                if (dto.rung != null)
                    throw new ContentImportException($"scope '{dto.id}' authors a rung; only an interior scope has one (12.3).");
                if (dto.events.Count > 0)
                    throw new ContentImportException($"scope '{dto.id}' authors events; root cannot host one (12.3, 12.8).");
            }

            foreach (var currency in dto.currencies)
                Declare<CurrencyDefinition>(build, scope, currency, document, options, materialize);
            foreach (var producer in dto.producers)
                Declare<ProducerDefinition>(build, scope, producer, document, options, materialize);
            foreach (var generator in dto.generators)
                Declare<GeneratorDefinition>(build, scope, generator, document, options, materialize);
            foreach (var upgrade in dto.upgrades)
                Declare<UpgradeDefinition>(build, scope, upgrade, document, options, materialize);
            foreach (var modifier in dto.modifiers)
                Declare<ModifierDefinition>(build, scope, modifier, document, options, materialize);
            foreach (var trigger in dto.triggers)
                Declare<TriggerDefinition>(build, scope, trigger, document, options, materialize);
            foreach (var evt in dto.events)
                Declare<EventDefinition>(build, scope, evt, document, options, materialize);
            foreach (var group in dto.barGroups)
            {
                Declare<BarGroupDefinition>(build, scope, group, document, options, materialize);
                foreach (var bar in group.bars)
                    Declare<BarDefinition>(build, scope, bar, document, options, materialize);
            }

            foreach (var child in dto.children)
                MaterializeScope(build, child, scope, document, options, materialize);
        }

        private static void Declare<T>(Build build, ScopeDefinition scope, DefinitionDto dto,
                                       Document document, Options options, Materialize materialize)
            where T : Definition
        {
            RequireId(dto.id, typeof(T).Name);
            var path = AssetPath(options, document.Id, FamilyOf(typeof(T)), dto.id);
            var definition = (T)materialize(typeof(T), path);
            Init(build, definition, dto.id, dto.tags, path);
            var declared = build.Declared[scope];
            if (declared.ContainsKey(dto.id))
                throw new ContentImportException($"scope '{scope.Id}' declares '{dto.id}' twice.");
            declared[dto.id] = definition;
            build.Built[dto] = definition;
        }

        private static void Init(Build build, Definition definition, string id, List<string> tags, string path)
        {
            definition.EditorInit(id, tags == null ? Array.Empty<string>() : tags.ToArray());
            build.Created.Add(definition);
            build.Paths[definition] = path;
        }

        private static void RequireId(string id, string what)
        {
            if (string.IsNullOrEmpty(id) || !IdGrammar.IsMatch(id))
                throw new ContentImportException(
                    $"{what} id '{id}' is outside the id grammar [a-z0-9_]+ - ids become path segments (12.14.5).");
            if (ReservedSegments.Contains(id))
                throw new ContentImportException(
                    $"{what} id '{id}' is a reserved device name on Windows, which no file may carry even with an extension.");
        }

        // ---- wiring ----

        private static void WireScope(Build build, ScopeDto dto)
        {
            var scope = build.ScopesById[dto.id];
            scope.children.Clear();
            scope.declaredCurrencies.Clear();
            scope.declaredFlags.Clear();
            scope.declaredTags.Clear();
            scope.producers.Clear();
            scope.generators.Clear();
            scope.upgrades.Clear();
            scope.modifiers.Clear();
            scope.permanentModifiers.Clear();
            scope.barGroups.Clear();
            scope.triggers.Clear();

            scope.declaredFlags.AddRange(dto.flags);
            scope.declaredTags.AddRange(dto.declaredTags);

            foreach (var currency in dto.currencies)
                scope.declaredCurrencies.Add((CurrencyDefinition)build.Built[currency]);

            foreach (var producerDto in dto.producers)
            {
                var producer = (ProducerDefinition)build.Built[producerDto];
                producer.produces = producerDto.produces.Select(e => Entry(build, scope, e)).ToList();
                scope.producers.Add(producer);
            }

            foreach (var generatorDto in dto.generators)
            {
                var generator = (GeneratorDefinition)build.Built[generatorDto];
                generator.availableWhen = BuildCondition(build, scope, generatorDto.availableWhen);
                generator.costCurrency = Resolve<CurrencyDefinition>(build, scope, generatorDto.costCurrency, "costCurrency");
                generator.baseCost = generatorDto.baseCost;
                generator.growth = generatorDto.growth;
                generator.produces = generatorDto.produces.Select(e => Entry(build, scope, e)).ToList();
                if (generator.costCurrency == null || generator.baseCost <= BigNumber.Zero
                    || generator.growth <= BigNumber.Zero)
                    throw new ContentImportException(
                        $"generator '{generator.Id}': a cost block needs a currency, a positive baseCost, and a positive growth.");
                scope.generators.Add(generator);
            }

            foreach (var upgradeDto in dto.upgrades)
            {
                var upgrade = (UpgradeDefinition)build.Built[upgradeDto];
                upgrade.gate = BuildCondition(build, scope, upgradeDto.gate);
                upgrade.costCurrency = Resolve<CurrencyDefinition>(build, scope, upgradeDto.costCurrency, "costCurrency");
                upgrade.cost = upgradeDto.cost;
                // The currency is ALWAYS required - Purchasing dereferences it -
                // and only the amount may be zero: cut_demo authors {cash, 0}.
                if (upgrade.costCurrency == null || upgrade.cost < BigNumber.Zero)
                    throw new ContentImportException(
                        $"upgrade '{upgrade.Id}': a cost block needs a currency and a nonnegative amount.");
                upgrade.effects = upgradeDto.effects.Select(e => BuildEffect(build, scope, e)).ToList();
                upgrade.actions = upgradeDto.actions.Select(a => BuildAction(build, scope, a)).ToList();
                scope.upgrades.Add(upgrade);
            }

            foreach (var modifierDto in dto.modifiers)
            {
                var modifier = (ModifierDefinition)build.Built[modifierDto];
                modifier.stacking = modifierDto.stacking;
                modifier.effects = modifierDto.effects.Select(e => BuildEffect(build, scope, e)).ToList();
                modifier.appliesWhen = BuildCondition(build, scope, modifierDto.appliesWhen);
                scope.modifiers.Add(modifier);
            }

            // Usage, not declaration: an entry resolves like any other reference,
            // outward from the scope that uses it.
            foreach (var id in dto.permanentModifiers)
                scope.permanentModifiers.Add(Resolve<ModifierDefinition>(build, scope, id, "permanentModifiers"));

            foreach (var groupDto in dto.barGroups)
            {
                var group = (BarGroupDefinition)build.Built[groupDto];
                group.maxActive = groupDto.maxActive;
                group.bars = new List<BarDefinition>();
                foreach (var barDto in groupDto.bars)
                {
                    var bar = (BarDefinition)build.Built[barDto];
                    bar.fillCurrency = string.IsNullOrEmpty(barDto.fillCurrency)
                        ? null
                        : Resolve<CurrencyDefinition>(build, scope, barDto.fillCurrency, "fillCurrency");
                    bar.fillAmount = barDto.fillAmount;
                    bar.fillRate = barDto.fillRate;
                    bar.repeating = barDto.repeating;
                    bar.availableWhen = BuildCondition(build, scope, barDto.availableWhen);
                    bar.onComplete = barDto.onComplete.Select(a => BuildAction(build, scope, a)).ToList();
                    bar.perFill = barDto.perFill
                        .Select(p => new PerFillEntry { effect = BuildEffect(build, scope, p.effect), growth = p.growth })
                        .ToList();
                    group.bars.Add(bar);
                }
                scope.barGroups.Add(group);
            }

            foreach (var triggerDto in dto.triggers)
            {
                var trigger = (TriggerDefinition)build.Built[triggerDto];
                trigger.condition = BuildCondition(build, scope, triggerDto.condition);
                trigger.actions = triggerDto.actions.Select(a => BuildAction(build, scope, a)).ToList();
                scope.triggers.Add(trigger);
            }

            if (scope is InteriorDefinition interior)
            {
                interior.events.Clear();
                foreach (var eventDto in dto.events)
                {
                    var evt = (EventDefinition)build.Built[eventDto];
                    evt.availableWhen = BuildCondition(build, scope, eventDto.availableWhen);
                    evt.goal = BuildCondition(build, scope, eventDto.goal);
                    evt.timeLimitSeconds = eventDto.timeLimitSeconds;
                    evt.handicaps = eventDto.handicaps.Select(e => BuildEffect(build, scope, e)).ToList();
                    evt.onEntry = eventDto.onEntry.Select(a => BuildAction(build, scope, a)).ToList();
                    evt.rewards = eventDto.rewards.Select(a => BuildAction(build, scope, a)).ToList();
                    evt.onEnd = eventDto.onEnd.Select(a => BuildAction(build, scope, a)).ToList();
                    interior.events.Add(evt);
                }
                interior.rung = dto.rung == null
                    ? null
                    : new Rung
                    {
                        offerCondition = BuildCondition(build, scope, dto.rung.offerCondition),
                        actions = dto.rung.actions.Select(a => BuildAction(build, scope, a)).ToList(),
                    };
            }

            foreach (var child in dto.children)
            {
                scope.children.Add(build.ScopesById[child.id]);
                WireScope(build, child);
            }
        }

        private static ProducesEntry Entry(Build build, ScopeDefinition scope, ProducesDto dto) => new()
        {
            currency = Resolve<CurrencyDefinition>(build, scope, dto.currency, "produces entry")
                       ?? throw new ContentImportException($"a produces entry at '{scope.Id}' names no currency."),
            stat = dto.stat,
            value = dto.value,
            condition = BuildCondition(build, scope, dto.condition),
        };

        private static Effect BuildEffect(Build build, ScopeDefinition scope, EffectDto dto)
        {
            if (dto == null)
                throw new ContentImportException($"a null effect at '{scope.Id}'.");
            return new Effect
            {
                // Selectors stay strings: target and currencyId are id-or-tag
                // MATCH strings the gather evaluates, never references (12.2).
                target = dto.target,
                currencyId = dto.currencyId,
                stat = dto.stat,
                multiplier = dto.multiplier,
                formula = BuildMultiplierFormula(build, scope, dto.formula),
            };
        }

        // ---- the polymorphic kinds ----

        private static Condition BuildCondition(Build build, ScopeDefinition scope, ConditionDto dto)
        {
            if (dto == null)
                return null;
            Condition condition = dto switch
            {
                CurrencyAtLeastDto d => new CurrencyAtLeast
                    { currency = Resolve<CurrencyDefinition>(build, scope, d.currency, "CurrencyAtLeast"), threshold = d.threshold },
                EarnedTotalAtLeastDto d => new EarnedTotalAtLeast
                    { currency = Resolve<CurrencyDefinition>(build, scope, d.currency, "EarnedTotalAtLeast"), threshold = d.threshold },
                OwnedCountAtLeastDto d => new OwnedCountAtLeast
                    { generator = Resolve<GeneratorDefinition>(build, scope, d.generator, "OwnedCountAtLeast"), count = d.count },
                FlagSetDto d => new FlagSet { flagId = d.flagId },
                UpgradePurchasedDto d => new UpgradePurchased
                    { upgrade = Resolve<UpgradeDefinition>(build, scope, d.upgrade, "UpgradePurchased") },
                BarsCompletedDto d => new BarsCompleted
                    { group = Resolve<BarGroupDefinition>(build, scope, d.group, "BarsCompleted"), count = d.count },
                EventRecordExistsDto d => new EventRecordExists { host = ResolveScope(build, d.host, "EventRecordExists") },
                EventRewardPendingDto d => new EventRewardPending { host = ResolveScope(build, d.host, "EventRewardPending") },
                AlwaysDto => new Always(),
                IdleAccumulationDto => new IdleAccumulation(),
                AllDto d => new All { conditions = d.conditions.Select(c => BuildCondition(build, scope, c)).ToList() },
                AnyDto d => new Any { conditions = d.conditions.Select(c => BuildCondition(build, scope, c)).ToList() },
                NotDto d => new Not { condition = BuildCondition(build, scope, d.condition) },
                _ => throw new ContentImportException($"condition kind '{dto.type}' has no builder."),
            };
            condition.uiText = dto.uiText;
            return condition;
        }

        private static GameAction BuildAction(Build build, ScopeDefinition scope, ActionDto dto) => dto switch
        {
            null => throw new ContentImportException($"a null action at '{scope.Id}'."),
            AddCurrencyDto d => new AddCurrency
            {
                currencies = d.currencies.Select(id => Resolve<CurrencyDefinition>(build, scope, id, "AddCurrency")).ToList(),
                amount = d.amount,
                formula = BuildPayoutFormula(build, scope, d.formula),
            },
            SetFlagDto d => new SetFlag { flagId = d.flagId },
            AddModifierDto d => new AddModifier
            {
                scope = ResolveScope(build, d.scope, "AddModifier"),
                modifier = Resolve<ModifierDefinition>(build, scope, d.modifier, "AddModifier"),
            },
            RemoveModifierDto d => new RemoveModifier
            {
                scope = ResolveScope(build, d.scope, "RemoveModifier"),
                modifier = Resolve<ModifierDefinition>(build, scope, d.modifier, "RemoveModifier"),
            },
            ResetScopeDto d => new ResetScope { scope = ResolveScope(build, d.scope, "ResetScope") },
            ExecuteRungDto d => new ExecuteRung { tier = ResolveScope(build, d.tier, "ExecuteRung") as InteriorDefinition
                ?? throw new ContentImportException($"ExecuteRung names '{d.tier}', which has no rung field - only an interior scope does (12.3).") },
            RestartScopeDto d => new RestartScope { scope = ResolveScope(build, d.scope, "RestartScope") },
            _ => throw new ContentImportException($"action kind '{dto.type}' has no builder."),
        };

        private static PayoutFormula BuildPayoutFormula(Build build, ScopeDefinition scope, PayoutFormulaDto dto) => dto switch
        {
            null => null,
            ConstantFormulaDto d => new ConstantFormula { value = d.value },
            RootCurveFormulaDto d => new RootCurveFormula
            {
                currency = Resolve<CurrencyDefinition>(build, scope, d.currency, "RootCurveFormula"),
                divisor = d.divisor,
                exponent = d.exponent,
            },
            _ => throw new ContentImportException($"payout formula kind '{dto.type}' has no builder."),
        };

        private static MultiplierFormula BuildMultiplierFormula(Build build, ScopeDefinition scope, MultiplierFormulaDto dto) => dto switch
        {
            null => null,
            LinearOnBalanceDto d => new LinearOnBalance
                { currency = Resolve<CurrencyDefinition>(build, scope, d.currency, "LinearOnBalance"), coefficient = d.coefficient },
            RoadieTotalBoostDto d => new RoadieTotalBoost { perRoadie = d.perRoadie },
            RoadieActiveBoostDto d => new RoadieActiveBoost { perRoadie = d.perRoadie },
            _ => throw new ContentImportException($"multiplier formula kind '{dto.type}' has no builder."),
        };

        // ---- reference resolution ----

        // An ordinary reference resolves by walking the authored tree OUTWARD
        // from the scope it sits on - the document's own chain, then the union's
        // candidate root chain. Import-time reach therefore equals runtime
        // reach, and sibling scopes reusing an id can never cross-wire.
        private static T Resolve<T>(Build build, ScopeDefinition from, string id, string use) where T : Definition
        {
            if (string.IsNullOrEmpty(id))
                return null;
            for (var scope = from; scope != null; scope = Parent(build, scope))
                if (build.Declared[scope].TryGetValue(id, out var found))
                    return found as T
                           ?? throw new ContentImportException(
                               $"{use} at '{from.Id}' names '{id}', which is a {found.GetType().Name} and not a {typeof(T).Name}.");
            throw new ContentImportException(
                $"{use} at '{from.Id}' names '{id}', which nothing on its chain declares (12.14.5).");
        }

        // SCOPE references resolve tree-wide: scope ids are tree-wide unique and
        // the runtime reads them downward (FindInSubtree), so the capstone's
        // ExecuteRung(tier1) points at a child. Whether the named scope is legal
        // from that site is each class's own 12.12 reach check, not this.
        private static ScopeDefinition ResolveScope(Build build, string id, string use)
        {
            if (string.IsNullOrEmpty(id))
                throw new ContentImportException($"{use} names no scope.");
            if (build.ScopesById.TryGetValue(id, out var scope))
                return scope;
            throw new ContentImportException($"{use} names scope '{id}', which no document declares.");
        }

        private static ScopeDefinition Parent(Build build, ScopeDefinition scope)
        {
            if (build.Parents.TryGetValue(scope, out var parent))
                return parent;
            // A chapter's parent is the candidate root: the union is composed,
            // so the chain out of a document continues into root.json.
            return scope is ChapterDefinition ? build.Root : null;
        }

        // ---- lints over the assembled union ----

        // The refusal is per CHAIN, exactly the uniqueness the runtime owns
        // (12.12) - sibling scopes reusing an id is legal authoring.
        private static void LintChainIds(Build build)
        {
            void Walk(ScopeDefinition scope, Dictionary<string, string> visible)
            {
                var here = new Dictionary<string, string>(visible);
                foreach (var pair in build.Declared[scope])
                {
                    if (here.TryGetValue(pair.Key, out var existing))
                        throw new ContentImportException(
                            $"'{pair.Key}' is declared twice on the chain at '{scope.Id}': {existing} and '{scope.Id}'.");
                    here[pair.Key] = $"'{scope.Id}'";
                }
                foreach (var child in ChildrenOf(build, scope))
                    Walk(child, here);
            }
            Walk(build.Root, new Dictionary<string, string>());
        }

        private static IEnumerable<ScopeDefinition> ChildrenOf(Build build, ScopeDefinition scope) =>
            scope == build.Root ? build.Content.Chapters : (IEnumerable<ScopeDefinition>)scope.children;

        // Every target path lands inside the managed directory, and no two
        // collide - `cash` and `Cash` on two chains are legal identity but one
        // file on Windows.
        private static void LintPaths(Build build, Options options)
        {
            var byLoweredPath = new Dictionary<string, Definition>();
            var managed = options.AssetRoot.TrimEnd('/') + "/";
            foreach (var pair in build.Paths)
            {
                if (!pair.Value.StartsWith(managed, StringComparison.Ordinal))
                    throw new ContentImportException($"'{pair.Key.Id}' would be written to '{pair.Value}', outside '{managed}'.");
                var key = pair.Value.ToLowerInvariant();
                if (byLoweredPath.TryGetValue(key, out var existing))
                    throw new ContentImportException(
                        $"'{existing.Id}' and '{pair.Key.Id}' both map to '{pair.Value}' - one file cannot hold two definitions.");
                byLoweredPath[key] = pair.Key;

                // What is ALREADY at the path is the writer's business too. This
                // importer will not delete an orphan, and CreateAsset replaces
                // whatever sits at its target - the same act with less warning.
                //
                // The question is FILESYSTEM occupancy, not what the
                // AssetDatabase can load: a stray file, a malformed asset, or one
                // whose script no longer binds is invisible to a load and still
                // very much there. So a load only decides the MESSAGE, and only
                // an asset of the kind we are about to write is ours to update
                // in place.
                // Ownership is the MAIN asset's type. A typed load returns the
                // first object of that type in the file, and one file can hold
                // several - so a foreign container with a matching SUBASSET
                // would read as ours, and the write pass would rewire and save
                // somebody else's file. Everything this importer writes is a
                // main asset, by CreateAsset, so nothing legitimate is lost.
                var occupant = AssetDatabase.LoadMainAssetAtPath(pair.Value);
                var mine = occupant != null && occupant.GetType() == pair.Key.GetType();
                if (!mine && (File.Exists(pair.Value) || Directory.Exists(pair.Value)))
                    throw new ContentImportException(occupant != null
                        ? $"'{pair.Value}' already holds a {occupant.GetType().Name} and '{pair.Key.Id}' is a {pair.Key.GetType().Name} - move or delete it by hand."
                        : $"'{pair.Value}' is occupied by something the AssetDatabase cannot load, and '{pair.Key.Id}' would overwrite it - move or delete it by hand.");
            }
        }

        // ---- asset paths and writing ----

        private static string FamilyOf(Type type)
        {
            if (type == typeof(CurrencyDefinition)) return "Currencies";
            if (type == typeof(ProducerDefinition)) return "Producers";
            if (type == typeof(GeneratorDefinition)) return "Generators";
            if (type == typeof(UpgradeDefinition)) return "Upgrades";
            if (type == typeof(ModifierDefinition)) return "Modifiers";
            if (type == typeof(BarGroupDefinition)) return "BarGroups";
            if (type == typeof(BarDefinition)) return "Bars";
            if (type == typeof(EventDefinition)) return "Events";
            if (type == typeof(TriggerDefinition)) return "Triggers";
            throw new ContentImportException($"{type.Name} has no asset family.");
        }

        // Grouped DOCUMENT then FAMILY, so a chapter's assets sit together and
        // each family folder is a per-chapter list rather than a game-wide one.
        // A scope asset sits at its document root.
        private static string AssetPath(Options options, string documentId, string family, string id) =>
            $"{options.AssetRoot}/{documentId}/{family}/{id}.asset";

        private static string ScopeAssetPath(Options options, string documentId, string id) =>
            $"{options.AssetRoot}/{documentId}/{id}.asset";

        // Update-in-place: the existing asset is loaded and overwritten, so
        // GUIDs survive and every direct reference stays intact.
        private static Definition LoadOrCreate(Type type, string path)
        {
            // The MAIN asset's type, for the reason LintPaths gives: a typed
            // load can hand back a subasset of a file that is not ours.
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && existing.GetType() == type)
                return (Definition)existing;
            // Preflight refuses an occupied path, so arriving here with one
            // occupied means the two passes disagree - never a licence to
            // overwrite, which is what CreateAsset would do next.
            if (File.Exists(path) || Directory.Exists(path))
                throw new ContentImportException(
                    $"'{path}' is occupied by something the preflight did not see.", true);
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var created = (Definition)ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ---- Addressables ----

        // Two kinds of entry and nothing else (12.14.5). Both live in ONE
        // PackTogether group: root-owned assets are implicit dependencies of the
        // root entry AND every chapter entry, and Addressables duplicates an
        // implicit dependency into every bundle referencing it - two `records`
        // instances at runtime would break the asset identity composition leans
        // on.
        private static void AssignAddressables(Build build, Options options)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new ContentImportException("no Addressables settings in this project.");
            var group = settings.FindGroup(options.GroupName) ?? settings.CreateGroup(
                options.GroupName, false, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            // Not optional: without this schema the group builds no bundle at
            // all, and without PackTogether root's own assets duplicate into
            // every chapter's bundle - two `records` instances at runtime, and
            // the composition resolves by asset identity. A pre-existing group
            // missing it gets it, rather than the entries being registered under
            // a packing rule nobody set.
            var schema = group.GetSchema<BundledAssetGroupSchema>()
                         ?? group.AddSchema<BundledAssetGroupSchema>();
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;

            var rootEntry = settings.CreateOrMoveEntry(GuidOf(build.Paths[build.Root]), group);
            rootEntry.address = ContentDatabase.RootAddress;
            rootEntry.SetLabel(ContentDatabase.ChapterLabel, false, true);

            var authored = new HashSet<string>();
            foreach (var chapter in build.Content.Chapters)
            {
                var guid = GuidOf(build.Paths[chapter]);
                authored.Add(guid);
                var entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = chapter.Id;
                entry.SetLabel(ContentDatabase.ChapterLabel, true, true);
            }

            // An orphaned chapter root loses the label - off the runtime roster,
            // still on disk. Deleting content is a human's call.
            foreach (var entry in group.entries.ToList())
            {
                if (entry.guid == rootEntry.guid || authored.Contains(entry.guid))
                    continue;
                // A renamed root leaves its old asset holding the FIXED address,
                // and an address is a primary key: two entries answering to
                // 'root' means boot loads whichever the catalogue reached first.
                // The entry goes; the asset stays, like every other orphan.
                if (entry.address == ContentDatabase.RootAddress)
                {
                    Debug.LogWarning($"Content import: '{AssetDatabase.GUIDToAssetPath(entry.guid)}' still claimed the '{ContentDatabase.RootAddress}' address - entry removed, asset left on disk.");
                    settings.RemoveAssetEntry(entry.guid, true);
                    continue;
                }
                if (!entry.labels.Contains(ContentDatabase.ChapterLabel))
                    continue;
                entry.SetLabel(ContentDatabase.ChapterLabel, false, true);
                Debug.LogWarning($"Content import: '{entry.address}' is no longer authored - de-labeled, not deleted.");
            }
            AssetDatabase.SaveAssets();
        }

        private static string GuidOf(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                throw new ContentImportException($"no asset was written at '{path}'.");
            return guid;
        }

        // What boot will load: the root at its address, the chapters off the
        // label. Validating the bare root asset would inspect nothing, since its
        // serialized child list is empty by contract.
        private static ComposedContent LoadPersisted(Options options)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings.FindGroup(options.GroupName);
            RootDefinition root = null;
            var chapters = new List<ChapterDefinition>();
            foreach (var entry in group.entries)
            {
                var path = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (entry.address == ContentDatabase.RootAddress)
                    root = AssetDatabase.LoadAssetAtPath<RootDefinition>(path);
                else if (entry.labels.Contains(ContentDatabase.ChapterLabel))
                    chapters.Add(AssetDatabase.LoadAssetAtPath<ChapterDefinition>(path));
            }
            if (root == null)
                throw new ContentImportException($"no asset carries the '{ContentDatabase.RootAddress}' address after the write.");
            return ComposedContent.Compose(root, chapters);
        }

        // An asset in the managed folders that the documents no longer author is
        // reported, never deleted.
        private static void ReportOrphans(Build build, Options options)
        {
            var written = new HashSet<string>(build.Paths.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(Definition), new[] { options.AssetRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!written.Contains(path))
                    Debug.LogWarning($"Content import: '{path}' is no longer authored by any document - left on disk.");
            }
        }
    }
}
