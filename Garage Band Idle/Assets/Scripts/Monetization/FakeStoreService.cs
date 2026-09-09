using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RidiculousGaming.GarageBandIdle.Monetization
{
    // The store seam until a real SDK arrives, in the same already-complete
    // shape the fake ad takes. What it does NOT carry is the launch-time
    // redelivery of an unacknowledged consumable: a fake would never fire one,
    // so the route lands with the real SDK.
    public sealed class FakeStoreService : IStoreService
    {
        // The scripted outcome, so a test or the editor forces a failed or
        // cancelled purchase.
        public PurchaseOutcome NextOutcome = PurchaseOutcome.Succeeded;

        // What RestoreEntitlements answers. A succeeded Pass purchase adds to
        // it, so restore-after-buy behaves the way a real store does.
        public readonly List<ProductId> Owned = new();

        public readonly List<(ProductId product, string transactionId)> Purchases = new();
        public readonly List<string> Acknowledged = new();

        private int nextTransaction;

        public Task<PurchaseResult> Purchase(ProductId product)
        {
            if (NextOutcome != PurchaseOutcome.Succeeded)
                return Task.FromResult(new PurchaseResult(NextOutcome, null));

            var transactionId = "fake-" + nextTransaction++;
            Purchases.Add((product, transactionId));
            if (product == ProductId.BackstagePass && !Owned.Contains(product))
                Owned.Add(product);
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.Succeeded, transactionId));
        }

        public void Acknowledge(string transactionId) => Acknowledged.Add(transactionId);

        // A copy, because the caller iterates it while the game may buy again.
        public Task<IReadOnlyList<ProductId>> RestoreEntitlements() =>
            Task.FromResult<IReadOnlyList<ProductId>>(Owned.ToList());
    }
}
