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
    // earns per second printed inside it; at the right the level's buy control
    // and the next unit's cost. Tapping the fill starts the bar when it is not
    // running and works it when it is; holding the title opens the level
    // generator's info screen. Every fact is read where it lives: the bar's at
    // its home, the level's at the generator's declaring scope (12.3), and the
    // earnings through BarSystem.CompletionRate, the read the tick's own
    // completion income is sized by (12.7).
    public sealed class LeveledBarRowUI
    {
        public VisualElement Root { get; }

        public BarDefinition Bar { get; }

        // What the row decided last refresh: shown while the bar is available,
        // so the interpolation moves exactly the rows that are on screen.
        public bool Visible { get; private set; }

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

        // The per-second earnings printed inside the fill, one line per
        // currency in BarSystem.CompletionRate's order, zero lines kept.
        public IReadOnlyList<(CurrencyDefinition currency, BigNumber perSecond)> ShownRates => shownRates;

        // The next level's cost printed under the buy buttons.
        public BigNumber ShownCost => buy.ShownCost;

        // The N on the max button's "+N", as printed.
        public int ShownMax => buy.ShownMax;

        // The level generator's declaring scope, resolved once: the count, the
        // price and the purchase are its facts (12.3/12.4).
        internal ScopeState LevelHome { get; }

        private readonly GameSession session;
        private readonly ScopeState home;
        private readonly GameClock clock;
        private readonly GroupDefinition group;
        private readonly IGeneratorInfoOpener infos;
        private readonly Action<BarDefinition> activate;
        private readonly Label nameLabel;
        private readonly Label countLabel;
        private readonly ProgressBar fill;
        private readonly BuyControl buy;
        private readonly List<(CurrencyDefinition currency, BigNumber perSecond)> shownRates = new();

        // The fill's snap: truth at the refresh and the bar slope the last
        // tick realized, for the frames between (12.11).
        private BigNumber truth = BigNumber.Zero;
        private BigNumber slope = BigNumber.Zero;
        private double stamp;

        // `home` is the group's declaring scope, which is the bar's: a group
        // lists members its own scope declares (12.7). `activate` is the
        // module's add-to-the-active-set, the one way a row or the Book button
        // starts a bar.
        public LeveledBarRowUI(GameSession session, ScopeState home, GameClock clock, GroupDefinition group,
                               BarDefinition bar, IGeneratorInfoOpener infos, Action<BarDefinition> activate)
        {
            this.session = session;
            this.home = home;
            this.clock = clock;
            this.group = group;
            this.infos = infos;
            this.activate = activate;
            Bar = bar;
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

            buy = new BuyControl(session, clock, bar.level, LevelHome);
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

        public void Refresh()
        {
            var ctx = Context();
            Visible = Available(ctx);
            Root.style.display = Visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!Visible)
            {
                Idle = false;
                Root.RemoveFromClassList("leveled-idle");
                return;
            }

            Idle = !IsActive() && !Complete(ctx);
            Root.EnableInClassList("leveled-idle", Idle);

            nameLabel.text = Bar.displayName;
            var levelCtx = ctx.Rebase(LevelHome);
            ShownPurchased = levelCtx.GetPurchasedCount(Bar.level.Id);
            ShownGranted = levelCtx.GetGrantedCount(Bar.level.Id);
            countLabel.text = GeneratorRowUI.CountText(ShownPurchased, ShownGranted);

            var report = session.LastTick;
            truth = ctx.GetBarProgress(Bar.Id);
            slope = report == null ? BigNumber.Zero : report.BarSlope(home, Bar.Id);
            stamp = clock.GameTimeSeconds;

            shownRates.Clear();
            foreach (var (currency, _, perSecond) in BarSystem.CompletionRate(ctx, Bar, GameSpeed(ctx)))
                shownRates.Add((currency, perSecond));
            fill.title = EarningsText(shownRates);
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
        // stands full has nothing left to run, so the press does nothing.
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

        // The two buy buttons' presses, public for the same reason as Tap.
        public void BuyOne() => buy.BuyOne();

        public void BuyMax() => buy.BuyMax();

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

        // "27.60 Cash/s, 4.60 Fans/s": every line, zeros included, so a level
        // nobody bought still names what the gig pays.
        private static string EarningsText(IReadOnlyList<(CurrencyDefinition currency, BigNumber perSecond)> rates)
        {
            var line = new StringBuilder();
            for (var i = 0; i < rates.Count; i++)
            {
                if (i > 0)
                    line.Append(", ");
                line.Append(NumberFormatter.Format(rates[i].perSecond))
                    .Append(" ").Append(rates[i].currency.displayName).Append("/s");
            }
            return line.ToString();
        }

        private GameContext Context() => new GameContext(home, clock.RealTimeUtc);

        // The level's buy control, kept in its own element and its own class so
        // the row's other parts never reach into it: "+1" and "+M" over the
        // next unit's cost, with the generator row's arithmetic and enabling -
        // M is Purchasing.MaxAffordable at the declaring scope, both buttons
        // read "+1" and disable when it is zero, and each submits the count
        // printed on it (12.11).
        private sealed class BuyControl
        {
            public VisualElement Root { get; }

            public int ShownMax { get; private set; }

            public BigNumber ShownCost { get; private set; } = BigNumber.Zero;

            private readonly GameSession session;
            private readonly GameClock clock;
            private readonly GeneratorDefinition level;
            private readonly ScopeState levelHome;
            private readonly Button one;
            private readonly Button max;
            private readonly Label cost;

            // The count the last refresh printed into the max button, kept as
            // an int beside the label: the tap buys the count the player saw.
            private int maxCount;

            public BuyControl(GameSession session, GameClock clock, GeneratorDefinition level, ScopeState levelHome)
            {
                this.session = session;
                this.clock = clock;
                this.level = level;
                this.levelHome = levelHome;

                Root = new VisualElement();
                Root.AddToClassList("leveled-buy");
                var buttons = new VisualElement();
                buttons.AddToClassList("leveled-buttons");
                one = new Button();
                one.AddToClassList("row-buy");
                max = new Button();
                max.AddToClassList("row-buy");
                buttons.Add(one);
                buttons.Add(max);
                cost = new Label();
                cost.AddToClassList("row-yield");
                cost.AddToClassList("leveled-cost");
                Root.Add(buttons);
                Root.Add(cost);

                one.clicked += BuyOne;
                max.clicked += BuyMax;
            }

            public void BuyOne() => session.TryBuy(Context(), level, 1);

            public void BuyMax() => session.TryBuy(Context(), level, maxCount);

            // True when one level is affordable, the answer the row dims by.
            public bool Refresh(GameContext levelCtx)
            {
                ShownCost = Purchasing.CostOf(level, levelCtx, 1);
                cost.text = NumberFormatter.Format(ShownCost) + " " + level.costCurrency.displayName;

                var affordable = Purchasing.MaxAffordable(levelCtx, level);
                maxCount = affordable;
                ShownMax = Math.Max(affordable, 1);
                one.text = "+1";
                max.text = "+" + ShownMax;
                one.SetEnabled(affordable >= 1);
                max.SetEnabled(affordable >= 1);
                return affordable >= 1;
            }

            private GameContext Context() => new GameContext(levelHome, clock.RealTimeUtc);
        }
    }
}
