using System.Text;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound generator's row (design doc 12.11): name, owned count, and a
    // button carrying the next cost. Visibility is the MODULE's visibleWhen,
    // never a decision here; pressability is Purchasing's own answer.
    public sealed class GeneratorRowUI : ModuleWidget
    {
        private readonly Label name;
        private readonly Label description;
        private readonly Label count;
        private readonly Button buy;

        private GeneratorDefinition generator;

        // The declaring scope, resolved once: the generator's count and its
        // cost are its declaring scope's facts (12.3/12.4).
        private ScopeState home;

        public GeneratorRowUI(VisualElement root) : base(root)
        {
            name = Require<Label>(root, "name", "GeneratorRow.uxml");
            description = Require<Label>(root, "description", "GeneratorRow.uxml");
            count = Require<Label>(root, "count", "GeneratorRow.uxml");
            buy = Require<Button>(root, "buy", "GeneratorRow.uxml");
        }

        protected override void OnBound()
        {
            generator = (GeneratorDefinition)Content;
            home = Producer.DeclaringScope<ScopeState>(Scope, generator);
            buy.clicked += () => Session.TryBuy(Context().Rebase(home), generator);
        }

        public override void Refresh()
        {
            var ctx = Context().Rebase(home);
            name.text = generator.displayName;
            // An absent description leaves the row one line tall.
            description.text = generator.description;
            description.style.display = string.IsNullOrEmpty(generator.description)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            count.text = "x" + ctx.GetOwnedCount(generator.Id);
            buy.text = NumberFormatter.Format(Purchasing.CostOf(generator, ctx))
                + " " + generator.costCurrency.displayName + UnitRateText(ctx);
            buy.SetEnabled(Purchasing.CanBuy(ctx, generator));
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
    }
}
