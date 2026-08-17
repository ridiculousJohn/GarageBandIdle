using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Builds a scope (design doc section 12, rule 12). All the construction
    // ORDER lives here, in one place, which is the reason this is a factory and
    // not a constructor with a long body: several systems must exist before
    // others can read them - modifiers before anything composing a stat, the
    // condition context before the systems whose gates are Conditions - and
    // there is exactly ONE Assemble however a scope is described, so the two
    // descriptions below cannot drift into two construction orders.
    //
    // Two descriptions, temporarily (mid-7.5): a ChapterDefinition, which is
    // how the game boots until step 7 authors scope assets, and a
    // ScopeDefinition, which is the tree's own shape and recurses over its
    // ordered children. The chapter path dies with step 7.
    //
    // Nothing here is chapter-1 specific and nothing branches on a currency
    // name. What differs between the frontier chapter, an event sandbox and a
    // replay economy is the recipe and the roster, both data.
    public static class ScopeFactory
    {
        // Builds the startup pool: every currency whose group is placed Global
        // (design doc section 12, rule 12). Created once, held by nobody's run,
        // and destined to be the permanent save block (slice 9) - its lifetime
        // comes from this call site being outside any economy, not from a flag
        // inside CurrencyManager.
        //
        // Placement is read from the group, and a currency whose group does not
        // resolve is left out: CurrencyManager already reports the broken
        // reference, and guessing a pool for it would put the player's permanent
        // progress somewhere arbitrary.
        public static CurrencyManager BuildPermanentPool(ContentDatabase database)
        {
            var groups = new Dictionary<string, CurrencyGroupDefinition>();
            foreach (var group in database.CurrencyGroups.All)
            {
                if (!string.IsNullOrEmpty(group.Id))
                    groups[group.Id] = group;
            }

            var global = new List<CurrencyDefinition>();
            foreach (var currency in database.Currencies.All)
            {
                if (groups.TryGetValue(currency.GroupId ?? "", out var group)
                    && group.Placement == CurrencyPlacement.Global)
                    global.Add(currency);
            }

            return new CurrencyManager(database.CurrencyGroups.All, global);
        }

        // Builds one scope from a chapter (the boot path until 7.5 step 7). The
        // scope's own pool comes from the chapter's authored roster
        // (ChapterDefinition.CurrencyIds), never from every currency the
        // database happens to hold: a currency exists in exactly one scope's
        // pool, and "everything that was imported" is not a statement about
        // which scope owns what. Its instance identity is the chapter's id -
        // one frontier instantiation per chapter definition, which stays true
        // until a replay instantiates a second one and names it differently.
        public static Scope Build(ChapterDefinition chapter, ContentDatabase database,
            CurrencyManager permanentPool, EconomyRecipe recipe, EconomyLocalSnapshot seed = null)
        {
            if (chapter == null)
            {
                Debug.LogError("ScopeFactory: Build with no chapter definition.");
                return null;
            }

            // The recipe decides which permanent pool this economy can reach, and it
            // is a routing decision rather than a filter: an event sandbox gets a
            // PRIVATE pool built from the same Global definitions, so a stray write
            // to Records or Roadies inside a challenge lands somewhere that is
            // discarded with the context. Withholding the income modifier alone
            // would have left the sandbox able to bank real progress.
            var reachablePermanentPool = recipe != null && recipe.PoolRouting == PermanentPoolRouting.Isolated
                ? BuildPermanentPool(database)
                : permanentPool;

            // The roster is still checked against the pool the CALLER owns, not the
            // private one: shadowing a global id is a content mistake regardless of
            // which economy is being built, and asking the fresh pool would let a
            // sandbox accept a roster the frontier refuses.
            var pool = new CurrencyManager(database.CurrencyGroups.All,
                ResolveChapterRoster(chapter, database, permanentPool));

            var scope = Assemble(null, chapter.Id, null, chapter, recipe, database,
                pool, reachablePermanentPool,
                chapter.Flags, chapter.ProducerIds, chapter.SectionIds,
                chapter.GeneratorIds, chapter.UpgradeIds, chapter.BarGroupIds, seed);

            // a chapter authors no child scopes (that is what step 7 changes), and
            // attaching the empty list here keeps "every scope's shape is fixed at
            // construction" a statement with no exceptions
            scope.AttachChildren(Array.Empty<Scope>());
            return scope;
        }

        // Builds one scope from its own definition, recursing over the ordered
        // children - the tree path (rule 12). The ROOT's instance id is the
        // caller's to assign (a replay names its instantiation differently from
        // the frontier's, which is the whole point of instance identity); each
        // child's derives as parent-id/child-definition-id - deterministic, and
        // position-independent so a save block written for 'frontier/tier_1'
        // still rematches after the ladder is re-ordered.
        //
        // outerPool is the pool beyond the tree's root (the permanent pool, for
        // a real tree; nothing, for a self-contained fixture). It becomes the
        // chain's outermost, currencies-only link, and it seeds the claim map
        // below, because "ids are unique tree-wide" includes the pool the tree
        // hangs under.
        public static Scope Build(ScopeDefinition definition, string instanceId, ContentDatabase database,
            CurrencyManager outerPool = null, Scope parent = null)
        {
            if (definition == null)
            {
                Debug.LogError("ScopeFactory: Build with no scope definition.");
                return null;
            }
            if (string.IsNullOrEmpty(instanceId))
            {
                Debug.LogError($"ScopeFactory: building scope '{definition.Id}' with no instance id - slice 9's save is one block per instance, matched by this id. Refusing.");
                return null;
            }

            // The claim map is ASSEMBLY-wide because uniqueness is TREE-wide
            // (rule 12): an ancestor walk sees only its own chain, and a
            // sibling's claim sits on no chain this scope could walk - so every
            // id placed anywhere in this build is claimed here, and the roster
            // check consults exactly one authority. Seeded nearest-first from
            // any existing chain the tree is being built under, then from the
            // pool it hangs under, so a refusal names the closest holder.
            var claimedBy = new Dictionary<string, string>();
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor.Pool == null)
                    continue;
                foreach (var held in ancestor.Pool.Definitions)
                {
                    if (!string.IsNullOrEmpty(held.Id) && !claimedBy.ContainsKey(held.Id))
                        claimedBy[held.Id] = $"scope instance '{ancestor.InstanceId}'";
                }
            }
            if (outerPool != null)
            {
                foreach (var held in outerPool.Definitions)
                {
                    if (!string.IsNullOrEmpty(held.Id) && !claimedBy.ContainsKey(held.Id))
                        claimedBy[held.Id] = "the pool the tree hangs under";
                }
            }

            return BuildNode(definition, instanceId, database, outerPool, parent,
                claimedBy, new List<string>());
        }

        // One node of the recursion, carrying the two facts a single call cannot
        // know: which currency ids the whole assembly has already placed, and
        // the definition ids on the path above (a definition naming itself - or
        // an ancestor of itself - describes a tree that contains itself, and
        // recursing into it would discover that as a stack overflow).
        private static Scope BuildNode(ScopeDefinition definition, string instanceId, ContentDatabase database,
            CurrencyManager outerPool, Scope parent,
            Dictionary<string, string> claimedBy, List<string> activePath)
        {
            if (activePath.Contains(definition.Id))
            {
                Debug.LogError($"ScopeFactory: scope '{definition.Id}' is an ancestor of itself - child references cycle ({string.Join(" -> ", activePath)} -> {definition.Id}). Skipping this edge; a tree cannot contain itself.");
                return null;
            }
            activePath.Add(definition.Id);

            var pool = new CurrencyManager(database.CurrencyGroups.All,
                ResolveScopeRoster(definition, database, instanceId, claimedBy));

            var scope = Assemble(definition, instanceId, parent, null, null, database,
                pool, parent != null ? parent.Pool : outerPool,
                definition.Flags, definition.ProducerIds, definition.SectionIds,
                definition.GeneratorIds, definition.UpgradeIds, definition.BarGroupIds, null);

            // children after the parent is restored and settled: a child's reads
            // go outward, so what it reads at construction must be finished state
            var seenChildIds = new HashSet<string>();
            var children = new List<Scope>(definition.ChildScopeIds.Count);
            foreach (var childId in definition.ChildScopeIds)
            {
                // one instantiation per authored edge: a repeated id would build
                // two children sharing instance id '{instanceId}/{childId}', and
                // slice 9 matches save blocks by exactly that id
                if (!seenChildIds.Add(childId))
                {
                    Debug.LogError($"ScopeFactory: scope '{definition.Id}' lists child scope id '{childId}' twice - two children would share instance id '{instanceId}/{childId}'. Building the first only.");
                    continue;
                }
                if (!database.Scopes.TryGet(childId, out var childDefinition))
                {
                    Debug.LogError($"ScopeFactory: scope '{definition.Id}' names unknown child scope id '{childId}'. Re-run the chapter import.");
                    continue;
                }

                var child = BuildNode(childDefinition, instanceId + "/" + childId, database,
                    outerPool, scope, claimedBy, activePath);
                if (child != null)
                    children.Add(child);
            }

            scope.AttachChildren(children);

            // off the path on the way out: a definition legitimately reused in
            // another BRANCH is a second instance, not a cycle - only the path
            // from here to the root can prove a tree contains itself
            activePath.RemoveAt(activePath.Count - 1);
            return scope;
        }

        // The one construction order (rule 12's bundle), whichever description
        // a scope was built from. The systems land in dependency order:
        // modifiers before anything composing a stat, the condition context
        // before the systems whose gates are Conditions, production last
        // because it ASSEMBLES its producers from the generator and upgrade
        // systems (rule 13). Chapter and recipe are the mid-7.5 passengers: a
        // definition-built scope passes neither, and every consumer of them
        // below already treats absence as "authors none".
        private static Scope Assemble(ScopeDefinition definition, string instanceId, Scope parent,
            ChapterDefinition chapter, EconomyRecipe recipe, ContentDatabase database,
            CurrencyManager pool, CurrencyManager outerPool,
            IReadOnlyList<FlagDeclaration> flagDeclarations,
            IReadOnlyList<string> producerIds, IReadOnlyList<string> sectionIds,
            IReadOnlyList<string> generatorIds, IReadOnlyList<string> upgradeIds,
            IReadOnlyList<string> barGroupIds, EconomyLocalSnapshot seed)
        {
            // built before the systems that read it: every stat effect in the
            // game composes through here, so no system holds its own stack
            var modifiers = new ModifierSystem();

            // the declared flags are the known set (setting or gating on
            // anything else is reported as a content mistake), each latch
            // carrying the lifetime its declaration states
            var flags = new FlagSystem(flagDeclarations);

            // This scope's link in the ONE iteration (rule 12): its own truth,
            // chained outward through its parent's link - or, at a tree's root,
            // the pool the tree hangs under, a currencies-only link. Built
            // before the router and the systems because they consume it: reads
            // resolve outward through it, and its aggregated signals carry
            // outer changes inward.
            var chain = new ScopeChain(
                parent != null ? parent.Chain : outerPool != null ? new ScopeChain(outerPool) : null,
                pool, flags, modifiers);

            var router = new CurrencyRouter(chain);

            var generators = new GeneratorSystem(
                Resolve(database.Generators, generatorIds, "generator"), router, chain);
            var upgrades = new UpgradeSystem(
                Resolve(database.Upgrades, upgradeIds, "upgrade"), router, flags, modifiers, chain);

            var rewards = new RewardManager(database.Rewards.All);
            var effects = new EffectContext(router, flags, modifiers);
            var bars = new BarSystem(Resolve(database.BarGroups, barGroupIds, "bar group"),
                database.Bars.All, router, rewards, effects);

            // every scope gets one: a scope whose description authors no
            // capstone holds an inert system (nothing latches, nothing
            // projects), which is cheaper to reason about than a null another
            // boundary has to remember to skip
            var capstone = new CapstoneSystem(chapter?.Capstone, flags, effects);

            var conditions = new ConditionContext(router, generators, flags,
                GameManager.RecordsCurrencyId, database, bars, chain);

            // Built after the condition context because contribution gates are
            // ordinary Conditions checked per composition, and after the generator
            // and upgrade systems because it ASSEMBLES its producers from them
            // (design doc section 12, rule 13) - generators and applied upgrades
            // contribute exactly as authored producer lines do. Only THIS scope's
            // content contributes: flag ids may legitimately repeat across
            // scopes, so ownership comes from the scope's own lists.
            var production = new ProductionSystem(
                Resolve(database.Producers, producerIds, "producer"),
                generators, upgrades, router, chain, conditions);

            // the Records buff is derived, not granted: one modifier per currency
            // the chapter's recordBuff declares, each reading the cumulative
            // Records total from the permanent pool, so production of anything
            // undeclared is untouched and nothing has to remember to re-apply it.
            // The recipe decides whether this economy sees it at all - an event
            // sandbox's fixed baseline is precisely this absence.
            if (chapter != null && recipe != null && recipe.RegistersRecordsIncome)
            {
                foreach (var currencyId in chapter.RecordBuff.AffectsCurrencyIds)
                {
                    modifiers.AddDerived(new RecordsIncomeModifier(
                        router, GameManager.RecordsCurrencyId, chapter.RecordBuff.PerRecord, currencyId));
                }
            }

            // Band size raises the fan rate with no code at all: each bandmate
            // generator CONTRIBUTES a fans rate line, so the bonus scales with owned
            // count because a generator's lines always do.

            var scope = new Scope(definition, instanceId, parent, chain, chapter, recipe, router, flags,
                modifiers, generators, upgrades, production, bars, rewards, capstone, conditions,
                Resolve(database.Sections, sectionIds, "section"));

            // Every scope comes up through the SAME door (design doc section 12,
            // rule 6): apply the seed, project, settle, announce - one order, one
            // implementation, inside Scope.Restore. A new run passes the
            // empty seed and the sequence still runs, which is the point: a fresh
            // scope and a loaded one are the same operation with different data,
            // so there is no second path that could skip the projection or forget to
            // settle. Empty rather than null so "restore nothing" is data a caller
            // can pass rather than a call it omits.
            scope.Restore(seed ?? EconomyLocalSnapshot.Empty);
            return scope;
        }

        // The chapter's roster, resolved and checked. Every failure here would
        // otherwise surface as a balance that silently does nothing: an
        // unresolvable id gives producers a currency to pay into that holds no
        // value, a global id in a chapter roster gives the same currency two
        // balances, and a shadowed id makes every read a coin flip decided by
        // code order.
        private static List<CurrencyDefinition> ResolveChapterRoster(ChapterDefinition chapter,
            ContentDatabase database, CurrencyManager permanentPool)
        {
            // The content rules - an id that resolves to no definition, and an id
            // whose group is placed Global (the chapter would accrue into its own
            // Records while the permanent pool's, which the income buff and the
            // capstone gate read, stayed at zero) - belong to ChapterCurrencies,
            // which is also what boot validation asks. One implementation, so a
            // roster validation accepts cannot be one construction then refuses.
            var content = new ChapterCurrencies(database, chapter);
            content.ValidateRoster();

            var roster = new List<CurrencyDefinition>();
            foreach (var currency in content.RosterDefinitions)
            {
                // The one rule content cannot answer: whether the pool actually
                // handed to this economy already holds the id. Globals are caught
                // above, but the permanent pool is an object a caller supplies,
                // so only the caller's pool can be asked about it.
                if (permanentPool != null && permanentPool.Contains(currency.Id))
                {
                    Debug.LogError($"ScopeFactory: chapter '{chapter.Id}' roster names currency '{currency.Id}', which the permanent pool already holds - two balances for one id means every read picks one arbitrarily.");
                    continue;
                }

                roster.Add(currency);
            }

            return roster;
        }

        // A scope definition's roster: resolved against the database, and
        // refused where the assembly has already placed the id - ids are unique
        // tree-wide (rule 12), because an id in two scopes has two balances and
        // every read would silently pick whichever the resolver reached first.
        // Uniqueness is also what makes moving a currency outward a pure data
        // edit. The claim map is the ONE authority on "already placed": it
        // covers ancestors, siblings and cousins alike, where any walk from
        // here could see only its own chain - a sibling's pool sits on no chain
        // this scope can reach, and a collision is a content mistake at any
        // relation. An accepted id is claimed under this instance, so the next
        // collision's report names the actual holder.
        private static List<CurrencyDefinition> ResolveScopeRoster(ScopeDefinition definition,
            ContentDatabase database, string instanceId, Dictionary<string, string> claimedBy)
        {
            var roster = new List<CurrencyDefinition>();
            foreach (var id in definition.CurrencyIds)
            {
                if (!database.Currencies.TryGet(id, out var currency))
                {
                    Debug.LogError($"ScopeFactory: scope '{definition.Id}' roster names unknown currency id '{id}'. Re-run the chapter import.");
                    continue;
                }

                if (claimedBy.TryGetValue(id, out var owner))
                {
                    Debug.LogError($"ScopeFactory: scope '{definition.Id}' roster names currency '{id}', which {owner} already holds - two balances for one id means every read picks one arbitrarily.");
                    continue;
                }

                claimedBy[id] = $"scope instance '{instanceId}'";
                roster.Add(currency);
            }

            return roster;
        }

        // maps an ordered id list to definitions, reporting any id that fails
        // to resolve (content is authored against the same JSON that generated
        // the assets, so a miss means a stale import)
        private static List<T> Resolve<T>(ContentDatabase.Registry<T> registry, IReadOnlyList<string> ids, string kind)
            where T : Definition
        {
            var definitions = new List<T>(ids.Count);
            foreach (var id in ids)
            {
                if (registry.TryGet(id, out var definition))
                    definitions.Add(definition);
                else
                    Debug.LogError($"ScopeFactory: content references unknown {kind} id '{id}'. Re-run the chapter import.");
            }
            return definitions;
        }
    }
}
