using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "currency": the current balance of a currency is at least Value.
    // Re-checks the live balance, so spending can un-meet it (contrast with
    // CurrencyEarnedTotalCondition).
    [Serializable]
    public class CurrencyBalanceCondition : Condition
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        private string _currencyId;

        [SerializeField]
        private double _value;

        public string CurrencyId => _currencyId;
        public double Value => _value;

        // Unity's serializer needs a parameterless constructor on plain classes
        public CurrencyBalanceCondition() { }

        public CurrencyBalanceCondition(string currencyId, double value)
        {
            _currencyId = currencyId;
            _value = value;
        }

        public override bool Evaluate(ConditionContext context)
            => ThresholdIsMet(_value, context.Currencies.Get(_currencyId));

        public override void Validate(ConditionContext context, string source)
        {
            context.Currencies.ValidateReference(_currencyId, source);
            ValidateThreshold(_value, source);
        }
    }
}
