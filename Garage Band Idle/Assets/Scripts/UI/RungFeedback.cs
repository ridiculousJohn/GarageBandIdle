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

        // A refusal as the player reads it (design doc 12.5/12.11): the event
        // by its displayName, in the one sentence every site uses - the rung
        // button and the event row render the same line, because it explains
        // the same fact. Naming the event is the whole point of the leg, so a
        // refusal whose record names an event its host does not declare is a
        // fault rather than a line with a hole in it: the save filter drops
        // such a record at load, so reaching this means the state was built
        // some other way (requirement 7).
        public static string RefusalText(Refusal refusal)
        {
            if (refusal.Event == null)
                throw new System.InvalidOperationException(
                    $"A refusal at scope '{refusal.Host.ScopeId}' names event '{refusal.Record.eventId}', which that scope does not declare.");
            return string.Format("Claim your {0} reward first", refusal.Event.displayName);
        }
    }
}
