using System.Text;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One buyable row (design doc 12.11): name, owned count, and a button
    // carrying the next cost. Pressability is Purchasing's own answer, never a
    // rule restated here - the domain owns the gate.
    public sealed class GeneratorRowUI
    {
        public VisualElement Root { get; }

        private readonly GameSession session;
        private readonly ScopeState scope;
        private readonly GameClock clock;
        private readonly GeneratorDefinition generator;
        private readonly Label nameLabel;
        private readonly Label countLabel;
        private readonly Button buyButton;

        public GeneratorRowUI(GameSession session, ScopeState scope, GameClock clock, GeneratorDefinition generator)
        {
            this.session = session;
            this.scope = scope;
            this.clock = clock;
            this.generator = generator;

            Root = new VisualElement();
            Root.AddToClassList("row");
            nameLabel = new Label();
            nameLabel.AddToClassList("row-name");
            countLabel = new Label();
            countLabel.AddToClassList("row-count");
            buyButton = new Button();
            buyButton.AddToClassList("row-buy");
            buyButton.clicked += () => this.session.TryBuy(Context(), this.generator);
            Root.Add(nameLabel);
            Root.Add(countLabel);
            Root.Add(buyButton);
        }

        // The list's filter, asked with the list's own context: a list module's
        // evaluation scope IS the declaring scope, so nothing rebases.
        public bool Available(GameContext ctx) => generator.IsAvailable(ctx);

        public void Refresh()
        {
            var ctx = Context();
            nameLabel.text = generator.displayName;
            countLabel.text = "x" + ctx.GetOwnedCount(generator.Id);
            buyButton.text = NumberFormatter.Format(Purchasing.CostOf(generator, ctx))
                + " " + generator.costCurrency.displayName + UnitRateText(ctx);
            buyButton.SetEnabled(Purchasing.CanBuy(ctx, generator));
        }

        // "cost => yield", the reference game's row: what one more unit pays,
        // through the same resolution the tick sums (12.5). A currency the unit
        // pays nothing is not a line, and a unit paying nothing has no arrow.
        private string UnitRateText(GameContext ctx)
        {
            var text = new StringBuilder();
            foreach (var (currency, amount) in Producer.UnitRate(ctx, generator))
            {
                if (amount == BigNumber.Zero)
                    continue;
                text.Append(text.Length == 0 ? " => " : ", ");
                text.Append(NumberFormatter.Format(amount)).Append(" ").Append(currency.displayName);
            }
            return text.ToString();
        }

        private GameContext Context() => new GameContext(scope, clock.RealTimeUtc);
    }
}
