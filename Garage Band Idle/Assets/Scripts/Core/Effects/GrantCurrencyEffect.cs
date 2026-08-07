using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON effect "grantCurrency": pays a flat amount of one currency. The
    // chapter-1 case is the capstone's first Roadie (design doc sections 1 and 2),
    // and it is the first effect in the game that is not safe to replay - which is
    // why EffectProjection exists at all.
    //
    // Which POOL the award lands in is not this effect's decision and must not
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
    public class GrantCurrencyEffect : GameEffect
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
        public GrantCurrencyEffect() { }

        public GrantCurrencyEffect(string currencyId, double amount)
        {
            _currencyId = currencyId;
            _amount = amount;
        }

        // One-shot: the earned total this feeds is monotonic, so a second
        // application is a second payment with nothing to undo it. The inherited
        // Project does nothing at all for this reason; only an acquisition pays.
        public override EffectProjection Projection => EffectProjection.OneShot;

        // The scope parameter is deliberately unused, and for a sharper reason
        // than SetFlagEffect's: a paid balance has no lifetime of its own to
        // declare. How long the award survives is the CURRENCY GROUP's call -
        // resetsOnAlbumRelease decides whether a release takes it back - exactly
        // as it is for every other balance in the game. A scope here would be a
        // second answer able to disagree with the group.
        public override void ApplyOnAcquisition(EffectContext context, ContentScope scope)
        {
            // fail closed on broken content (boot validation reports it): a
            // non-positive award would charge the player or do nothing, and
            // neither is what authoring a grant means
            if (_amount <= 0)
            {
                Debug.LogError($"GameEffect: grantCurrency for '{_currencyId}' has a non-positive amount ({_amount}). Nothing granted.");
                return;
            }

            context.Currencies.Add(_currencyId, _amount);
        }

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_currencyId))
            {
                Debug.LogError($"GameEffect: {source} has a grantCurrency effect with an empty currency id.");
                return;
            }

            // Resolved through the validating chapter's reachable set, which
            // includes every GLOBAL currency precisely because the router reaches
            // the permanent pool from any chapter (ChapterCurrencies). So awarding
            // Roadies from Chapter 1 validates without Roadies being in Chapter
            // 1's roster - a global currency in a chapter roster is the mistake,
            // not a global currency in a chapter's payload.
            context.Currencies.ValidateReference(_currencyId, source);

            if (_amount <= 0)
                Debug.LogError($"GameEffect: {source} grants a non-positive amount ({_amount}) of '{_currencyId}' - an award is never a charge.");
        }
    }
}
