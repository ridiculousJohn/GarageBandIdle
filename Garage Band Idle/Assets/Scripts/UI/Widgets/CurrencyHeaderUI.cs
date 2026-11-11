using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One header line for the currency the module binds (design doc 12.11):
    // the authored name, the balance through step 1's display rules, and the
    // tick report's realized income per second beside it - the deposit slope
    // plus the completion slope of every bar that filled, zero before the
    // first tick and for a currency nothing pays. The reveals are the module's
    // own visibleWhen, never a decision made here. The balance rides the
    // deposit slope alone between refreshes, because completion income lands
    // in lumps at the crossing; that is the shared readout's job rather than
    // this widget's.
    public sealed class CurrencyHeaderUI : ModuleWidget
    {
        private readonly Label nameLabel;
        private readonly Label valueLabel;
        private readonly Label rateLabel;

        private CurrencyReadout readout;

        // The balance on the line.
        public BigNumber ShownValue => readout.Shown;

        // The rate printed beside the balance: continuous income plus what the
        // drawing bars' completions earned, per real second.
        public BigNumber ShownRate { get; private set; } = BigNumber.Zero;

        public CurrencyHeaderUI(VisualElement root) : base(root)
        {
            nameLabel = Require<Label>(root, "name", "CurrencyLine.uxml");
            valueLabel = Require<Label>(root, "value", "CurrencyLine.uxml");
            rateLabel = Require<Label>(root, "rate", "CurrencyLine.uxml");
        }

        protected override void OnBound() =>
            readout = new CurrencyReadout(valueLabel, Scope, (CurrencyDefinition)Content);

        public override void Refresh()
        {
            nameLabel.text = Content.displayName;
            var report = Session.LastTick;
            readout.Snap(Context(), report, Clock.GameTimeSeconds);
            // Keyed by the readout's own home, the key both ledgers share.
            var completion = report == null
                ? BigNumber.Zero
                : report.CompletionSlope(readout.Home, Content.Id);
            ShownRate = readout.Slope + completion;
            rateLabel.text = "(" + NumberFormatter.Format(ShownRate) + "/s)";
        }

        public override void Interpolate() => readout.Interpolate(Clock.GameTimeSeconds);
    }
}
