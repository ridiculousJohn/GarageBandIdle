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
    // home or above it (validated, 12.12). Nothing here stores a derived value:
    // GetMultiplier is a pure read, and FireProducer's deposit is the only write.
    public static class Producer
    {
        // ---- multiplier gathering ----

        // Every factor applying to one number, gathered from the origin scope
        // outward to the root. The origin IS one stage's gather origin: the
        // source's declaring scope for stage 1, the currency's home for stage 2,
        // and the acting scope for an owner-less consumer-stat read (the tick's
        // game_speed), which matches wildcard effects only. Career formulas
        // compute against this context, per the ruling on MultiplierFormula.
        public static BigNumber GetMultiplier(GameContext origin, Definition owner, CurrencyDefinition currency, string stat)
        {
            // Each scope on the chain composes its own factor however it likes;
            // this only multiplies what comes back.
            var product = BigNumber.One;
            for (var node = origin.Scope; node != null; node = node.Parent)
                product *= node.MultiplierFor(origin, owner, currency, stat);
            return product;
        }

        // An effect matches an owner plus coordinates when its target names the
        // owner - by id or by any of its tags - the queried stat is EXACTLY its
        // stat, and the optional currency coordinate agrees (design doc 12.2).
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

        // Count scaling, the one arithmetic both consumers of the vocabulary
        // share (design doc 12.7): Linear adds the excess per count, Multiply
        // compounds. Linear SATURATES at zero: a multiplier below 1 is legal
        // authoring (a debuff that decays linearly), but 1 + (m-1)*n crosses
        // zero once n > 1/(1-m), and a negative factor would run production
        // backwards - a negative yield reaches Deposit, which would drive an
        // earned total DOWNWARD. Reduced to nothing is the semantic; reduced
        // past nothing is not one.
        // The multiplier arrives as BigNumber rather than the authored double so
        // the count scaling happens IN BigNumber: (m-1)*n in double arithmetic
        // can overflow to infinity before the wrapper ever sees it, which the
        // whole point of the wrapper is to prevent.
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

        // One source's term: the sum of its matching entries whose conditions
        // hold, times the stored count that scales it (1 for a producer), times
        // the stage-1 product. Conditions are judged in the declaring scope.
        internal static BigNumber SourceTerm(GameContext declaringCtx, Definition source,
                                            List<ProducesEntry> entries, int countScale,
                                            CurrencyDefinition currency, string stat)
        {
            var baseSum = BigNumber.Zero;
            foreach (var entry in entries)
            {
                if (entry == null || entry.currency != currency || entry.stat != stat)
                    continue;
                if (!entry.Holds(declaringCtx))
                    continue;
                baseSum += entry.value;
            }
            if (baseSum == BigNumber.Zero)
                return BigNumber.Zero;      // nothing contributes, and no factor changes that
            return baseSum * countScale * GetMultiplier(declaringCtx, source, currency, stat);
        }

        // The stage-2 product for one currency, gathered from its home outward.
        // The currency's own definition is the owner, so its tags match - which
        // is how the income tag carries every career effect (design doc 8.2).
        private static BigNumber CurrencyStage(GameContext atHome, CurrencyDefinition currency, string stat) =>
            GetMultiplier(atHome, currency, currency, stat);

        // The rate one subtree pays into one currency, per second of production
        // time. Enumerates the subtree's declared producers and generators,
        // applies both stages, and sums. The tick and the idle claim consume
        // this; the subtree root is explicit because "the foreground chapter" is
        // a session concept, not an economy one.
        public static BigNumber GetRate(ScopeState subtreeRoot, DateTime nowUtc, CurrencyDefinition currency)
        {
            var sum = BigNumber.Zero;
            ScopeState home = null;
            Accumulate(subtreeRoot);
            if (sum == BigNumber.Zero)
                return BigNumber.Zero;
            return sum * CurrencyStage(new GameContext(home, nowUtc), currency, Stat.Rate);

            void Accumulate(ScopeState node)
            {
                Add(node, node.SourceTermsFor(nowUtc, currency, Stat.Rate));
                foreach (var child in node.Children)
                    Accumulate(child);
            }

            // The home is resolved from a CONTRIBUTING scope, never from the
            // subtree root: the currency may be homed below where the walk
            // started (fans at a tier, asked for across the chapter), and every
            // contributor has it on its own outward chain by validation.
            void Add(ScopeState node, BigNumber term)
            {
                if (term == BigNumber.Zero)
                    return;
                sum += term;
                home ??= FindCurrencyHome(node, currency);
            }
        }

        // Fires one producer: every yield entry resolved against PRE-FIRE state
        // - conditions and amounts judged together, multipliers included - and
        // only then deposited, so no output can flip a sibling output's
        // condition mid-fire (design doc 12.2).
        public static void FireProducer(GameContext ctx, ProducerDefinition producer)
        {
            var declaring = DeclaringScope<ScopeState>(ctx.Scope, producer);
            var declaringCtx = ctx.Rebase(declaring);

            // Every currency this firing pays, in authored order, resolved
            // before any deposit lands.
            var currencies = new List<CurrencyDefinition>();
            foreach (var entry in producer.produces)
                if (entry != null && entry.stat == Stat.Yield && entry.currency != null &&
                    !currencies.Contains(entry.currency))
                    currencies.Add(entry.currency);

            var amounts = new List<BigNumber>(currencies.Count);
            foreach (var currency in currencies)
            {
                var term = SourceTerm(declaringCtx, producer, producer.produces, 1, currency, Stat.Yield);
                if (term == BigNumber.Zero)
                {
                    amounts.Add(BigNumber.Zero);
                    continue;
                }
                var home = FindCurrencyHome(declaring, currency);
                amounts.Add(term * CurrencyStage(declaringCtx.Rebase(home), currency, Stat.Yield));
            }

            for (var i = 0; i < currencies.Count; i++)
                if (amounts[i] != BigNumber.Zero)
                    declaringCtx.Deposit(currencies[i].Id, amounts[i]);
        }

        // ---- tree lookups ----

        // The currency's home: the first scope OUTWARD from here that DECLARES
        // this exact asset. Placement is the whole lookup - a currency off this
        // chain is content the validator refuses, so failing to find one is a
        // bug, not a branch. Matching the id instead would answer with a
        // same-named currency from another chapter.
        internal static ScopeState FindCurrencyHome(ScopeState from, CurrencyDefinition currency) =>
            DeclaringScope<ScopeState>(from, currency);

        // A granted stack names its modifier by id, because the save is ids;
        // the definition is found by walking outward to the scope declaring it,
        // which is the same lookup every other reference gets.
        internal static ModifierDefinition FindModifier(ScopeState from, string modifierId)
        {
            for (var node = from; node != null; node = node.Parent)
                foreach (var modifier in node.Definition.modifiers)
                    if (modifier != null && modifier.Id == modifierId)
                        return modifier;
            throw new InvalidOperationException(
                $"No scope on the chain from '{from.ScopeId}' declares modifier '{modifierId}'.");
        }

        // Declaration is ownership (design doc 12.3): a definition's declaring
        // scope is the one whose list holds the reference, found by walking
        // OUTWARD from the acting scope. Anything off that chain is unreachable
        // at runtime and refused at load, so a miss is a bug.
        // The type parameter is the OTHER half of the question: an event is
        // declared by a scope that can host one, so the search says so and root
        // is not a candidate rather than being skipped by a check.
        internal static T DeclaringScope<T>(ScopeState from, Definition definition) where T : ScopeState
        {
            for (var node = from; node != null; node = node.Parent)
                if (node is T typed && typed.Definition.Declares(definition))
                    return typed;
            throw new InvalidOperationException(
                $"No {typeof(T).Name} on the chain from '{from.ScopeId}' declares '{definition.Id}'.");
        }
    }
}
