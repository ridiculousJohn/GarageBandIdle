using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound upgrade's row (design doc 12.11): the generator row's shape
    // without a count, since the fact behind an upgrade is a latch rather than
    // a number. A bought upgrade reads Bought, the way a bar row reads Done -
    // the latch is a fact this renders; visibility is the module's visibleWhen.
    public sealed class UpgradeRowUI : ModuleWidget
    {
        private readonly Label name;
        private readonly Label description;
        private readonly Button buy;

        private UpgradeDefinition upgrade;

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
            name.text = upgrade.displayName;
            // An absent description leaves the row one line tall.
            description.text = upgrade.description;
            description.style.display = string.IsNullOrEmpty(upgrade.description)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            // The purchased latch is the one-shot Purchasing itself reads, so a
            // tier reset that clears it leaves the row reading as buyable.
            if (ctx.IsUpgradePurchased(upgrade.Id))
            {
                buy.text = "Bought";
                buy.SetEnabled(false);
            }
            else
            {
                buy.text = NumberFormatter.Format(upgrade.cost) + " " + upgrade.costCurrency.displayName;
                buy.SetEnabled(Purchasing.CanBuy(ctx, upgrade));
            }
        }
    }
}
