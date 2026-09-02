using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The rung button (design doc 12.11). It binds no content and presses its
    // evaluation scope's OWN rung, exactly as TryRung reads it. Pressability,
    // the legs, and the preview all come from the objects the operation
    // enforces, so what the button explains is what would refuse. Nothing here
    // interpolates: a gate has no slope, and its progress numbers snap with
    // the refresh that measured them.
    public sealed class RungButtonUI : ModuleWidget
    {
        private readonly Button press;
        private readonly Label preview;
        private readonly VisualElement legs;

        // The legs paired with the conditions they render: the gate's top-level
        // list is fixed, so the labels are built once and toggled, and the
        // pairing is what lets the unmet set be matched by identity.
        private readonly List<(Condition condition, Label label)> legViews = new();

        private Rung rung;

        public RungButtonUI(VisualElement root) : base(root)
        {
            press = Require<Button>(root, "press", "RungButton.uxml");
            preview = Require<Label>(root, "preview", "RungButton.uxml");
            legs = Require<VisualElement>(root, "legs", "RungButton.uxml");
        }

        protected override void OnBound()
        {
            rung = (Scope.Definition as InteriorDefinition)?.rung;
            // Static content cannot legitimately be unresolvable (requirement
            // 7): a module authored here without a rung to press is a content
            // fault, not a state the player produced.
            if (rung == null)
                throw new InvalidOperationException(
                    $"Module scope '{Scope.ScopeId}' declares no rung for a rung_button (design doc 12.11).");
            press.text = rung.label;
            press.clicked += () => Session.TryRung(Context());
            foreach (var leg in GateFeedback.Legs(rung.offerCondition))
            {
                var label = new Label();
                label.AddToClassList("leg");
                legViews.Add((leg, label));
                legs.Add(label);
            }
        }

        public override void Refresh()
        {
            var ctx = Context();
            press.SetEnabled(rung.IsOffered(ctx));
            // Each leg is judged on its own, so the button names every unmet
            // one and not just the first the All would have stopped at (12.11).
            var unmet = GateFeedback.UnmetLegs(rung.offerCondition, ctx);
            foreach (var (condition, label) in legViews)
            {
                var visible = unmet.Contains(condition);
                label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    label.text = GateFeedback.LegText(condition, ctx);
            }
            ShowPreview(ctx);
        }

        // The payout preview, through the rung's own first action - so a rung
        // opening with anything else previews nothing rather than a wrong
        // number, which is what the capstone's ExecuteRung opening wants (12.5).
        private void ShowPreview(GameContext ctx)
        {
            if (!RungFeedback.TryPreviewPayout(rung, ctx, out var amount, out var currencies))
            {
                preview.style.display = DisplayStyle.None;
                return;
            }
            var names = new StringBuilder();
            foreach (var currency in currencies)
            {
                if (names.Length > 0)
                    names.Append(", ");
                names.Append(currency.displayName);
            }
            preview.style.display = DisplayStyle.Flex;
            preview.text = "Would bank: +" + NumberFormatter.Format(amount) + " " + names.ToString();
        }
    }
}
