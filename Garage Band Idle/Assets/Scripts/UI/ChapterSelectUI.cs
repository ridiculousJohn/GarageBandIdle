using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The fresh-game select (design doc 12.9): one button per chapter in root's
    // roster, in composition order (12.14.5 sorts it), named from content. It
    // is the whole screen for a fresh game and an overlay from Live chapter
    // chrome; only the latter offers Back. Built once because the roster is
    // fixed for the process.
    public sealed class ChapterSelectUI
    {
        public VisualElement Root { get; }

        private readonly Button close;
        private readonly GameSession session;
        private readonly GameClock clock;
        private readonly Action beforeSelection;
        private readonly Dictionary<ChapterScopeState, Button> buttons = new();

        public ChapterSelectUI(VisualElement root, GameSession session, GameClock clock,
                               Action beforeSelection, Action onClose)
        {
            Root = root;
            this.session = session;
            this.clock = clock;
            this.beforeSelection = beforeSelection;
            var chapters = ScreenHost.Require<VisualElement>(root, "chapters");
            close = ScreenHost.Require<Button>(root, "select-close");
            close.clicked += onClose;

            foreach (var child in session.Root.Children)
            {
                var button = new Button { text = child.Definition.displayName };
                button.AddToClassList("select-chapter");
                // Root's children are chapters by construction (12.3). The pick
                // is a switch at the clock's time, in a fresh context per press,
                // exactly as every widget command runs.
                button.clicked += () =>
                    Select((ChapterScopeState)child);
                chapters.Add(button);
                buttons[(ChapterScopeState)child] = button;
            }
        }

        // What each generated chapter button calls. Kept as one public UI
        // action so headless tests exercise the same presentation-before-
        // command order without manufacturing pointer events.
        public void Select(ChapterScopeState chapter)
        {
            if (!session.IsChapterUnlocked(chapter, clock.RealTimeUtc))
                return;
            // In overlay mode this closes before the switch. Selecting the
            // live chapter is a deliberate session no-op with no refresh, so
            // presentation cannot wait on one.
            beforeSelection();
            session.SwitchChapter(chapter, clock.RealTimeUtc);
        }

        public void Show(bool canClose)
        {
            foreach (var pair in buttons)
                pair.Value.SetEnabled(session.IsChapterUnlocked(pair.Key, clock.RealTimeUtc));
            close.style.display = canClose ? DisplayStyle.Flex : DisplayStyle.None;
            Root.style.display = DisplayStyle.Flex;
        }

        public void Hide() => Root.style.display = DisplayStyle.None;
    }
}
