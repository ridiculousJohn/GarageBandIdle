using System;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // An upgrade's details window (design doc 12.11), opened by a hold on its
    // tile: the upgrade's name, and what it does - what buying it will do while
    // it is not bought, what it is doing once it is. A levelled upgrade says
    // both while it is partly bought: the level it stands at as its current
    // effect, and the level the next purchase buys as Next; its title carries
    // the owned level in parentheses once one is owned. Close at the bottom
    // right. No command of its own, and no state beyond the upgrade and the
    // scope the tile's hold handed over.
    public sealed class UpgradeDetailsUI
    {
        public VisualElement Root { get; }

        private readonly GameClock clock;
        private readonly Action close;
        private readonly Label name;
        private readonly Label effect;
        private readonly Label next;

        private UpgradeDefinition upgrade;

        // The scope the tile resolved as the upgrade's declaring one, which is
        // where its latch and level are facts and its ownedWhen is asked
        // (12.3), so nothing here walks for it.
        private ScopeState scope;

        public bool IsOpen { get; private set; }

        // Null when nothing is held: the pair is the request, and the host asks
        // for the window only after handing one over.
        public UpgradeDefinition ShownUpgrade => upgrade;

        public ScopeState HeldScope => scope;

        // True when the upgrade reads as owned - nothing left to buy, so only
        // the current effect is shown.
        public bool ShownOwned { get; private set; }

        // True when the "Current effect" paragraph shows: the upgrade is bought,
        // or a levelled one stands at a level.
        public bool ShownCurrent { get; private set; }

        // True when the forward paragraph shows: "On buy" before any purchase,
        // "Next" while a levelled upgrade has levels left after the one owned.
        public bool ShownNext { get; private set; }

        // The owned level the title shows in parentheses; zero shows none, and
        // an upgrade with no levels is always zero.
        public int ShownLevel { get; private set; }

        // The level each paragraph describes, zero while that paragraph is
        // hidden or the upgrade has no levels.
        public int ShownCurrentLevel { get; private set; }
        public int ShownNextLevel { get; private set; }

        public UpgradeDetailsUI(VisualElement rootElement, GameClock clock, Action close)
        {
            Root = rootElement;
            this.clock = clock;
            this.close = close;
            name = ScreenHost.Require<Label>(rootElement, "details-name");
            effect = ScreenHost.Require<Label>(rootElement, "details-effect");
            next = ScreenHost.Require<Label>(rootElement, "details-next");
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
        // stands, so a purchase made beneath it turns the paragraphs with no
        // reopen.
        public void Refresh()
        {
            var ctx = new GameContext(scope, clock.RealTimeUtc);
            ShownOwned = Purchasing.IsOwned(ctx, upgrade);
            ShownLevel = upgrade.LevelAt(scope);
            name.text = UpgradeTileUI.NameAt(upgrade, ShownLevel);

            string current, forward;
            if (upgrade.IsLevelled)
            {
                ShownCurrentLevel = ShownLevel;
                ShownNextLevel = ShownOwned ? 0 : ShownLevel + 1;
                current = ShownCurrentLevel > 0 ? upgrade.levels[ShownCurrentLevel - 1].description : null;
                forward = ShownNextLevel > 0 ? upgrade.levels[ShownNextLevel - 1].description : null;
                ShownCurrent = ShownCurrentLevel > 0;
                ShownNext = ShownNextLevel > 0;
            }
            else
            {
                ShownCurrentLevel = 0;
                ShownNextLevel = 0;
                ShownCurrent = ShownOwned;
                ShownNext = !ShownOwned;
                current = forward = upgrade.description;
            }

            effect.text = ShownCurrent ? "Current effect: " + current : string.Empty;
            effect.style.display = ShownCurrent ? DisplayStyle.Flex : DisplayStyle.None;
            // "On buy" while nothing is owned; "Next" once a level is.
            next.text = ShownNext ? (ShownCurrent ? "Next: " : "On buy: ") + forward : string.Empty;
            next.style.display = ShownNext ? DisplayStyle.Flex : DisplayStyle.None;
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
            ShownCurrent = false;
            ShownNext = false;
            ShownLevel = 0;
            ShownCurrentLevel = 0;
            ShownNextLevel = 0;
            Hide();
        }
    }
}
