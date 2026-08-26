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
        ScopePlacement,         // root -> chapters -> tiers: a scope's authored kind fits where it sits
        DuplicateId,            // Definition ids and flags share one id space per chain
        DuplicateHome,          // a currency, flag, or trigger declared by two scopes
        TagIdCollision,         // a tag may not collide with any id
        UnresolvedReference,    // a referenced id resolves to nothing
        NullEntry,              // a null slot in an authored list, or a required operand
        InertOperand,           // an operand authored where the shape's own behavior never reads it
        ScopeReach,             // ResetScope / RestartScope / ExecuteRung / modifier-grant reach rules
        ChainReach,             // ordinary reads and writes address only the acting chain
        EffectReach,            // an effect sits where its target's outward walk visits it
        EffectTargetUnmatched,  // an effect target matching nothing reachable (warn)
        FlagNoSetter,           // a declared flag nothing sets (warn)
        SetThenWiped,           // a list sets a fact, then resets the scope declaring it
        FormulaReadsCleared,    // a formula-driven grant after a reset clearing its inputs (warn)
        StrandedValue,          // a rung resets a subtree holding a payout rung it never invokes (warn)
        StrandedReward,         // a rung's reset closure holds an event host its own gate does not guard (warn)
        BalanceGoalWithoutReset,// an event balance goal whose onEntry never resets the host (warn)
        ReferenceCycle,         // cycles across nested action references
        RemoveWithoutGrant,     // RemoveModifier naming a stack nothing grants there (warn)
        UnconsumedStat,         // a stat outside its site's vocabulary (warn) - the typo guard a named vocabulary lacks
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
        public readonly Economy.ModifierDefinition Modifier;
        public readonly ScopeDefinition Target;

        public ModifierGrantRecord(Economy.ModifierDefinition modifier, ScopeDefinition target)
        {
            Modifier = modifier;
            Target = target;
        }
    }

    internal readonly struct ModifierRemoveRecord
    {
        public readonly Economy.ModifierDefinition Modifier;
        public readonly ScopeDefinition Target;
        public readonly string Site;

        public ModifierRemoveRecord(Economy.ModifierDefinition modifier, ScopeDefinition target, string site)
        {
            Modifier = modifier;
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
        public ScopeDefinition RootScope { get; }
        public ScopeDefinition ActingScope { get; private set; }

        private readonly ValidationReport report;
        private readonly Dictionary<ScopeDefinition, ScopeDefinition> parentByScope;
        private readonly Dictionary<Definition, ScopeDefinition> declaringScopeByDefinition;
        private readonly List<ScopeDefinition> treeScopes;
        private readonly List<Definition> allDefinitions;

        private string site;
        private string containerKey;
        private int actionIndex = -1;

        // True only while validating a site the runtime can evaluate under the
        // claim's idle-accumulation context: a modifier's appliesWhen, and a
        // RATE entry's condition on a source some chapter's subtree contains.
        // Everywhere else an IdleAccumulation condition reads a circumstance
        // that is never set (12.5).
        public bool IdleCircumstancePossible { get; internal set; }

        internal List<FlagSetterRecord> FlagSetters { get; } = new();
        internal List<ModifierGrantRecord> ModifierGrants { get; } = new();
        internal List<ModifierRemoveRecord> ModifierRemoves { get; } = new();
        internal List<RungEdgeRecord> RungEdges { get; } = new();
        internal List<FactWriteRecord> FactWrites { get; } = new();
        internal List<FormulaReadRecord> FormulaReads { get; } = new();
        internal List<ResetRecord> Resets { get; } = new();

        internal ValidationContext(
            ValidationReport report,
            ScopeDefinition rootScope,
            Dictionary<ScopeDefinition, ScopeDefinition> parentByScope,
            Dictionary<Definition, ScopeDefinition> declaringScopeByDefinition,
            List<ScopeDefinition> treeScopes,
            List<Definition> allDefinitions)
        {
            this.report = report;
            RootScope = rootScope;
            this.parentByScope = parentByScope;
            this.declaringScopeByDefinition = declaringScopeByDefinition;
            this.treeScopes = treeScopes;
            this.allDefinitions = allDefinitions;
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

        // Every targetable member of a subtree that answers to a name, by id or
        // by tag. An effect's address is a FILTER the gather applies to
        // candidates it has already found (Producer.Matches), never a lookup -
        // so the question is never "which asset is this id". A tag names many
        // owners on purpose, and ids repeat across chains, so this is a LIST:
        // the address is inert only when NONE of them can satisfy the rest of
        // the tuple.
        public List<Definition> MatchTargets(ScopeDefinition top, string name)
        {
            var matches = new List<Definition>();
            if (top == null || string.IsNullOrEmpty(name))
                return matches;
            foreach (var scope in ScopesInSubtree(top))
                foreach (var candidate in ContentValidator.Targetables(scope))
                    if (candidate.Id == name || candidate.HasTag(name))
                        matches.Add(candidate);
            return matches;
        }

        // A definition of a kind no multiplier resolves against, answering to
        // the name by id. Asked only once nothing targetable answers, so a kind
        // mistake reads as one rather than as a typo.
        public Definition MatchOtherKind(ScopeDefinition top, string name)
        {
            if (top == null || string.IsNullOrEmpty(name))
                return null;
            foreach (var scope in ScopesInSubtree(top))
            {
                if (scope.Id == name)
                    return scope;
                foreach (var declared in ContentValidator.DeclaredBy(scope))
                    if (declared.Id == name)
                        return declared;
            }
            return null;
        }

        // The currency coordinate narrows which currency's contribution an
        // effect applies to, and the gather only ever offers currencies an
        // outward walk from somewhere in the effect's subtree can see - so that
        // subtree plus the effect's own chain is the whole legal space.
        public bool MatchNarrowingCurrency(ScopeDefinition top, string name)
        {
            if (top == null || string.IsNullOrEmpty(name))
                return false;
            foreach (var scope in treeScopes)
            {
                if (!InSubtree(top, scope) && !InSubtree(scope, top))
                    continue;
                foreach (var currency in scope.declaredCurrencies)
                    if (currency != null && (currency.Id == name || currency.HasTag(name)))
                        return true;
            }
            return false;
        }

        public ScopeDefinition FindScope(ScopeDefinition scope) =>
            scope != null && treeScopes.Contains(scope) ? scope : null;

        // The scope whose declaration list holds this definition - declaration is
        // ownership (12.3), so this is the home of every fact it creates. Null
        // when no scope declares it.
        public ScopeDefinition DeclaringScope(Definition definition) =>
            definition != null && declaringScopeByDefinition.TryGetValue(definition, out var scope) ? scope : null;

        // The runtime flag walk (12.3): outward from the acting scope, stopping
        // at the first scope that declares the name. Two chains may each declare
        // one of their own, so the answer depends on where you ask - which is
        // why this takes the asking scope instead of consulting a map.
        public ScopeDefinition FlagHomeFrom(ScopeDefinition from, string flagId)
        {
            if (string.IsNullOrEmpty(flagId))
                return null;
            for (var node = from; node != null; node = Parent(node))
                if (node.declaredFlags.Contains(flagId))
                    return node;
            return null;
        }

        public ScopeDefinition FlagHome(string flagId) => FlagHomeFrom(ActingScope, flagId);

        // Diagnostics only: when the walk above comes back empty, this says
        // whether the name was a typo or a misplacement. It never decides a
        // verdict - an off-chain declaration is unreachable either way.
        public ScopeDefinition AnyScopeDeclaringFlag(string flagId)
        {
            if (string.IsNullOrEmpty(flagId))
                return null;
            foreach (var scope in treeScopes)
                if (scope.declaredFlags.Contains(flagId))
                    return scope;
            return null;
        }

        public ScopeDefinition Parent(ScopeDefinition scope) =>
            scope != null && parentByScope.TryGetValue(scope, out var parent) ? parent : null;

        // True when node is top or sits anywhere inside top's subtree. The graph
        // is known to be a tree before a context exists - Validate refuses a
        // second parent or a parented root and returns - so this walk ends.
        public bool InSubtree(ScopeDefinition top, ScopeDefinition node)
        {
            if (top == null || node == null)
                return false;
            for (var current = node; current != null; current = Parent(current))
                if (current == top)
                    return true;
            return false;
        }

        // The runtime read/write walk: the acting scope or an ancestor of it.
        public bool OnActingChain(ScopeDefinition scope) => InSubtree(scope, ActingScope);

        public bool InActingSubtree(ScopeDefinition scope) => InSubtree(ActingScope, scope);

        // The shared rule for every ordinary currency read and write (12.12):
        // the id resolves, the currency has a home, and the home sits on the
        // acting chain. Returns the home when usable; reports and returns null
        // otherwise.
        // An ordinary read or write addresses only the acting chain (12.12).
        // The reference itself cannot dangle, so what is left to check is
        // placement: some scope on the chain must declare it.
        public ScopeDefinition RequireOnChain(Definition definition, string use)
        {
            if (definition == null)
            {
                AddError(ValidationCheck.NullEntry, $"{use} names nothing.");
                return null;
            }
            var home = DeclaringScope(definition);
            if (home == null)
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} references '{definition.Id}', which no scope declares.");
                return null;
            }
            if (!OnActingChain(home))
            {
                AddError(ValidationCheck.ChainReach, $"{use} addresses '{definition.Id}' declared at '{home.Id}', which is not on the chain from '{ActingScope.Id}' (12.12).");
                return null;
            }
            return home;
        }

        // A fact written at TARGET whose definition is resolved outward from
        // there: the declaring scope must be the target or an ancestor of it,
        // or the write lands somewhere the read can never explain.
        public ScopeDefinition RequireDeclaredFor(ScopeDefinition target, Definition definition, string use)
        {
            var home = DeclaringScope(definition);
            if (home == null)
            {
                AddError(ValidationCheck.UnresolvedReference, $"{use} references '{definition.Id}', which no scope declares.");
                return null;
            }
            if (!InSubtree(home, target))
            {
                AddError(ValidationCheck.ScopeReach, $"{use} writes '{definition.Id}' at '{target.Id}', but it is declared at '{home.Id}', which '{target.Id}' cannot reach outward (12.12).");
                return null;
            }
            return home;
        }

        // The same rule for a scope-attached definition's fact (12.12): the id
        // resolves, some scope declares it, and that scope sits on the acting
        // chain - the runtime walk reaches nowhere else, so a cross-tree read is
        // a load-time error rather than a silent runtime miss.
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

        // A produced stat means something because a contribution sums into it
        // (12.2); the effect-address stats are read through GetMultiplier
        // alone, so a produces entry naming one is a contribution nothing ever
        // sums. The warn is what recovers the typo protection an enum would
        // have given a named vocabulary.
        public void RequireProducedStat(string stat, string what)
        {
            if (Economy.Stat.IsProduced(stat))
                return;
            AddWarning(ValidationCheck.UnconsumedStat, string.IsNullOrEmpty(stat)
                ? $"{what} names no stat - no contribution sums it."
                : $"{what} names stat '{stat}', which no contribution sums ({Economy.Stat.ProducedNames}).");
        }

        // ---- ledgers ----

        public void RecordFlagSetter(string flagId) =>
            FlagSetters.Add(new FlagSetterRecord(flagId, ActingScope));

        public void RecordModifierGrant(Economy.ModifierDefinition modifier, ScopeDefinition target) =>
            ModifierGrants.Add(new ModifierGrantRecord(modifier, target));

        public void RecordModifierRemove(Economy.ModifierDefinition modifier, ScopeDefinition target) =>
            ModifierRemoves.Add(new ModifierRemoveRecord(modifier, target, site));

        public void RecordRungInvocation(ScopeDefinition target) =>
            RungEdges.Add(new RungEdgeRecord(containerKey, RungKey(target.Id), actionIndex, site));

        public void RecordFactWrite(string description, ScopeDefinition home) =>
            FactWrites.Add(new FactWriteRecord(containerKey, actionIndex, description, home, site));

        public void RecordFormulaRead(string currencyId, ScopeDefinition home) =>
            FormulaReads.Add(new FormulaReadRecord(containerKey, actionIndex, currencyId, home, site));

        public void RecordReset(ScopeDefinition target) =>
            Resets.Add(new ResetRecord(containerKey, actionIndex, target, site));

        // ---- tag membership for effect targets ----

        // Whether any source can pay this currency with this stat. An entry's
        // currency must resolve on its source's chain (12.12), so every possible
        // payer sits at or below the currency's home. The entry's CONDITION does
        // not matter: whether a gate ever opens is not a load-time question.
        public bool SomeSourcePays(Economy.CurrencyDefinition currency, string stat)
        {
            var home = DeclaringScope(currency);
            if (home == null)
                return true;   // an undeclared currency is reported where it is referenced
            foreach (var scope in ScopesInSubtree(home))
            {
                foreach (var producer in scope.producers)
                    if (producer != null && PaysWith(producer.produces, currency, stat))
                        return true;
                foreach (var generator in scope.generators)
                    if (generator != null && PaysWith(generator.produces, currency, stat))
                        return true;
            }
            return false;
        }

        // The entry's currency is compared by REFERENCE: a same-named currency
        // on another chain is a different currency, and it pays nothing here.
        private static bool PaysWith(List<Economy.ProducesEntry> entries, Economy.CurrencyDefinition currency, string stat)
        {
            foreach (var entry in entries)
                if (entry != null && entry.currency == currency && entry.stat == stat)
                    return true;
            return false;
        }

        // Whether the usage scope's subtree homes a currency the narrowing
        // selects (any currency when the narrowing is empty) that some source
        // pays with this stat. This is a wildcard's whole reachable set: it is
        // collected only on home-to-root walks, only subtree homes put this
        // scope on such a walk, and the walk itself only runs for a
        // (currency, stat) pair some contribution pays - GetRate returns
        // before its currency stage on a zero sum, and FireProducer only
        // stages the currencies its entries name.
        public bool SubtreeHomesMatchingCurrency(ScopeDefinition top, string currencyId, string stat)
        {
            foreach (var scope in ScopesInSubtree(top))
                foreach (var currency in scope.declaredCurrencies)
                {
                    if (currency == null)
                        continue;
                    if (!string.IsNullOrEmpty(currencyId) && currency.Id != currencyId && !currency.HasTag(currencyId))
                        continue;
                    if (SomeSourcePays(currency, stat))
                        return true;
                }
            return false;
        }

        // Whether a name is vocabulary anywhere in the content. Deliberately
        // unscoped, and deliberately including the kinds no multiplier resolves
        // against: the scoped question is asked first, by MatchTarget, and this
        // one only separates "you typed a word nothing carries" from "that tag
        // exists, just not where this effect can see it".
        public bool TagExists(string tag) => allDefinitions.Any(d => d.HasTag(tag));

        internal IReadOnlyList<ScopeDefinition> TreeScopes => treeScopes;

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
        // Everything a scope declares, in one place: the id space, and what a
        // string coordinate may name. The scope itself is not in here - a scope
        // is the container, and its own id lives in a tree-wide space.
        internal static IEnumerable<Definition> DeclaredBy(ScopeDefinition scope)
        {
            foreach (var currency in scope.declaredCurrencies) if (currency != null) yield return currency;
            foreach (var trigger in scope.triggers) if (trigger != null) yield return trigger;
            foreach (var producer in scope.producers) if (producer != null) yield return producer;
            foreach (var modifier in scope.modifiers) if (modifier != null) yield return modifier;
            foreach (var generator in scope.generators) if (generator != null) yield return generator;
            foreach (var upgrade in scope.upgrades) if (upgrade != null) yield return upgrade;
            if (scope is InteriorDefinition interior)
                foreach (var evt in interior.events) if (evt != null) yield return evt;
            foreach (var group in scope.barGroups)
            {
                if (group == null) continue;
                yield return group;
                foreach (var bar in group.bars) if (bar != null) yield return bar;
            }
        }

        // The kinds a multiplier can resolve against (12.2): the currencies a
        // scope homes plus the sources and bars it declares. Scope, trigger,
        // upgrade, and modifier tags are vocabulary, not targets - no effect
        // ever multiplies one of those, and neither is a bar GROUP: its only
        // members are a set of bars and an int cap, so there is no number for a
        // gather to reach. Buffing a set of bars is a tag they share (12.7).
        internal static IEnumerable<Definition> Targetables(ScopeDefinition scope)
        {
            foreach (var currency in scope.declaredCurrencies) if (currency != null) yield return currency;
            foreach (var producer in scope.producers) if (producer != null) yield return producer;
            foreach (var generator in scope.generators) if (generator != null) yield return generator;
            foreach (var group in scope.barGroups)
            {
                if (group == null) continue;
                foreach (var bar in group.bars) if (bar != null) yield return bar;
            }
        }

        // The content IS the tree: every definition the game can reach hangs
        // off the root scope by direct reference, so validation walks it rather
        // than auditing a catalogue (design doc 12.12).
        public static ValidationReport Validate(ScopeDefinition rootDefinition)
        {
            var report = new ValidationReport();

            // ---- scope graph: the root and everything its children reach ----
            var allScopes = new List<ScopeDefinition>();
            var scopeSeen = new HashSet<ScopeDefinition>();
            if (rootDefinition != null && scopeSeen.Add(rootDefinition))
                allScopes.Add(rootDefinition);
            for (var i = 0; i < allScopes.Count; i++)
                foreach (var child in allScopes[i].children)
                    if (child != null && scopeSeen.Add(child))
                        allScopes.Add(child);

            var parentByScope = new Dictionary<ScopeDefinition, ScopeDefinition>();
            var graphFault = false;
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
                        graphFault = true;
                        continue;
                    }
                    parentByScope[child] = scope;
                }
            }

            // The shape of the graph is a precondition for every check below it:
            // a chain walk and a subtree walk both assume a tree, and an
            // authored back-edge would make them recurse forever rather than
            // report. Every cycle shows up as one of these two shapes - a scope
            // gains a second parent, or the root gains its first - so refusing
            // both is what lets the walks below run unguarded.
            if (graphFault)
                return report;

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

            // ---- kind placement: root, then chapters, then tiers all the way down ----
            // A scope's authored class decides its state class and its payload,
            // so a kind in the wrong place builds a node whose payload and
            // parentage disagree with where it sits - a RootDefinition given a
            // parent, a TierScopeState where Chapter() expects a chapter. Both
            // are content faults, refused before any state is built.
            if (root is not RootDefinition)
                report.Add(ValidationSeverity.Error, ValidationCheck.ScopePlacement,
                    $"the tree's root scope '{root.Id}' is a {root.GetType().Name}; a root scope is a RootDefinition (12.3).");
            foreach (var scope in treeScopes)
            {
                foreach (var child in scope.children)
                {
                    if (child == null)
                        continue;
                    if (scope == root)
                    {
                        if (child is not ChapterDefinition)
                            report.Add(ValidationSeverity.Error, ValidationCheck.ScopePlacement,
                                $"scope '{child.Id}' is a {child.GetType().Name} directly under the root; root's children are chapters (12.3).");
                    }
                    else if (child is not TierDefinition)
                    {
                        report.Add(ValidationSeverity.Error, ValidationCheck.ScopePlacement,
                            $"scope '{child.Id}' is a {child.GetType().Name} under '{scope.Id}'; everything below a chapter is a tier (12.3).");
                    }
                }
            }

            // ---- id space: every definition the tree declares, plus flags ----
            var allDefinitions = new List<Definition>();
            var definitionSeen = new HashSet<Definition>();
            // Declaration is the only way content enters the game, so the
            // declaration lists ARE the id space - there is nothing else to
            // enumerate.
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
                }
            }

            foreach (var scope in allScopes)
            {
                if (definitionSeen.Add(scope))
                    allDefinitions.Add(scope);
                CollectDeclared(scope, scope.declaredCurrencies, "declaredCurrencies");
                CollectDeclared(scope, scope.triggers, "triggers");
                CollectDeclared(scope, scope.producers, "producers");
                CollectDeclared(scope, scope.modifiers, "modifiers");
                CollectDeclared(scope, scope.barGroups, "barGroups");
                foreach (var group in scope.barGroups)
                    if (group != null)
                        CollectDeclared(scope, group.bars, $"barGroup '{group.Id}' bars");
                CollectDeclared(scope, scope.generators, "generators");
                CollectDeclared(scope, scope.upgrades, "upgrades");
                if (scope is InteriorDefinition interiorScope)
                    CollectDeclared(scope, interiorScope.events, "events");
            }

            // ---- ids are unique along a CHAIN, not tree-wide ----
            // A read walks outward and stops at the first scope that declares
            // what it names, so two sibling chapters may each declare a 'cash'
            // - their tiers can never see each other's. What must not repeat is
            // an id VISIBLE from one scope: itself plus everything it inherits.
            var scopeIds = new Dictionary<string, ScopeDefinition>();
            foreach (var scope in allScopes)
            {
                if (string.IsNullOrEmpty(scope.Id))
                {
                    report.Add(ValidationSeverity.Error, ValidationCheck.NullEntry, "a scope asset has a null or empty id.");
                    continue;
                }
                // Scope ids stay unique tree-wide: subtree searches and root's
                // own maps (the roadie allocation, the foreground chapter) key
                // by them.
                if (scopeIds.TryGetValue(scope.Id, out var twin) && twin != scope)
                    report.Add(ValidationSeverity.Error, ValidationCheck.DuplicateId,
                        $"two scopes share the id '{scope.Id}'.");
                else
                    scopeIds[scope.Id] = scope;
            }

            void CheckChain(ScopeDefinition scope, Dictionary<string, string> inherited, Dictionary<string, string> inheritedTags)
            {
                var visible = new Dictionary<string, string>(inherited);
                var visibleTags = new Dictionary<string, string>(inheritedTags);
                var claimedHere = new List<string>();
                void Claim(string id, string desc, bool isFlag)
                {
                    if (string.IsNullOrEmpty(id))
                        return;
                    if (visible.TryGetValue(id, out var existing))
                        report.Add(ValidationSeverity.Error,
                            isFlag ? ValidationCheck.DuplicateHome : ValidationCheck.DuplicateId,
                            $"'{id}' is declared twice on the chain at '{scope.Id}': {existing} and {desc}.");
                    else
                    {
                        visible[id] = desc;
                        claimedHere.Add(id);
                    }
                }

                foreach (var definition in DeclaredBy(scope))
                {
                    if (string.IsNullOrEmpty(definition.Id))
                        report.Add(ValidationSeverity.Error, ValidationCheck.NullEntry,
                            $"a {definition.GetType().Name} declared at '{scope.Id}' has a null or empty id.");
                    Claim(definition.Id, $"{definition.GetType().Name} at '{scope.Id}'", false);
                }
                for (var i = 0; i < scope.declaredFlags.Count; i++)
                {
                    var flag = scope.declaredFlags[i];
                    if (string.IsNullOrEmpty(flag))
                        report.Add(ValidationSeverity.Error, ValidationCheck.NullEntry,
                            $"scope '{scope.Id}' declaredFlags[{i}] is empty.");
                    else
                        Claim(flag, $"flag at '{scope.Id}'", true);
                }

                // A tag is vocabulary; colliding with an id VISIBLE here would
                // make an effect target ambiguous exactly where it resolves,
                // since the filter tries the same string as an id and as a tag.
                // Both directions collide, so tags travel down the chain the way
                // ids do: a tag declared here meets the ids from above, and an id
                // declared here meets the tags from above.
                foreach (var definition in DeclaredBy(scope))
                    foreach (var tag in definition.Tags)
                    {
                        if (string.IsNullOrEmpty(tag))
                            continue;
                        if (visible.ContainsKey(tag))
                            report.Add(ValidationSeverity.Error, ValidationCheck.TagIdCollision,
                                $"tag '{tag}' (on {definition.GetType().Name} '{definition.Id}') collides with an id visible at '{scope.Id}'.");
                        else if (!visibleTags.ContainsKey(tag))
                            visibleTags[tag] = $"{definition.GetType().Name} '{definition.Id}' at '{scope.Id}'";
                    }

                foreach (var id in claimedHere)
                    if (inheritedTags.TryGetValue(id, out var carrier))
                        report.Add(ValidationSeverity.Error, ValidationCheck.TagIdCollision,
                            $"id '{id}' declared at '{scope.Id}' collides with tag '{id}', carried by {carrier} and visible here.");

                foreach (var child in scope.children)
                    if (child != null)
                        CheckChain(child, visible, visibleTags);
            }
            if (rootDefinition != null)
                CheckChain(rootDefinition, new Dictionary<string, string>(), new Dictionary<string, string>());

            // ---- homes: declaration is ownership, one asset to one scope ----
            // Keyed by ASSET and never by id: two chains may each declare their
            // own 'cash', and a read walking outward from either one can only
            // ever reach its own. Flags need no pass here - a flag has no asset
            // to declare twice, so the chain check above is the whole rule.
            var declaringScopeByDefinition = new Dictionary<Definition, ScopeDefinition>();
            void RecordHome<T>(ScopeDefinition scope, List<T> list, string rule = "declaration is ownership") where T : Definition
            {
                foreach (var definition in list)
                {
                    if (definition == null)
                        continue; // flagged during id collection
                    if (declaringScopeByDefinition.TryGetValue(definition, out var existing))
                        report.Add(ValidationSeverity.Error, ValidationCheck.DuplicateHome,
                            $"{definition.GetType().Name} '{definition.Id}' is declared by both '{existing.Id}' and '{scope.Id}' - {rule}.");
                    else
                        declaringScopeByDefinition[definition] = scope;
                }
            }
            foreach (var scope in treeScopes)
            {
                RecordHome(scope, scope.declaredCurrencies, "a currency has one home");
                RecordHome(scope, scope.triggers, "a trigger has one home");
                RecordHome(scope, scope.modifiers);
                RecordHome(scope, scope.barGroups);
                foreach (var group in scope.barGroups)
                    if (group != null)
                        RecordHome(scope, group.bars);
                RecordHome(scope, scope.producers);
                RecordHome(scope, scope.generators);
                RecordHome(scope, scope.upgrades);
                if (scope is InteriorDefinition interiorScope)
                    RecordHome(scope, interiorScope.events, "an event has one home");
            }

            var ctx = new ValidationContext(report, root, parentByScope,
                declaringScopeByDefinition, treeScopes, allDefinitions);

            // ---- container walk: every rung and trigger, in tree order ----
            foreach (var scope in treeScopes)
            {
                // Only an interior scope has a rung at all - the root has no such
                // field, so "no rung on the root" needs no check here.
                if (scope is InteriorDefinition interior && interior.rung != null)
                {
                    ctx.EnterContainer(scope, ValidationContext.RungKey(scope.Id));
                    ctx.SetSite($"scope '{scope.Id}' rung offer");
                    // A gate may not be null (12.12): the runtime refuses one
                    // fail-closed either way, and Always is how an author says
                    // the gate is open.
                    if (interior.rung.offerCondition == null)
                        ctx.AddError(ValidationCheck.NullEntry,
                            "offerCondition is unauthored - a gate may not be null, and Always is how an author says the gate is open (12.12).");
                    else
                        interior.rung.offerCondition.Validate(ctx);
                    ValidateActionList(ctx, interior.rung.actions, $"scope '{scope.Id}' rung");
                }

                foreach (var trigger in scope.triggers)
                {
                    if (trigger == null)
                        continue;
                    ctx.EnterContainer(scope, "trigger:" + trigger.Id);
                    ctx.SetSite($"trigger '{trigger.Id}' condition");
                    // Same gate rule: the sweep treats a null condition as
                    // closed and never dereferences it, but load refuses it.
                    if (trigger.condition == null)
                        ctx.AddError(ValidationCheck.NullEntry,
                            "condition is unauthored - a gate may not be null, and Always is how an author says the gate is open (12.12).");
                    else
                        trigger.condition.Validate(ctx);
                    ValidateActionList(ctx, trigger.actions, $"trigger '{trigger.Id}'");
                }

                if (scope is InteriorDefinition eventHost)
                {
                    foreach (var evt in eventHost.events)
                    {
                        if (evt == null)
                            continue; // flagged during id collection
                        ValidateEvent(ctx, evt, scope);
                    }
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

                foreach (var group in scope.barGroups)
                {
                    if (group == null)
                        continue;
                    ctx.EnterContainer(scope, "barGroup:" + group.Id);
                    ValidateBarGroup(ctx, group);
                    foreach (var bar in group.bars)
                    {
                        if (bar == null)
                            continue;
                        // Each bar is its own container: its completion list
                        // carries the same set-then-wiped, flag-setter and cycle
                        // bookkeeping every other action list does.
                        ctx.EnterContainer(scope, "bar:" + bar.Id);
                        ValidateBar(ctx, bar, scope);
                    }
                }

                foreach (var upgrade in scope.upgrades)
                {
                    if (upgrade == null)
                        continue;
                    ctx.EnterContainer(scope, "upgrade:" + upgrade.Id);
                    ValidateUpgrade(ctx, upgrade, scope);
                }

                // Usage, not declaration: each entry must reference a modifier
                // declared on the chain reachable from the usage scope - the
                // same reach a grant gets - and appear ONCE, because membership
                // is one implicit application and the read iterates the list.
                // The effects and appliesWhen are judged per usage site with
                // the granted ones, below.
                var permanentSeen = new HashSet<Economy.ModifierDefinition>();
                for (var i = 0; i < scope.permanentModifiers.Count; i++)
                {
                    var permanent = scope.permanentModifiers[i];
                    ctx.EnterContainer(scope, "permanent:" + scope.Id);
                    ctx.SetSite($"scope '{scope.Id}' permanentModifiers[{i}]");
                    if (permanent == null)
                    {
                        ctx.AddError(ValidationCheck.NullEntry, "null permanent modifier entry.");
                        continue;
                    }
                    if (!permanentSeen.Add(permanent))
                    {
                        ctx.AddError(ValidationCheck.DuplicateId,
                            $"'{permanent.Id}' is listed twice - permanent membership is one implicit application, and a second entry would double-apply outside the stacking vocabulary (12.5).");
                        continue;
                    }
                    ctx.RequireOnChain(permanent, "a permanent modifier entry");
                }
            }

            // ---- cross-container checks over the ledgers ----
            ctx.ClearSite();
            FinalizeListChecks(ctx);
            FinalizeStrandedValue(ctx);
            FinalizeStrandedReward(ctx);
            FinalizeFlagChecks(ctx);
            FinalizeModifierChecks(ctx);
            FinalizeCycles(ctx);

            return report;
        }

        // A produces entry addresses its currency on the declaring chain, its
        // condition is judged there, and its stat must be one a contribution
        // sums into. A null condition is legal authoring: the condition is
        // optional, and an entry is not a gate (12.2).
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
                ctx.RequireOnChain(entry.currency, "a produces entry");
                ctx.RequireProducedStat(entry.stat, "a produces entry");
                if (entry.value < BigNumber.Zero)
                    ctx.AddError(ValidationCheck.NumericRange,
                        $"value is {entry.value} - a contribution never subtracts.");
                // Only the idle claim's gather carries the circumstance, and it
                // asks a chapter subtree for RATE alone - a yield condition is
                // read only by live FireProducer calls, and a root-declared
                // source sits outside every chapter's walk - so "this line pays
                // only while idle" is coherent authoring exactly there (12.5).
                ctx.IdleCircumstancePossible = entry.stat == Economy.Stat.Rate
                    && !(ctx.ActingScope is RootDefinition);
                entry.condition?.Validate(ctx);
                ctx.IdleCircumstancePossible = false;
            }
        }

        // An event's own shape plus its two ledger containers (12.12): onEntry
        // stands alone; rewards and onEnd are ONE container in that order,
        // because they run back to back in a single transaction - a flag set in
        // rewards and wiped by onEnd's reset is exactly what set-then-wiped
        // exists to catch. Gates, goal, and handicaps are judged in the HOST's
        // scope (12.4), which the container's acting scope already is.
        private static void ValidateEvent(ValidationContext ctx, Events.EventDefinition evt, ScopeDefinition scope)
        {
            var site = $"event '{evt.Id}'";
            ctx.EnterContainer(scope, "event:" + evt.Id + " entry");
            ctx.SetSite(site);
            if (evt.availableWhen == null)
                ctx.AddError(ValidationCheck.NullEntry,
                    "availableWhen is unauthored - a gate may not be null, and Always is how an author says the gate is open (12.12).");
            else
            {
                ctx.SetSite($"{site} availableWhen");
                evt.availableWhen.Validate(ctx);
            }

            ctx.SetSite($"{site} goal");
            if (evt.goal == null)
                ctx.AddWarning(ValidationCheck.NullEntry,
                    "goal is unauthored - the event is dismiss-only and can never reward.");
            else
                evt.goal.Validate(ctx);

            ctx.SetSite(site);
            if (ctx.RequireFiniteDouble(evt.timeLimitSeconds, $"{site} timeLimitSeconds") && evt.timeLimitSeconds < 0)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"timeLimitSeconds is {evt.timeLimitSeconds} - zero is untimed, and a negative limit means nothing.");

            for (var i = 0; i < evt.handicaps.Count; i++)
                ValidateEffect(ctx, evt.handicaps[i], $"{site} handicaps[{i}]", scope);

            // A live-balance goal over a host onEntry never resets is met by
            // whatever the player already holds when the event starts (12.12).
            if (evt.goal != null && ReadsABalance(evt.goal) && !ResetsScope(evt.onEntry, scope))
            {
                ctx.SetSite($"{site} goal");
                ctx.AddWarning(ValidationCheck.BalanceGoalWithoutReset,
                    "a balance goal on an event whose onEntry never resets the host is met by whatever the player already holds.");
            }

            ValidateActionList(ctx, evt.onEntry, $"{site} onEntry");

            ctx.EnterContainer(scope, "event:" + evt.Id + " end");
            var ending = new List<GameAction>(evt.rewards.Count + evt.onEnd.Count);
            ending.AddRange(evt.rewards);
            ending.AddRange(evt.onEnd);
            ValidateActionList(ctx, ending, $"{site} rewards+onEnd");
        }

        // Whether a condition reads a live balance anywhere in its tree.
        private static bool ReadsABalance(Condition condition) => condition switch
        {
            CurrencyAtLeast => true,
            All all => all.conditions.Any(c => c != null && ReadsABalance(c)),
            Any any => any.conditions.Any(c => c != null && ReadsABalance(c)),
            Not not => not.condition != null && ReadsABalance(not.condition),
            _ => false
        };

        private static bool ResetsScope(List<GameAction> actions, ScopeDefinition scope)
        {
            foreach (var action in actions)
            {
                if (action is ResetScope reset && reset.scope == scope)
                    return true;
                if (action is RestartScope restart && restart.scope == scope)
                    return true;
            }
            return false;
        }

        private static void ValidateGenerator(ValidationContext ctx, Economy.GeneratorDefinition generator)
        {
            var site = $"generator '{generator.Id}'";
            ctx.SetSite(site);
            if (generator.availableWhen == null)
                ctx.AddError(ValidationCheck.NullEntry,
                    "availableWhen is unauthored - a gate may not be null, and Always is how an author says the gate is open (12.12).");
            else
            {
                ctx.SetSite($"{site} availableWhen");
                generator.availableWhen.Validate(ctx);
            }

            ctx.SetSite(site);
            ctx.RequireOnChain(generator.costCurrency, "generator cost");
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
                ctx.AddError(ValidationCheck.NullEntry,
                    "gate is unauthored - a gate may not be null, and Always is how an author says the gate is open (12.12).");
            else
            {
                ctx.SetSite($"{site} gate");
                upgrade.gate.Validate(ctx);
            }

            ctx.SetSite(site);
            ctx.RequireOnChain(upgrade.costCurrency, "upgrade cost");
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

        // A group holds bars and caps how many run at once. That is the whole
        // contract - what a bar drinks and how fast is the bar's own business
        // (12.7).
        private static void ValidateBarGroup(ValidationContext ctx, Economy.BarGroupDefinition group)
        {
            var site = $"bar group '{group.Id}'";
            ctx.SetSite(site);
            if (group.maxActive < 1)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"maxActive is {group.maxActive} - a group nothing can be selected in.");

            for (var i = 0; i < group.bars.Count; i++)
                if (group.bars[i] == null)
                {
                    ctx.SetSite($"{site} bars[{i}]");
                    ctx.AddError(ValidationCheck.NullEntry, "null bar entry.");
                }
        }

        private static void ValidateBar(ValidationContext ctx, Economy.BarDefinition bar, ScopeDefinition scope)
        {
            var site = $"bar '{bar.Id}'";
            ctx.SetSite(site);

            // A null fill currency is legal: that bar fills from time alone. A
            // named one gets the reach check every other currency operand gets.
            if (bar.fillCurrency != null)
                ctx.RequireOnChain(bar.fillCurrency, $"{site} fill currency");

            if (bar.fillAmount <= BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"fillAmount is {bar.fillAmount} - a nonpositive threshold is an unbounded settlement loop.");
            if (bar.fillRate <= BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"fillRate is {bar.fillRate} - a bar no multiplier can ever move.");

            // A null gate on a bar is OPEN (12.7), the opposite of a purchase
            // gate: fail-closed binds entry points that create value out of a
            // spend, and a bar's availability is a selection filter. So an
            // unauthored one is not reported at all.
            if (bar.availableWhen != null)
            {
                ctx.SetSite($"{site} availableWhen");
                bar.availableWhen.Validate(ctx);
            }

            ctx.SetSite(site);

            // The cascade count is fillCounts, which only a repeating bar ever
            // acquires (12.6's row is titled "Repeating bars"). A non-repeating
            // bar's completion leaves no derivable effect-fact, which is why its
            // reward is an AddModifier grant instead - so the authored effect
            // here is unreachable rather than merely inert.
            if (!bar.repeating && bar.perFill.Count > 0)
                ctx.AddError(ValidationCheck.InertOperand,
                    "perFill entries on a non-repeating bar: the cascade scales by fillCount, and only a repeating bar acquires one (12.6).");

            for (var i = 0; i < bar.perFill.Count; i++)
            {
                var entry = bar.perFill[i];
                if (entry == null)
                {
                    ctx.SetSite($"{site} perFill[{i}]");
                    ctx.AddError(ValidationCheck.NullEntry, "null perFill entry.");
                    continue;
                }
                ctx.SetSite(site);
                ValidateEffect(ctx, entry.effect, $"{site} perFill[{i}]", scope);
            }

            // The fill count is a fact write at index -1, exactly as the upgrade
            // latch is: it lands before actions[0], so a completion list that
            // resets the scope homing the count its own cascade reads trips
            // set-then-wiped instead of quietly never accumulating. A bar with no
            // cascade records nothing, so ordinary "fill, then reset the tier"
            // authoring stays clean.
            if (bar.repeating && bar.perFill.Count > 0)
            {
                ctx.SetSite($"{site} fill count");
                ctx.RecordFactWrite($"the fill count of bar '{bar.Id}'", scope);
            }

            ValidateActionList(ctx, bar.onComplete, site);
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
        // chain - only the first hop needs to precede the reset. A RestartScope
        // records its rung edge and its reset at the SAME index, and the bank
        // runs before the clear inside the one action, so the seed takes an
        // edge at the reset's own index too. The acting
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
                    if (edge.FromKey == reset.ContainerKey && edge.Index <= reset.Index && reached.Add(edge.ToKey))
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
                    if (scope is not InteriorDefinition interior || interior.rung == null)
                        continue;
                    var rungKey = ValidationContext.RungKey(scope.Id);
                    if (rungKey == reset.ContainerKey)
                        continue;
                    if (!interior.rung.actions.Any(a => a is AddCurrency))
                        continue;
                    if (!reached.Contains(rungKey))
                        ctx.AddWarning(ValidationCheck.StrandedValue,
                            $"{reset.Site}: resets '{reset.Target.Id}', which contains the payout rung at '{scope.Id}' with no ExecuteRung before the reset - stranded value (12.12).");
                }
            }
        }

        // A rung whose reset closure contains an event host must REQUIRE that
        // host to hold no armed reward, or firing it destroys an unclaimed
        // reward with the record (12.12: stranded reward). Required means the
        // whole condition or a conjunct reached through All alone: a leg under
        // an Any is satisfied by its sibling branch, and a positive leg means
        // the opposite. A warn, not an error: resetting over cheap disposable
        // events is authorable on purpose.
        private static void FinalizeStrandedReward(ValidationContext ctx)
        {
            foreach (var reset in ctx.Resets)
            {
                if (!reset.ContainerKey.StartsWith("rung:"))
                    continue;
                Rung rung = null;
                foreach (var scope in ctx.TreeScopes)
                {
                    if (scope is InteriorDefinition owner && owner.rung != null
                        && ValidationContext.RungKey(scope.Id) == reset.ContainerKey)
                    {
                        rung = owner.rung;
                        break;
                    }
                }
                if (rung == null)
                    continue;

                var guarded = new HashSet<ScopeDefinition>();
                CollectRequiredGuards(rung.offerCondition, guarded);
                foreach (var scope in ctx.ScopesInSubtree(reset.Target))
                {
                    if (scope is not InteriorDefinition host)
                        continue;
                    var hostsAnEvent = false;
                    foreach (var evt in host.events)
                    {
                        if (evt != null)
                        {
                            hostsAnEvent = true;
                            break;
                        }
                    }
                    if (!hostsAnEvent || guarded.Contains(scope))
                        continue;
                    ctx.AddWarning(ValidationCheck.StrandedReward,
                        $"{reset.Site}: resets '{reset.Target.Id}', which contains the event host '{scope.Id}', and the rung's offer condition carries no required Not(EventRewardPending('{scope.Id}')) - an armed, unclaimed reward dies with the record (stranded reward, 12.12).");
                }
            }
        }

        // The hosts guarded as a REQUIRED conjunct: the whole condition, or a
        // leg reached through All alone.
        private static void CollectRequiredGuards(Condition condition, HashSet<ScopeDefinition> guarded)
        {
            if (condition is Not not && not.condition is EventRewardPending pending && pending.host != null)
                guarded.Add(pending.host);
            else if (condition is All all)
                foreach (var leg in all.conditions)
                    if (leg != null)
                        CollectRequiredGuards(leg, guarded);
        }

        // Every declaration is its own flag, so the no-setter question is asked
        // once per declaring scope: a setter counts for THIS scope only when its
        // own outward walk lands here, not merely when it names the same word.
        private static void FinalizeFlagChecks(ValidationContext ctx)
        {
            foreach (var scope in ctx.TreeScopes)
                foreach (var flagId in scope.declaredFlags)
                {
                    if (string.IsNullOrEmpty(flagId))
                        continue;
                    if (!ctx.FlagSetters.Any(s => s.FlagId == flagId && ctx.FlagHomeFrom(s.ActingScope, flagId) == scope))
                        ctx.AddWarning(ValidationCheck.FlagNoSetter,
                            $"flag '{flagId}' (declared at '{scope.Id}') has no setter.");
                }
        }

        private static void FinalizeModifierChecks(ValidationContext ctx)
        {
            foreach (var remove in ctx.ModifierRemoves)
                if (!ctx.ModifierGrants.Any(g => g.Modifier == remove.Modifier && g.Target == remove.Target))
                    ctx.AddWarning(ValidationCheck.RemoveWithoutGrant,
                        $"{remove.Site}: RemoveModifier removes '{remove.Modifier.Id}' at '{remove.Target.Id}', where nothing grants it.");

            // An effect's numbers are the same wherever the modifier is applied;
            // its ADDRESS is not, so the address - and the formulas and
            // appliesWhen judged from the same place - is validated once per
            // usage site: every grant target, plus every scope whose
            // permanentModifiers list carries the modifier. A modifier nothing
            // uses is judged from the scope that DECLARES it, which is the
            // loosest legal grant site (a grant must name a scope the modifier's
            // own declaration can reach), so every authored reference still
            // resolves (12.12).
            foreach (var scope in ctx.TreeScopes)
                foreach (var modifier in scope.modifiers)
                {
                    if (modifier == null)
                        continue;
                    for (var i = 0; i < modifier.effects.Count; i++)
                        ValidateEffectNumbers(ctx, modifier.effects[i], $"modifier '{modifier.Id}' effects[{i}]");

                    var sites = new List<(ScopeDefinition home, string at)>();
                    foreach (var target in ctx.ModifierGrants
                                 .Where(g => g.Modifier == modifier).Select(g => g.Target).Distinct())
                        sites.Add((target, $" (granted at '{target.Id}')"));
                    foreach (var user in ctx.TreeScopes)
                        if (user.permanentModifiers.Contains(modifier) && !sites.Any(s => s.home == user))
                            sites.Add((user, $" (applied at '{user.Id}')"));
                    if (sites.Count == 0)
                        sites.Add((scope, string.Empty));
                    foreach (var (home, at) in sites)
                        ValidateModifierAtSite(ctx, modifier, home, at);
                }
            ctx.ClearSite();
        }

        // One modifier judged from one usage site: each effect's address and
        // formula, then the appliesWhen condition - the reads all walk outward
        // from the site, so the site is the acting scope for all of them.
        private static void ValidateModifierAtSite(ValidationContext ctx, Economy.ModifierDefinition modifier,
                                                   ScopeDefinition home, string at)
        {
            ctx.EnterContainer(home, "modifier:" + modifier.Id);
            for (var i = 0; i < modifier.effects.Count; i++)
            {
                var effectSite = $"modifier '{modifier.Id}'{at} effects[{i}]";
                ValidateEffectAddress(ctx, modifier.effects[i], effectSite, home);
                if (modifier.effects[i].formula != null)
                {
                    ctx.SetSite(effectSite);
                    modifier.effects[i].formula.Validate(ctx);
                    ctx.SetSite(null);
                }
            }
            if (modifier.appliesWhen != null)
            {
                ctx.SetSite($"modifier '{modifier.Id}'{at} appliesWhen");
                ctx.IdleCircumstancePossible = true;
                modifier.appliesWhen.Validate(ctx);
                ctx.IdleCircumstancePossible = false;
                ctx.SetSite(null);
            }
        }

        // One Effect atom: numbers, then address, then the formula when one is
        // authored - its reads walk outward from where the effect lives, which
        // the walker has already made the acting scope here.
        private static void ValidateEffect(ValidationContext ctx, Effect effect, string site, ScopeDefinition home)
        {
            ValidateEffectNumbers(ctx, effect, site);
            ValidateEffectAddress(ctx, effect, site, home);
            effect.formula?.Validate(ctx);
        }

        // The multiplier is the same number wherever the effect is granted, so
        // it is judged independently of placement.
        private static void ValidateEffectNumbers(ValidationContext ctx, Effect effect, string site)
        {
            if (ctx.RequireFiniteDouble(effect.multiplier, $"{site} multiplier") && effect.multiplier < 0)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"{site}: multiplier is {effect.multiplier} - a multiplier never flips a number's sign (zero is legal: an event handicap is x0).");
        }

        private static void ValidateEffectAddress(ValidationContext ctx, Effect effect, string site, ScopeDefinition home) =>
            ValidateEffectAddress(ctx, effect.target, effect.currencyId, effect.stat, site, home);

        // One effect address (12.12), judged from the scope the effect LIVES in.
        // Resolution and reach are the same question here: the gather walks
        // outward from a source or a currency home, so it passes this effect
        // only for candidates sitting in this scope's subtree, and the
        // coordinates are the filter it applies to them.
        private static void ValidateEffectAddress(ValidationContext ctx, string target, string currencyId, string stat, string site, ScopeDefinition home)
        {
            // The currency coordinate narrows to a currency, by id or by a tag a
            // currency carries; anything else narrows to nothing at all.
            var currencyResolves = string.IsNullOrEmpty(currencyId) || ctx.MatchNarrowingCurrency(home, currencyId);
            if (!currencyResolves)
                ctx.AddError(ValidationCheck.UnresolvedReference,
                    $"{site}: narrows to '{currencyId}', which is no currency id and no tag any currency carries.");

            // The stat coordinate is required and exact (12.2): an empty one
            // matches nothing at runtime, so it is refused at load rather than
            // shipped inert; a name outside the effect vocabulary is the typo
            // warn. Either way, a coordinate already reported as naming nothing
            // is not reported a second time as half of an unsatisfiable pair,
            // so the target check below runs without the bad stat filter.
            var statValid = Economy.Stat.IsProduced(stat) || Economy.Stat.IsEffectAddress(stat);
            if (string.IsNullOrEmpty(stat))
                ctx.AddError(ValidationCheck.NullEntry,
                    $"{site}: no stat - the coordinate is required, and a stat-less effect matches nothing (12.2).");
            else if (!statValid)
                ctx.AddWarning(ValidationCheck.UnconsumedStat,
                    $"{site} stat narrowing names stat '{stat}', which no consumer reads ({Economy.Stat.EffectStatNames}).");

            // game_speed is read by an owner-less, currency-less query - the
            // tick's once-per-segment gather - so a target or currency narrowing
            // on one is never passed a coordinate to match: dead content.
            if (stat == Economy.Stat.GameSpeed && (!string.IsNullOrEmpty(target) || !string.IsNullOrEmpty(currencyId)))
            {
                ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                    $"{site}: a game_speed effect is matched by an owner-less, currency-less query, so the narrowing never selects anything (12.2).");
                return;
            }

            // An empty target is the wildcard (12.2). What is left to check is
            // reach, which differs by consumer. game_speed is gathered from the
            // foreground chapter outward, so below chapter level it is never
            // visited. A currency-stage wildcard is collected on the walk from
            // a currency's HOME outward, so it answers only for currencies
            // homed in the usage scope's subtree - the ancestor direction
            // MatchNarrowingCurrency allows belongs to source-targeted effects,
            // whose stage-1 walk visits this scope; a wildcard has no stage 1.
            if (string.IsNullOrEmpty(target))
            {
                if (stat == Economy.Stat.GameSpeed)
                {
                    if (!(home is ChapterDefinition) && !(home is RootDefinition))
                        ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                            $"{site}: a game_speed effect at '{home.Id}' is never gathered - the tick reads from the foreground chapter outward, so it must live at a chapter or the root (12.2).");
                }
                else if (statValid && !ctx.SubtreeHomesMatchingCurrency(home, currencyResolves ? currencyId : null, stat))
                {
                    ctx.AddWarning(ValidationCheck.EffectTargetUnmatched, string.IsNullOrEmpty(currencyId)
                        ? $"{site}: the wildcard applies at the currency stage, and no currency homed within '{home.Id}' is paid at '{stat}' - the home-to-root gather never visits this effect (12.12)."
                        : $"{site}: the wildcard applies at the currency stage, and no currency matching '{currencyId}' homed within '{home.Id}' is paid at '{stat}' - the home-to-root gather never visits this effect (12.12).");
                }
                return;
            }
            ValidateTargetCoordinate(ctx, target, site, home,
                currencyResolves ? currencyId : null,
                statValid ? stat : null);
        }

        private static void ValidateTargetCoordinate(ValidationContext ctx, string target, string site, ScopeDefinition home,
                                                     string currencyId, string stat)
        {
            void WrongKind(Definition definition) =>
                ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                    $"{site}: targets '{target}' ({definition.GetType().Name}), which is not an effect target kind (12.2: currency, producer, generator, bar, or tag).");

            var candidates = ctx.MatchTargets(home, target);
            if (candidates.Count > 0)
            {
                // The three coordinates address ONE entry TOGETHER (12.2), so
                // they are judged together: a tag matching three sources is
                // right as long as one of them pays the narrowed currency, and
                // wrong when none does - Producer.Matches would then refuse
                // every candidate the gather ever offers.
                if (!candidates.Any(c => SatisfiesNarrowing(ctx, c, currencyId, stat)))
                    ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                        $"{site}: targets '{target}', but nothing it matches within '{home.Id}' pairs with {NarrowingText(currencyId, stat)} - the coordinates never select an entry together (12.2).");
                return;
            }

            var wrongKind = ctx.MatchOtherKind(home, target);
            if (wrongKind != null)
            {
                WrongKind(wrongKind);
                return;
            }

            // Nothing the gather can bring here answers to the name. Whether
            // that is a typo or a misplacement is the rest of the tree's
            // business, and its answer picks the MESSAGE - it never accepts an
            // address this effect could not reach.
            var elsewhere = ctx.MatchTargets(ctx.RootScope, target);
            var namedElsewhere = elsewhere.FirstOrDefault(d => d.Id == target);
            if (namedElsewhere != null)
            {
                var declaredAt = ctx.DeclaringScope(namedElsewhere)?.Id;
                ctx.AddError(ValidationCheck.EffectReach, namedElsewhere is Economy.CurrencyDefinition
                    ? $"{site}: targets currency '{target}' homed at '{declaredAt}', but '{home.Id}' is not the home or an ancestor of it - the home-to-root gather never visits this effect (12.12)."
                    : $"{site}: targets '{target}' declared at '{declaredAt}', but '{home.Id}' is not that scope or an ancestor of it - the source's outward walk never visits this effect (12.12).");
                return;
            }

            var otherKindElsewhere = ctx.MatchOtherKind(ctx.RootScope, target);
            if (otherKindElsewhere != null)
            {
                WrongKind(otherKindElsewhere);
                return;
            }

            if (elsewhere.Count > 0 || ctx.TagExists(target))
                ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                    $"{site}: tag '{target}' matches no member within '{home.Id}' (12.12).");
            else
                ctx.AddWarning(ValidationCheck.EffectTargetUnmatched,
                    $"{site}: target '{target}' matches no id and no tag.");
        }

        // Whether one candidate owner can satisfy the narrowing coordinates -
        // the question Producer.Matches asks at runtime, asked here of authored
        // data. Empty narrowings are satisfied by anything.
        private static bool SatisfiesNarrowing(ValidationContext ctx, Definition candidate, string currencyId, string stat)
        {
            if (string.IsNullOrEmpty(currencyId) && string.IsNullOrEmpty(stat))
                return true;
            switch (candidate)
            {
                // The currency stage evaluates with owner == currency, so the
                // narrowing can only name that same currency. The stat is not
                // free either: GetRate asks the stage for `rate`, FireProducer
                // asks for `yield` once per yield term, so a stat nothing pays
                // this currency with is a stage that never runs. (game_speed
                // never reaches here: a narrowed one is reported as dead
                // content before the target check runs.)
                case Economy.CurrencyDefinition currency:
                    if (!string.IsNullOrEmpty(currencyId) && currency.Id != currencyId && !currency.HasTag(currencyId))
                        return false;
                    return string.IsNullOrEmpty(stat) || ctx.SomeSourcePays(currency, stat);
                case Economy.ProducerDefinition producer:
                    return producer.produces.Any(entry => EntrySatisfies(entry, currencyId, stat));
                case Economy.GeneratorDefinition generator:
                    return generator.produces.Any(entry => EntrySatisfies(entry, currencyId, stat));
                // A bar's one produced number is its fill rate, read as
                // GetMultiplier(.., bar.fillCurrency, rate) - stage 1 only, since
                // a bar consumes rather than produces. So the pair a coordinate
                // can name is that bar's own currency and `rate`; one that fills
                // from time has no currency for a coordinate to name.
                case Economy.BarDefinition bar:
                    if (!string.IsNullOrEmpty(stat) && stat != Economy.Stat.Rate)
                        return false;
                    if (string.IsNullOrEmpty(currencyId))
                        return true;
                    return bar.fillCurrency != null
                        && (bar.fillCurrency.Id == currencyId || bar.fillCurrency.HasTag(currencyId));
                default:
                    return true;
            }
        }

        private static bool EntrySatisfies(Economy.ProducesEntry entry, string currencyId, string stat)
        {
            if (entry?.currency == null)
                return false;   // a null entry or operand is reported where it is authored
            if (!string.IsNullOrEmpty(currencyId) && entry.currency.Id != currencyId && !entry.currency.HasTag(currencyId))
                return false;
            return string.IsNullOrEmpty(stat) || entry.stat == stat;
        }

        private static string NarrowingText(string currencyId, string stat) =>
            string.IsNullOrEmpty(stat) ? $"currency '{currencyId}'"
            : string.IsNullOrEmpty(currencyId) ? $"stat '{stat}'"
            : $"currency '{currencyId}' and stat '{stat}'";

        // Cycles across nested action references are errors (12.12). The edges
        // are the rung invocations ExecuteRung and RestartScope record, from
        // rungs and trigger lists; the event lifecycle operations are commands
        // and stay out of the graph.
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
