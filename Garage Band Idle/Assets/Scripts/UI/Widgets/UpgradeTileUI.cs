using System;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound upgrade as a tile (design doc 12.11), the strip's widget: a
    // placeholder icon, the name, and beneath it the price or "Owned". A
    // levelled upgrade's name carries its owned level in parentheses once one
    // is owned, and its price is the next level's until the last. A tap
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
        private readonly Label level;
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

        // The owned level the name shows in parentheses; zero shows none, and
        // an upgrade with no levels is always zero.
        public int ShownLevel { get; private set; }

        // The level count of a levelled upgrade, zero for one with none.
        // ShownOwned is "at ShownMaxLevel" for a levelled upgrade.
        public int ShownMaxLevel { get; private set; }

        public UpgradeTileUI(VisualElement root, IUpgradeDetailsOpener details) : base(root)
        {
            tile = Require<Button>(root, "tile", "UpgradeTile.uxml");
            name = Require<Label>(root, "name", "UpgradeTile.uxml");
            level = Require<Label>(root, "level", "UpgradeTile.uxml");
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
            ShownLevel = upgrade.LevelAt(declaring);
            ShownMaxLevel = upgrade.levels.Count;
            // The level is its own label, so a name cut short on the narrow
            // tile never takes the level with it.
            name.text = upgrade.displayName;
            level.text = ShownLevel > 0 ? $"({ShownLevel})" : string.Empty;
            level.style.display = ShownLevel > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            // Owned is the rule Purchasing itself refuses on (the latch, or the
            // upgrade's ownedWhen, or a levelled upgrade's last level), so a
            // tier reset that clears the latch leaves the tile buyable unless
            // ownedWhen still holds.
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
                // the upgrade's declaring scope like the command reads it (12.2)
                // - the next level's, for a levelled upgrade.
                ShownCost = Purchasing.CostOf(upgrade, ctx.Rebase(declaring));
                price.text = NumberFormatter.Format(ShownCost) + " " + upgrade.costCurrency.displayName;
            }
        }

        // An upgrade's name as the row and the details window's title show it:
        // the display name, and once a level is owned that level in
        // parentheses, "Crowd Work (2)". The tile shows the same two parts in
        // separate labels.
        internal static string NameAt(UpgradeDefinition upgrade, int level) =>
            level > 0 ? $"{upgrade.displayName} ({level})" : upgrade.displayName;

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
