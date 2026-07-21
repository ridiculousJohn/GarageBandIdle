using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // JSON effect "allCashPerSecMultiplier": multiplies all passive Cash income
    // (tight_set). Run-scoped buff.
    [Serializable]
    public class AllCashPerSecMultiplierPayload : UpgradePayload
    {
        [SerializeField]
        [Tooltip("Income multiplier, e.g. 1.5 for +50%.")]
        private double _value;

        public double Value => _value;

        public AllCashPerSecMultiplierPayload() { }

        public AllCashPerSecMultiplierPayload(double value)
        {
            _value = value;
        }

        public override void Apply(UpgradePayloadContext context)
        {
            Debug.LogError("AllCashPerSecMultiplierPayload: income buff application arrives with the buff slice.");
        }

        public override void Validate(ConditionContext context, string source) { }
    }
}
