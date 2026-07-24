using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // JSON effect "currencyPerSecMultiplier": multiplies passive production of
    // the currencies it names (tight_set, cash in Ch1). Run-scoped buff.
    // The affected currencies are declared data, not implied by the effect
    // name, for the same reason the Records buff declares them (design doc
    // section 3): a generator producing fans or merch must never inherit an
    // income buff just because the buff exists.
    [Serializable]
    public class CurrencyPerSecMultiplierPayload : UpgradePayload
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency ids whose generator production this multiplier applies to. Anything not listed is untouched.")]
        private List<string> _affectsCurrencyIds = new();

        [SerializeField]
        [Tooltip("Income multiplier, e.g. 1.5 for +50%.")]
        private double _value;

        public IReadOnlyList<string> AffectsCurrencyIds => _affectsCurrencyIds;
        public double Value => _value;

        public CurrencyPerSecMultiplierPayload() { }

        public CurrencyPerSecMultiplierPayload(List<string> affectsCurrencyIds, double value)
        {
            _affectsCurrencyIds = affectsCurrencyIds;
            _value = value;
        }

        public override void Apply(UpgradePayloadContext context)
        {
            Debug.LogError("CurrencyPerSecMultiplierPayload: income buff application arrives with the buff slice.");
        }

        public override void Validate(ConditionContext context, string source)
        {
            // an empty affects list can never change production (the importer
            // refuses to write one; this catches stale assets), and a
            // non-positive multiplier would zero or negate the stack it lands in
            if (_affectsCurrencyIds.Count == 0)
                Debug.LogError($"UpgradePayload: {source} names no affected currencies - the multiplier could never apply.");
            foreach (var currencyId in _affectsCurrencyIds)
                context.Currencies.ValidateReference(currencyId, source);
            if (_value <= 0)
                Debug.LogError($"UpgradePayload: {source} has a non-positive multiplier ({_value}).");
        }
    }
}
