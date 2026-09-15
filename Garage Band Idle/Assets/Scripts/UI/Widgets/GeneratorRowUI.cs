using System;
using System.Text;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound generator's row (design doc 12.11): name, owned count, the cost
    // and yield line beneath the name, and two buy buttons - "+1" and "+M", M
    // the largest count the balance affords. A long press on the text opens the
    // info screen, which is where the description is read. Visibility is the
    // MODULE's visibleWhen, never a decision here; pressability is Purchasing's
    // own answer.
    public sealed class GeneratorRowUI : ModuleWidget
    {
        private readonly Label name;
        private readonly Label yieldLine;
        private readonly Label count;
        private readonly Button buy;
        private readonly Button buyMax;

        // The hold's target: the text, never the button beside it.
        private readonly VisualElement text;

        // The host, behind the one method a row needs of it: the info screen is
        // the host's, and the row hands over the generator and its scope.
        private readonly IGeneratorInfoOpener infos;

        private GeneratorDefinition generator;

        // The count the last Refresh printed into the max button, kept as an
        // int beside the label: the tap buys the count the player saw, and
        // parsing the text back would be a second source for that number.
        private int shownMax;

        // The declaring scope, resolved once: the generator's count and its
        // cost are its declaring scope's facts (12.3/12.4).
        private ScopeState home;

        public GeneratorRowUI(VisualElement root, IGeneratorInfoOpener infos) : base(root)
        {
            name = Require<Label>(root, "name", "GeneratorRow.uxml");
            yieldLine = Require<Label>(root, "yield", "GeneratorRow.uxml");
            count = Require<Label>(root, "count", "GeneratorRow.uxml");
            buy = Require<Button>(root, "buy", "GeneratorRow.uxml");
            buyMax = Require<Button>(root, "buy_max", "GeneratorRow.uxml");
            text = Require<VisualElement>(root, "text", "GeneratorRow.uxml");
            // A row with nowhere to send the hold would render a dead gesture,
            // and the host is the only caller (requirement 7).
            this.infos = infos ?? throw new ArgumentNullException(nameof(infos));
        }

        protected override void OnBound()
        {
            generator = (GeneratorDefinition)Content;
            home = Producer.DeclaringScope<ScopeState>(Scope, generator);
            // Each button submits the count printed on it: the command refuses
            // whole if the flush moved the balance under it, and the close's
            // refresh repaints (12.11).
            buy.clicked += () => Session.TryBuy(Context().Rebase(home), generator, 1);
            buyMax.clicked += () => Session.TryBuy(Context().Rebase(home), generator, shownMax);
            text.AddManipulator(new LongPressManipulator(OpenInfo, Session.Config.longPressSeconds));
        }

        public override void Refresh()
        {
            var ctx = Context().Rebase(home);
            name.text = generator.displayName;
            count.text = "x" + ctx.GetOwnedCount(generator.Id);
            yieldLine.text = CostAndYieldText(ctx, generator);

            // One read answers both the labels and the pressability: M is zero
            // exactly when a single unit is unbuyable, and at zero the max
            // button reads "+1" disabled beside its neighbor (12.11).
            var max = Purchasing.MaxAffordable(ctx, generator);
            shownMax = max;
            buy.text = "+1";
            buyMax.text = "+" + Math.Max(max, 1);
            buy.SetEnabled(max >= 1);
            buyMax.SetEnabled(max >= 1);
        }

        // What the hold calls. Kept as a public UI action for the reason
        // EncoreWindowUI.RequestAd is public: a headless test exercises what the
        // hold exercises without manufacturing pointer events. The scope is the
        // declaring one the row resolved at bind, so the screen reads what the
        // row reads.
        public void OpenInfo() => infos.OpenGeneratorInfo(generator, home);

        // "cost => yield", the reference game's row: the next unit's cost and
        // what that one unit pays, through the same resolution the tick sums
        // (12.5). A currency the unit pays nothing is not a line, and a unit
        // paying nothing has no arrow. Static, so the info screen prints the
        // identical line.
        public static string CostAndYieldText(GameContext ctx, GeneratorDefinition generator)
        {
            var line = new StringBuilder();
            line.Append(NumberFormatter.Format(Purchasing.CostOf(generator, ctx, 1)))
                .Append(" ").Append(generator.costCurrency.displayName);
            var yields = 0;
            foreach (var (currency, amount) in Producer.UnitRate(ctx, generator))
            {
                if (amount == BigNumber.Zero)
                    continue;
                line.Append(yields++ == 0 ? " => " : ", ");
                line.Append(NumberFormatter.Format(amount)).Append(" ").Append(currency.displayName);
            }
            return line.ToString();
        }
    }
}
