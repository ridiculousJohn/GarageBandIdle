using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // JSON effect "tapValueAdd": adds a flat amount to the Jam tap value
    // (stage_presence). Run-scoped buff.
    [Serializable]
    public class TapValueAddPayload : UpgradePayload
    {
        [SerializeField]
        [Tooltip("Flat Cash added per tap.")]
        private double _value;

        public double Value => _value;

        public TapValueAddPayload() { }

        public TapValueAddPayload(double value)
        {
            _value = value;
        }

        // an Add lands before every multiplier (ModifierComposition), so a flat
        // tap bonus is worth more the more tap multipliers are already in play
        public override void Apply(UpgradePayloadContext context, ContentScope scope)
            => context.Modifiers.Grant(ModifierTargetKey.Global(ModifierTarget.TapValue),
                ModifierOperation.Add, scope, _value);

        public override void Validate(ConditionContext context, string source)
        {
            // a negative add subtracts from the tap and nothing else restores
            // it; the registry refuses it at runtime, this catches the asset
            if (_value < 0)
                Debug.LogError($"UpgradePayload: {source} adds a negative amount ({_value}) to the tap value.");
        }
    }
}
