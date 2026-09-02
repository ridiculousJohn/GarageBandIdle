using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One header line for the currency the module binds (design doc 12.11):
    // the authored name and the balance through step 1's display rules. The
    // reveals are the module's own visibleWhen, never a decision made here.
    public sealed class CurrencyHeaderUI : ModuleWidget
    {
        private readonly Label nameLabel;
        private readonly Label valueLabel;

        public CurrencyHeaderUI(VisualElement root) : base(root)
        {
            nameLabel = Require<Label>(root, "name", "CurrencyLine.uxml");
            valueLabel = Require<Label>(root, "value", "CurrencyLine.uxml");
        }

        public override void Refresh()
        {
            nameLabel.text = Content.displayName;
            valueLabel.text = NumberFormatter.Format(Context().GetBalance(Content.Id));
        }
    }
}
