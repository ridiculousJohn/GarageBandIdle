using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Builds an economy (design doc section 12, rule 12) from a chapter
    // definition, the content database, the permanent pool, and a recipe. All
    // the construction ORDER lives here, in one place, which is the reason this
    // is a factory and not a constructor with a long body: several systems must
    // exist before others can read them - modifiers before anything composing a
    // stat, the condition context before the systems whose gates are Conditions
    // - and that order was previously spelled out inside GameManager.Awake,
    // where a second economy could not reuse it.
    //
    // Nothing here is chapter-1 specific and nothing branches on a currency
    // name. What differs between the frontier chapter, an event sandbox and a
    // replay economy is the recipe and the roster, both data.
    public static class EconomyContextFactory
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

        // Builds one economy. The chapter's own pool comes from its authored
        // roster (ChapterDefinition.CurrencyIds), never from every currency the
        // database happens to hold: a currency exists in exactly one economy's
        // pool, and "everything that was imported" is not a statement about
        // which economy owns what.
        public static EconomyContext Build(ChapterDefinition chapter, ContentDatabase database,
            CurrencyManager permanentPool, EconomyRecipe recipe, EconomyLocalSnapshot seed = null)
        {
            if (chapter == null)
            {
                Debug.LogError("EconomyContextFactory: Build with no chapter definition.");
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
                ResolveRoster(chapter, database, permanentPool));
            var router = new CurrencyRouter(pool, reachablePermanentPool);

            // built before the systems that read it: every stat effect in the
            // game composes through here, so no system holds its own stack
            var modifiers = new ModifierSystem();

            // the chapter's declared flags are the known set (setting or gating
            // on anything else is reported as a content mistake), each latch
            // carrying the lifetime its declaration states
            var flags = new FlagSystem(chapter.Flags);

            var generators = new GeneratorSystem(
                Resolve(database.Generators, chapter.GeneratorIds, "generator"), router, modifiers);
            var upgrades = new UpgradeSystem(
                Resolve(database.Upgrades, chapter.UpgradeIds, "upgrade"), router, flags, modifiers);

            var rewards = new RewardManager(database.Rewards.All);
            var effects = new EffectContext(router, flags, modifiers);
            var bars = new BarSystem(Resolve(database.BarGroups, chapter.BarGroupIds, "bar group"),
                database.Bars.All, router, rewards, effects);

            // every recipe gets one: an economy whose chapter authors no
            // capstone holds an inert system (nothing latches, nothing
            // projects), which is cheaper to reason about than a null another
            // boundary has to remember to skip
            var capstone = new CapstoneSystem(chapter.Capstone, flags, effects);

            var conditions = new ConditionContext(router, generators, flags,
                GameManager.RecordsCurrencyId, database, bars);


            // Built after the condition context because contribution gates are
            // ordinary Conditions checked per composition, and after the generator
            // and upgrade systems because it ASSEMBLES its producers from them
            // (design doc section 12, rule 13) - generators and applied upgrades
            // contribute exactly as authored producer lines do. Only THIS chapter's
            // content contributes: flag ids may legitimately repeat across
            // chapters, so ownership comes from the chapter's own lists.
            var production = new ProductionSystem(
                Resolve(database.Producers, chapter.ProducerIds, "producer"),
                generators, upgrades, router, modifiers, conditions);

            // the Records buff is derived, not granted: one modifier per currency
            // the chapter's recordBuff declares, each reading the cumulative
            // Records total from the permanent pool, so production of anything
            // undeclared is untouched and nothing has to remember to re-apply it.
            // The recipe decides whether this economy sees it at all - an event
            // sandbox's fixed baseline is precisely this absence.
            if (recipe != null && recipe.RegistersRecordsIncome)
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

            var context = new EconomyContext(chapter, recipe, router, flags, modifiers, generators, upgrades,
                production, bars, rewards, capstone, conditions,
                Resolve(database.Sections, chapter.SectionIds, "section"));

            // Every economy comes up through the SAME door (design doc section 12,
            // rule 6): apply the seed, project, settle, announce - one order, one
            // implementation, inside EconomyContext.Restore. A new run passes the
            // empty seed and the sequence still runs, which is the point: a fresh
            // economy and a loaded one are the same operation with different data,
            // so there is no second path that could skip the projection or forget to
            // settle. Empty rather than null so "restore nothing" is data a caller
            // can pass rather than a call it omits.
            context.Restore(seed ?? EconomyLocalSnapshot.Empty);
            return context;
        }

        // The chapter's roster, resolved and checked. Every failure here would
        // otherwise surface as a balance that silently does nothing: an
        // unresolvable id gives producers a currency to pay into that holds no
        // value, a global id in a chapter roster gives the same currency two
        // balances, and a shadowed id makes every read a coin flip decided by
        // code order.
        private static List<CurrencyDefinition> ResolveRoster(ChapterDefinition chapter,
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
                    Debug.LogError($"EconomyContextFactory: chapter '{chapter.Id}' roster names currency '{currency.Id}', which the permanent pool already holds - two balances for one id means every read picks one arbitrarily.");
                    continue;
                }

                roster.Add(currency);
            }

            return roster;
        }

        // maps a chapter's ordered id list to definitions, reporting any id that
        // fails to resolve (the chapter is authored against the same JSON that
        // generated the assets, so a miss means a stale import)
        private static List<T> Resolve<T>(ContentDatabase.Registry<T> registry, IReadOnlyList<string> ids, string kind)
            where T : Definition
        {
            var definitions = new List<T>(ids.Count);
            foreach (var id in ids)
            {
                if (registry.TryGet(id, out var definition))
                    definitions.Add(definition);
                else
                    Debug.LogError($"EconomyContextFactory: chapter references unknown {kind} id '{id}'. Re-run the chapter import.");
            }
            return definitions;
        }
    }
}
