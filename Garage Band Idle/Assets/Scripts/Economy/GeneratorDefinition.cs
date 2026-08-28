using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The purchasable (design doc 12.2): the same produces-entry shape as a
    // producer, scaled by the ownedCount stored in its declaring scope. Cost
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

        // Geometric: the (owned+1)th purchase costs baseCost * growth^owned.
        public BigNumber CostAt(int owned) => baseCost * BigNumber.Pow(growth, owned);

        // Fail-closed, and the domain owns the gate - never the UI's visibility
        // (design doc 12.2). ctx must be rebased to the declaring scope.
        public bool IsAvailable(GameContext declaringCtx) =>
            availableWhen != null && availableWhen.Evaluate(declaringCtx);
    }
}
