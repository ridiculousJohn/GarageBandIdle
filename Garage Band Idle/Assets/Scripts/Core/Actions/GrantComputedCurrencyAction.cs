using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Pays a COMPUTED amount of one currency, once: the payout of a prestige
    // rung (design doc section 5, rule 14). The amount is a PayoutFormula - the
    // formula is the parameter, so "does this rung have a payout" is never a
    // branch: a rung that awards nothing simply has no action, and a payout is
    // one of the rung's GameActions rather than a field of its own (6.5's
    // classification: payouts are actions on the player-action moment that
    // earns them, unreachable from every release, load and projection).
    //
    // Zero is a legal payout and Execute still counts as executed: a release at
    // zero fans banks nothing and still resets the run. Which pool the award
    // lands in is the router's decision, exactly as GrantCurrencyAction - the
    // formula reads outward and the grant resolves outward, with no branch here.
    [Serializable]
    public class GrantComputedCurrencyAction : GameAction
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency to pay - Ch1: records. The router resolves it to the owning pool.")]
        private string _currencyId;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("The amount: math over balances the executing scope can reach - Ch1: floor((fans/5)^0.5).")]
        private PayoutFormula _amount;

        public string CurrencyId => _currencyId;
        public PayoutFormula Amount => _amount;

        // Unity's serializer needs a parameterless constructor on plain classes
        public GrantComputedCurrencyAction() { }

        public GrantComputedCurrencyAction(string currencyId, PayoutFormula amount)
        {
            _currencyId = currencyId;
            _amount = amount;
        }

        // Structural validity plus a sane evaluation: the target must be
        // reachable, the formula must exist, every input it declares must be
        // reachable, and the computed amount must not be negative. ZERO passes -
        // a zero payout is a real press (the reset still runs) - so unlike
        // GrantCurrencyAction this does not demand a positive amount.
        public override bool CanExecute(EffectContext context)
        {
            if (_amount == null || string.IsNullOrEmpty(_currencyId) || !context.Currencies.Contains(_currencyId))
                return false;

            foreach (var inputId in _amount.InputCurrencyIds)
            {
                if (string.IsNullOrEmpty(inputId) || !context.Currencies.Contains(inputId))
                    return false;
            }

            return _amount.Evaluate(context) >= BigNumber.Zero;
        }

        public override void Execute(EffectContext context)
        {
            // fail closed on broken content (boot validation reports it): no
            // formula computes nothing, and a negative amount would charge the
            // player for pressing an award
            if (_amount == null)
            {
                Debug.LogError($"GameAction: grantComputedCurrency for '{_currencyId}' has no formula. Nothing granted.");
                return;
            }

            var amount = _amount.Evaluate(context);
            if (amount < BigNumber.Zero)
            {
                Debug.LogError($"GameAction: grantComputedCurrency for '{_currencyId}' computed a negative amount. Nothing granted.");
                return;
            }

            if (amount > BigNumber.Zero)
                context.Currencies.Add(_currencyId, amount);
        }

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_currencyId))
            {
                Debug.LogError($"GameAction: {source} has a grantComputedCurrency action with an empty currency id.");
                return;
            }

            context.Currencies?.ValidateReference(_currencyId, source);

            if (_amount == null)
                Debug.LogError($"GameAction: {source} has a grantComputedCurrency action for '{_currencyId}' with no formula - the payout would compute nothing, ever.");
            else
                _amount.Validate(context, source);
        }
    }
}
