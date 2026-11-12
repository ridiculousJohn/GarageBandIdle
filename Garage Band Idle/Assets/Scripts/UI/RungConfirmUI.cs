using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // A rung's confirmation window (design doc 12.11), opened by Confirm on a
    // prestige card whose rung authors confirmText: the label with a question
    // mark as the title, the confirmText as the body, and Cancel and Confirm at
    // the bottom right. Confirm presses the rung through the same TryRung the
    // card presses, at the scope the card handed over, so the window adds a
    // question and never a path around the gate.
    public sealed class RungConfirmUI
    {
        public VisualElement Root { get; }

        private readonly GameSession session;
        private readonly GameClock clock;
        private readonly Action close;
        private readonly Label title;
        private readonly Label body;

        private Rung rung;

        // The card's own evaluation scope, which is where TryRung reads the
        // rung (12.5), so nothing here walks for it.
        private ScopeState scope;

        public bool IsOpen { get; private set; }

        // Null when nothing is held: the pair is the request, and the host asks
        // for the window only after handing one over.
        public Rung ShownRung => rung;

        public ScopeState HeldScope => scope;

        public RungConfirmUI(VisualElement rootElement, GameSession session, GameClock clock, Action close)
        {
            Root = rootElement;
            this.session = session;
            this.clock = clock;
            this.close = close;
            title = ScreenHost.Require<Label>(rootElement, "rung-confirm-title");
            body = ScreenHost.Require<Label>(rootElement, "rung-confirm-text");
            ScreenHost.Require<Button>(rootElement, "rung-confirm-cancel").clicked += Cancel;
            ScreenHost.Require<Button>(rootElement, "rung-confirm-ok").clicked += Confirm;
        }

        public void Hold(Rung rung, ScopeState scope)
        {
            this.rung = rung;
            this.scope = scope;
        }

        public void Show()
        {
            if (rung == null)
                throw new InvalidOperationException(
                    "RungConfirmUI.Show with no rung held (design doc 12.11).");
            title.text = rung.label + "?";
            body.text = rung.confirmText;
            Root.style.display = DisplayStyle.Flex;
            IsOpen = true;
        }

        // What the Confirm button calls, public so a headless test presses what
        // the pointer presses. The window comes down first, so the rung's own
        // refresh renders the screen it leaves behind; TryRung is fail-closed,
        // so a gate that closed while the window stood runs nothing.
        public void Confirm()
        {
            if (rung == null)
                return;
            var ctx = new GameContext(scope, clock.RealTimeUtc);
            close();
            session.TryRung(ctx);
        }

        // What the Cancel button calls: the host's close, and nothing runs.
        public void Cancel() => close();

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
            IsOpen = false;
        }

        // The request is dropped, so the pair goes with it and the window comes
        // down.
        public void Drop()
        {
            rung = null;
            scope = null;
            Hide();
        }
    }
}
