using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The discography (design doc 12.11): one row per song root keeps, in the
    // order they were written, each a name field and a button that renames the
    // song through the session. The UI holds no name of its own - a field shows
    // what is stored and a rename is a command.
    public sealed class DiscographyUI
    {
        public VisualElement Root { get; }

        private readonly RootScopeState root;
        private readonly GameSession session;
        private readonly GameClock clock;
        private readonly VisualElement entries;

        // The name field standing for each song's row, by song id, so a
        // rename's answer reaches the row it came from and a show can tell
        // which songs already have one.
        private readonly Dictionary<string, TextField> fields = new();

        public DiscographyUI(VisualElement rootElement, RootScopeState root, GameSession session, GameClock clock,
                             Action close)
        {
            Root = rootElement;
            this.root = root;
            this.session = session;
            this.clock = clock;
            entries = ScreenHost.Require<VisualElement>(rootElement, "discography-entries");
            ScreenHost.Require<Button>(rootElement, "discography-close").clicked += close;
        }

        // The host calls this when the window opens and again on every refresh
        // while it stands, a tick's included. Songs are only ever appended, so
        // a row is built once, for a song with none, and kept. A field takes
        // the stored name when the window OPENS - when it was hidden coming in
        // - and is left alone on a refresh, which is what lets the player type
        // a name across ticks without the text being put back under them.
        public void Show()
        {
            var opening = Root.style.display != DisplayStyle.Flex;
            Root.style.display = DisplayStyle.Flex;
            foreach (var song in root.songs)
            {
                if (song == null)
                    continue;
                if (fields.TryGetValue(song.songId, out var standing))
                {
                    if (opening)
                        standing.value = song.name;
                    continue;
                }
                var songId = song.songId;
                var row = new VisualElement();
                row.AddToClassList("discography-row");
                var field = new TextField { value = song.name, maxLength = SongEntry.MaxNameLength };
                field.AddToClassList("discography-name");
                var rename = new Button { text = "Rename" };
                rename.AddToClassList("discography-rename");
                rename.clicked += () => Rename(songId, field.value);
                row.Add(field);
                row.Add(rename);
                entries.Add(row);
                fields[songId] = field;
            }
        }

        public void Hide() => Root.style.display = DisplayStyle.None;

        // What each row's button calls. Kept as one public UI action so headless
        // tests exercise it without manufacturing pointer events, the same
        // reason StoryLogUI.Open is public. Once the command has run, the row's
        // field shows the stored name: the trimmed, capped one a rename
        // accepted, or the unchanged one a refused rename leaves.
        public void Rename(string songId, string name)
        {
            session.RenameSong(songId, name, clock.RealTimeUtc, _ =>
            {
                if (!fields.TryGetValue(songId, out var field))
                    return;
                foreach (var song in root.songs)
                {
                    if (song == null || song.songId != songId)
                        continue;
                    field.value = song.name;
                    return;
                }
            });
        }
    }
}
