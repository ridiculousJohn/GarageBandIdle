using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bar row (design doc 12.11): the authored name, the description, and
    // the fill. Every fact is read where it lives - progress from the scope,
    // completion derived from it rather than stored (12.7) - and the fill rides
    // the tick's realized bar slope between refreshes. Making the bar active is
    // the group widget's control, on the wrapper beside this row, because
    // membership is a group's fact and a bar is only one kind of member.
    public sealed class BarRowUI
    {
        public VisualElement Root { get; }

        // What the list decided last refresh, so the interpolation moves
        // exactly the rows that are on screen.
        public bool Visible { get; private set; }

        private readonly GameSession session;
        private readonly ScopeState scope;
        private readonly GameClock clock;
        private readonly BarDefinition bar;
        private readonly Label nameLabel;
        private readonly Label descriptionLabel;
        private readonly ProgressBar fill;
        private readonly Label progressLabel;

        private BigNumber truth = BigNumber.Zero;
        private BigNumber slope = BigNumber.Zero;
        private double stamp;

        public BarRowUI(GameSession session, ScopeState scope, GameClock clock, BarDefinition bar)
        {
            this.session = session;
            this.scope = scope;
            this.clock = clock;
            this.bar = bar;

            Root = new VisualElement();
            Root.AddToClassList("bar-row");
            // The name and its description are one column, so the row stays a
            // row and the description sits under the name (12.11).
            var text = new VisualElement();
            text.AddToClassList("bar-text");
            nameLabel = new Label();
            nameLabel.AddToClassList("bar-name");
            descriptionLabel = new Label();
            descriptionLabel.AddToClassList("bar-description");
            text.Add(nameLabel);
            text.Add(descriptionLabel);
            fill = new ProgressBar { lowValue = 0, highValue = 100 };
            fill.AddToClassList("bar-fill");
            progressLabel = new Label();
            progressLabel.AddToClassList("bar-progress");
            Root.Add(text);
            Root.Add(fill);
            Root.Add(progressLabel);

            // The row is the tap target when the bar names a producer (12.11):
            // the same command the Jam button issues, on a fresh context per
            // press, since the command is a clock sample. Choosing is the
            // group's control and paying is the row's, and the control sits
            // outside this element, so its own click can never also be a tap.
            if (bar.tap != null)
            {
                Root.AddToClassList("bar-tappable");
                Root.RegisterCallback<ClickEvent>(evt => this.session.FireProducer(Context(), this.bar.tap));
            }
        }

        // The list's filter, asked with the list's own context: a bar's
        // declaring scope IS the group widget's evaluation scope, so nothing
        // rebases. A null gate on a bar is OPEN, the opposite of a purchase
        // gate (12.7).
        public bool Available(GameContext ctx) =>
            bar.availableWhen == null || bar.availableWhen.Evaluate(ctx);

        public void SetVisible(bool visible)
        {
            Visible = visible;
            Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Refresh()
        {
            var ctx = Context();
            var report = session.LastTick;
            truth = ctx.GetBarProgress(bar.Id);
            slope = report == null ? BigNumber.Zero : report.BarSlope(scope, bar.Id);
            stamp = clock.GameTimeSeconds;

            nameLabel.text = bar.displayName;
            // An absent description leaves the row one line tall.
            descriptionLabel.text = bar.description;
            descriptionLabel.style.display = string.IsNullOrEmpty(bar.description)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            Show(truth);
        }

        public void Interpolate() => Show(truth + slope * (clock.GameTimeSeconds - stamp));

        // The display never leaves [0, fillAmount]: overfill is a real fact and
        // a wrong picture. A nonpositive fillAmount is refused at load, so the
        // division needs no guard of its own.
        private void Show(BigNumber value)
        {
            var display = BigNumber.Min(bar.fillAmount, BigNumber.Max(BigNumber.Zero, value));
            progressLabel.text = NumberFormatter.Format(display) + " / " + NumberFormatter.Format(bar.fillAmount);
            fill.value = (float)((display / bar.fillAmount).ToDouble() * 100);
        }

        private GameContext Context() => new GameContext(scope, clock.RealTimeUtc);
    }
}
