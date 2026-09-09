using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Stateless resolution (design doc 12.2/12.13). Every produced number has
    // one shape - the sum of matching contributions whose conditions hold times
    // the product of matching multipliers - and the matches come from two
    // explicit stages:
    //
    //   sourceContribution = base entries x source-targeted effects  gathered SOURCE scope to root
    //   currencyTotal      = sum of sourceContributions
    //                        x currency-targeted effects             gathered CURRENCY home to root
    //
    // The two stages are what keep sibling scopes isolated (12.3), and the
    // stage-2 walk is why a currency-total effect must sit at the currency's
    // home or above it (validated, 12.12). Both walks happen ONCE, when the tree
    // is built: GatherCompiler turns each stage into a CoordinatePlan and every
    // read here is a plan read (12.6/12.14.8). Nothing stores a derived value:
    // GetMultiplier is a pure read, and FireProducer's deposit is the only write.
    public static class Producer
    {
        // ---- multiplier gathering ----

        // Every factor applying to one number: the plan's links, in the order
        // the compiler fixed, each answering with its own fact. The plan already
        // IS the chain walk - the source's declaring scope outward for stage 1,
        // the currency's home outward for stage 2, the chapter for the tick's
        // owner-less game_speed read - so nothing here walks or matches. Effect
        // formulas are judged against this context, per the ruling on
        // MultiplierFormula; appliesWhen is judged at each link's own node.
        public static BigNumber GetMultiplier(GameContext origin, CoordinatePlan plan)
        {
            var product = BigNumber.One;
            var links = plan.Links;
            for (var i = 0; i < links.Count; i++)
                product *= links[i].Factor(origin);
            return product;
        }

        // An effect matches an owner plus coordinates when its target names the
        // owner - by id or by any of its tags - the queried stat is EXACTLY its
        // stat, and the optional currency coordinate agrees (design doc 12.2).
        // COMPILE TIME ONLY: GatherCompiler is the one caller, because which
        // effects can ever match a coordinate is authored and validated, and a
        // gather that re-asked would be re-deriving the static half every read.
        // The stat leg is required: a query resolves one number and a number
        // always has a stat, so a stat-less effect would claim to answer
        // questions of different kinds with one factor - "rate and yield alike"
        // is two entries, and an empty effect stat matches nothing, the
        // fail-closed backstop behind the load-time error.
        //
        // An empty TARGET is the wildcard, "every currency": it applies at the
        // currency stage only, because root sits on both gather walks and a
        // stage-less wildcard would be collected twice. An owner-less query
        // (the tick's game_speed read) matches wildcards only.
        //
        // The currency coordinate matches by id OR tag, exactly as target does:
        // "every rate entry paying an income currency" is one effect rather than
        // one per currency, and a currency stays out by not carrying the tag -
        // which is how 8.2 already states the fans rule ("the fan rate must
        // never carry a roadie-targeted tag").
        internal static bool Matches(string target, string effectCurrencyId, string effectStat,
                                    Definition owner, CurrencyDefinition currency, string stat)
        {
            if (string.IsNullOrEmpty(effectStat) || effectStat != stat)
                return false;
            if (string.IsNullOrEmpty(target))
            {
                if (owner != null && !(owner is CurrencyDefinition))
                    return false;
            }
            else if (owner == null || (target != owner.Id && !owner.HasTag(target)))
            {
                return false;
            }
            // A null currency is "this query has no currency coordinate" - the
            // rate of a bar that fills from time, or the tick's game_speed read.
            // A narrowing effect names a coordinate such a query never passes.
            if (!string.IsNullOrEmpty(effectCurrencyId) && (currency == null
                || (effectCurrencyId != currency.Id && !currency.HasTag(effectCurrencyId))))
                return false;
            return true;
        }

        // An effect's factor is a constant or a formula (design doc 12.2): the
        // authored multiplier when no formula is present, the formula computed
        // against the gather-origin context when one is. Count scaling composes
        // on the computed value.
        internal static BigNumber FactorOf(in Effect effect, GameContext origin) =>
            effect.formula != null ? effect.formula.Compute(origin) : effect.multiplier;

        // Count scaling, the one arithmetic both consumers of the vocabulary
        // share (design doc 12.7): Linear adds the excess per count, Multiply
        // compounds. Linear SATURATES at zero: a multiplier below 1 is legal
        // authoring (a debuff that decays linearly), but 1 + (m-1)*n crosses
        // zero once n > 1/(1-m), and a negative factor would run production
        // backwards - a negative yield reaches Deposit, which would drive an
        // earned total DOWNWARD. Reduced to nothing is the semantic; reduced
        // past nothing is not one.
        internal static BigNumber Grown(BigNumber multiplier, int count, GrowthKind growth)
        {
            if (growth == GrowthKind.Linear)
                return BigNumber.Max(BigNumber.Zero, BigNumber.One + (multiplier - 1) * count);
            return BigNumber.Pow(multiplier, count);
        }

        // Granted stacks add the one case a cascade has no room for: Replace
        // holds a re-grant at count 1 rather than scaling it (design doc 12.5).
        internal static BigNumber Stacked(BigNumber multiplier, int count, StackingKind stacking)
        {
            switch (stacking)
            {
                case StackingKind.Linear:
                    return Grown(multiplier, count, GrowthKind.Linear);
                case StackingKind.Multiply:
                    return Grown(multiplier, count, GrowthKind.Multiply);
                default:
                    return multiplier;
            }
        }

        // ---- composed numbers ----

        // One source's term for ONE coordinate: the sum of the entries the
        // compiler grouped under it whose conditions hold, times the stored
        // count that scales it (1 for a producer), times the stage-1 product the
        // plan holds. The entries arrive already selected, so nothing here
        // re-asks which coordinate they name. Conditions are judged in the
        // declaring scope.
        internal static BigNumber SourceTerm(GameContext declaringCtx, IReadOnlyList<ProducesEntry> entries,
                                             int countScale, CoordinatePlan plan)
        {
            var baseSum = BigNumber.Zero;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.Holds(declaringCtx))
                    baseSum += entry.value;
            }
            if (baseSum == BigNumber.Zero)
                return BigNumber.Zero;      // nothing contributes, and no factor changes that
            // An inactive currency takes nothing from any source (12.2). Asked
            // AFTER the entry sum, so a source paying this currency nothing
            // never pays for the gate at all. Zeroing the term rather than
            // refusing the deposit is what keeps a per-source readout, the
            // total, and the balance agreeing - and what lets the tick stay
            // quiet, since a zero amount is never handed to Deposit at all.
            //
            // The gate is asked at the currency's HOME, which is on the plan.
            if (!plan.Currency.IsActive(declaringCtx.Rebase(plan.Home)))
                return BigNumber.Zero;
            return baseSum * countScale * GetMultiplier(declaringCtx, plan);
        }

        // The stage-2 product for one currency, from the pair of plans compiled
        // at its home. The currency's own definition is the owner, so its tags
        // match - which is how the income tag carries the Records and Roadie
        // factors (design doc 8.2).
        private static BigNumber CurrencyStage(GameContext atHome, CurrencyDefinition currency, string stat) =>
            GetMultiplier(atHome, atHome.Scope.Link<CurrencyPlans>(currency).For(stat));

        // The rate one subtree pays into one currency, per second of production
        // time: the subtree root's compiled contributors, both stages, summed.
        // The tick and the idle claim consume this; the context carries the
        // subtree root explicitly - "the foreground chapter" is a session
        // concept, not an economy one - along with the timestamp and the
        // circumstance, so the idle claim's gather differs from the tick's only
        // in the context it hands over.
        public static BigNumber GetRate(GameContext ctx, CurrencyDefinition currency)
        {
            // No entry is the plan's ANSWER, not a miss: nothing in this subtree
            // pays this currency at rate, which is a rate of zero.
            var paying = ContributorPlan.At(ctx.Scope).For(currency);
            if (paying == null)
                return BigNumber.Zero;

            var sum = BigNumber.Zero;
            var contributors = paying.Contributors;
            for (var i = 0; i < contributors.Count; i++)
            {
                var contributor = contributors[i];
                // An unowned generator scales its entries by zero, so it is
                // skipped rather than summed as a zero term.
                var count = contributor.Source.CountAt(contributor.Node);
                if (count <= 0)
                    continue;
                sum += SourceTerm(ctx.Rebase(contributor.Node), contributor.Entries, count, contributor.Plan);
            }
            if (sum == BigNumber.Zero)
                return BigNumber.Zero;
            return sum * CurrencyStage(ctx.Rebase(paying.Home), currency, Stat.Rate);
        }

        // The unique (currency, home) pairs one subtree's sources pay at
        // Stat.Rate, in tree order then declaration order - GetRate's sibling,
        // enumerating what it sums, and now a projection of the contributor plan
        // rather than a walk. The pair is the point: the tick deposits through
        // the home reference and the idle claim's lines retain it, so neither
        // consumer looks anything up twice.
        public static List<(CurrencyDefinition currency, ScopeState home)> RatePairs(ScopeState subtreeRoot)
        {
            var currencies = ContributorPlan.At(subtreeRoot).Currencies;
            var pairs = new List<(CurrencyDefinition currency, ScopeState home)>(currencies.Count);
            for (var i = 0; i < currencies.Count; i++)
                pairs.Add((currencies[i].Currency, currencies[i].Home));
            return pairs;
        }

        // What one firing would pay against the given state: every yield
        // currency in authored order with its resolved amount, multipliers
        // included, zeros kept. The Jam button's preview reads this, so the
        // preview and the execution are one implementation of the number rather
        // than two that agree until they do not (design doc 12.5).
        public static List<(CurrencyDefinition currency, BigNumber amount)> ResolveYield(
            GameContext ctx, ProducerDefinition producer) =>
            ResolveUnit(ctx, producer, producer.produces, Stat.Yield);

        // What ONE unit of a generator pays per second against the given state:
        // the generator row's "cost => yield" line (12.11). The same resolution
        // as a firing's, over the rate entries with a count of one; nothing in
        // the effect vocabulary reads the owned count, so one unit's term is
        // also what the next unit adds.
        public static List<(CurrencyDefinition currency, BigNumber amount)> UnitRate(
            GameContext ctx, GeneratorDefinition generator) =>
            ResolveUnit(ctx, generator, generator.produces, Stat.Rate);

        // One source's per-unit payment for one stat: every matching currency in
        // authored order with its resolved amount, both stages, zeros kept.
        // Grouping the entries by currency is reading the AUTHORED shape, which
        // is what the plans are keyed by; the gather itself asks nothing.
        private static List<(CurrencyDefinition currency, BigNumber amount)> ResolveUnit(
            GameContext ctx, Definition source, List<ProducesEntry> entries, string stat)
        {
            var declaring = DeclaringScope<ScopeState>(ctx.Scope, source);
            var declaringCtx = ctx.Rebase(declaring);

            var currencies = new List<CurrencyDefinition>();
            var grouped = new List<List<ProducesEntry>>();
            foreach (var entry in entries)
            {
                if (entry == null || entry.currency == null || entry.stat != stat)
                    continue;
                var index = currencies.IndexOf(entry.currency);
                if (index < 0)
                {
                    currencies.Add(entry.currency);
                    grouped.Add(new List<ProducesEntry>());
                    index = currencies.Count - 1;
                }
                grouped[index].Add(entry);
            }

            var amounts = new List<(CurrencyDefinition currency, BigNumber amount)>(currencies.Count);
            for (var i = 0; i < currencies.Count; i++)
            {
                // Entries naming one coordinate share one plan, so the first of
                // the group names it for all of them.
                var plan = declaring.Link<CoordinatePlan>(grouped[i][0]);
                var term = SourceTerm(declaringCtx, grouped[i], 1, plan);
                if (term == BigNumber.Zero)
                {
                    amounts.Add((currencies[i], BigNumber.Zero));
                    continue;
                }
                amounts.Add((currencies[i],
                    term * CurrencyStage(declaringCtx.Rebase(plan.Home), currencies[i], stat)));
            }
            return amounts;
        }

        // Fires one producer: every yield entry resolved against PRE-FIRE state
        // - conditions and amounts judged together, multipliers included - and
        // only then deposited, so no output can flip a sibling output's
        // condition mid-fire (design doc 12.2).
        public static void FireProducer(GameContext ctx, ProducerDefinition producer)
        {
            var declaringCtx = ctx.Rebase(DeclaringScope<ScopeState>(ctx.Scope, producer));
            foreach (var (currency, amount) in ResolveYield(ctx, producer))
                if (amount != BigNumber.Zero)
                    declaringCtx.DepositResolved(currency.Id, amount);
        }

        // ---- the outward walk ----

        // Declaration is ownership (design doc 12.3): a definition's declaring
        // scope is the one whose list holds the reference, found by walking
        // OUTWARD from the acting scope. The one walk, with two answers - this
        // one for the compiler, which resolves a home for a plan and throws at
        // build when there is none (requirement 12.14.7); DeclaringScope below
        // for every command, where a miss is a code bug.
        // The type parameter is the OTHER half of the question: an event is
        // declared by a scope that can host one, so the walk says so and root is
        // not a candidate rather than being skipped by a check.
        internal static T FindDeclaringScope<T>(ScopeState from, Definition definition) where T : ScopeState
        {
            for (var node = from; node != null; node = node.Parent)
                if (node is T typed && typed.Definition.Declares(definition))
                    return typed;
            return null;
        }

        // The declaring scope where there has to be one: anything off the acting
        // chain is unreachable at runtime and refused at load, so a miss is a
        // bug (requirement 7). Every command starts here.
        internal static T DeclaringScope<T>(ScopeState from, Definition definition) where T : ScopeState =>
            FindDeclaringScope<T>(from, definition)
            ?? throw new InvalidOperationException(
                $"No {typeof(T).Name} on the chain from '{from.ScopeId}' declares '{definition.Id}'.");
    }
}
