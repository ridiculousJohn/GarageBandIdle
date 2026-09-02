using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The idle dialog (design doc 12.9): the offer's lines - currency name and
    // amount, all references, formatted - and OK, which settles through
    // ClaimIdle. The dialog shows what the session holds and computes nothing:
    // what is shown is what is paid, because only one offer is ever alive.
    // "Double it" arrives with the AdManager; the button only requests the ad
    // and the callback's own transaction doubles and settles, so the dialog is
    // OK-only until then.
    public sealed class CollectScreenUI
    {
        public VisualElement Root { get; }

        private readonly GameSession session;
        private readonly VisualElement lines;

        public CollectScreenUI(VisualElement root, GameSession session, GameClock clock)
        {
            Root = root;
            this.session = session;
            lines = ScreenHost.Require<VisualElement>(root, "lines");
            var ok = ScreenHost.Require<Button>(root, "ok");
            ok.clicked += () => session.ClaimIdle(clock.RealTimeUtc);
        }

        // Rebuilt on every pass, because the offer object is replaced on every
        // entry and a repaint under the dialog (an entitlement written
        // mid-dialog, 12.9) must show the offer as it stands. No interpolation:
        // an offer is a fixed number over a window that already ended.
        public void Refresh()
        {
            lines.Clear();
            var offer = session.CurrentOffer;
            // The host shows this screen only in AwaitingIdleClaim, where the
            // offer is non-null by the session's rule; the guard is for a
            // headless caller.
            if (offer == null)
                return;

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
