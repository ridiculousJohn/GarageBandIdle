using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RidiculousGaming.GarageBandIdle.Meta;

namespace RidiculousGaming.GarageBandIdle.Monetization
{
    // The store side of the callback surface (design doc 12.11): plain C#,
    // owned by GameManager, and the only caller of the purchase commands. A
    // button REQUESTS; the grant is this class's own transaction, delivered
    // from the driver's Update so it never interleaves with a running command.
    public sealed class IAPManager
    {
        // One purchase in flight: what was bought and the store's completion.
        private sealed class Request
        {
            public ProductId Product;
            public Task<PurchaseResult> Result;
        }

        private readonly GameSession session;
        private readonly IStoreService store;
        private readonly GameConfig config;

        // The driver's one save site, called between the grant and the
        // acknowledge so a real store re-delivers anything not yet on disk.
        private readonly Action save;

        private readonly List<Request> pending = new();

        // At most one restore is in flight: it asks one idempotent question, so
        // a second request while the first is out would only ask it twice.
        private Task<IReadOnlyList<ProductId>> restore;

        public IAPManager(GameSession session, IStoreService store, GameConfig config, Action save)
        {
            this.session = session;
            this.store = store;
            this.config = config;
            this.save = save;
        }

        public void RequestPurchase(ProductId product) =>
            pending.Add(new Request { Product = product, Result = store.Purchase(product) });

        public void RequestRestore()
        {
            if (restore != null)
                return;
            restore = store.RestoreEntitlements();
        }

        // Called by the driver each frame after the session accumulates, so a
        // result always lands between transactions. A request is removed before
        // it is delivered: a throw out of the grant must not leave it to be
        // granted again next frame.
        public void Update(DateTime nowUtc)
        {
            for (var i = 0; i < pending.Count;)
            {
                var request = pending[i];
                if (!request.Result.IsCompleted)
                {
                    i++;
                    continue;
                }
                pending.RemoveAt(i);
                Deliver(request, nowUtc);
            }

            if (restore == null || !restore.IsCompleted)
                return;
            var restored = restore;
            restore = null;
            DeliverRestore(restored, nowUtc);
        }

        // GRANT, SAVE, ACKNOWLEDGE, in that order: a real store re-delivers any
        // transaction the app never acknowledged, so a throw out of the save
        // leaves the purchase unacknowledged and it arrives again. Failed,
        // Cancelled, and a faulted task alike grant nothing.
        private void Deliver(Request request, DateTime nowUtc)
        {
            if (request.Result.Status != TaskStatus.RanToCompletion)
                return;
            var result = request.Result.Result;
            if (result.Outcome != PurchaseOutcome.Succeeded)
                return;

            Grant(request.Product, nowUtc);
            save();
            store.Acknowledge(result.TransactionId);
        }

        // A Tip Jar buys nothing (design doc 9: no gated content), so it grants
        // nothing and still takes the save and the acknowledge every other
        // succeeded purchase takes. An unhandled product is a code fault: the
        // store would have already reported success against a grant that does
        // not exist.
        private void Grant(ProductId product, DateTime nowUtc)
        {
            switch (product)
            {
                case ProductId.BackstagePass:
                    session.PurchasePassFromDialog(nowUtc);
                    break;
                case ProductId.RoadieBundleSmall:
                    session.GrantRoadies(config.roadieBundleSmall, nowUtc);
                    break;
                case ProductId.RoadieBundleMedium:
                    session.GrantRoadies(config.roadieBundleMedium, nowUtc);
                    break;
                case ProductId.RoadieBundleLarge:
                    session.GrantRoadies(config.roadieBundleLarge, nowUtc);
                    break;
                case ProductId.TipJarSmall:
                case ProductId.TipJarMedium:
                case ProductId.TipJarLarge:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"IAPManager: product '{product}' has no grant - the store reported a purchase nothing delivers.");
            }
        }

        // Restoration is the entitlement write for each id the store answers
        // with, and one save if anything was written. A consumable is never
        // restored, so a bundle in the list is ignored. The write is idempotent
        // by construction - the id is in root's set or it is not - so no ledger
        // of processed transactions is needed here.
        private void DeliverRestore(Task<IReadOnlyList<ProductId>> completed, DateTime nowUtc)
        {
            if (completed.Status != TaskStatus.RanToCompletion || completed.Result == null)
                return;

            var wrote = false;
            foreach (var product in completed.Result)
            {
                if (product != ProductId.BackstagePass)
                    continue;
                session.GrantEntitlement(BackstagePass.EntitlementId, nowUtc);
                wrote = true;
            }
            if (wrote)
                save();
        }
    }
}
