using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One factor a count threshold switches on (design doc 12.2/12.7): the
    // Effect atom's coordinates with its owner held as a REFERENCE rather than
    // a selector. A threshold names one row - the generator carrying it, or
    // another definition its declaring scope declares - so the owner resolves
    // outward like every other reference and the toast can name it. The factor
    // is a constant: what the toast prints is what the gather multiplies.
    [Serializable]
    public class ThresholdEffect
    {
        // The owner the factor answers to. Never null once imported: the
        // importer fills in the carrying generator when the entry names none.
        public Definition target;

        // Optional, as on Effect: narrows to entries paying this currency.
        public string currencyId;
        public string stat;
        public BigNumber multiplier = 1;

        // The Effect atom the gather matches: the selector is the owner's own
        // id, so it answers to exactly that row (12.2).
        public Effect Effect => new Effect
        {
            target = target != null ? target.Id : null,
            currencyId = currencyId,
            stat = stat,
            multiplier = multiplier,
        };
    }

    // "At this owned count, these effects" (design doc 12.7): an entry of a
    // generator's thresholds list. Whether it is reached is derived from the
    // count every time it is asked, so nothing about it is stored.
    [Serializable]
    public class CountThreshold
    {
        public int count;
        public List<ThresholdEffect> effects = new();
    }

    // The purchasable (design doc 12.2): the same produces-entry shape as a
    // producer, scaled by its owned count - purchased plus granted - stored in
    // its declaring scope. Cost
    // currency is independent of what it produces.
    [CreateAssetMenu(menuName = "Garage Band Idle/Generator")]
    public class GeneratorDefinition : Definition
    {
        // A null gate refuses the buy (the same ruling as Rung's offer
        // condition) - the fail-closed backstop behind the load-time check,
        // which refuses a null gate outright; Always is how an author says
        // the gate is open (12.12).
        [SerializeReference, SubclassPicker] public Condition availableWhen;

        public CurrencyDefinition costCurrency;
        public BigNumber baseCost;

        // A curve ratio, and BigNumber like every other authored number the
        // runtime can compute past a double: CostAt raises it to the owned
        // count, so the curve itself is unbounded. Pow's POWER stays a double,
        // by the library's own signature.
        public BigNumber growth = 1;

        public List<ProducesEntry> produces = new();

        // Count thresholds in strictly increasing count order (validated,
        // 12.12): each one's effects apply while the owned count - purchased
        // plus granted, the sum OwnedCountAtLeast reads - is at least its
        // count. They are a carrier kind of the declaring scope (12.6), so the
        // tick, the idle offer and every preview gather them with no path of
        // their own.
        public List<CountThreshold> thresholds = new();

        // Geometric: the (owned+1)th purchase costs baseCost * growth^owned.
        public BigNumber CostAt(int owned) => baseCost * BigNumber.Pow(growth, owned);

        // Fail-closed, and the domain owns the gate - never the UI's visibility
        // (design doc 12.2). ctx must be rebased to the declaring scope.
        public bool IsAvailable(GameContext declaringCtx) =>
            availableWhen != null && availableWhen.Evaluate(declaringCtx);

        // The first threshold not yet reached at this owned count, or null when
        // every one is.
        public CountThreshold NextThreshold(BigNumber owned)
        {
            foreach (var threshold in thresholds)
                if (threshold != null && threshold.count > owned)
                    return threshold;
            return null;
        }

        // The same answer at the owned count the declaring scope holds now.
        // ctx must be rebased to the declaring scope.
        public CountThreshold NextThreshold(GameContext declaringCtx) =>
            NextThreshold(declaringCtx.GetOwnedCount(Id));

        // The thresholds a move from `before` to `after` reaches: every count
        // above the first and at most the second. A move down reaches none.
        public List<CountThreshold> ThresholdsCrossed(BigNumber before, BigNumber after)
        {
            var crossed = new List<CountThreshold>();
            foreach (var threshold in thresholds)
                if (threshold != null && threshold.count > before && threshold.count <= after)
                    crossed.Add(threshold);
            return crossed;
        }
    }
}
