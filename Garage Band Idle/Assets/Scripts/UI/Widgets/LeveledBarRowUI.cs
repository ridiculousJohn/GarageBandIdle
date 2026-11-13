using System;
using System.Collections.Generic;
using System.Text;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One row of a levelled group (design doc 12.11): a bar shown with its
    // level, the reference game's combined row. The title line is the bar's
    // name and the level's owned count; beneath it the fill, with what the bar
    // earns printed inside it - per second, or per completion, as the card's
    // figures toggle reads; at the right the level's buy control and the price
    // of what it buys. Tapping the fill starts the bar when it is not running
    // and works it when it is; holding the title opens the level generator's
    // info screen. A row at level 0 - the card's first bar while no bar on the
    // card is available - takes no tap, and its buy control's press hires the
    // first level and starts the gig, as the card's Book button does. Every fact is read where it lives: the bar's at its home,
    // the level's at the generator's declaring scope (12.3), and the earnings
    // through BarSystem.CompletionRate or BarSystem.CompletionPayout, the reads
    // the tick's own completion income is sized by (12.7). The row also watches
    // its level for count thresholds: a refresh that finds the owned count
    // risen past one raises a toast per effect it switched on, and the first
    // refresh after the row is built only takes the count as it stands.
    public sealed class LeveledBarRowUI
    {
        public VisualElement Root { get; }

        public BarDefinition Bar { get; }

        // What the row decided last refresh: shown while the bar is available
        // or stands at level 0, so the interpolation moves exactly the rows
        // that are on screen.
        public bool Visible { get; private set; }

        // True while the row stands at level 0: the card's first bar, shown
        // while no bar on the card is available (LeveledGroupUI). Its fill
        // takes no tap, since the bar is not available, and its buy control
        // hires the first level and starts the gig in the same press, as
        // the Book button does.
        public bool ShownLevelZero { get; private set; }

        // True while the bar is available and not running, which is the state
        // a tap answers by starting it; the row carries `leveled-idle` for it.
        // A bar that fills once and stands full has nothing a tap could start,
        // so it is never idle.
        public bool Idle { get; private set; }

        // The progress the fill draws, clamped to [0, fillAmount].
        public BigNumber ShownProgress { get; private set; } = BigNumber.Zero;

        // The purchased count inside the count label's parentheses.
        public int ShownPurchased { get; private set; }

        // The granted count added inside the count label's parentheses; zero
        // when the label prints none.
        public BigNumber ShownGranted { get; private set; } = BigNumber.Zero;

        // The per-second earnings printed inside the fill while the card reads
        // Per Second, one line per currency in BarSystem.CompletionRate's
        // order, zero lines kept; empty while it reads Raw.
        public IReadOnlyList<(CurrencyDefinition currency, BigNumber perSecond)> ShownRates => shownRates;

        // What one completion pays, printed inside the fill while the card
        // reads Raw, one line per currency in BarSystem.CompletionPayout's
        // order, zero lines kept; empty while it reads Per Second.
        public IReadOnlyList<(CurrencyDefinition currency, BigNumber amount)> ShownPayouts => shownPayouts;

        // The price printed under the buy control: one level under the pair,
        // the N levels the Hire button buys under the cycle.
        public BigNumber ShownCost => buy.ShownCost;

        // The N on the pair's max button's "+N", as printed; zero under the
        // cycle, which draws no max button.
        public int ShownMax => buy is PairControl pair ? pair.ShownMax : 0;

        // The N on the cycle's "Hire: xN", as printed; zero under the pair.
        public int ShownCount => buy is HireControl hire ? hire.ShownCount : 0;

        // Which buy control the row draws, the card's choice (12.11).
        public BuyControlKind ControlKind { get; }

        // The level generator's declaring scope, resolved once: the count, the
        // price and the purchase are its facts (12.3/12.4).
        internal ScopeState LevelHome { get; }

        private readonly GameSession session;
        private readonly ScopeState home;
        private readonly GameClock clock;
        private readonly GroupDefinition group;
        private readonly IGeneratorInfoOpener infos;
        private readonly IToastOpener toasts;
        private readonly Action<BarDefinition> activate;
        private readonly Label nameLabel;
        private readonly Label countLabel;
        private readonly ProgressBar fill;
        private readonly BuyControl buy;
        private readonly Func<FigureMode> figures;
        private readonly List<(CurrencyDefinition currency, BigNumber perSecond)> shownRates = new();
        private readonly List<(CurrencyDefinition currency, BigNumber amount)> shownPayouts = new();

        // The fill's snap: truth at the refresh and the bar slope the last
        // tick realized, for the frames between (12.11).
        private BigNumber truth = BigNumber.Zero;
        private BigNumber slope = BigNumber.Zero;
        private double stamp;

        // The level's owned count the last refresh read, and whether there has
        // been one: the count a crossing is measured from. A row's first
        // refresh is after a bind, a chapter switch or a return to Live, and
        // what was already reached then is not news.
        private BigNumber seenCount = BigNumber.Zero;
        private bool watching;

        // `home` is the group's declaring scope, which is the bar's: a group
        // lists members its own scope declares (12.7). `activate` is the
        // module's add-to-the-active-set, the one way a row or the Book button
        // starts a bar. `mode` is the card's cycle button, read by a Cycle
        // control on every refresh; `figures` is the card's figures toggle,
        // read on every refresh.
        public LeveledBarRowUI(GameSession session, ScopeState home, GameClock clock, GroupDefinition group,
                               BarDefinition bar, IGeneratorInfoOpener infos, IToastOpener toasts,
                               Action<BarDefinition> activate, BuyControlKind buyControl, Func<BuyMode> mode,
                               Func<FigureMode> figures)
        {
            this.figures = figures;
            this.session = session;
            this.home = home;
            this.clock = clock;
            this.group = group;
            this.infos = infos;
            this.toasts = toasts;
            this.activate = activate;
            Bar = bar;
            ControlKind = buyControl;
            LevelHome = Producer.DeclaringScope<ScopeState>(home, bar.level);

            Root = new VisualElement();
            Root.AddToClassList("leveled-row");

            // The name and count above the fill, one column taking the width
            // the buy control leaves.
            var text = new VisualElement();
            text.AddToClassList("leveled-text");
            var title = new VisualElement();
            title.AddToClassList("row-title");
            title.AddToClassList("leveled-title");
            nameLabel = new Label();
            nameLabel.AddToClassList("row-name");
            countLabel = new Label();
            countLabel.AddToClassList("row-count");
            title.Add(nameLabel);
            title.Add(countLabel);
            fill = new ProgressBar { lowValue = 0, highValue = 100 };
            fill.AddToClassList("leveled-fill");
            text.Add(title);
            text.Add(fill);
            Root.Add(text);

            buy = buyControl == BuyControlKind.Cycle
                ? new HireControl(bar.level, Hire, mode)
                : new PairControl(bar.level, Hire);
            Root.Add(buy.Root);

            // The fill is the tap target and the title the hold's: the buy
            // control sits outside both, so a buy is never also a tap. A tap is
            // a fresh context per press, since the command is a clock sample.
            fill.RegisterCallback<ClickEvent>(evt => Tap());
            title.AddManipulator(new LongPressManipulator(OpenInfo, session.Config.longPressSeconds));
        }

        // A null gate on a bar is OPEN, the opposite of a purchase gate (12.7).
        // Static too, so the automation window lists exactly the rows the card
        // shows.
        public bool Available(GameContext homeCtx) => Available(Bar, homeCtx);

        public static bool Available(BarDefinition bar, GameContext homeCtx) =>
            bar.availableWhen == null || bar.availableWhen.Evaluate(homeCtx);

        // `levelZero` is the card's answer for this row: true when no bar on
        // the card is available and this is its first bar (LeveledGroupUI).
        public void Refresh(bool levelZero)
        {
            var ctx = Context();
            var levelCtx = ctx.Rebase(LevelHome);
            Watch(levelCtx);
            ShownLevelZero = levelZero;
            Visible = levelZero || Available(ctx);
            Root.style.display = Visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!Visible)
            {
                Idle = false;
                Root.RemoveFromClassList("leveled-idle");
                return;
            }

            Idle = !levelZero && !IsActive() && !Complete(ctx);
            Root.EnableInClassList("leveled-idle", Idle);

            nameLabel.text = Bar.displayName;
            ShownPurchased = levelCtx.GetPurchasedCount(Bar.level.Id);
            ShownGranted = levelCtx.GetGrantedCount(Bar.level.Id);
            countLabel.text = GeneratorRowUI.CountText(ShownPurchased, ShownGranted);

            var report = session.LastTick;
            truth = ctx.GetBarProgress(Bar.Id);
            slope = report == null ? BigNumber.Zero : report.BarSlope(home, Bar.Id);
            stamp = clock.GameTimeSeconds;

            // The figures are set here and nowhere else: what a level or a
            // modifier changes moves them, and the fill alone interpolates.
            shownRates.Clear();
            shownPayouts.Clear();
            if (figures() == FigureMode.Raw)
            {
                foreach (var (currency, _, amount) in BarSystem.CompletionPayout(ctx, Bar))
                    shownPayouts.Add((currency, amount));
                fill.title = EarningsText(shownPayouts, "");
            }
            else
            {
                foreach (var (currency, _, perSecond) in BarSystem.CompletionRate(ctx, Bar, GameSpeed(ctx)))
                    shownRates.Add((currency, perSecond));
                fill.title = EarningsText(shownRates, "/s");
            }
            Show(truth);

            // The whole row dims while not one level can be bought (12.11).
            var affordable = buy.Refresh(levelCtx);
            Root.EnableInClassList("row-unaffordable", !affordable);
        }

        public void Interpolate() => Show(truth + slope * (clock.GameTimeSeconds - stamp));

        // What a press on the fill does, public for the reason
        // GeneratorRowUI.OpenInfo is: a headless test presses what the pointer
        // presses. A bar not running is started - added to the group's set,
        // never removed from it; a running bar that names a producer is
        // worked, the command the Jam button issues; a bar that fills once and
        // stands full has nothing left to run, and a row at level 0 shows a
        // bar that is not available, so either press does nothing.
        public void Tap()
        {
            var ctx = Context();
            if (!Available(ctx) || Complete(ctx))
                return;
            if (!IsActive())
                activate(Bar);
            else if (Bar.tap != null)
                session.FireProducer(ctx, Bar.tap);
        }

        // The pair's two presses, public for the same reason as Tap. Pressing a
        // button the row does not draw is a caller bug.
        public void BuyOne() => Pair().BuyOne();

        public void BuyMax() => Pair().BuyMax();

        // The cycle's one press: the N levels the Hire button printed.
        public void Buy() => Hire().Buy();

        // What the hold calls: the level generator's info screen, at the
        // declaring scope the row resolved at construction.
        public void OpenInfo() => infos.OpenGeneratorInfo(Bar.level, LevelHome);

        // The display never leaves [0, fillAmount]: overfill is a real fact and
        // a wrong picture. A nonpositive fillAmount is refused at load.
        private void Show(BigNumber value)
        {
            var display = BigNumber.Min(Bar.fillAmount, BigNumber.Max(BigNumber.Zero, value));
            ShownProgress = display;
            fill.value = (float)((display / Bar.fillAmount).ToDouble() * 100);
        }

        // The crossing watch, ahead of the visibility test so a level bought
        // while the gig is not yet on the calendar is still measured from. A
        // count that rose past thresholds raises each one's effects in order;
        // a count that fell (a reset) raises nothing and is simply the new
        // count to measure from.
        private void Watch(GameContext levelCtx)
        {
            var owned = levelCtx.GetOwnedCount(Bar.level.Id);
            if (watching && owned > seenCount)
                foreach (var threshold in Bar.level.ThresholdsCrossed(seenCount, owned))
                    foreach (var effect in threshold.effects)
                        if (effect != null)
                            toasts.OpenToast(effect);
            seenCount = owned;
            watching = true;
        }

        // What every buy control's press submits: `count` levels at the
        // level's declaring scope. On a row at level 0 the press is the Book
        // button's pair - the purchase, then the bar added to the active set
        // by the card's `activate` - so the first hire starts the gig. The
        // row's state is read before the purchase, whose own refresh moves the
        // row off level 0; the add is fail-closed, so a refused purchase
        // leaves the bar unavailable and nothing changes.
        private void Hire(int count)
        {
            var starts = ShownLevelZero;
            session.TryBuy(new GameContext(LevelHome, clock.RealTimeUtc), Bar.level, count);
            if (starts)
                activate(Bar);
        }

        private PairControl Pair() =>
            buy as PairControl ?? throw new InvalidOperationException(
                $"The row for '{Bar.Id}' draws the {ControlKind} control, which has no +1 or +M (12.11).");

        private HireControl Hire() =>
            buy as HireControl ?? throw new InvalidOperationException(
                $"The row for '{Bar.Id}' draws the {ControlKind} control, which has no Hire button (12.11).");

        private bool IsActive() =>
            home.activeMembers.TryGetValue(group.Id, out var active) && active.Contains(Bar.Id);

        private bool Complete(GameContext ctx) =>
            Bar.repeatWhen == null && ctx.GetBarProgress(Bar.Id) >= Bar.fillAmount;

        // game_speed over the chapter the bar draws in, the read the tick
        // scales the same fill by; a bar declared at root draws in no chapter.
        private double GameSpeed(GameContext ctx)
        {
            var chapter = home.Chapter();
            return chapter == null ? 1 : TickSystem.GameSpeed(ctx.Rebase(chapter), chapter, session.Config);
        }

        // "27.60 Cash/s, 4.60 Fans/s" with the unit "/s", "30.87 Cash, 4.11
        // Fans" with none: every line, zeros included, so a level nobody
        // bought still names what the gig pays.
        private static string EarningsText(IReadOnlyList<(CurrencyDefinition currency, BigNumber amount)> lines,
                                           string unit)
        {
            var line = new StringBuilder();
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    line.Append(", ");
                line.Append(NumberFormatter.Format(lines[i].amount))
                    .Append(" ").Append(lines[i].currency.displayName).Append(unit);
            }
            return line.ToString();
        }

        private GameContext Context() => new GameContext(home, clock.RealTimeUtc);

        // The level's buy control, kept in its own element and its own class so
        // the row's other parts never reach into it and one control replaces
        // the other whole: the price beneath whatever buttons it draws, and the
        // answer the row dims by (12.11). `hire` is the row's purchase of N
        // levels, the one way a control's press buys.
        private abstract class BuyControl
        {
            public VisualElement Root { get; }

            public BigNumber ShownCost { get; private set; } = BigNumber.Zero;

            protected readonly GeneratorDefinition level;
            protected readonly Action<int> hire;
            protected readonly VisualElement buttons;
            private readonly Label cost;

            protected BuyControl(GeneratorDefinition level, Action<int> hire)
            {
                this.level = level;
                this.hire = hire;

                Root = new VisualElement();
                Root.AddToClassList("leveled-buy");
                buttons = new VisualElement();
                buttons.AddToClassList("leveled-buttons");
                cost = new Label();
                cost.AddToClassList("row-yield");
                cost.AddToClassList("leveled-cost");
                Root.Add(buttons);
                Root.Add(cost);
            }

            // True when what the control buys is affordable now, the answer
            // the row dims by.
            public abstract bool Refresh(GameContext levelCtx);

            protected void ShowCost(BigNumber price)
            {
                ShownCost = price;
                cost.text = NumberFormatter.Format(price) + " " + level.costCurrency.displayName;
            }
        }

        // The generator row's "+1" and "+M" over the next unit's cost, with its
        // arithmetic and enabling - M is Purchasing.MaxAffordable at the
        // declaring scope, both buttons read "+1" and disable when it is zero,
        // and each submits the count printed on it (12.11).
        private sealed class PairControl : BuyControl
        {
            public int ShownMax { get; private set; }

            private readonly Button one;
            private readonly Button max;

            // The count the last refresh printed into the max button, kept as
            // an int beside the label: the tap buys the count the player saw.
            private int maxCount;

            public PairControl(GeneratorDefinition level, Action<int> hire)
                : base(level, hire)
            {
                one = new Button();
                one.AddToClassList("row-buy");
                max = new Button();
                max.AddToClassList("row-buy");
                buttons.Add(one);
                buttons.Add(max);

                one.clicked += BuyOne;
                max.clicked += BuyMax;
            }

            public void BuyOne() => hire(1);

            public void BuyMax() => hire(maxCount);

            public override bool Refresh(GameContext levelCtx)
            {
                ShowCost(Purchasing.CostOf(level, levelCtx, 1));

                var affordable = Purchasing.MaxAffordable(levelCtx, level);
                maxCount = affordable;
                ShownMax = Math.Max(affordable, 1);
                one.text = "+1";
                max.text = "+" + ShownMax;
                one.SetEnabled(affordable >= 1);
                max.SetEnabled(affordable >= 1);
                return affordable >= 1;
            }
        }

        // Ctrl C's "Hire: xN" over the price of those N, N from the card's
        // cycle button: Buy One is 1; Buy Max the most the balance affords
        // now, Purchasing.MaxAffordable, and 1 while that is zero; Buy Next the
        // levels still needed to reach the level's next count threshold, and 1
        // when no threshold is left. The press submits the N printed, which
        // the command buys whole or refuses. Dimmed by a class and never
        // disabled while N is not affordable, as the Auto button is.
        private sealed class HireControl : BuyControl
        {
            public int ShownCount { get; private set; } = 1;

            private readonly Func<BuyMode> mode;
            private readonly Button button;

            public HireControl(GeneratorDefinition level, Action<int> hire, Func<BuyMode> mode)
                : base(level, hire)
            {
                this.mode = mode;
                button = new Button();
                button.AddToClassList("row-buy");
                button.AddToClassList("leveled-hire");
                buttons.Add(button);
                button.clicked += Buy;
            }

            public void Buy() => hire(ShownCount);

            public override bool Refresh(GameContext levelCtx)
            {
                ShownCount = Count(levelCtx);
                ShowCost(Purchasing.CostOf(level, levelCtx, ShownCount));
                button.text = "Hire: x" + ShownCount;
                var affordable = Purchasing.CanBuy(levelCtx, level, ShownCount);
                button.EnableInClassList("leveled-hire-unaffordable", !affordable);
                return affordable;
            }

            private int Count(GameContext levelCtx)
            {
                switch (mode())
                {
                    case BuyMode.Max:
                        return Math.Max(Purchasing.MaxAffordable(levelCtx, level), 1);
                    case BuyMode.Next:
                    {
                        var next = level.NextThreshold(levelCtx);
                        if (next == null)
                            return 1;
                        var needed = (next.count - levelCtx.GetOwnedCount(level.Id)).ToDouble();
                        return Math.Max((int)Math.Ceiling(needed), 1);
                    }
                    default:
                        return 1;
                }
            }
        }
    }
}
