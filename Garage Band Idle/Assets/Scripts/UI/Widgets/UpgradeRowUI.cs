using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One upgrade row (design doc 12.11): the generator row's shape without a
    // count, since the fact behind an upgrade is a latch rather than a number.
    public sealed class UpgradeRowUI
    {
        public VisualElement Root { get; }

        private readonly GameSession session;
        private readonly ScopeState scope;
        private readonly GameClock clock;
        private readonly UpgradeDefinition upgrade;
        private readonly Label nameLabel;
        private readonly Button buyButton;

        public UpgradeRowUI(GameSession session, ScopeState scope, GameClock clock, UpgradeDefinition upgrade)
        {
            this.session = session;
            this.scope = scope;
            this.clock = clock;
            this.upgrade = upgrade;

            Root = new VisualElement();
            Root.AddToClassList("row");
            nameLabel = new Label();
            nameLabel.AddToClassList("row-name");
            buyButton = new Button();
            buyButton.AddToClassList("row-buy");
            buyButton.clicked += () => this.session.TryBuy(Context(), this.upgrade);
            Root.Add(nameLabel);
            Root.Add(buyButton);
        }

        // Offered AND unbought: chapter 1's gates are progression conditions
        // that never exclude their own purchase, so without the purchased leg a
        // bought row would sit disabled forever (12.11).
        public bool Offered(GameContext ctx) =>
            upgrade.IsOffered(ctx) && !ctx.IsUpgradePurchased(upgrade.Id);

        public void Refresh()
        {
            var ctx = Context();
            nameLabel.text = upgrade.displayName;
            buyButton.text = NumberFormatter.Format(upgrade.cost) + " " + upgrade.costCurrency.displayName;
            buyButton.SetEnabled(Purchasing.CanBuy(ctx, upgrade));
        }

        private GameContext Context() => new GameContext(scope, clock.RealTimeUtc);
    }
}
