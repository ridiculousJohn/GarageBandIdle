using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    public enum ValidationSeverity
    {
        Error,
        Warning
    }

    // One member per implemented check family of the content-load pass (design
    // doc 12.12). The pass is incremental by design (build plan): every later
    // build step is required to extend this enum and the pass with the checks
    // its own shapes introduce - the full 12.12 set exists only once step 6
    // lands.
    public enum ValidationCheck
    {
        ScopeGraph,             // exactly one root, one parent each, every scope reachable
        DuplicateId,            // Definition ids and flags share one tree-wide id space
        DuplicateHome,          // a currency, flag, or trigger declared by two scopes
        TagIdCollision,         // a tag may not collide with any id
        UnresolvedReference,    // a referenced id resolves to nothing
        NullEntry,              // a null slot in an authored list, or a required operand
        RungOnRoot,
        ScopeReach,             // ResetScope / ExecuteRung / modifier-grant reach rules
        ChainReach,             // ordinary reads and writes address only the acting chain
        EffectReach,            // an effect sits where its target's outward walk visits it
        EffectTargetUnmatched,  // an effect target matching nothing reachable (warn)
        FlagNoSetter,           // a declared flag nothing sets (warn)
        SetThenWiped,           // a list sets a fact, then resets the scope declaring it
        FormulaReadsCleared,    // a formula-driven grant after a reset clearing its inputs (warn)
        StrandedValue,          // a rung resets a subtree holding a payout rung it never invokes (warn)
        ReferenceCycle,         // cycles across nested action references
        RemoveWithoutGrant,     // RemoveModifier naming a stack nothing grants there (warn)
        UnconsumedStat,         // a stat no system consumes (warn) - the typo guard a named vocabulary lacks
        NumericRange            // an authored number outside its legal range: NaN, infinity, wrong sign
    }

    public readonly struct ValidationFinding
    {
        public readonly ValidationSeverity Severity;
        public readonly ValidationCheck Check;
        public readonly string Message;

        public ValidationFinding(ValidationSeverity severity, ValidationCheck check, string message)
        {
            Severity = severity;
            Check = check;
            Message = message;
        }

        public override string ToString() => $"[{Severity}] {Check}: {Message}";
    }

    public class ValidationReport
    {
        private readonly List<ValidationFinding> findings = new();

        public IReadOnlyList<ValidationFinding> Findings => findings;

        public bool HasErrors => findings.Any(f => f.Severity == ValidationSeverity.Error);

        public IEnumerable<ValidationFinding> OfCheck(ValidationCheck check) =>
            findings.Where(f => f.Check == check);

        internal void Add(ValidationSeverity severity, ValidationCheck check, string message) =>
            findings.Add(new ValidationFinding(severity, check, message));

        // The fail-loudly surface (design doc 12.14.6). The boot call site lands
        // with GameManager; development builds treat HasErrors as fatal.
        public void LogAll()
        {
            foreach (var finding in findings)
            {
                if (finding.Severity == ValidationSeverity.Error)
                    Debug.LogError(finding.ToString());
                else
                    Debug.LogWarning(finding.ToString());
            }
        }
    }

    // ---- ledger records the cross-container checks aggregate after the walk ----

    internal readonly struct FlagSetterRecord
    {
        public readonly string FlagId;
        public readonly ScopeDefinition ActingScope;

        public FlagSetterRecord(string flagId, ScopeDefinition actingScope)
        {
            FlagId = flagId;
            ActingScope = actingScope;
        }
    }

    internal readonly struct ModifierGrantRecord
    {
        public readonly string ModifierId;
        public readonly ScopeDefinition Target;

        public ModifierGrantRecord(string modifierId, ScopeDefinition target)
        {
            ModifierId = modifierId;
            Target = target;
        }
    }

    internal readonly struct ModifierRemoveRecord
    {
        public readonly string ModifierId;
        public readonly ScopeDefinition Target;
        public readonly string Site;

        public ModifierRemoveRecord(string modifierId, ScopeDefinition target, string site)
        {
            ModifierId = modifierId;
            Target = target;
            Site = site;
        }
    }

    internal readonly struct RungEdgeRecord
    {
        public readonly string FromKey;
        public readonly string ToKey;
        public readonly int Index;
        public readonly string Site;

        public RungEdgeRecord(string fromKey, string toKey, int index, string site)
        {
            FromKey = fromKey;
            ToKey = toKey;
            Index = index;
            Site = site;
        }
    }

    internal readonly struct FactWriteRecord
    {
        public readonly string ContainerKey;
        public readonly int Index;
        public readonly string Description;
        public readonly ScopeDefinition Home;
        public readonly string Site;

        public FactWriteRecord(string containerKey, int index, string description, ScopeDefinition home, string site)
        {
            ContainerKey = containerKey;
            Index = index;
            Description = description;
            Home = home;
            Site = site;
        }
    }

    internal readonly struct FormulaReadRecord
    {
        public readonly string ContainerKey;
        public readonly int Index;
        public readonly string CurrencyId;
        public readonly ScopeDefinition Home;
        public readonly string Site;

        public FormulaReadRecord(string containerKey, int index, string currencyId, ScopeDefinition home, string site)
        {
            ContainerKey = containerKey;
            Index = index;
            CurrencyId = currencyId;
            Home = home;
            Site = site;
        }
    }

    internal readonly struct ResetRecord
    {
        public readonly string ContainerKey;
        public readonly int Index;
        public readonly ScopeDefinition Target;
        public readonly string Site;

        public ResetRecord(string containerKey, int index, ScopeDefinition target, string site)
        {
            ContainerKey = containerKey;
            Index = index;
            Target = target;
            Site = site;
        }
    }

    // What a kind's Validate sees: the acting scope, definition lookup, tree
    // queries mirroring the runtime walks, finding sinks, and the ledgers the
    // cross-container checks read after the walk. The walker (ContentValidator)
    // positions it; kinds only query, report, and record.
    public class ValidationContext
    {
        public IDefinitionSource Defs { get; }
        public ScopeDefinition RootScope { get; }
        public ScopeDefinition ActingScope { get; private set; }

        private readonly ValidationReport report;
        private readonly Dictionary<ScopeDefinition, ScopeDefinition> parentByScope;
        private readonly Dictionary<string, ScopeDefinition> scopeById;
        private readonly Dictionary<string, ScopeDefinition> currencyHomeById;
        private readonly Dictionary<string, ScopeDefinition> flagHomeById;
        private readonly Dictionary<Definition, ScopeDefinition> declaringScopeByDefinition;
        private readonly List<ScopeDefinition> treeScopes;
        private readonly List<Definition> allDefinitions;
        private readonly int parentWalkGuard; // bounds chain walks against malformed graphs

        private string site;
        private string containerKey;
        private int actionIndex = -1;

        internal List<FlagSetterRecord> FlagSetters { get; } = new();
        internal List<ModifierGrantRecord> ModifierGrants { get; } = new();
        internal List<ModifierRemoveRecord> ModifierRemoves { get; } = new();
        internal List<RungEdgeRecord> RungEdges { get; } = new();
        internal List<FactWriteRecord> FactWrites { get; } = new();
        internal List<FormulaReadRecord> FormulaReads { get; } = new();
        internal List<ResetRecord> Resets { get; } = new();

        internal ValidationContext(
            IDefinitionSource defs,
            ValidationReport report,
            ScopeDefinition rootScope,
            Dictionary<ScopeDefinition, ScopeDefinition> parentByScope,
            Dictionary<string, ScopeDefinition> scopeById,
            Dictionary<string, ScopeDefinition> currencyHomeById,
            Dictionary<string, ScopeDefinition> flagHomeById,
            Dictionary<Definition, ScopeDefinition> declaringScopeByDefinition,
            List<ScopeDefinition> treeScopes,
            List<Definition> allDefinitions)
        {
            Defs = defs;
            this.report = report;
            RootScope = rootScope;
            this.parentByScope = parentByScope;
            this.scopeById = scopeById;
            this.currencyHomeById = currencyHomeById;
            this.flagHomeById = flagHomeById;
            this.declaringScopeByDefinition = declaringScopeByDefinition;
            this.treeScopes = treeScopes;
            this.allDefinitions = allDefinitions;
            parentWalkGuard = scopeById.Count + parentByScope.Count + 1;
        }

        internal static string RungKey(string scopeId) => "rung:" + scopeId;

        internal void EnterContainer(ScopeDefinition actingScope, string key)
        {
            ActingScope = actingScope;
            containerKey = key;
            SetSite(null);
        }

        internal void SetSite(string newSite, int index = -1)
        {
            site = newSite;
            actionIndex = index;
        }

        internal void ClearSite()
        {
            ActingScope = null;
            containerKey = null;
            SetSite(null);
        }

        // ---- finding sinks; the site prefix names where the finding lives ----

        public void AddError(ValidationCheck check, string message) =>
            report.Add(ValidationSeverity.Error, check, Prefix(message));

        public void AddWarning(ValidationCheck check, string message) =>
            report.Add(ValidationSeverity.Warning, check, Prefix(message));

        private string Prefix(string message) => site == null ? message : $"{site}: {message}";

        // ---- tree queries mirroring the runtime walks ----

        public ScopeDefinition FindScope(string id) =>
            id != null && scopeById.TryGetValue(id, out var scope) ? scope : null;

        public ScopeDefinition CurrencyHome(string currencyId) =>
            currencyId != null && currencyHomeById.TryGetValue(currencyId, out var home) ? home : null;

        // The scope whose declaration list holds this definition - declaration is
        // ownership (12.3), so this is the home of every fact it creates. Null
        // when no scope declares it.
        public ScopeDefinition DeclaringScope(Definition definition) =>
            definition != null && declaringScopeByDefinition.TryGetValue(definition, out var scope) ? scope : null;

        public ScopeDefinition FlagHome(string flagId) =>
            flagId != null && flagHomeById.TryGetValue(flagId, out var home) ? home : null;

        public ScopeDefinition Parent(ScopeDefinition scope) =>
            scope != null && parentByScope.TryGetValue(scope, out var parent) ? parent : null;

        // True when node is top or sits anywhere inside top's subtree.
        public bool InSubtree(ScopeDefinition top, ScopeDefinition node)
        {
            if (top == null || node == null)
                return false;
            var guard = 0;
            for (var current = node; current != null && guard <= parentWalkGuard; current = Parent(current), guard++)
                if (current == top)
                    return true;
            return false;
        }

        // The runtime read/write walk: the acting scope or an ancestor of it.
        public bool OnActingChain(ScopeDefinition scope) => InSubtree(scope, ActingScope);

        public bool InActingSubtree(ScopeDefinition scope) => InSubtree(ActingScope, scope);

        public bool IsSiblingOfActing(ScopeDefinition scope) =>
            scope != null && scope != ActingScope &&
            Parent(scope) != null && Parent(scope) == Parent(ActingScope);

        public bool IsProperAncestor(ScopeDefinition outer, ScopeDefinition inner) =>
            outer != inner && InSubtree(outer, inner);

        // The shared rule for every ordinary currency read and write (12.12):
        // the id resolves, the currency has a home, and the home sits on the
        // acting chain. Returns the home when usable; reports and returns null
        // otherwise.
        public ScopeDefinition RequireChainCurrency(string currencyId, string use)
        {
            if (string.IsNullOrEmpty(currencyId))
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} names an empty currency id.");
                return null;
            }
            if (Defs.Get<Economy.CurrencyDefinition>(currencyId) == null)
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} references unknown currency '{currencyId}'.");
                return null;
            }
            var home = CurrencyHome(currencyId);
            if (home == null)
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} references currency '{currencyId}', which no scope declares.");
                return null;
            }
            if (!OnActingChain(home))
            {
                AddError(ValidationCheck.ChainReach, $"{use} addresses currency '{currencyId}' homed at '{home.Id}', which is not on the chain from '{ActingScope.Id}' (12.12).");
                return null;
            }
            return home;
        }

        // The same rule for a scope-attached definition's fact (12.12): the id
        // resolves, some scope declares it, and that scope sits on the acting
        // chain - the runtime walk reaches nowhere else, so a cross-tree read is
        // a load-time error rather than a silent runtime miss.
        public ScopeDefinition RequireChainDeclaration<T>(string id, string use) where T : Definition
        {
            if (string.IsNullOrEmpty(id))
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} names an empty id.");
                return null;
            }
            var definition = Defs.Get<T>(id);
            if (definition == null)
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} references unknown {typeof(T).Name} '{id}'.");
                return null;
            }
            var home = DeclaringScope(definition);
            if (home == null)
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} references '{id}', which no scope declares.");
                return null;
            }
            if (!OnActingChain(home))
            {
                AddError(ValidationCheck.ChainReach, $"{use} reads '{id}' declared at '{home.Id}', which is not on the chain from '{ActingScope.Id}' (12.12).");
                return null;
            }
            return home;
        }

        // NaN and infinity are refused on every authored double: either one
        // poisons a product silently, and no legal tuning value is either.
        public bool RequireFiniteDouble(double value, string what)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddError(ValidationCheck.NumericRange, $"{what} is {value} - authored numbers must be finite.");
                return false;
            }
            return true;
        }

        // A stat means something because a system consumes it (12.2), so one
        // outside the consumed set produces nothing. The warn is what recovers
        // the typo protection an enum would have given a named vocabulary.
        public void RequireConsumedStat(string stat, string what)
        {
            if (Economy.Stat.IsConsumed(stat))
                return;
            AddWarning(ValidationCheck.UnconsumedStat, string.IsNullOrEmpty(stat)
                ? $"{what} names no stat - no system consumes it."
                : $"{what} names stat '{stat}', which no system consumes ({Economy.Stat.ConsumedNames}).");
        }

        // ---- ledgers ----

        public void RecordFlagSetter(string flagId) =>
            FlagSetters.Add(new FlagSetterRecord(flagId, ActingScope));

        public void RecordModifierGrant(string modifierId, ScopeDefinition target) =>
            ModifierGrants.Add(new ModifierGrantRecord(modifierId, target));

        public void RecordModifierRemove(string modifierId, ScopeDefinition target) =>
            ModifierRemoves.Add(new ModifierRemoveRecord(modifierId, target, site));

        public void RecordRungInvocation(ScopeDefinition target) =>
            RungEdges.Add(new RungEdgeRecord(containerKey, RungKey(target.Id), actionIndex, site));

        public void RecordFactWrite(string description, ScopeDefinition home) =>
            FactWrites.Add(new FactWriteRecord(containerKey, actionIndex, description, home, site));

        public void RecordFormulaRead(string currencyId, ScopeDefinition home) =>
            FormulaReads.Add(new FormulaReadRecord(containerKey, actionIndex, currencyId, home, site));

        public void RecordReset(ScopeDefinition target) =>
            Resets.Add(new ResetRecord(containerKey, actionIndex, target, site));

        // ---- tag membership for effect targets ----

        public bool TagExists(string tag) => allDefinitions.Any(d => d.HasTag(tag));

        // A currency coordinate matches an entry's CURRENCY, so the tag has to
        // live on a currency. A tag that exists only on producers narrows to
        // nothing at runtime, which would otherwise validate clean and leave the
        // effect permanently inert.
        public bool CurrencyTagExists(string tag) =>
            Defs.All<Economy.CurrencyDefinition>().Any(c => c != null && c.HasTag(tag));

        // A tag target must match a TARGETABLE member within the effect's
        // declaring scope's subtree (12.12) - something whose numbers a
        // multiplier can apply to. That is the currencies homed there plus the
        // producers and generators declared there; bars and groups join with
        // their scope attachments. Scope and trigger tags are vocabulary, not
        // targets - a multiplier never resolves against them.
        public bool TagHasMemberInSubtree(ScopeDefinition top, string tag)
        {
            foreach (var scope in treeScopes)
            {
                if (!InSubtree(top, scope))
                    continue;
                foreach (var currency in scope.declaredCurrencies)
                    if (currency != null && currency.HasTag(tag))
                        return true;
                foreach (var producer in scope.producers)
                    if (producer != null && producer.HasTag(tag))
                        return true;
                foreach (var generator in scope.generators)
                    if (generator != null && generator.HasTag(tag))
                        return true;
            }
            return false;
        }

        internal IEnumerable<ScopeDefinition> ScopesInSubtree(ScopeDefinition top)
        {
            foreach (var scope in treeScopes)
                if (InSubtree(top, scope))
                    yield return scope;
        }
    }

    // The content-load validation pass (design doc 12.12, hosted alongside
    // ContentDatabase per 12.13). Structural and cross-cutting checks live
    // here; per-kind reference and reach checks live on the kinds' own
    // Validate(ValidationContext) overrides, so a new kind ships its checks
    // with its class.
    public static class ContentValidator
    {
        public static ValidationReport Validate(IDefinitionSource defs)
        {
            var report = new ValidationReport();

            // ---- scope graph: collect every scope, including ones reachable
            // only through a children list ----
            var allScopes = new List<ScopeDefinition>();
            var scopeSeen = new HashSet<ScopeDefinition>();
            foreach (var scope in defs.All<ScopeDefinition>())
                if (scope != null && scopeSeen.Add(scope))
                    allScopes.Add(scope);
            for (var i = 0; i < allScopes.Count; i++)
                foreach (var child in allScopes[i].children)
                    if (child != null && scopeSeen.Add(child))
                        allScopes.Add(child);

            var parentByScope = new Dictionary<ScopeDefinition, ScopeDefinition>();
            foreach (var scope in allScopes)
            {
                for (var i = 0; i < scope.children.Count; i++)
                {
                    var child = scope.children[i];
                    if (child == null)
                    {
                        report.Add(ValidationSeverity.Error, ValidationCheck.NullEntry,
                            $"scope '{scope.Id}' children[{i}] is null.");
                        continue;
                    }
                    if (parentByScope.TryGetValue(child, out var existing))
                    {
                        report.Add(ValidationSeverity.Error, ValidationCheck.ScopeGraph,
                            $"scope '{child.Id}' is a child of both '{existing.Id}' and '{scope.Id}'.");
                        continue;
                    }
                    parentByScope[child] = scope;
                }
            }

            // ---- id space: Definition ids and declared flags, tree-wide ----
            var allDefinitions = new List<Definition>();
            var definitionSeen = new HashSet<Definition>();
            foreach (var definition in defs.All<Definition>())
                if (definition != null && definitionSeen.Add(definition))
                    allDefinitions.Add(definition);
            // Scope-attached families are content like any other: their ids join
            // the tree-wide id space, so they are collected here rather than
            // discovered separately.
            void CollectDeclared<T>(ScopeDefinition scope, List<T> list, string label) where T : Definition
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var definition = list[i];
                    if (definition == null)
                    {
                        report.Add(ValidationSeverity.Error, ValidationCheck.NullEntry,
                            $"scope '{scope.Id}' {label}[{i}] is null.");
                        continue;
                    }
                    if (definitionSeen.Add(definition))
                        allDefinitions.Add(definition);

                    // A declaration is a direct reference, but every runtime
                    // lookup goes through the database - TryBuy and FireProducer
                    // resolve by id. An asset missing its Addressables label is
                    // therefore visible here and to the rate walk, yet cannot be
                    // bought or fired: half-working content that would otherwise
                    // validate clean.
                    if (!string.IsNullOrEmpty(definition.Id) && defs.Get<T>(definition.Id) != definition)
                        report.Add(ValidationSeverity.Error, ValidationCheck.UnresolvedReference,
                            $"scope '{scope.Id}' declares {typeof(T).Name} '{definition.Id}', which the content database does not resolve to this asset - check its Addressables label.");
                }
            }

            foreach (var scope in allScopes)
            {
                if (definitionSeen.Add(scope))
                    allDefinitions.Add(scope);
                CollectDeclared(scope, scope.declaredCurrencies, "declaredCurrencies");
                CollectDeclared(scope, scope.triggers, "triggers");
                CollectDeclared(scope, scope.producers, "producers");
                CollectDeclared(scope, scope.generators, "generators");
                CollectDeclared(scope, scope.upgrades, "upgrades");
                CollectDeclared(scope, scope.careerEffects, "careerEffects");
            }

            var idOwners = new Dictionary<string, List<(string desc, bool isFlag)>>();
            void AddOwner(string id, string desc, bool isFlag)
            {
                if (!idOwners.TryGetValue(id, out var owners))
                    idOwners[id] = owners = new List<(string, bool)>();
                owners.Add((desc, isFlag));
            }

            foreach (var definition in allDefinitions)
            {
                if (string.IsNullOrEmpty(definition.Id))
                {
                    report.Add(ValidationSeverity.Error, ValidationCheck.NullEntry,
                        $"a {definition.GetType().Name} asset has a null or empty id.");
                    continue;
                }
                AddOwner(definition.Id, $"{definition.GetType().Name} '{definition.Id}'", false);
            }
            foreach (var scope in allScopes)
            {
                for (var i = 0; i < scope.declaredFlags.Count; i++)
                {
                    var flag = scope.declaredFlags[i];
                    if (string.IsNullOrEmpty(flag))
                        report.Add(ValidationSeverity.Error, ValidationCheck.NullEntry,
                            $"scope '{scope.Id}' declaredFlags[{i}] is empty.");
                    else
                        AddOwner(flag, $"flag declared at scope '{scope.Id}'", true);
                }
            }

            foreach (var pair in idOwners)
            {
                if (pair.Value.Count <= 1)
                    continue;
                var owners = string.Join(", ", pair.Value.Select(o => o.desc));
                if (pair.Value.All(o => o.isFlag))
                    report.Add(ValidationSeverity.Error, ValidationCheck.DuplicateHome,
                        $"flag '{pair.Key}' has multiple declarations: {owners} - a flag has one home.");
                else
                    report.Add(ValidationSeverity.Error, ValidationCheck.DuplicateId,
                        $"id '{pair.Key}' is not unique tree-wide: {owners}.");
            }

            var reportedTags = new HashSet<string>();
            foreach (var definition in allDefinitions)
                foreach (var tag in definition.Tags)
                    if (!string.IsNullOrEmpty(tag) && idOwners.ContainsKey(tag) && reportedTags.Add(tag))
                        report.Add(ValidationSeverity.Error, ValidationCheck.TagIdCollision,
                            $"tag '{tag}' (on {definition.GetType().Name} '{definition.Id}') collides with an id.");

            // ---- root resolution; everything tree-shaped needs exactly one ----
            var roots = allScopes.Where(s => !parentByScope.ContainsKey(s)).ToList();
            if (roots.Count == 0)
            {
                report.Add(ValidationSeverity.Error, ValidationCheck.ScopeGraph,
                    allScopes.Count == 0
                        ? "no scope definitions were loaded."
                        : "no root scope exists - every scope has a parent (a children cycle).");
                return report;
            }
            if (roots.Count > 1)
            {
                report.Add(ValidationSeverity.Error, ValidationCheck.ScopeGraph,
                    $"multiple root scopes: {string.Join(", ", roots.Select(r => $"'{r.Id}'"))} - exactly one scope must be no scope's child.");
                return report;
            }
            var root = roots[0];

            var treeScopes = new List<ScopeDefinition>();
            var visited = new HashSet<ScopeDefinition>();
            void Walk(ScopeDefinition scope)
            {
                if (!visited.Add(scope))
                    return;
                treeScopes.Add(scope);
                foreach (var child in scope.children)
                    if (child != null)
                        Walk(child);
            }
            Walk(root);
            foreach (var scope in allScopes)
                if (!visited.Contains(scope))
                    report.Add(ValidationSeverity.Error, ValidationCheck.ScopeGraph,
                        $"scope '{scope.Id}' is not reachable from the root '{root.Id}'.");

            // ---- homes: a currency, flag, or trigger is declared exactly once ----
            var currencyHomeById = new Dictionary<string, ScopeDefinition>();
            foreach (var scope in treeScopes)
            {
                foreach (var currency in scope.declaredCurrencies)
                {
                    // Null slots and undiscoverable assets are reported by the
                    // declaration collection above; this pass owns homes.
                    if (currency == null || string.IsNullOrEmpty(currency.Id))
                        continue;
                    var currencyId = currency.Id;
                    if (currencyHomeById.TryGetValue(currencyId, out var existing))
                    {
                        report.Add(ValidationSeverity.Error, ValidationCheck.DuplicateHome,
                            $"currency '{currencyId}' is declared by both '{existing.Id}' and '{scope.Id}' - a currency has one home.");
                        continue;
                    }
                    currencyHomeById[currencyId] = scope;
                }
            }

            var flagHomeById = new Dictionary<string, ScopeDefinition>();
            foreach (var scope in treeScopes)
                foreach (var flag in scope.declaredFlags)
                    if (!string.IsNullOrEmpty(flag) && !flagHomeById.ContainsKey(flag))
                        flagHomeById[flag] = scope; // duplicates already refused above

            var triggerHomes = new Dictionary<TriggerDefinition, ScopeDefinition>();
            foreach (var scope in treeScopes)
            {
                foreach (var trigger in scope.triggers)
                {
                    if (trigger == null)
                        continue; // flagged during id collection
                    if (triggerHomes.TryGetValue(trigger, out var existing))
                        report.Add(ValidationSeverity.Error, ValidationCheck.DuplicateHome,
                            $"trigger '{trigger.Id}' is declared by both '{existing.Id}' and '{scope.Id}' - a trigger has one home.");
                    else
                        triggerHomes[trigger] = scope;
                }
            }

            // Declaration is ownership, so a producer, generator, upgrade, or
            // career effect belongs to exactly one scope - the same rule the
            // trigger check above enforces.
            var declaringScopeByDefinition = new Dictionary<Definition, ScopeDefinition>();
            void RecordHome<T>(ScopeDefinition scope, List<T> list) where T : Definition
            {
                foreach (var definition in list)
                {
                    if (definition == null)
                        continue; // flagged during id collection
                    if (declaringScopeByDefinition.TryGetValue(definition, out var existing))
                        report.Add(ValidationSeverity.Error, ValidationCheck.DuplicateHome,
                            $"{definition.GetType().Name} '{definition.Id}' is declared by both '{existing.Id}' and '{scope.Id}' - declaration is ownership.");
                    else
                        declaringScopeByDefinition[definition] = scope;
                }
            }
            foreach (var scope in treeScopes)
            {
                RecordHome(scope, scope.producers);
                RecordHome(scope, scope.generators);
                RecordHome(scope, scope.upgrades);
                RecordHome(scope, scope.careerEffects);
            }

            var scopeById = new Dictionary<string, ScopeDefinition>();
            foreach (var scope in allScopes)
                if (!string.IsNullOrEmpty(scope.Id) && !scopeById.ContainsKey(scope.Id))
                    scopeById[scope.Id] = scope;

            var ctx = new ValidationContext(defs, report, root, parentByScope, scopeById,
                currencyHomeById, flagHomeById, declaringScopeByDefinition, treeScopes, allDefinitions);

            // ---- container walk: every rung and trigger, in tree order ----
            foreach (var scope in treeScopes)
            {
                if (scope.rung != null)
                {
                    if (scope == root)
                        report.Add(ValidationSeverity.Error, ValidationCheck.RungOnRoot,
                            $"the root scope '{root.Id}' declares a rung - rungs live on tiers and chapters (12.12).");
                    ctx.EnterContainer(scope, ValidationContext.RungKey(scope.Id));
                    if (scope.rung.offerCondition != null)
                    {
                        // A null offer condition is legal authoring: it never
                        // offers (fail-closed), so only a present one validates.
                        ctx.SetSite($"scope '{scope.Id}' rung offer");
                        scope.rung.offerCondition.Validate(ctx);
                    }
                    ValidateActionList(ctx, scope.rung.actions, $"scope '{scope.Id}' rung");
                }

                foreach (var trigger in scope.triggers)
                {
                    if (trigger == null)
                        continue;
                    ctx.EnterContainer(scope, "trigger:" + trigger.Id);
                    if (trigger.condition != null)
                    {
                        ctx.SetSite($"trigger '{trigger.Id}' condition");
                        trigger.condition.Validate(ctx);
                    }
                    ValidateActionList(ctx, trigger.actions, $"trigger '{trigger.Id}'");
                }

                foreach (var producer in scope.producers)
                {
                    if (producer == null)
                        continue;
                    ctx.EnterContainer(scope, "producer:" + producer.Id);
                    ValidateProducesEntries(ctx, producer.produces, $"producer '{producer.Id}'");
                }

                foreach (var generator in scope.generators)
                {
                    if (generator == null)
                        continue;
                    ctx.EnterContainer(scope, "generator:" + generator.Id);
                    ValidateGenerator(ctx, generator);
                }

                foreach (var upgrade in scope.upgrades)
                {
                    if (upgrade == null)
                        continue;
                    ctx.EnterContainer(scope, "upgrade:" + upgrade.Id);
                    ValidateUpgrade(ctx, upgrade, scope);
                }

                foreach (var career in scope.careerEffects)
                {
                    if (career == null)
                        continue;
                    ctx.EnterContainer(scope, "career:" + career.Id);
                    ValidateCareerEffect(ctx, career, scope);
                }
            }

            // ---- cross-container checks over the ledgers ----
            ctx.ClearSite();
            FinalizeListChecks(ctx);
            FinalizeStrandedValue(ctx);
            FinalizeFlagChecks(ctx, flagHomeById);
            FinalizeModifierChecks(ctx);
            FinalizeCycles(ctx);

            return report;
        }

        // A produces entry addresses its currency on the declaring chain, its
        // condition is judged there, and its stat must be one a system consumes.
        // A null condition is legal authoring: the condition is optional, and an
        // entry is not a gate (12.2).
        private static void ValidateProducesEntries(ValidationContext ctx, List<Economy.ProducesEntry> entries, string siteBase)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                ctx.SetSite($"{siteBase} produces[{i}]");
                if (entry == null)
                {
                    ctx.AddError(ValidationCheck.NullEntry, "null produces entry.");
                    continue;
                }
                ctx.RequireChainCurrency(entry.currencyId, "a produces entry");
                ctx.RequireConsumedStat(entry.stat, "a produces entry");
                if (entry.value < BigNumber.Zero)
                    ctx.AddError(ValidationCheck.NumericRange,
                        $"value is {entry.value} - a contribution never subtracts.");
                entry.condition?.Validate(ctx);
            }
        }

        private static void ValidateGenerator(ValidationContext ctx, Economy.GeneratorDefinition generator)
        {
            var site = $"generator '{generator.Id}'";
            ctx.SetSite(site);
            if (generator.availableWhen == null)
                ctx.AddWarning(ValidationCheck.NullEntry,
                    "availableWhen is unauthored, so the buy is always refused - an unauthored gate is closed, not open (permanently inert content).");
            else
            {
                ctx.SetSite($"{site} availableWhen");
                generator.availableWhen.Validate(ctx);
            }

            ctx.SetSite(site);
            ctx.RequireChainCurrency(generator.costCurrencyId, "generator cost");
            if (generator.baseCost <= BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"baseCost is {generator.baseCost} - generator purchases repeat, so a free one is an unbounded rate printer.");
            if (ctx.RequireFiniteDouble(generator.growth, $"{site} growth") && generator.growth <= 0)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"growth is {generator.growth} - the cost curve is a positive ratio.");
            ValidateProducesEntries(ctx, generator.produces, site);
        }

        private static void ValidateUpgrade(ValidationContext ctx, Economy.UpgradeDefinition upgrade, ScopeDefinition scope)
        {
            var site = $"upgrade '{upgrade.Id}'";
            ctx.SetSite(site);
            if (upgrade.gate == null)
                ctx.AddWarning(ValidationCheck.NullEntry,
                    "gate is unauthored, so the buy is always refused - an unauthored gate is closed, not open (permanently inert content).");
            else
            {
                ctx.SetSite($"{site} gate");
                upgrade.gate.Validate(ctx);
            }

            ctx.SetSite(site);
            ctx.RequireChainCurrency(upgrade.costCurrencyId, "upgrade cost");
            if (upgrade.cost < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"cost is {upgrade.cost} - a purchase never pays out.");

            // The effects live where the upgrade is declared, so reach is a
            // static question here - unlike a modifier's, which is judged per
            // grant site.
            for (var i = 0; i < upgrade.effects.Count; i++)
                ValidateEffect(ctx, upgrade.effects[i], $"{site} effects[{i}]", scope);

            // The purchase latch is a fact write at index -1: it lands before
            // actions[0], so a payload that resets the latch's own scope trips
            // set-then-wiped instead of yielding a repeatably-purchasable
            // upgrade. Only actions record fact writes, so -1 collides with
            // nothing.
            ctx.SetSite($"{site} purchase latch");
            ctx.RecordFactWrite($"the purchase latch of upgrade '{upgrade.Id}'", scope);

            ValidateActionList(ctx, upgrade.actions, site);
        }

        private static void ValidateCareerEffect(ValidationContext ctx, Economy.CareerEffectDefinition career, ScopeDefinition scope)
        {
            var site = $"career effect '{career.Id}'";
            ctx.SetSite(site);
            if (career.formula == null)
                ctx.AddError(ValidationCheck.NullEntry, "no formula - there is no factor to compute.");
            ValidateEffectCoordinates(ctx, career.target, career.currencyId, career.stat, site);
            ValidateEffectTargetReach(ctx, career.target, site, scope);
            career.formula?.Validate(ctx);
        }

        private static void ValidateActionList(ValidationContext ctx, List<GameAction> actions, string siteBase)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (action == null)
                {
                    ctx.SetSite($"{siteBase} actions[{i}]");
                    ctx.AddError(ValidationCheck.NullEntry, "null action entry.");
                    continue;
                }
                ctx.SetSite($"{siteBase} actions[{i}] ({action.GetType().Name})", i);
                action.Validate(ctx);
            }
        }

        // Set-then-wiped (error) and formula-reads-cleared (warn), both
        // list-order questions within one container (12.12).
        private static void FinalizeListChecks(ValidationContext ctx)
        {
            foreach (var reset in ctx.Resets)
            {
                foreach (var write in ctx.FactWrites)
                    if (write.ContainerKey == reset.ContainerKey && write.Index < reset.Index &&
                        ctx.InSubtree(reset.Target, write.Home))
                        ctx.AddError(ValidationCheck.SetThenWiped,
                            $"{write.Site}: {write.Description} is set here and wiped by the ResetScope of '{reset.Target.Id}' at actions[{reset.Index}] (set-then-wiped, 12.12).");

                foreach (var read in ctx.FormulaReads)
                    if (read.ContainerKey == reset.ContainerKey && reset.Index < read.Index &&
                        ctx.InSubtree(reset.Target, read.Home))
                        ctx.AddWarning(ValidationCheck.FormulaReadsCleared,
                            $"{read.Site}: the formula reads currency '{read.CurrencyId}' after actions[{reset.Index}] resets '{reset.Target.Id}', which holds it - the grant reads zeros (12.12).");
            }
        }

        // A rung that resets a scope containing payout rungs it never invokes
        // warns - the value those rungs would cash dies with the reset (12.12:
        // stranded value). Payout today means a top-level AddCurrency in the
        // rung's list. "Invokes" is transitive: an ExecuteRung issued before
        // the reset executes the target rung's whole list at that moment,
        // including its own ExecuteRungs, so nested ladders cash through the
        // chain - only the first hop needs to precede the reset. The acting
        // rung itself is exempt: payout-before-clear is list order, and
        // set-then-wiped covers the misordering. The doc bullet names rungs,
        // so trigger resets are not judged here.
        private static void FinalizeStrandedValue(ValidationContext ctx)
        {
            foreach (var reset in ctx.Resets)
            {
                if (!reset.ContainerKey.StartsWith("rung:"))
                    continue;

                // Everything reachable through rung invocations issued before
                // this reset. Visited-set traversal: a cycle (its own error)
                // cannot loop it.
                var reached = new HashSet<string>();
                var frontier = new Stack<string>();
                foreach (var edge in ctx.RungEdges)
                    if (edge.FromKey == reset.ContainerKey && edge.Index < reset.Index && reached.Add(edge.ToKey))
                        frontier.Push(edge.ToKey);
                while (frontier.Count > 0)
                {
                    var key = frontier.Pop();
                    foreach (var edge in ctx.RungEdges)
                        if (edge.FromKey == key && reached.Add(edge.ToKey))
                            frontier.Push(edge.ToKey);
                }

                foreach (var scope in ctx.ScopesInSubtree(reset.Target))
                {
                    if (scope.rung == null)
                        continue;
                    var rungKey = ValidationContext.RungKey(scope.Id);
                    if (rungKey == reset.ContainerKey)
                        continue;
                    if (!scope.rung.actions.Any(a => a is AddCurrency))
                        continue;
                    if (!reached.Contains(rungKey))
                        ctx.AddWarning(ValidationCheck.StrandedValue,
                            $"{reset.Site}: resets '{reset.Target.Id}', which contains the payout rung at '{scope.Id}' with no ExecuteRung before the reset - stranded value (12.12).");
                }
            }
        }

        private static void FinalizeFlagChecks(ValidationContext ctx, Dictionary<string, ScopeDefinition> flagHomeById)
        {
            foreach (var pair in flagHomeById)
            {
                var flagId = pair.Key;
                var home = pair.Value;
                var setters = ctx.FlagSetters.Where(s => s.FlagId == flagId).ToList();
                if (setters.Count == 0)
                {
                    ctx.AddWarning(ValidationCheck.FlagNoSetter,
                        $"flag '{flagId}' (declared at '{home.Id}') has no setter.");
                    continue;
                }
            }
        }

        private static void FinalizeModifierChecks(ValidationContext ctx)
        {
            foreach (var remove in ctx.ModifierRemoves)
                if (!ctx.ModifierGrants.Any(g => g.ModifierId == remove.ModifierId && g.Target == remove.Target))
                    ctx.AddWarning(ValidationCheck.RemoveWithoutGrant,
                        $"{remove.Site}: RemoveModifier removes '{remove.ModifierId}' at '{remove.Target.Id}', where nothing grants it.");

            // Scope-independent reference resolution runs for EVERY authored
            // modifier, granted or not - every authored reference resolves
            // (12.12).
            foreach (var modifier in ctx.Defs.All<Economy.ModifierDefinition>())
            {
                if (modifier == null)
                    continue;
                for (var i = 0; i < modifier.effects.Count; i++)
                    ValidateEffect(ctx, modifier.effects[i], $"modifier '{modifier.Id}' effects[{i}]", null);
            }

            // Reach is judged per grant site: the granted-to scope is where the
            // effect lives, so that is where its target's outward walk must be
            // able to see it (12.12).
            var validatedGrants = new HashSet<(string, ScopeDefinition)>();
            foreach (var grant in ctx.ModifierGrants)
            {
                if (!validatedGrants.Add((grant.ModifierId, grant.Target)))
                    continue;
                var modifier = ctx.Defs.Get<Economy.ModifierDefinition>(grant.ModifierId);
                if (modifier == null) // grants record only after the id resolved
                    continue;
                for (var i = 0; i < modifier.effects.Count; i++)
                    ValidateEffectTargetReach(ctx, modifier.effects[i].target,
                        $"modifier '{modifier.Id}' (granted at '{grant.Target.Id}') effects[{i}]", grant.Target);
            }
        }

        // One Effect atom: its address, its multiplier's range, and - when the
        // effect's home is statically known (an upgrade or career effect, unlike
        // a modifier's per-grant-site home) - its reach.
        private static void ValidateEffect(ValidationContext ctx, Effect effect, string site, ScopeDefinition declaringScope)
        {
            ValidateEffectCoordinates(ctx, effect.target, effect.currencyId, effect.stat, site);
            if (ctx.RequireFiniteDouble(effect.multiplier, $"{site} multiplier") && effect.multiplier < 0)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"{site}: multiplier is {effect.multiplier} - a multiplier never flips a number's sign (zero is legal: an event handicap is x0).");
            if (declaringScope != null)
                ValidateEffectTargetReach(ctx, effect.target, site, declaringScope);
        }

        // Reference resolution for one effect address (12.12). The coordinate
        // triple IS the address, so modifier effects, upgrade effects, and the
        // formula-shaped career effects all validate through here - a career
        // effect carries the same target/currencyId/stat without being an Effect.
        private static void ValidateEffectCoordinates(ValidationContext ctx, string target, string currencyId, string stat, string site)
        {
            if (string.IsNullOrEmpty(target))
            {
                ctx.AddWarning(ValidationCheck.EffectTargetUnmatched, $"{site}: empty target matches nothing.");
            }
            else
            {
                var targetDefinition = ctx.Defs.Get<Definition>(target);
                if (targetDefinition is Economy.CurrencyDefinition)
                {
                    if (ctx.CurrencyHome(target) == null)
                        ctx.AddError(ValidationCheck.UnresolvedReference,
                            $"{site}: targets currency '{target}', which no scope declares.");
                }
                else if (targetDefinition is Economy.ProducerDefinition || targetDefinition is Economy.GeneratorDefinition)
                {
                    if (ctx.DeclaringScope(targetDefinition) == null)
                        ctx.AddError(ValidationCheck.UnresolvedReference,
                            $"{site}: targets '{target}', which no scope declares.");
                }
                else if (targetDefinition is Economy.BarDefinition || targetDefinition is Economy.BarGroupDefinition)
                {
                    // Exact-source reach for bars and groups lands with build
                    // step 5, when bars gain a scope attachment to measure
                    // against; today the id resolving is the whole check.
                }
                else if (targetDefinition != null)
                {
                    ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                        $"{site}: targets '{target}' ({targetDefinition.GetType().Name}), which is not an effect target kind (12.2: currency, producer, generator, bar, group, or tag).");
                }
                else if (!ctx.TagExists(target))
                {
                    ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                        $"{site}: target '{target}' matches no id and no tag.");
                }
                // A known tag resolves here; whether it matches a member is a
                // question about where the effect lives, judged in
                // ValidateEffectTargetReach.
            }

            // The currency coordinate is a currency id or a tag CARRIED BY a
            // currency; anything else narrows to nothing at all.
            if (!string.IsNullOrEmpty(currencyId) &&
                ctx.Defs.Get<Economy.CurrencyDefinition>(currencyId) == null &&
                !ctx.CurrencyTagExists(currencyId))
                ctx.AddError(ValidationCheck.UnresolvedReference,
                    $"{site}: narrows to '{currencyId}', which is no currency id and no tag any currency carries.");

            // An empty stat is the legal "every stat" address; a non-empty one no
            // system consumes narrows to nothing.
            if (!string.IsNullOrEmpty(stat))
                ctx.RequireConsumedStat(stat, $"{site} stat narrowing");
        }

        // An effect must sit where its target's outward walk visits it (12.12):
        // the currency's home or above for a currency total, the source's
        // declaring scope or above for an exact source, and a tag must match a
        // member of the subtree the effect lives in.
        private static void ValidateEffectTargetReach(ValidationContext ctx, string target, string site, ScopeDefinition fromScope)
        {
            if (string.IsNullOrEmpty(target))
                return;
            var targetDefinition = ctx.Defs.Get<Definition>(target);
            if (targetDefinition is Economy.CurrencyDefinition)
            {
                var home = ctx.CurrencyHome(target);
                if (home != null && fromScope != home && !ctx.IsProperAncestor(fromScope, home))
                    ctx.AddError(ValidationCheck.EffectReach,
                        $"{site}: targets currency '{target}' homed at '{home.Id}', but '{fromScope.Id}' is not the home or an ancestor of it - the home-to-root gather never visits this effect (12.12).");
            }
            else if (targetDefinition is Economy.ProducerDefinition || targetDefinition is Economy.GeneratorDefinition)
            {
                var home = ctx.DeclaringScope(targetDefinition);
                if (home != null && fromScope != home && !ctx.IsProperAncestor(fromScope, home))
                    ctx.AddError(ValidationCheck.EffectReach,
                        $"{site}: targets '{target}' declared at '{home.Id}', but '{fromScope.Id}' is not that scope or an ancestor of it - the source's outward walk never visits this effect (12.12).");
            }
            else if (targetDefinition == null && ctx.TagExists(target))
            {
                if (!ctx.TagHasMemberInSubtree(fromScope, target))
                    ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                        $"{site}: tag '{target}' matches no member within '{fromScope.Id}' (12.12).");
            }
        }

        // Cycles across nested action references are errors (12.12). Today's
        // edges are ExecuteRung invocations from rungs and trigger lists;
        // step 6 adds the event lifecycle operations to the same graph.
        private static void FinalizeCycles(ValidationContext ctx)
        {
            var adjacency = new Dictionary<string, List<RungEdgeRecord>>();
            foreach (var edge in ctx.RungEdges)
            {
                if (!adjacency.TryGetValue(edge.FromKey, out var bucket))
                    adjacency[edge.FromKey] = bucket = new List<RungEdgeRecord>();
                bucket.Add(edge);
            }

            var state = new Dictionary<string, int>(); // 0 unvisited, 1 on stack, 2 done
            var stack = new List<string>();
            void Visit(string node)
            {
                state[node] = 1;
                stack.Add(node);
                if (adjacency.TryGetValue(node, out var edges))
                {
                    foreach (var edge in edges)
                    {
                        state.TryGetValue(edge.ToKey, out var toState);
                        if (toState == 1)
                        {
                            var start = stack.IndexOf(edge.ToKey);
                            var path = string.Join(" -> ", stack.Skip(start).Append(edge.ToKey));
                            ctx.AddError(ValidationCheck.ReferenceCycle,
                                $"{edge.Site}: rung invocation cycle: {path} (12.12).");
                        }
                        else if (toState == 0)
                        {
                            Visit(edge.ToKey);
                        }
                    }
                }
                stack.RemoveAt(stack.Count - 1);
                state[node] = 2;
            }

            foreach (var node in adjacency.Keys.ToList())
            {
                state.TryGetValue(node, out var nodeState);
                if (nodeState == 0)
                    Visit(node);
            }
        }
    }
}
