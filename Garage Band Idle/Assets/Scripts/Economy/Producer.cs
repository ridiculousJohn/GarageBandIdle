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
        // source's declaring scope for stage 1, the currency's home for stage 2.
        // Career formulas compute against this context, per the ruling on
        // MultiplierFormula.
        public static BigNumber GetMultiplier(GameContext origin, Definition owner, string currencyId, string stat)
        {
            if (owner == null)
                return BigNumber.One;

            var product = BigNumber.One;
            for (var node = origin.Scope; node != null; node = node.Parent)
            {
                // Purchased upgrades, read through the DECLARATION list: the
                // order is the authored one, and a latch for an upgrade this
                // scope never declared cannot contribute.
                foreach (var upgrade in node.Definition.upgrades)
                {
                    if (upgrade == null || !node.purchasedUpgrades.Contains(upgrade.Id))
                        continue;
                    foreach (var effect in upgrade.effects)
                        if (Matches(effect.target, effect.currencyId, effect.stat, owner, currencyId, stat))
                            product *= effect.multiplier;
                }

                // Granted modifier stacks: the stored count scales the effect by
                // the definition's own stacking kind (design doc 12.5).
                foreach (var entry in node.activeModifiers)
                {
                    if (entry == null || entry.count <= 0)
                        continue;
                    var modifier = origin.Defs.Get<ModifierDefinition>(entry.modifierId);
                    if (modifier == null)
                        continue;
                    foreach (var effect in modifier.effects)
                        if (Matches(effect.target, effect.currencyId, effect.stat, owner, currencyId, stat))
                            product *= Stacked(effect.multiplier, entry.count, modifier.stacking);
                }

                // Career effects declared here, computed against the ORIGIN
                // context rather than this scope's.
                foreach (var career in node.Definition.careerEffects)
                {
                    if (career == null || career.formula == null)
                        continue;
                    if (Matches(career.target, career.currencyId, career.stat, owner, currencyId, stat))
                        product *= career.formula.Compute(origin);
                }
            }
            return product;
        }

        // An effect matches an owner plus coordinates when its target names the
        // owner - by id or by any of its tags - and each optional coordinate it
        // sets agrees: both empty matches everything the owner has, either one
        // narrows, both name one entry exactly (design doc 12.2). A currency-
        // stage effect with no stat is what lets records_income reach the tap
        // yield while a stat: rate narrowing leaves it alone.
        private static bool Matches(string target, string effectCurrencyId, string effectStat,
                                    Definition owner, string currencyId, string stat)
        {
            if (string.IsNullOrEmpty(target))
                return false;
            if (target != owner.Id && !owner.HasTag(target))
                return false;
            if (!string.IsNullOrEmpty(effectCurrencyId) && effectCurrencyId != currencyId)
                return false;
            if (!string.IsNullOrEmpty(effectStat) && effectStat != stat)
                return false;
            return true;
        }

        // Count scaling by stacking kind (design doc 12.5): Replace holds a
        // re-grant at count 1, Linear adds the excess per stack, Multiply
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
        private static BigNumber Stacked(BigNumber multiplier, int count, StackingKind stacking)
        {
            switch (stacking)
            {
                case StackingKind.Linear:
                    return BigNumber.Max(BigNumber.Zero, BigNumber.One + (multiplier - 1) * count);
                case StackingKind.Multiply:
                    return BigNumber.Pow(multiplier, count);
                default:
                    return multiplier;
            }
        }

        // ---- composed numbers ----

        // One source's term: the sum of its matching entries whose conditions
        // hold, times the stored count that scales it (1 for a producer), times
        // the stage-1 product. Conditions are judged in the declaring scope.
        private static BigNumber SourceTerm(GameContext declaringCtx, Definition source,
                                            List<ProducesEntry> entries, int countScale,
                                            string currencyId, string stat)
        {
            var baseSum = BigNumber.Zero;
            foreach (var entry in entries)
            {
                if (entry == null || entry.currencyId != currencyId || entry.stat != stat)
                    continue;
                if (!entry.Holds(declaringCtx))
                    continue;
                baseSum += entry.value;
            }
            if (baseSum == BigNumber.Zero)
                return BigNumber.Zero;      // nothing contributes, and no factor changes that
            return baseSum * countScale * GetMultiplier(declaringCtx, source, currencyId, stat);
        }

        // The stage-2 product for one currency, gathered from its home outward.
        // The currency's own definition is the owner, so its tags match - which
        // is how the income tag carries every career effect (design doc 8.2).
        private static BigNumber CurrencyStage(GameContext atHome, string currencyId, string stat)
        {
            var currency = atHome.Defs.Get<CurrencyDefinition>(currencyId);
            if (currency == null)
                return BigNumber.One;       // an unresolved currency is a load-time error, not a runtime branch
            return GetMultiplier(atHome, currency, currencyId, stat);
        }

        // The rate one subtree pays into one currency, per second of production
        // time. Enumerates the subtree's declared producers and generators,
        // applies both stages, and sums. The tick and the idle claim consume
        // this; the subtree root is explicit because "the foreground chapter" is
        // a session concept, not an economy one.
        public static BigNumber GetRate(ScopeState subtreeRoot, IDefinitionSource defs, DateTime nowUtc, string currencyId)
        {
            var sum = BigNumber.Zero;
            Accumulate(subtreeRoot);
            if (sum == BigNumber.Zero)
                return BigNumber.Zero;

            var home = FindCurrencyHome(subtreeRoot, currencyId);
            if (home == null)
            {
                Debug.LogError($"GetRate: no scope holds currency '{currencyId}'.");
                return BigNumber.Zero;
            }
            return sum * CurrencyStage(new GameContext(home, defs, nowUtc), currencyId, Stat.Rate);

            void Accumulate(ScopeState node)
            {
                var declaringCtx = new GameContext(node, defs, nowUtc);
                foreach (var producer in node.Definition.producers)
                {
                    if (producer == null)
                        continue;
                    sum += SourceTerm(declaringCtx, producer, producer.produces, 1, currencyId, Stat.Rate);
                }
                foreach (var generator in node.Definition.generators)
                {
                    if (generator == null)
                        continue;
                    if (!node.generatorCounts.TryGetValue(generator.Id, out var owned) || owned <= 0)
                        continue;
                    sum += SourceTerm(declaringCtx, generator, generator.produces, owned, currencyId, Stat.Rate);
                }
                foreach (var child in node.Children)
                    Accumulate(child);
            }
        }

        // Fires one producer: every yield entry resolved against PRE-FIRE state
        // - conditions and amounts judged together, multipliers included - and
        // only then deposited, so no output can flip a sibling output's
        // condition mid-fire (design doc 12.2).
        public static void FireProducer(GameContext ctx, string producerId)
        {
            var producer = ctx.Defs.Get<ProducerDefinition>(producerId);
            if (producer == null)
            {
                Debug.LogError($"FireProducer: no ProducerDefinition with id '{producerId}'.");
                return;
            }
            var declaring = DeclaringScope(ctx.Scope, producer, s => s.producers);
            if (declaring == null)
            {
                Debug.LogError($"FireProducer: no scope declares producer '{producerId}'.");
                return;
            }
            var declaringCtx = ctx.Rebase(declaring);

            // Every currency this firing pays, in authored order, resolved
            // before any deposit lands.
            var currencies = new List<string>();
            foreach (var entry in producer.produces)
                if (entry != null && entry.stat == Stat.Yield && !string.IsNullOrEmpty(entry.currencyId) &&
                    !currencies.Contains(entry.currencyId))
                    currencies.Add(entry.currencyId);

            var amounts = new List<BigNumber>(currencies.Count);
            foreach (var currencyId in currencies)
            {
                var term = SourceTerm(declaringCtx, producer, producer.produces, 1, currencyId, Stat.Yield);
                if (term == BigNumber.Zero)
                {
                    amounts.Add(BigNumber.Zero);
                    continue;
                }
                var home = FindCurrencyHome(declaring, currencyId);
                amounts.Add(home == null
                    ? BigNumber.Zero
                    : term * CurrencyStage(declaringCtx.Rebase(home), currencyId, Stat.Yield));
            }

            for (var i = 0; i < currencies.Count; i++)
                if (amounts[i] != BigNumber.Zero)
                    declaringCtx.Deposit(currencies[i], amounts[i]);
        }

        // ---- tree lookups ----

        // The currency's home: the scope holding the key. Homes are unique
        // tree-wide (validated), so the search starts at the root and finds it
        // wherever the caller's subtree sits relative to it.
        internal static ScopeState FindCurrencyHome(ScopeState anyNode, string currencyId)
        {
            return Search(anyNode.Root);

            ScopeState Search(ScopeState node)
            {
                if (node.balances.ContainsKey(currencyId))
                    return node;
                foreach (var child in node.Children)
                {
                    var found = Search(child);
                    if (found != null)
                        return found;
                }
                return null;
            }
        }

        // Declaration is ownership (design doc 12.3): a definition's declaring
        // scope is the one whose list holds the reference, and a duplicate
        // declaration is refused at load.
        internal static ScopeState DeclaringScope<T>(ScopeState anyNode, T definition,
                                                     Func<ScopeDefinition, List<T>> lists) where T : Definition
        {
            return Search(anyNode.Root);

            ScopeState Search(ScopeState node)
            {
                var list = lists(node.Definition);
                for (var i = 0; i < list.Count; i++)
                    if (list[i] == definition)
                        return node;
                foreach (var child in node.Children)
                {
                    var found = Search(child);
                    if (found != null)
                        return found;
                }
                return null;
            }
        }
    }
}
