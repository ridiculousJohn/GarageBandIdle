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
            CurrencyManager permanentPool, EconomyRecipe recipe)
        {
            if (chapter == null)
            {
                Debug.LogError("EconomyContextFactory: Build with no chapter definition.");
                return null;
            }

            var pool = new CurrencyManager(database.CurrencyGroups.All,
                ResolveRoster(chapter, database, permanentPool));
            var router = new CurrencyRouter(pool, permanentPool);

            // built before the systems that read it: every stat effect in the
            // game composes through here, so no system holds its own stack
            var modifiers = new ModifierSystem();

            // the chapter's declared flags are the known set; setting or gating
            // on anything else is reported as a content mistake
            var flags = new FlagSystem(chapter.FlagIds);

            var generators = new GeneratorSystem(
                Resolve(database.Generators, chapter.GeneratorIds, "generator"), router, modifiers);
            var upgrades = new UpgradeSystem(
                Resolve(database.Upgrades, chapter.UpgradeIds, "upgrade"), router, flags, modifiers);

            var rewards = new RewardManager(database.Rewards.All);
            var effects = new EffectContext(router, flags, modifiers);
            var bars = new BarSystem(Resolve(database.BarGroups, chapter.BarGroupIds, "bar group"),
                database.Bars.All, router, rewards, effects);

            var conditions = new ConditionContext(router, generators, flags,
                GameManager.RecordsCurrencyId, database, bars);


            // built after the condition context because config gates are
            // ordinary Conditions checked per firing. Only THIS chapter's
            // producers fire: flag ids may legitimately repeat across chapters,
            // so ownership comes from the chapter's producer list, never from
            // flags.
            var production = new ProductionSystem(
                Resolve(database.Producers, chapter.ProducerIds, "producer"), router, modifiers, conditions);

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

            // band size raises the fan rate, derived for the same reason: it is a
            // function of owned counts, so nothing grants it and nothing has to
            // re-apply it after a release resets those counts. Unconditional,
            // unlike the Records buff - every economy that has generators has a
            // band, and an event sandbox's fixed baseline comes from the tier
            // rules rather than from withholding this.
            modifiers.AddDerived(new BandmateFanRateModifier(
                generators, chapter.Fans.PerBandmateOwnedBonus));

            var context = new EconomyContext(chapter, recipe, router, flags, modifiers, generators, upgrades,
                production, bars, rewards, conditions,
                Resolve(database.Sections, chapter.SectionIds, "section"));

            // A fresh economy has no facts, so this grants nothing; a loaded one
            // (slice 9) has restored latches and completed bars, and this is what
            // turns them back into effects. Running it unconditionally is the
            // point - the projection is the only door a modifier enters through,
            // so there is no path that skips it (rule 6).
            context.ProjectModifiers();
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
            var roster = new List<CurrencyDefinition>();

            foreach (var id in chapter.CurrencyIds)
            {
                if (!database.Currencies.TryGet(id, out var currency))
                {
                    Debug.LogError($"EconomyContextFactory: chapter '{chapter.Id}' roster names unknown currency id '{id}'. Re-run the chapter import.");
                    continue;
                }

                // Placement says which pool owns the balance, so a roster naming
                // a global currency is asking for a second copy of it. Refused
                // rather than honored: the chapter would accrue into its own
                // Records while the permanent pool's - the one the income buff
                // and the capstone gate read - stayed at zero.
                var group = database.CurrencyGroups.TryGet(currency.GroupId ?? "", out var groupDefinition)
                    ? groupDefinition
                    : null;
                if (group != null && group.Placement == CurrencyPlacement.Global)
                {
                    Debug.LogError($"EconomyContextFactory: chapter '{chapter.Id}' roster names currency '{id}', whose group '{group.Id}' is placed Global - it is held by the startup pool and must not be in a chapter roster.");
                    continue;
                }

                if (permanentPool != null && permanentPool.Contains(id))
                {
                    Debug.LogError($"EconomyContextFactory: chapter '{chapter.Id}' roster names currency '{id}', which the permanent pool already holds - two balances for one id means every read picks one arbitrarily.");
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
            where T : ScriptableObject
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
