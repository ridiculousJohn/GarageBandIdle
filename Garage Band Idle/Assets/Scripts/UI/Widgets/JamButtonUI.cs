using System.Text;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The tap button for the producer the module binds (design doc 12.11). The
    // preview reads the same resolution the firing deposits, so what the label
    // promises is what the tap pays (12.5).
    public sealed class JamButtonUI : ModuleWidget
    {
        private readonly Button jamButton;
        private readonly Label yieldLabel;

        private ProducerDefinition producer;

        public JamButtonUI(VisualElement root) : base(root)
        {
            jamButton = Require<Button>(root, "jam", "JamButton.uxml");
            yieldLabel = Require<Label>(root, "yield", "JamButton.uxml");
        }

        protected override void OnBound()
        {
            producer = (ProducerDefinition)Content;
            jamButton.text = Content.displayName;
            // A fresh context per press: the command is a clock sample, so the
            // time it acts at is the time it is pressed at.
            jamButton.clicked += () => Session.FireProducer(Context(), producer);
        }

        public override void Refresh()
        {
            var text = new StringBuilder();
            foreach (var (currency, amount) in Producer.ResolveYield(Context(), producer))
            {
                // Zeros are kept by the resolution and dropped by the reading:
                // a currency this firing pays nothing is not a line.
                if (amount == BigNumber.Zero)
                    continue;
                if (text.Length > 0)
                    text.Append(", ");
                text.Append("+").Append(NumberFormatter.Format(amount)).Append(" ").Append(currency.displayName);
            }
            yieldLabel.text = text.ToString();
        }
    }
}
