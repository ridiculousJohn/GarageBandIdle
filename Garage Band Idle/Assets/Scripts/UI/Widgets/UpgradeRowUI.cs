using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound upgrade's row (design doc 12.11): the generator row's shape
    // without a count, since the fact behind an upgrade is a latch rather than
    // a number. An owned upgrade (Purchasing.IsOwned: the latch, its
    // ownedWhen, or a levelled upgrade's last level) reads Bought, the way a
    // bar row reads Done - ownership is a fact this renders; visibility is the
    // module's visibleWhen. A levelled upgrade's name carries its owned level
    // in parentheses once one is owned, its price is the next level's, and its
    // description is the level it stands at, or the first before any is owned.
    public sealed class UpgradeRowUI : ModuleWidget
    {
        private readonly Label name;
        private readonly Label description;
        private readonly Button buy;

        private UpgradeDefinition upgrade;

        // True when the buy button reads as bought.
        public bool ShownBought { get; private set; }

        // The price on the buy button; zero while ShownBought.
        public BigNumber ShownCost { get; private set; } = BigNumber.Zero;

        // The owned level the name shows in parentheses; zero shows none, and
        // an upgrade with no levels is always zero.
        public int ShownLevel { get; private set; }

        // The level count of a levelled upgrade, zero for one with none.
        // ShownBought is "at ShownMaxLevel" for a levelled upgrade.
        public int ShownMaxLevel { get; private set; }

        public UpgradeRowUI(VisualElement root) : base(root)
        {
            name = Require<Label>(root, "name", "UpgradeRow.uxml");
            description = Require<Label>(root, "description", "UpgradeRow.uxml");
            buy = Require<Button>(root, "buy", "UpgradeRow.uxml");
        }

        protected override void OnBound()
        {
            upgrade = (UpgradeDefinition)Content;
            buy.clicked += () => Session.TryBuy(Context(), upgrade);
        }

        public override void Refresh()
        {
            var ctx = Context();
            var declaringCtx = ctx.Rebase(Producer.DeclaringScope<ScopeState>(ctx.Scope, upgrade));
            ShownLevel = upgrade.LevelAt(declaringCtx.Scope);
            ShownMaxLevel = upgrade.levels.Count;
            name.text = UpgradeTileUI.NameAt(upgrade, ShownLevel);
            // An absent description leaves the row one line tall.
            var text = upgrade.IsLevelled
                ? upgrade.levels[System.Math.Max(ShownLevel, 1) - 1].description
                : upgrade.description;
            description.text = text;
            description.style.display = string.IsNullOrEmpty(text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            // Owned is the rule Purchasing itself refuses on (the latch, the
            // upgrade's ownedWhen, or a levelled upgrade's last level), so a
            // tier reset that clears the latch leaves the row buyable unless
            // ownedWhen still holds.
            // The row dims whole while it cannot be bought, and reads as
            // settled once it has been (12.11).
            var purchased = Purchasing.IsOwned(ctx, upgrade);
            Root.EnableInClassList("row-bought", purchased);
            Root.EnableInClassList("row-unaffordable", !purchased && !Purchasing.CanBuy(ctx, upgrade));
            ShownBought = purchased;
            if (purchased)
            {
                ShownCost = BigNumber.Zero;
                buy.text = "Bought";
                buy.SetEnabled(false);
            }
            else
            {
                // The price the command would spend, factor included, read at the
                // upgrade's declaring scope like the command reads it (12.2) - the
                // next level's, for a levelled upgrade.
                ShownCost = Purchasing.CostOf(upgrade, declaringCtx);
                buy.text = NumberFormatter.Format(ShownCost) + " " + upgrade.costCurrency.displayName;
                buy.SetEnabled(Purchasing.CanBuy(ctx, upgrade));
            }
        }
    }
}
