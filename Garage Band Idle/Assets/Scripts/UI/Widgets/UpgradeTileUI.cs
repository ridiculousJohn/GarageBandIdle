using System;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound upgrade as a tile (design doc 12.11), the strip's widget: a
    // placeholder icon, the name, and beneath it the price or "Owned". A tap
    // buys through the purchase the upgrade row issues; a hold opens the
    // upgrade's details window through the host. A tile that cannot be bought
    // now is dimmed by a class and never disabled: a disabled element receives
    // no pointer events, and the hold is how a player reads what the upgrade
    // does before affording it. Visibility is the module's visibleWhen.
    public sealed class UpgradeTileUI : ModuleWidget
    {
        private readonly IUpgradeDetailsOpener details;
        private readonly Button tile;
        private readonly Label name;
        private readonly Label price;

        private UpgradeDefinition upgrade;

        // The scope declaring the upgrade, resolved once by the outward walk
        // (12.3): where its price is read and where the window reads ownership.
        private ScopeState declaring;

        public UpgradeDefinition Upgrade => upgrade;

        // True when the second line reads as owned.
        public bool ShownOwned { get; private set; }

        // True when a tap would buy now: not owned, and the purchase would
        // succeed. False dims the tile.
        public bool ShownBuyable { get; private set; }

        // The price on the second line; zero while ShownOwned.
        public BigNumber ShownCost { get; private set; } = BigNumber.Zero;

        public UpgradeTileUI(VisualElement root, IUpgradeDetailsOpener details) : base(root)
        {
            tile = Require<Button>(root, "tile", "UpgradeTile.uxml");
            name = Require<Label>(root, "name", "UpgradeTile.uxml");
            price = Require<Label>(root, "price", "UpgradeTile.uxml");
            // A tile with nowhere to send the hold would render a dead
            // gesture, and the host is the only caller (requirement 7).
            this.details = details ?? throw new ArgumentNullException(nameof(details));
            tile.clicked += Buy;
        }

        protected override void OnBound()
        {
            upgrade = Content as UpgradeDefinition
                ?? throw new InvalidOperationException(
                    $"The upgrade_tile module binds '{Content?.Id}', which is not an UpgradeDefinition (12.11).");
            declaring = Producer.DeclaringScope<ScopeState>(Scope, upgrade);
            // A tap buys and a hold opens the window. The hold's manipulator
            // lets go of the button's pointer when it fires, so the release
            // that ends a hold is never also a tap (LongPressManipulator).
            tile.AddManipulator(new LongPressManipulator(OpenDetails, Session.Config.longPressSeconds));
        }

        public override void Refresh()
        {
            var ctx = Context();
            name.text = upgrade.displayName;
            // Owned is the rule Purchasing itself refuses on (the latch, or the
            // upgrade's ownedWhen), so a tier reset that clears the latch leaves
            // the tile buyable unless ownedWhen still holds.
            var purchased = Purchasing.IsOwned(ctx, upgrade);
            ShownOwned = purchased;
            ShownBuyable = !purchased && Purchasing.CanBuy(ctx, upgrade);
            Root.EnableInClassList("tile-owned", purchased);
            Root.EnableInClassList("tile-unbuyable", !purchased && !ShownBuyable);
            if (purchased)
            {
                ShownCost = BigNumber.Zero;
                price.text = "Owned";
            }
            else
            {
                // The price the command would spend, factor included, read at
                // the upgrade's declaring scope like the command reads it (12.2).
                ShownCost = Purchasing.CostOf(upgrade, ctx.Rebase(declaring));
                price.text = NumberFormatter.Format(ShownCost) + " " + upgrade.costCurrency.displayName;
            }
        }

        // What a tap presses, public so a headless test presses what the
        // pointer presses: the upgrade row's purchase. The tile stays
        // pressable while dimmed, so a tap on an owned or unaffordable
        // upgrade does nothing here; the command is fail-closed besides.
        public void Buy()
        {
            var ctx = Context();
            if (Purchasing.IsOwned(ctx, upgrade) || !Purchasing.CanBuy(ctx, upgrade))
                return;
            Session.TryBuy(ctx, upgrade);
        }

        // What the hold calls, public for the reason Buy is: the details
        // window over this upgrade, at the declaring scope resolved at bind.
        public void OpenDetails() => details.OpenUpgradeDetails(upgrade, declaring);
    }
}
