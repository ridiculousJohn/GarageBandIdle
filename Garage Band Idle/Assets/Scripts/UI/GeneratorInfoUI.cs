using System;
using System.Text;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One generator's info screen (design doc 12.11): the flavor text, the row's
    // own cost and yield line, the owned count, and what those units produce -
    // the figure no row shows. No command of its own, and no state beyond the
    // generator and the scope a row's hold handed over.
    public sealed class GeneratorInfoUI
    {
        public VisualElement Root { get; }

        private readonly GameClock clock;
        private readonly Label name;
        private readonly Label description;
        private readonly Label cost;
        private readonly Label ownedLabel;
        private readonly Label production;

        private GeneratorDefinition generator;

        // The scope the row resolved as the generator's declaring one, which is
        // where the count, the cost and the rate are facts (12.3).
        private ScopeState scope;

        public GeneratorInfoUI(VisualElement rootElement, GameClock clock, Action close)
        {
            Root = rootElement;
            this.clock = clock;
            name = ScreenHost.Require<Label>(rootElement, "info-name");
            description = ScreenHost.Require<Label>(rootElement, "info-description");
            cost = ScreenHost.Require<Label>(rootElement, "info-cost");
            ownedLabel = ScreenHost.Require<Label>(rootElement, "info-owned");
            production = ScreenHost.Require<Label>(rootElement, "info-production");
            ScreenHost.Require<Button>(rootElement, "info-close").clicked += close;
        }

        // Null when nothing is held: the pair is the request, and the host asks
        // for the screen only after handing one over.
        public GeneratorDefinition Held => generator;

        public ScopeState HeldScope => scope;

        public void Hold(GeneratorDefinition generator, ScopeState scope)
        {
            this.generator = generator;
            this.scope = scope;
        }

        public void Show()
        {
            if (generator == null)
                throw new InvalidOperationException(
                    "GeneratorInfoUI.Show with no generator held (design doc 12.11).");
            Root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Refresh()
        {
            // The scope handed in IS the declaring scope, resolved by the row,
            // so nothing here walks for it.
            var ctx = new GameContext(scope, clock.RealTimeUtc);
            name.text = generator.displayName;
            // An absent description leaves the panel without the line.
            description.text = generator.description;
            description.style.display = string.IsNullOrEmpty(generator.description)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            cost.text = GeneratorRowUI.CostAndYieldText(ctx, generator);
            var owned = ctx.GetOwnedCount(generator.Id);
            ownedLabel.text = "Owned: " + owned;
            production.text = "Producing: " + ProductionText(ctx, owned);
        }

        public void Hide() => Root.style.display = DisplayStyle.None;

        // The request is dropped, so the pair goes with it and the screen comes
        // down.
        public void Drop()
        {
            generator = null;
            scope = null;
            Hide();
        }

        // The count times the per-unit rate IS the production: nothing in the
        // effect vocabulary reads the owned count, so one unit's term is what
        // every unit pays (the Producer.UnitRate comment). This is per GAME
        // second at the declaring scope, while the header's slope is the
        // realized per-real-second figure - two honest numbers, neither
        // converted into the other.
        private string ProductionText(GameContext ctx, int owned)
        {
            var line = new StringBuilder();
            foreach (var (currency, amount) in Producer.UnitRate(ctx, generator))
            {
                var product = amount * owned;
                if (product == BigNumber.Zero)
                    continue;
                if (line.Length > 0)
                    line.Append(", ");
                line.Append(NumberFormatter.Format(product)).Append(" ")
                    .Append(currency.displayName).Append("/s");
            }
            return line.Length == 0 ? "nothing" : line.ToString();
        }
    }
}
