using System;
using System.Collections.Generic;
using System.Linq;
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

        // The count's snap: truth at the refresh, and the granted slope the last
        // tick realized at the home, for the frames between (12.11). The
        // purchased half moves only by a buy, which is itself a refresh.
        private int purchased;
        private BigNumber granted = BigNumber.Zero;
        private BigNumber grantedSlope = BigNumber.Zero;
        private double stamp;

        private readonly List<(Definition target, BigNumber amount)> shownYields = new();

        // The purchased count inside the count label's parentheses.
        public int ShownPurchased { get; private set; }

        // The granted count added inside the count label's parentheses; zero
        // when the label prints none.
        public BigNumber ShownGranted { get; private set; } = BigNumber.Zero;

        // The N on the max button's "+N", as printed.
        public int ShownMax { get; private set; }

        // The cost at the head of the "cost => yield" line.
        public BigNumber ShownCost { get; private set; } = BigNumber.Zero;

        // The yields the "cost => yield" line prints after the arrow, in printed
        // order, zeros left out as the line leaves them out.
        public IReadOnlyList<(Definition target, BigNumber amount)> ShownYields => shownYields;

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
            purchased = ctx.GetPurchasedCount(generator.Id);
            granted = ctx.GetGrantedCount(generator.Id);
            grantedSlope = Session.LastTick == null
                ? BigNumber.Zero
                : Session.LastTick.DepositSlope(home, generator.Id);
            stamp = Clock.GameTimeSeconds;
            ShownPurchased = purchased;
            ShownGranted = granted;
            count.text = CountText(ShownPurchased, ShownGranted);
            // One read answers both the line and the shown cost and yields.
            var (cost, yields) = CostAndYields(ctx, generator);
            ShownCost = cost;
            shownYields.Clear();
            shownYields.AddRange(yields);
            yieldLine.text = FormatCostAndYields(generator, ShownCost, shownYields);

            // One read answers both the labels and the pressability: M is zero
            // exactly when a single unit is unbuyable, and at zero the max
            // button reads "+1" disabled beside its neighbor (12.11).
            var max = Purchasing.MaxAffordable(ctx, generator);
            shownMax = max;
            ShownMax = Math.Max(max, 1);
            buy.text = "+1";
            buyMax.text = "+" + ShownMax;
            buy.SetEnabled(max >= 1);
            buyMax.SetEnabled(max >= 1);
            // The whole row dims while not one unit can be bought (12.11).
            Root.EnableInClassList("row-unaffordable", max < 1);
        }

        // What the hold calls. Kept as a public UI action for the reason
        // EncoreWindowUI.RequestAd is public: a headless test exercises what the
        // hold exercises without manufacturing pointer events. The scope is the
        // declaring one the row resolved at bind, so the screen reads what the
        // row reads.
        public void OpenInfo() => infos.OpenGeneratorInfo(generator, home);

        // The owned count as the row prints it (12.11): the purchased count in
        // parentheses, with the granted count added inside them when a payment
        // has landed - "(3)" and "(3+8.89e11)". The two halves are one fact read
        // two ways, so the label says which is which rather than a single sum
        // the player cannot square with the price. Static, so the info screen
        // prints the identical text.
        public static string CountText(GameContext ctx, GeneratorDefinition generator) =>
            CountText(ctx.GetPurchasedCount(generator.Id), ctx.GetGrantedCount(generator.Id));

        public static string CountText(int purchased, BigNumber granted) =>
            granted > BigNumber.Zero
                ? "(" + purchased + "+" + NumberFormatter.Format(granted) + ")"
                : "(" + purchased + ")";

        // Presentation only (12.11): the granted count follows the slope the
        // last tick realized, clamped at zero like a balance, and the frame
        // that refreshed shows truth exactly since its elapsed term is zero.
        public override void Interpolate()
        {
            if (grantedSlope == BigNumber.Zero)
                return;
            var display = BigNumber.Max(BigNumber.Zero, granted + grantedSlope * (Clock.GameTimeSeconds - stamp));
            ShownGranted = display;
            count.text = CountText(ShownPurchased, ShownGranted);
        }

        // "cost => yield", the reference game's row: the next unit's cost and
        // what that one unit pays, through the same resolution the tick sums
        // (12.5). A target the unit pays nothing is not a line, and a unit
        // paying nothing has no arrow. Static, so the info screen prints the
        // identical line.
        public static string CostAndYieldText(GameContext ctx, GeneratorDefinition generator)
        {
            var (cost, yields) = CostAndYields(ctx, generator);
            return FormatCostAndYields(generator, cost, yields);
        }

        // The data the "cost => yield" line prints: the next unit's cost, then
        // rate entries and yield entries, each one unit's figure - per second
        // for a rate, per firing for a yield a bar fires (12.11). A target the
        // unit pays nothing is left out here, so the list is the line's own.
        public static (BigNumber cost, List<(Definition target, BigNumber amount)> yields) CostAndYields(
            GameContext ctx, GeneratorDefinition generator)
        {
            var cost = Purchasing.CostOf(generator, ctx, 1);
            var yields = new List<(Definition target, BigNumber amount)>();
            foreach (var (target, amount) in Producer.UnitRate(ctx, generator).Concat(Producer.UnitYield(ctx, generator)))
                if (amount != BigNumber.Zero)
                    yields.Add((target, amount));
            return (cost, yields);
        }

        // The line itself, from data already read, so a caller that stores the
        // data prints exactly what it stored. The unit is implied on the row;
        // the info screen names it.
        internal static string FormatCostAndYields(GeneratorDefinition generator, BigNumber cost,
                                                  IReadOnlyList<(Definition target, BigNumber amount)> yields)
        {
            var line = new StringBuilder();
            line.Append(NumberFormatter.Format(cost))
                .Append(" ").Append(generator.costCurrency.displayName);
            for (var i = 0; i < yields.Count; i++)
            {
                line.Append(i == 0 ? " => " : ", ");
                line.Append(NumberFormatter.Format(yields[i].amount)).Append(" ").Append(yields[i].target.displayName);
            }
            return line.ToString();
        }
    }
}
