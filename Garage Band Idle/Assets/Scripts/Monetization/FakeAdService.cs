using System.Collections.Generic;
using System.Threading.Tasks;

namespace RidiculousGaming.GarageBandIdle.Monetization
{
    // The ad seam until a real SDK arrives. The task is already complete when
    // it is handed back; the asynchrony that matters is the MANAGER's, which
    // delivers only from the driver's Update, so a callback still lands under a
    // phase that may have changed.
    public sealed class FakeAdService : IAdService
    {
        // The scripted outcome, so a test or the editor forces an abort or a
        // failure without a network.
        public AdResult NextResult = AdResult.Rewarded;

        // What was requested, in order.
        public readonly List<AdPlacement> Shown = new();

        public Task<AdResult> ShowRewarded(AdPlacement placement)
        {
            Shown.Add(placement);
            return Task.FromResult(NextResult);
        }
    }
}
