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

        public override void Apply(UpgradePayloadContext context)
        {
            // nothing can grant this yet: buff purchase (the only path here)
            // arrives with the buff slice, which adds the tap-value stack
            Debug.LogError("TapValueAddPayload: tap-value application arrives with the buff slice.");
        }

        public override void Validate(ConditionContext context, string source) { }
    }
}
