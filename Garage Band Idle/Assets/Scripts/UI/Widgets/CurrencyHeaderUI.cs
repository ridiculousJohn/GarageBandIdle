using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One header line for the currency the module binds (design doc 12.11):
    // the authored name and the balance through step 1's display rules. The
    // reveals are the module's own visibleWhen, never a decision made here.
    // The balance rides the tick's realized slope between refreshes, which is
    // the shared readout's job rather than this widget's.
    public sealed class CurrencyHeaderUI : ModuleWidget
    {
        private readonly Label nameLabel;
        private readonly Label valueLabel;

        private CurrencyReadout readout;

        public CurrencyHeaderUI(VisualElement root) : base(root)
        {
            nameLabel = Require<Label>(root, "name", "CurrencyLine.uxml");
            valueLabel = Require<Label>(root, "value", "CurrencyLine.uxml");
        }

        protected override void OnBound() =>
            readout = new CurrencyReadout(valueLabel, Scope, (CurrencyDefinition)Content);

        public override void Refresh()
        {
            nameLabel.text = Content.displayName;
            readout.Snap(Context(), Session.LastTick, Clock.GameTimeSeconds);
        }

        public override void Interpolate() => readout.Interpolate(Clock.GameTimeSeconds);
    }
}
