using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // A rung's info window (design doc 12.11), opened by the (i) on its
    // prestige card: the rung's label as the title, its authored info text as
    // the body, and Close in the accent at the bottom right. Both texts are
    // authored and fixed, so nothing here reads state; the rung handed over is
    // the whole request.
    public sealed class RungInfoUI
    {
        public VisualElement Root { get; }

        private readonly Action close;
        private readonly Label title;
        private readonly Label body;

        private Rung rung;

        public bool IsOpen { get; private set; }

        // Null when nothing is held: the rung is the request, and the host asks
        // for the window only after handing one over.
        public Rung ShownRung => rung;

        public RungInfoUI(VisualElement rootElement, Action close)
        {
            Root = rootElement;
            this.close = close;
            title = ScreenHost.Require<Label>(rootElement, "rung-info-title");
            body = ScreenHost.Require<Label>(rootElement, "rung-info-text");
            ScreenHost.Require<Button>(rootElement, "rung-info-close").clicked += Close;
        }

        public void Hold(Rung rung) => this.rung = rung;

        public void Show()
        {
            if (rung == null)
                throw new InvalidOperationException(
                    "RungInfoUI.Show with no rung held (design doc 12.11).");
            title.text = rung.label;
            body.text = rung.info;
            Root.style.display = DisplayStyle.Flex;
            IsOpen = true;
        }

        // What the Close button calls, public so a headless test presses what
        // the pointer presses: the host's close, which drops the request.
        public void Close() => close();

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
            IsOpen = false;
        }

        // The request is dropped, so the rung goes with it and the window comes
        // down.
        public void Drop()
        {
            rung = null;
            Hide();
        }
    }
}
