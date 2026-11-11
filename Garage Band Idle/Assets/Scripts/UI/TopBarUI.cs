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
        private readonly TimerPill encore;
        private readonly Button discography;

        // The id the chrome counts down, authored on the pill and handed on to the
        // window, so the screen names its timer once and the code names none.
        public string TimerId => encore.Timer;

        // The pill shows infinity.
        public bool ShownPermanent { get; private set; }

        // The seconds the pill's countdown shows; zero when ShownPermanent.
        public long ShownSeconds { get; private set; }

        public TopBarUI(VisualElement rootElement, RootScopeState root, GameClock clock,
                        Action openEncore, Action openStoryLog, Action openDiscography, Action openChapterSelect,
                        Action openSettings)
        {
            Root = rootElement;
            this.root = root;
            this.clock = clock;
            encore = ScreenHost.Require<TimerPill>(rootElement, "encore");
            // A pill naming a timer root does not declare counts down nothing and
            // would read as an expired one forever, so the screen is refused here the
            // way Require refuses a missing element (requirement 7).
            if (string.IsNullOrEmpty(encore.Timer) || !root.Definition.DeclaresTimer(encore.Timer))
                throw new InvalidOperationException(
                    $"Screen.uxml's pill 'encore' names timer '{encore.Timer}', which root does not declare (design doc 12.11).");
            encore.clicked += openEncore;

            var story = ScreenHost.Require<Button>(rootElement, "story-log");
            story.tooltip = "Story log";
            story.clicked += openStoryLog;
            discography = ScreenHost.Require<Button>(rootElement, "discography");
            discography.tooltip = "Discography";
            discography.clicked += openDiscography;
            ShowDiscography();
            var chapters = ScreenHost.Require<Button>(rootElement, "chapter-select");
            chapters.tooltip = "Chapters";
            chapters.clicked += openChapterSelect;
            var settings = ScreenHost.Require<Button>(rootElement, "settings-button");
            settings.tooltip = "Settings";
            settings.clicked += openSettings;
        }

        public void Refresh()
        {
            ShowDiscography();
            Interpolate();
        }

        public void Interpolate()
        {
            ShownPermanent = EncoreTime.Permanent(root);
            ShownSeconds = ShownPermanent ? 0 : EncoreTime.RemainingSeconds(root, encore.Timer, clock.RealTimeUtc);
            encore.text = "\u23F1  " + EncoreTime.Format(ShownPermanent, ShownSeconds);
        }

        // The discography button stands only while root keeps a song, so a
        // player with none never opens an empty window.
        private void ShowDiscography() =>
            discography.style.display = root.songs.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // One display answer shared by the pill and window. It is deliberately not
    // simulation truth: BuffActive remains the condition that judges gameplay.
    public static class EncoreTime
    {
        public static string Text(RootScopeState root, string timerId, DateTime nowUtc)
        {
            var permanent = Permanent(root);
            return Format(permanent, permanent ? 0 : RemainingSeconds(root, timerId, nowUtc));
        }

        // The Pass is owned, so the timer reads as infinity.
        public static bool Permanent(RootScopeState root) => BackstagePass.Owned(root);

        // Whole seconds left on the timer, rounded up, zero when root holds no
        // such timer or it has expired. Meaningful when not Permanent.
        public static long RemainingSeconds(RootScopeState root, string timerId, DateTime nowUtc)
        {
            DateTime? expiry = null;
            foreach (var buff in root.timedBuffs)
            {
                if (buff == null || buff.buffId != timerId)
                    continue;
                expiry = buff.expiresAtUtc;
                break;
            }

            var seconds = expiry.HasValue ? Math.Max(0, Math.Ceiling((expiry.Value - nowUtc).TotalSeconds)) : 0;
            return (long)seconds;
        }

        // The text for an answer already read, so a caller that stores the
        // answer prints exactly what it stored.
        internal static string Format(bool permanent, long whole)
        {
            if (permanent)
                return "\u221E";
            var hours = whole / 3600;
            return $"{hours:00}:{whole / 60 % 60:00}:{whole % 60:00}";
        }
    }
}
