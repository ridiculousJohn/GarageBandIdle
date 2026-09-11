using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Meta;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // A short-lived editor for the whole allocation map. The draft begins when
    // the overlay opens, dies when it closes, and Done submits exactly one
    // replace command with a copy the UI no longer owns.
    public sealed class RoadieAllocationUI
    {
        public VisualElement Root { get; }

        private readonly GameSession session;
        private readonly GameClock clock;
        private readonly Action close;
        private readonly VisualElement rows;
        private readonly Label unallocated;
        private readonly Button done;
        private readonly Dictionary<string, int> draft = new();
        private readonly Dictionary<string, Label> counts = new();
        private readonly Dictionary<string, Button> minuses = new();
        private readonly Dictionary<string, Button> pluses = new();
        private readonly Dictionary<string, ChapterScopeState> chapters = new();

        public RoadieAllocationUI(VisualElement root, GameSession session, GameClock clock, Action close)
        {
            Root = root;
            this.session = session;
            this.clock = clock;
            this.close = close;
            rows = ScreenHost.Require<VisualElement>(root, "roadie-rows");
            unallocated = ScreenHost.Require<Label>(root, "roadie-unallocated");
            done = ScreenHost.Require<Button>(root, "roadie-done");
            done.clicked += Done;
            ScreenHost.Require<Button>(root, "roadie-close").clicked += close;
            BuildRows();
        }

        public void Open()
        {
            draft.Clear();
            foreach (var child in session.Root.Children)
            {
                session.Root.roadieAllocation.TryGetValue(child.ScopeId, out var value);
                draft[child.ScopeId] = value;
            }
            RefreshDraft();
            done.SetEnabled(true);
            Root.style.display = DisplayStyle.Flex;
        }

        // A grant or another authored write may repaint while this draft is
        // open. Keep the player's counts and refresh only what changed around
        // them: the available pool and the buttons that depend on it.
        public void Refresh()
        {
            if (Root.style.display.value == DisplayStyle.Flex)
                RefreshDraft();
        }

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
            draft.Clear();
        }

        private void BuildRows()
        {
            rows.Clear();
            foreach (var child in session.Root.Children)
            {
                var chapter = (ChapterScopeState)child;
                var id = child.ScopeId;
                var row = new VisualElement();
                row.AddToClassList("roadie-row");
                var name = new Label(child.Definition.displayName);
                name.AddToClassList("roadie-name");
                var minus = new Button { text = "-" };
                minus.name = "roadie-minus-" + id;
                minus.clicked += () => Decrease(id);
                var count = new Label("0");
                count.name = "roadie-count-" + id;
                count.AddToClassList("roadie-count");
                var plus = new Button { text = "+" };
                plus.name = "roadie-plus-" + id;
                plus.clicked += () => Increase(id);
                row.Add(name);
                row.Add(minus);
                row.Add(count);
                row.Add(plus);
                rows.Add(row);
                counts[id] = count;
                minuses[id] = minus;
                pluses[id] = plus;
                chapters[id] = chapter;
            }
        }

        public void Increase(string chapterId) => Change(chapterId, 1);

        public void Decrease(string chapterId) => Change(chapterId, -1);

        private void Change(string id, int delta)
        {
            if (!session.IsChapterUnlocked(chapters[id], clock.RealTimeUtc))
                return;
            var current = draft[id];
            if (delta < 0)
            {
                if (current == 0)
                    return;
                draft[id] = current - 1;
            }
            else
            {
                if (current == int.MaxValue || Remaining() < BigNumber.One)
                    return;
                draft[id] = current + 1;
            }
            RefreshDraft();
        }

        private BigNumber Remaining()
        {
            var stationed = 0L;
            foreach (var value in draft.Values)
                stationed += value;
            return new GameContext(session.Root, clock.RealTimeUtc).GetBalance(Roadies.CurrencyId) - stationed;
        }

        private void RefreshDraft()
        {
            // Eligibility is state, so every session refresh re-reads it. A
            // chapter that is locked while the editor stands contributes no
            // submitted entry; allocations for every still-eligible chapter
            // remain the player's local draft.
            foreach (var pair in chapters)
                if (!session.IsChapterUnlocked(pair.Value, clock.RealTimeUtc))
                    draft[pair.Key] = 0;

            foreach (var pair in draft)
            {
                var unlocked = session.IsChapterUnlocked(chapters[pair.Key], clock.RealTimeUtc);
                counts[pair.Key].text = pair.Value.ToString();
                minuses[pair.Key].SetEnabled(unlocked && pair.Value > 0);
            }
            var remaining = Remaining();
            foreach (var pair in draft)
            {
                var unlocked = session.IsChapterUnlocked(chapters[pair.Key], clock.RealTimeUtc);
                pluses[pair.Key].SetEnabled(unlocked && pair.Value < int.MaxValue && remaining >= BigNumber.One);
            }
            unallocated.text = "Unallocated  " + NumberFormatter.Format(remaining);
        }

        public void Done()
        {
            var submitted = new Dictionary<string, int>();
            foreach (var pair in draft)
                if (pair.Value > 0)
                    submitted[pair.Key] = pair.Value;
            done.SetEnabled(false);
            session.SetRoadieAllocation(submitted, clock.RealTimeUtc, accepted =>
            {
                done.SetEnabled(true);
                if (accepted)
                    close();
                else
                    Refresh();
            });
        }
    }
}
