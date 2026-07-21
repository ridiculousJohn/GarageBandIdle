using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "currencyEarnedTotal": the lifetime earned total of a currency is
    // at least Value. Spending never lowers it (CurrencyManager.GetLifetimeEarned),
    // so once met it stays met for the run.
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
            => context.Currencies.GetLifetimeEarned(_currencyId) >= _value;

        public override void Validate(ConditionContext context, string source)
            => context.Currencies.ValidateReference(_currencyId, source);
    }
}
