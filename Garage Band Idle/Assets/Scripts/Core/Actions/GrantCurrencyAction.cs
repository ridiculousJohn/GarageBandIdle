using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Pays a flat amount of one currency, once. The chapter-1 case is the
    // capstone's first Roadie (design doc sections 1 and 2) - the first award in
    // the game that must never be paid twice, which is why the GameAction family
    // exists at all.
    //
    // Which POOL the award lands in is not this action's decision and must not
    // be: it adds through ICurrencies, so the router resolves the id to whichever
    // pool owns it. A global currency (Records, Roadies) therefore lands in the
    // permanent pool from a frontier economy and in the sandbox's own private pool
    // from an event context, with no branch here and no way for a sandbox to spend
    // the player's permanent progress.
    //
    // Nothing composes: an award is not production. CurrencyManager.Add applies no
    // modifiers, so a granted Roadie is one Roadie regardless of the income
    // multipliers standing at the time (design doc section 9's boundary - only
    // producers compose).
    [Serializable]
    public class GrantCurrencyAction : GameAction
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency to pay. May name a global currency (records, roadies) - the router resolves it to the owning pool.")]
        private string _currencyId;

        [SerializeField]
        [Tooltip("Flat amount to award. Must be positive - an award is never a charge.")]
        private double _amount;

        public string CurrencyId => _currencyId;
        public double Amount => _amount;

        // Unity's serializer needs a parameterless constructor on plain classes
        public GrantCurrencyAction() { }

        public GrantCurrencyAction(string currencyId, double amount)
        {
            _currencyId = currencyId;
            _amount = amount;
        }

        // A non-positive amount grants nothing (Execute refuses it), and an id no
        // reachable pool holds has nowhere to land - either way the award is not
        // real, and the asking operation must refuse BEFORE it charges. Resolved
        // through the same surface Execute pays through (the router), silently:
        // the refusal is the operation's report, not this probe's.
        public override bool CanExecute(EffectContext context)
            => _amount > 0 && context.Currencies.Contains(_currencyId);

        public override void Execute(EffectContext context)
        {
            // fail closed on broken content (boot validation reports it): a
            // non-positive award would charge the player or do nothing, and
            // neither is what authoring a grant means
            if (_amount <= 0)
            {
                Debug.LogError($"GameAction: grantCurrency for '{_currencyId}' has a non-positive amount ({_amount}). Nothing granted.");
                return;
            }

            context.Currencies.Add(_currencyId, _amount);
        }

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_currencyId))
            {
                Debug.LogError($"GameAction: {source} has a grantCurrency action with an empty currency id.");
                return;
            }

            // Resolved through the validating chapter's reachable set, which
            // includes every GLOBAL currency precisely because the router reaches
            // the permanent pool from any chapter (ChapterCurrencies). So awarding
            // Roadies from Chapter 1 validates without Roadies being in Chapter
            // 1's roster - a global currency in a chapter roster is the mistake,
            // not a global currency in a chapter's award.
            context.Currencies.ValidateReference(_currencyId, source);

            if (_amount <= 0)
                Debug.LogError($"GameAction: {source} grants a non-positive amount ({_amount}) of '{_currencyId}' - an award is never a charge.");
        }
    }
}
