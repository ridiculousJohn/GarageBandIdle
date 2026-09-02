using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bar row (design doc 12.11): the authored name, the fill, and the
    // button that selects it. Every fact is read where it lives - progress from
    // the scope, completion derived from it rather than stored (12.7), the
    // selection from the same activeBars read BarSystem.Eligible makes - and
    // the fill rides the tick's realized bar slope between refreshes.
    public sealed class BarRowUI
    {
        public VisualElement Root { get; }

        // What the list decided last refresh, so the interpolation moves
        // exactly the rows that are on screen.
        public bool Visible { get; private set; }

        private readonly GameSession session;
        private readonly ScopeState scope;
        private readonly GameClock clock;
        private readonly BarGroupDefinition group;
        private readonly BarDefinition bar;
        private readonly Label nameLabel;
        private readonly ProgressBar fill;
        private readonly Label progressLabel;
        private readonly Button selectButton;

        private BigNumber truth = BigNumber.Zero;
        private BigNumber slope = BigNumber.Zero;
        private double stamp;

        public BarRowUI(GameSession session, ScopeState scope, GameClock clock,
                        BarGroupDefinition group, BarDefinition bar)
        {
            this.session = session;
            this.scope = scope;
            this.clock = clock;
            this.group = group;
            this.bar = bar;

            Root = new VisualElement();
            Root.AddToClassList("bar-row");
            nameLabel = new Label();
            nameLabel.AddToClassList("bar-name");
            fill = new ProgressBar { lowValue = 0, highValue = 100 };
            fill.AddToClassList("bar-fill");
            progressLabel = new Label();
            progressLabel.AddToClassList("bar-progress");
            selectButton = new Button();
            selectButton.AddToClassList("bar-select");
            // The pressed bar BECOMES the selection: choosing is the mechanic
            // (12.7), and SetActiveBars is fail-closed, so a refused set changes
            // nothing. A chapter authoring maxActive above one wants a toggle
            // here instead of a replacement; none does.
            selectButton.clicked += () => this.session.SetActiveBars(Context(), this.group, new[] { this.bar });
            Root.Add(nameLabel);
            Root.Add(fill);
            Root.Add(progressLabel);
            Root.Add(selectButton);
        }

        // The list's filter, asked with the list's own context: a list module's
        // evaluation scope IS the group's declaring scope, so nothing rebases.
        // A null gate on a bar is OPEN, the opposite of a purchase gate (12.7).
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
            var complete = !bar.repeating && truth >= bar.fillAmount;
            var active = scope.activeBars.TryGetValue(group.Id, out var selected) && selected.Contains(bar.Id);
            selectButton.text = complete ? "Done" : active ? "Selected" : "Select";
            selectButton.SetEnabled(!complete && !active);
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
