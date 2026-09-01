using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The rung-specific half of the feedback contract (design doc 12.11).
    // Pressability is Rung.IsOffered and the legs are GateFeedback over
    // offerCondition - the same objects TryRung enforces - so this class holds
    // only what a rung adds: the payout preview.
    public static class RungFeedback
    {
        // "would bank: N" (design doc 5): the rung's FIRST action through the
        // same Compute the execution runs, and only when it is an AddCurrency.
        // First-only is what makes parity hold by construction - nothing has
        // mutated when the first action evaluates, while even a second
        // AddCurrency may read what the first deposited. Any other opening kind
        // previews nothing rather than a wrong number. ctx is the rung's own
        // scope, as for Execute.
        public static bool TryPreviewPayout(Rung rung, GameContext ctx,
            out BigNumber amount, out IReadOnlyList<CurrencyDefinition> currencies)
        {
            if (rung.actions.Count == 0 || rung.actions[0] is not AddCurrency payout)
            {
                amount = BigNumber.Zero;
                currencies = System.Array.Empty<CurrencyDefinition>();
                return false;
            }
            amount = payout.Compute(ctx);
            currencies = payout.currencies;
            return true;
        }
    }
}
