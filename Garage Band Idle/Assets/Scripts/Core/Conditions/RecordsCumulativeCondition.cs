using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "recordsCumulative": cumulative Records earned is at least Value.
    // Reads the records currency's lifetime-earned total (Records are never
    // spent, so cumulative equals earned). The capstone gate and event
    // availability both use this — one type, different thresholds.
    [Serializable]
    public class RecordsCumulativeCondition : Condition
    {
        [SerializeField]
        private double _value;

        public double Value => _value;

        public RecordsCumulativeCondition() { }

        public RecordsCumulativeCondition(double value)
        {
            _value = value;
        }

        public override bool Evaluate(ConditionContext context)
            => context.Currencies.GetLifetimeEarned(context.RecordsCurrencyId) >= _value;

        public override void Validate(ConditionContext context, string source)
            => context.Currencies.ValidateReference(context.RecordsCurrencyId, source);
    }
}
