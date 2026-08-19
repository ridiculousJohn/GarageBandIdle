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
        // A null gate refuses the buy - an unauthored gate is closed, not open
        // (the same ruling as Rung's offer condition). Validation warns rather
        // than errors: permanently inert content, the same species as a declared
        // flag nothing sets.
        [SerializeReference, SubclassPicker] public Condition availableWhen;

        [DefinitionId(typeof(CurrencyDefinition))] public string costCurrencyId;
        public BigNumber baseCost;

        // A curve ratio, so a double - same species as Effect.multiplier and
        // Pow's power, not a currency value.
        public double growth = 1;

        public List<ProducesEntry> produces = new();

        // Geometric: the (owned+1)th purchase costs baseCost * growth^owned.
        public BigNumber CostAt(int owned) => baseCost * BigNumber.Pow(growth, owned);

        // Fail-closed, and the domain owns the gate - never the UI's visibility
        // (design doc 12.2). ctx must be rebased to the declaring scope.
        public bool IsAvailable(GameContext declaringCtx) =>
            availableWhen != null && availableWhen.Evaluate(declaringCtx);
    }
}
