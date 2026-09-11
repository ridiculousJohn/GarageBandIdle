using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The story log (design doc 10, 12.11): one button per read beat, over every
    // chapter in root's roster, reopening the beat through the host's card. It
    // reads no section and no module - a beat is content on the chapter, so the
    // log walks each chapter's own declaration list.
    public sealed class StoryLogUI
    {
        public VisualElement Root { get; }

        private readonly GameClock clock;
        private readonly IStoryOpener stories;
        private readonly Label empty;

        // Every candidate beat with the chapter it is declared on and the button
        // that stands for it, the pairing StoryRowUI.legViews uses: root's
        // roster and each chapter's declaration list are fixed for the process,
        // so the buttons are built at construction and a show toggles them.
        private readonly List<(Story.StoryBeatDefinition beat, ChapterScopeState chapter, Button button)> buttons = new();

        public StoryLogUI(VisualElement rootElement, RootScopeState root, GameClock clock,
                          IStoryOpener stories, Action close)
        {
            Root = rootElement;
            this.clock = clock;
            this.stories = stories;
            var entries = ScreenHost.Require<VisualElement>(rootElement, "story-log-entries");
            empty = ScreenHost.Require<Label>(rootElement, "story-log-empty");
            ScreenHost.Require<Button>(rootElement, "story-log-close").clicked += close;
            foreach (var child in root.Children)
            {
                // Root's children are chapters by construction (12.3), and this
                // is the one walk downward through a roster the log already
                // holds (12.14.8) - nothing is searched for.
                var chapter = (ChapterScopeState)child;
                foreach (var beat in ((ChapterDefinition)chapter.Definition).storyBeats)
                {
                    if (beat == null)
                        continue;
                    var button = new Button { text = beat.displayName };
                    button.AddToClassList("story-log-entry");
                    button.clicked += () => Open(beat, chapter);
                    button.style.display = DisplayStyle.None;
                    buttons.Add((beat, chapter, button));
                    entries.Add(button);
                }
            }
        }

        // The buttons exist for the whole process, so a show is a toggle per
        // latch: a refresh under a press changes a display and leaves every
        // element where the pointer found it, which is what keeps the press.
        public void Show()
        {
            Root.style.display = DisplayStyle.Flex;
            var listed = false;
            foreach (var (beat, chapter, button) in buttons)
            {
                // The latch read the row and the host's card take: outward from
                // the beat's declaring chapter, where its flag is homed (12.3).
                // Unread beats are not in the log.
                var ctx = new GameContext(chapter, clock.RealTimeUtc);
                var read = ctx.IsFlagSet(beat.seenFlag);
                button.style.display = read ? DisplayStyle.Flex : DisplayStyle.None;
                listed |= read;
            }
            empty.style.display = listed ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void Hide() => Root.style.display = DisplayStyle.None;

        // What each generated beat button calls. Kept as one public UI action so
        // headless tests exercise it without manufacturing pointer events, the
        // same reason ChapterSelectUI.Select is public.
        public void Open(Story.StoryBeatDefinition beat, ChapterScopeState chapter)
        {
            stories.OpenStory(beat, chapter);
        }
    }
}
