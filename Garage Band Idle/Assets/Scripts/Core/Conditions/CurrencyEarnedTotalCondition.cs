using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "currencyEarnedTotal": the earned total of a currency is at
    // least Value. Spending never lowers it (CurrencyManager.GetEarned), so
    // once met it stays met for as long as the currency's group keeps the
    // total - for a run-reset group, the rest of the run.
    [Serializable]
    public class CurrencyEarnedTotalCondition : Condition
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        private string _currencyId;

        [SerializeField]
        private double _value;

        public string CurrencyId => _currencyId;
        public double Value => _value;

        public CurrencyEarnedTotalCondition() { }

        public CurrencyEarnedTotalCondition(string currencyId, double value)
        {
            _currencyId = currencyId;
            _value = value;
        }

        public override bool Evaluate(ConditionContext context)
            => ThresholdIsMet(_value, context.Currencies.GetEarned(_currencyId));

        public override void Validate(ConditionContext context, string source)
        {
            context.Currencies.ValidateReference(_currencyId, source);
            ValidateThreshold(_value, source);
        }
    }
}
