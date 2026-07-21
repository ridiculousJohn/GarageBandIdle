using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "currency": the current balance of a currency is at least Amount.
    // Re-checks the live balance, so spending can un-meet it (contrast with
    // CurrencyEarnedTotalCondition).
    [Serializable]
    public class CurrencyBalanceCondition : Condition
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        private string _currencyId;

        [SerializeField]
        private double _amount;

        public string CurrencyId => _currencyId;
        public double Amount => _amount;

        // Unity's serializer needs a parameterless constructor on plain classes
        public CurrencyBalanceCondition() { }

        public CurrencyBalanceCondition(string currencyId, double amount)
        {
            _currencyId = currencyId;
            _amount = amount;
        }

        public override bool Evaluate(ConditionContext context)
            => context.Currencies.Get(_currencyId) >= _amount;

        public override void Validate(ConditionContext context, string source)
            => context.Currencies.ValidateReference(_currencyId, source);
    }
}
