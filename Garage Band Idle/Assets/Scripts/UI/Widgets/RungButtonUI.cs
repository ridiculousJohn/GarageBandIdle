using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The prestige card (design doc 12.11), Ctrl C's layout in centred lines:
    // the title with the gain, the current balance of what it pays, the
    // requirements while locked, and Confirm. It binds no content and presses
    // its evaluation scope's OWN rung, exactly as TryRung reads it.
    // Pressability, the requirement lines and the gain all come from the
    // objects the operation enforces, so what the card explains is what would
    // refuse. Nothing here interpolates: a gate has no slope, and its progress
    // numbers snap with the refresh that measured them.
    public sealed class RungButtonUI : ModuleWidget
    {
        private readonly IRungOpener windows;
        private readonly Label title;
        private readonly Button info;
        private readonly Label balance;
        private readonly VisualElement legs;
        private readonly Button confirm;

        // The legs paired with the conditions they render: the gate's top-level
        // list is fixed, so the labels are built once and toggled, and the
        // pairing is what lets the unmet set be matched by identity.
        private readonly List<(Condition condition, Label label)> legViews = new();

        private Rung rung;

        // The refusal line (design doc 12.5/12.11), created and removed per
        // refresh rather than built once and toggled: the condition legs are a
        // fixed authored list, while WHETHER anything refuses - and which event
        // it names - is state. It lands after the condition legs, so a rung
        // with nothing refused shows exactly its condition legs.
        private Label refusalLabel;

        // Whether the payout preview is shown: the gain in the title and the
        // Currently line beneath it.
        public bool PreviewShown { get; private set; }

        // The amounts the title's gain prints, parallel to ShownCurrencies;
        // empty when no preview shows.
        public IReadOnlyList<BigNumber> ShownAmounts { get; private set; } = Array.Empty<BigNumber>();

        // The currencies the preview prints, parallel to ShownAmounts and
        // ShownBalances.
        public IReadOnlyList<Economy.CurrencyDefinition> ShownCurrencies { get; private set; } =
            Array.Empty<Economy.CurrencyDefinition>();

        // The Currently line's balances, one per shown currency, read where the
        // payout deposits; empty when no preview shows.
        public IReadOnlyList<BigNumber> ShownBalances { get; private set; } = Array.Empty<BigNumber>();

        // The top-level legs the requirement lines show, in authored order;
        // empty once the gate holds.
        public IReadOnlyList<Condition> ShownUnmetLegs { get; private set; } = Array.Empty<Condition>();

        // True while the refusal line shows: the rung's actions are refused.
        public bool ShownRefused { get; private set; }

        // True when the rung authors info text, so the (i) is drawn.
        public bool InfoShown { get; private set; }

        // True when the rung is offered: Confirm reads in the accent and the
        // title is not greyed.
        public bool ConfirmEnabled { get; private set; }

        public RungButtonUI(VisualElement root, IRungOpener windows) : base(root)
        {
            title = Require<Label>(root, "title", "RungButton.uxml");
            info = Require<Button>(root, "info", "RungButton.uxml");
            balance = Require<Label>(root, "balance", "RungButton.uxml");
            legs = Require<VisualElement>(root, "legs", "RungButton.uxml");
            confirm = Require<Button>(root, "confirm", "RungButton.uxml");
            // A card with nowhere to send its windows would render dead
            // buttons, and the host is the only caller (requirement 7).
            this.windows = windows ?? throw new ArgumentNullException(nameof(windows));
            info.clicked += OpenInfo;
            confirm.clicked += Press;
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
            // Authored and fixed for the process, so the (i) is judged once.
            InfoShown = !string.IsNullOrEmpty(rung.info);
            info.style.display = InfoShown ? DisplayStyle.Flex : DisplayStyle.None;
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
            ConfirmEnabled = rung.IsOffered(ctx);
            confirm.SetEnabled(ConfirmEnabled);
            title.EnableInClassList("rung-title-locked", !ConfirmEnabled);
            // Each leg is judged on its own, so the card names every unmet one
            // and not just the first the All would have stopped at (12.11).
            // An offered rung has no unmet leg and no refusal, so the
            // requirement lines are there only while it is locked.
            var unmet = GateFeedback.UnmetLegs(rung.offerCondition, ctx);
            ShownUnmetLegs = unmet;
            foreach (var (condition, label) in legViews)
            {
                var visible = unmet.Contains(condition);
                label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    label.text = GateFeedback.LegText(condition, ctx);
            }
            ShowRefusal(ctx);
            ShowPreview(ctx);
        }

        // What Confirm presses, public so a headless test presses what the
        // pointer presses: the confirmation window when the rung authors
        // confirmText, else the rung at once. A rung that is not offered does
        // nothing here, so the window never asks about a press that would
        // refuse; TryRung is fail-closed besides.
        public void Press()
        {
            var ctx = Context();
            if (!rung.IsOffered(ctx))
                return;
            if (string.IsNullOrEmpty(rung.confirmText))
                Session.TryRung(ctx);
            else
                windows.OpenRungConfirm(rung, Scope);
        }

        // What the (i) presses, public for the reason Press is: the info
        // window over this rung. A rung with no info text draws no (i), so
        // there is nothing to open.
        public void OpenInfo()
        {
            if (InfoShown)
                windows.OpenRungInfo(rung);
        }

        // The other half of what closed the card: GateFeedback stays the pure
        // condition half, and the refusal comes from the same runner IsOffered
        // asked (12.5) - one implementation, so what the card explains is what
        // refused.
        private void ShowRefusal(GameContext ctx)
        {
            if (refusalLabel != null)
            {
                legs.Remove(refusalLabel);
                refusalLabel = null;
            }
            var refusal = ActionList.Refuses(rung.actions, ctx);
            ShownRefused = refusal != null;
            if (refusal == null)
                return;
            refusalLabel = new Label(RungFeedback.RefusalText(refusal));
            refusalLabel.AddToClassList("leg");
            legs.Add(refusalLabel);
        }

        // The payout preview, through the rung's own first action - so a rung
        // opening with anything else previews nothing rather than a wrong
        // number (12.5), and its title is the label alone. Each target shows
        // its own amount, since each is paid under its own factors, and the
        // gain shows while locked too, since it reads what the run has earned
        // so far. The Currently line reads each target's balance at the home
        // the payout deposits to.
        private void ShowPreview(GameContext ctx)
        {
            if (!RungFeedback.TryPreviewPayout(rung, ctx, out var amounts, out var currencies))
            {
                PreviewShown = false;
                ShownAmounts = Array.Empty<BigNumber>();
                ShownCurrencies = Array.Empty<Economy.CurrencyDefinition>();
                ShownBalances = Array.Empty<BigNumber>();
                title.text = rung.label;
                balance.style.display = DisplayStyle.None;
                return;
            }
            ShownAmounts = amounts;
            ShownCurrencies = currencies;
            var balances = new List<BigNumber>(currencies.Count);
            foreach (var currency in currencies)
                balances.Add(ctx.GetBalance(currency.Id));
            ShownBalances = balances;
            PreviewShown = true;
            title.text = rung.label + " (Gain " + Join(ShownAmounts) + ")";
            balance.text = "Currently: " + Join(ShownBalances);
            balance.style.display = DisplayStyle.Flex;
        }

        // One line's amounts, each beside its currency's name, comma-joined in
        // the payout's own target order.
        private string Join(IReadOnlyList<BigNumber> values)
        {
            var line = new StringBuilder();
            for (var i = 0; i < ShownCurrencies.Count; i++)
            {
                if (line.Length > 0)
                    line.Append(", ");
                line.Append(NumberFormatter.Format(values[i])).Append(' ').Append(ShownCurrencies[i].displayName);
            }
            return line.ToString();
        }
    }
}
