using System;
using RidiculousGaming.GarageBandIdle.Meta;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The chapter chrome's two pills (design doc 12.11): state is painted by
    // the host's refresh pass, while the countdown is presentation and follows
    // the shared clock every frame.
    public sealed class TopBarUI
    {
        public VisualElement Root { get; }

        private readonly RootScopeState root;
        private readonly GameClock clock;
        private readonly Button encore;

        public TopBarUI(VisualElement rootElement, RootScopeState root, GameClock clock,
                        Action openEncore, Action openChapterSelect, Action openSettings)
        {
            Root = rootElement;
            this.root = root;
            this.clock = clock;
            encore = ScreenHost.Require<Button>(rootElement, "encore");
            encore.clicked += openEncore;

            var story = ScreenHost.Require<Button>(rootElement, "story-log");
            story.SetEnabled(false); // Slice E supplies the story log.
            story.tooltip = "Story log";
            var chapters = ScreenHost.Require<Button>(rootElement, "chapter-select");
            chapters.tooltip = "Chapters";
            chapters.clicked += openChapterSelect;
            var settings = ScreenHost.Require<Button>(rootElement, "settings-button");
            settings.tooltip = "Settings";
            settings.clicked += openSettings;
        }

        public void Refresh() => Interpolate();

        public void Interpolate() => encore.text = "\u23F1  " + EncoreTime.Text(root, clock.RealTimeUtc);
    }

    // One display answer shared by the pill and window. It is deliberately not
    // simulation truth: BuffActive remains the condition that judges gameplay.
    internal static class EncoreTime
    {
        public static string Text(RootScopeState root, DateTime nowUtc)
        {
            if (BackstagePass.Owned(root))
                return "\u221E";

            DateTime? expiry = null;
            foreach (var buff in root.timedBuffs)
            {
                if (buff == null || buff.buffId != Encore.ModifierId)
                    continue;
                expiry = buff.expiresAtUtc;
                break;
            }

            var seconds = expiry.HasValue ? Math.Max(0, Math.Ceiling((expiry.Value - nowUtc).TotalSeconds)) : 0;
            var whole = (long)seconds;
            var hours = whole / 3600;
            return $"{hours:00}:{whole / 60 % 60:00}:{whole % 60:00}";
        }
    }
}
