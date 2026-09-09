using System.Collections.Generic;
using System.Threading.Tasks;

namespace RidiculousGaming.GarageBandIdle.Monetization
{
    // Everything the store sells (design doc 9): the lifetime Pass, the Roadie
    // bundles, and the Tip Jar tiers. A closed, code-defined set, so an enum -
    // a product is code plus a store listing, never authored content.
    public enum ProductId
    {
        BackstagePass,
        RoadieBundleSmall,
        RoadieBundleMedium,
        RoadieBundleLarge,
        TipJarSmall,
        TipJarMedium,
        TipJarLarge
    }

    // How a purchase attempt ended. Failed and Cancelled are ordinary answers;
    // only Succeeded delivers.
    public enum PurchaseOutcome
    {
        Succeeded,
        Failed,
        Cancelled
    }

    public readonly struct PurchaseResult
    {
        public readonly PurchaseOutcome Outcome;

        // Null unless Succeeded: the id the acknowledge names, and the only
        // thing a store needs back from us.
        public readonly string TransactionId;

        public PurchaseResult(PurchaseOutcome outcome, string transactionId)
        {
            Outcome = outcome;
            TransactionId = transactionId;
        }
    }

    // What a store SDK presents, in the same request-now-result-later shape the
    // ad seam takes.
    public interface IStoreService
    {
        Task<PurchaseResult> Purchase(ProductId product);

        // A real store re-delivers any transaction never acknowledged, which is
        // why the manager acknowledges only AFTER the grant is saved.
        void Acknowledge(string transactionId);

        Task<IReadOnlyList<ProductId>> RestoreEntitlements();
    }
}
