using System;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // An upgrade's details window (design doc 12.11), opened by a hold on its
    // tile: the upgrade's name, and one paragraph saying what it does - what
    // buying it will do while it is not bought, what it is doing once it is.
    // Close at the bottom right. No command of its own, and no state beyond the
    // upgrade and the scope the tile's hold handed over.
    public sealed class UpgradeDetailsUI
    {
        public VisualElement Root { get; }

        private readonly GameClock clock;
        private readonly Action close;
        private readonly Label name;
        private readonly Label effect;

        private UpgradeDefinition upgrade;

        // The scope the tile resolved as the upgrade's declaring one, which is
        // where its latch is a fact and its ownedWhen is asked (12.3), so
        // nothing here walks for it.
        private ScopeState scope;

        public bool IsOpen { get; private set; }

        // Null when nothing is held: the pair is the request, and the host asks
        // for the window only after handing one over.
        public UpgradeDefinition ShownUpgrade => upgrade;

        public ScopeState HeldScope => scope;

        // True when the paragraph reads as the upgrade's current effect, false
        // when it reads as what a purchase would do.
        public bool ShownOwned { get; private set; }

        public UpgradeDetailsUI(VisualElement rootElement, GameClock clock, Action close)
        {
            Root = rootElement;
            this.clock = clock;
            this.close = close;
            name = ScreenHost.Require<Label>(rootElement, "details-name");
            effect = ScreenHost.Require<Label>(rootElement, "details-effect");
            ScreenHost.Require<Button>(rootElement, "details-close").clicked += Close;
        }

        public void Hold(UpgradeDefinition upgrade, ScopeState scope)
        {
            this.upgrade = upgrade;
            this.scope = scope;
        }

        public void Show()
        {
            if (upgrade == null)
                throw new InvalidOperationException(
                    "UpgradeDetailsUI.Show with no upgrade held (design doc 12.11).");
            Root.style.display = DisplayStyle.Flex;
            IsOpen = true;
            Refresh();
        }

        // The host calls this through Show on every refresh while the window
        // stands, so a purchase made beneath it turns the paragraph with no
        // reopen.
        public void Refresh()
        {
            var ctx = new GameContext(scope, clock.RealTimeUtc);
            name.text = upgrade.displayName;
            ShownOwned = Purchasing.IsOwned(ctx, upgrade);
            effect.text = (ShownOwned ? "Current effect: " : "On buy: ") + upgrade.description;
        }

        // What the Close button calls, public so a headless test presses what
        // the pointer presses: the host's close, which drops the request.
        public void Close() => close();

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
            IsOpen = false;
        }

        // The request is dropped, so the pair goes with it and the window
        // comes down.
        public void Drop()
        {
            upgrade = null;
            scope = null;
            ShownOwned = false;
            Hide();
        }
    }
}
