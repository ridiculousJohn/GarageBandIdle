using System;
using System.Globalization;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Meta;
using RidiculousGaming.GarageBandIdle.Monetization;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    public sealed class EncoreWindowUI
    {
        public VisualElement Root { get; }

        private readonly RootScopeState root;
        private readonly GameClock clock;
        private readonly Label remaining;
        private readonly Label description;
        private readonly Button ad;
        private readonly Button pass;
        private readonly ModifierDefinition modifier;

        public EncoreWindowUI(VisualElement rootElement, RootScopeState root, GameClock clock,
                              AdManager ads, IAPManager store, Action close)
        {
            Root = rootElement;
            this.root = root;
            this.clock = clock;
            remaining = ScreenHost.Require<Label>(rootElement, "encore-remaining");
            description = ScreenHost.Require<Label>(rootElement, "encore-description");
            ad = ScreenHost.Require<Button>(rootElement, "encore-ad");
            pass = ScreenHost.Require<Button>(rootElement, "encore-pass");
            modifier = Encore.Modifier(root);
            ad.text = "Boost for " + GrantDuration(ads.EncoreAdSeconds);
            ad.clicked += ads.RequestEncoreExtension;
            pass.clicked += () => store.RequestPurchase(ProductId.BackstagePass);
            ScreenHost.Require<Button>(rootElement, "encore-close").clicked += close;
        }

        public void Show()
        {
            Root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Refresh()
        {
            var owned = BackstagePass.Owned(root);
            ad.style.display = owned ? DisplayStyle.None : DisplayStyle.Flex;
            pass.style.display = owned ? DisplayStyle.None : DisplayStyle.Flex;
            RefreshDescription();
            Interpolate();
        }

        public void Interpolate() => remaining.text = "Time remaining " + EncoreTime.Text(root, clock.RealTimeUtc);

        public void Hide() => Root.style.display = DisplayStyle.None;

        private void RefreshDescription()
        {
            // Current content authors Encore as wildcard game_speed. The UI
            // reads that shape when present, but Encore remains valid if later
            // content expresses the same boost through another authored stat.
            foreach (var effect in modifier.effects)
            {
                if (effect.stat != Stat.GameSpeed || !string.IsNullOrEmpty(effect.target)
                    || !string.IsNullOrEmpty(effect.currencyId))
                    continue;
                var factor = Producer.FactorOf(effect, new GameContext(root, clock.RealTimeUtc));
                description.text = "While active, game speed is "
                    + NumberFormatter.Format(factor) + "x.";
                return;
            }
            description.text = "While active, Encore boosts your band.";
        }

        private static string GrantDuration(double seconds)
        {
            var hours = seconds / 3600;
            if (hours == Math.Floor(hours))
                return hours.ToString("0", CultureInfo.InvariantCulture) + (hours == 1 ? " hour" : " hours");
            var whole = (long)Math.Ceiling(seconds);
            return $"{whole / 3600:00}:{whole / 60 % 60:00}:{whole % 60:00}";
        }
    }
}
