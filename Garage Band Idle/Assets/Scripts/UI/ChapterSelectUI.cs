using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The fresh-game select (design doc 12.9): one button per chapter in root's
    // roster, in composition order (12.14.5 sorts it), named from content. Only
    // a save with no recorded chapter ever shows it, because boot auto-enters a
    // recorded chapter and such a save owes no idle. Built once: the roster is
    // fixed for the process, so there is nothing to refresh - the host toggles
    // the whole screen.
    public sealed class ChapterSelectUI
    {
        public VisualElement Root { get; }

        public ChapterSelectUI(VisualElement root, GameSession session, GameClock clock)
        {
            Root = root;
            var chapters = ScreenHost.Require<VisualElement>(root, "chapters");

            foreach (var child in session.Root.Children)
            {
                var button = new Button { text = child.Definition.displayName };
                button.AddToClassList("select-chapter");
                // Root's children are chapters by construction (12.3). The pick
                // is a switch at the clock's time, in a fresh context per press,
                // exactly as every widget command runs.
                button.clicked += () => session.SwitchChapter((ChapterScopeState)child, clock.RealTimeUtc);
                chapters.Add(button);
            }
        }
    }
}
