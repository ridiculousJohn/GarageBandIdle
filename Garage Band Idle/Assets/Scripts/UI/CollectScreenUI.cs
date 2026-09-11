using RidiculousGaming.GarageBandIdle.Meta;
using RidiculousGaming.GarageBandIdle.Monetization;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The idle dialog (design doc 12.9): the offer's lines - currency name and
    // amount, all references, formatted - and three actions. OK settles through
    // ClaimIdle; Double It and Backstage Pass only REQUEST, and the payout is
    // the ad's or the store's own callback transaction (12.11). The dialog
    // shows what the session holds and computes nothing: what is shown is what
    // is paid, because only one offer is ever alive.
    public sealed class CollectScreenUI
    {
        public VisualElement Root { get; }

        private readonly GameSession session;
        private readonly VisualElement lines;

        // Held so Refresh can hide them: a Pass owner already has both rewards,
        // so the dialog is OK alone (section 9).
        private readonly Button doubleButton;
        private readonly Button passButton;

        // The offer the rows stand for, by identity: the session mints one offer
        // object per entry (12.9), so this is what tells a repaint under the
        // dialog apart from a dialog being raised over a different offer.
        private IdleOffer builtFor;

        public CollectScreenUI(VisualElement root, GameSession session, GameClock clock,
                               AdManager ads, IAPManager store)
        {
            Root = root;
            this.session = session;
            lines = ScreenHost.Require<VisualElement>(root, "lines");
            doubleButton = ScreenHost.Require<Button>(root, "double");
            doubleButton.clicked += ads.RequestIdleDouble;
            passButton = ScreenHost.Require<Button>(root, "pass");
            passButton.clicked += () => store.RequestPurchase(ProductId.BackstagePass);
            var ok = ScreenHost.Require<Button>(root, "ok");
            ok.clicked += () => session.ClaimIdle(clock.RealTimeUtc);
        }

        // The button set is a fact of the entitlement, so it is judged on every
        // pass: that is what a repaint under the dialog is for, an entitlement
        // written mid-dialog (12.9). The lines are a fact of the offer, built
        // when the offer is first shown and left alone after, because an
        // offer's amounts move only inside the transaction that settles it. No
        // interpolation: an offer is a fixed number over a window that ended.
        public void Refresh()
        {
            // Both requests buy what the Pass already gives, so an owner's
            // dialog is OK alone (section 9). Judged before the offer, because
            // the button set is a fact of the entitlement and not of the offer.
            var owned = BackstagePass.Owned(session.Root);
            doubleButton.style.display = owned ? DisplayStyle.None : DisplayStyle.Flex;
            passButton.style.display = owned ? DisplayStyle.None : DisplayStyle.Flex;

            var offer = session.CurrentOffer;
            // The host shows this screen only in AwaitingIdleClaim, where the
            // offer is non-null by the session's rule; the null arm is for a
            // headless caller, and the identity arm is every pass after the one
            // that built the rows.
            if (offer == null || offer == builtFor)
                return;
            builtFor = offer;
            lines.Clear();

            foreach (var line in offer.lines)
            {
                var row = new VisualElement();
                row.AddToClassList("currency-line");
                row.Add(new Label(line.currency.displayName));
                row.Add(new Label("+" + NumberFormatter.Format(line.amount)));
                lines.Add(row);
            }
        }
    }
}
