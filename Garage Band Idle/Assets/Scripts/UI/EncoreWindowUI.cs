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
        private readonly AdManager ads;
        private readonly IAPManager store;
        private readonly Action close;
        private readonly Label remaining;
        private readonly Label description;
        private readonly Button ad;
        private readonly Button pass;

        // The timer the chrome counts down, authored on the pill and handed here by
        // the host, so this window names no timer of its own.
        private readonly string timerId;

        public EncoreWindowUI(VisualElement rootElement, RootScopeState root, string timerId, GameClock clock,
                              AdManager ads, IAPManager store, Action close)
        {
            Root = rootElement;
            this.root = root;
            this.timerId = timerId;
            this.clock = clock;
            this.ads = ads;
            this.store = store;
            this.close = close;
            remaining = ScreenHost.Require<Label>(rootElement, "encore-remaining");
            description = ScreenHost.Require<Label>(rootElement, "encore-description");
            ad = ScreenHost.Require<Button>(rootElement, "encore-ad");
            pass = ScreenHost.Require<Button>(rootElement, "encore-pass");
            var grant = GrantSeconds(root.DefinitionAs<RootDefinition>());
            ad.text = grant > 0 ? "Boost for " + GrantDuration(grant) : "Boost";
            ad.clicked += RequestAd;
            pass.clicked += RequestPass;
            ScreenHost.Require<Button>(rootElement, "encore-close").clicked += close;
        }

        // What each request button calls. Kept as public UI actions for the
        // reason ChapterSelectUI.Select is public: a headless test exercises
        // what the button exercises without manufacturing pointer events.
        public void RequestAd() => ads.RequestEncoreExtension(CloseIfShown);

        public void RequestPass() => store.RequestPurchase(ProductId.BackstagePass, CloseIfShown);

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

        public void Interpolate() => remaining.text = "Time remaining " + EncoreTime.Text(root, timerId, clock.RealTimeUtc);

        public void Hide() => Root.style.display = DisplayStyle.None;

        // The grant may land after the player closed the window and opened
        // something else, and closing then would take down whatever stands - so
        // the window closes only itself.
        private void CloseIfShown()
        {
            if (Root.style.display.value == DisplayStyle.Flex)
                close();
        }

        private void RefreshDescription()
        {
            // What the timer buys right now: every root modifier reading the pill's
            // timer contributes its wildcard game_speed factor, and they multiply -
            // a rung whose own appliesWhen holds, which is the membership the gather
            // reads (null is always), and a rung with no band regardless, since that
            // is the promise the ad pays for. So one rung prints 2.00x, a ladder
            // prints 4.00x once its gate holds and 2.00x before, and the tick agrees
            // whatever legs the tier's gate is authored with. A timer whose buffs
            // express the boost through some other authored stat has no factor to
            // print, and the window says so in words instead.
            var factor = BigNumber.One;
            var found = false;
            var ctx = new GameContext(root, clock.RealTimeUtc);
            foreach (var modifier in root.Definition.modifiers)
            {
                if (modifier == null || modifier.timer != timerId)
                    continue;
                var applies = modifier.appliesWhen == null || modifier.appliesWhen.Evaluate(ctx);
                if (!applies && modifier.activeAfterSeconds > 0)
                    continue;
                foreach (var effect in modifier.effects)
                {
                    if (effect.stat != Stat.GameSpeed || !string.IsNullOrEmpty(effect.target)
                        || !string.IsNullOrEmpty(effect.currencyId))
                        continue;
                    factor *= Producer.FactorOf(effect, ctx);
                    found = true;
                }
            }
            description.text = found
                ? "While active, game speed is " + NumberFormatter.Format(factor) + "x."
                : "While active, Encore boosts your band.";
        }

        // The promise is the grant: the seconds of the first ExtendTimer root's reward
        // list authors, and zero when the list pays in something else.
        internal static double GrantSeconds(RootDefinition root)
        {
            foreach (var action in root.encoreAdReward)
                if (action is ExtendTimer extend)
                    return extend.seconds;
            return 0;
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
