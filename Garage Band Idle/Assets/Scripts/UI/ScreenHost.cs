using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Monetization;
using RidiculousGaming.GarageBandIdle.Story;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The screen's structure logic (design doc 12.11) - the authored sections
    // while Live, the select while NoChapter, the collect dialog while
    // AwaitingIdleClaim, and the story card over the live sections - plain C# so
    // an EditMode test builds it over imported content with no panel; UIRoot is
    // the MonoBehaviour shell around it. This is the ONE Refreshed subscriber -
    // widgets subscribe to nothing, so there is one dispatch order.
    public sealed class ScreenHost : IDisposable, IStoryOpener
    {
        // One module and the widget standing for it. The host owns these views,
        // so it writes them and anything holding the host reads them.
        public sealed class ModuleView
        {
            public ModuleDefinition Definition { get; }
            public ScopeState Scope { get; }
            public bool Visible { get; internal set; }

            // Null until the module is first visible: instantiation is lazy,
            // and the widget is toggled rather than rebuilt thereafter.
            public ModuleWidget Widget { get; internal set; }

            internal ModuleView(ModuleDefinition definition, ScopeState scope)
            {
                Definition = definition;
                Scope = scope;
            }
        }

        // One authored band of the screen. Its elements are built in code
        // rather than from a UXML: a section is a title and a container, and
        // nothing about that shape is authored per chapter.
        public sealed class SectionView
        {
            public SectionDefinition Definition { get; }
            public ScopeState Scope { get; }
            public bool Visible { get; internal set; }
            public VisualElement Root { get; }
            public IReadOnlyList<ModuleView> Modules => modules;

            private readonly List<ModuleView> modules = new();

            // Where module roots land, kept apart from the title so a widget's
            // index among the section's modules is its index here.
            internal VisualElement ModulesContainer { get; }

            internal SectionView(SectionDefinition definition, ScopeState scope)
            {
                Definition = definition;
                Scope = scope;

                Root = new VisualElement();
                Root.AddToClassList("section");
                var title = new Label(definition.title);
                title.AddToClassList("section-title");
                Root.Add(title);
                ModulesContainer = new VisualElement();
                ModulesContainer.AddToClassList("section-modules");
                Root.Add(ModulesContainer);
            }

            internal void AddModule(ModuleView module) => modules.Add(module);
        }

        private readonly VisualElement container;
        private readonly ModuleRegistry registry;
        private readonly GameSession session;
        private readonly GameClock clock;
        private readonly ChapterSelectUI select;
        private readonly CollectScreenUI collect;
        private readonly StoryBeatUI story;
        private readonly TopBarUI topBar;
        private readonly SettingsUI settings;
        private readonly RoadieAllocationUI allocation;
        private readonly EncoreWindowUI encore;
        private readonly StoryLogUI storyLog;
        private readonly List<SectionView> sections = new();

        private enum LiveOverlay
        {
            None,
            ChapterSelect,
            Settings,
            Roadies,
            Encore,
            StoryLog,
        }

        private LiveOverlay requestedOverlay;

        // The chapter the section views describe. Identity, not id: a switch
        // rebuilds, and a same-definition node from another tree is a different
        // screen.
        private ChapterScopeState builtFor;

        // The standing request: the beat whose card is open - asked for by a
        // row's button or by the marked-beat walk alike - held until a close
        // drops it or the phase leaves Live.
        private StoryBeatDefinition requestedBeat;

        public IReadOnlyList<SectionView> Sections => sections;

        public StoryBeatDefinition ShownStory => requestedBeat;

        public RoadieAllocationUI RoadieAllocation => allocation;

        public StoryLogUI StoryLog => storyLog;

        // Over the screen's own root: the host owns every app-owned screen and
        // overlay, so it is the one place that knows which named elements
        // Screen.uxml promises.
        public ScreenHost(VisualElement screenRoot, ModuleRegistry registry, GameSession session, GameClock clock,
                          AdManager ads, IAPManager store)
        {
            container = Require<VisualElement>(screenRoot, "sections");
            this.registry = registry;
            this.session = session;
            this.clock = clock;
            select = new ChapterSelectUI(Require<VisualElement>(screenRoot, "select"), session, clock,
                BeforeChapterSelection, CloseOverlay);
            collect = new CollectScreenUI(Require<VisualElement>(screenRoot, "collect"), session, clock, ads, store);
            story = new StoryBeatUI(Require<VisualElement>(screenRoot, "story"), CloseStory);
            topBar = new TopBarUI(Require<VisualElement>(screenRoot, "top-bar"), session.Root, clock,
                OpenEncore, OpenStoryLog, OpenChapterSelect, OpenSettings);
            settings = new SettingsUI(Require<VisualElement>(screenRoot, "settings"), OpenRoadies, CloseOverlay);
            allocation = new RoadieAllocationUI(Require<VisualElement>(screenRoot, "roadie-allocation"),
                session, clock, CloseOverlay);
            encore = new EncoreWindowUI(Require<VisualElement>(screenRoot, "encore-window"),
                session.Root, clock, ads, store, CloseOverlay);
            storyLog = new StoryLogUI(Require<VisualElement>(screenRoot, "story-log-window"),
                session.Root, clock, this, CloseOverlay);
            session.Refreshed += Render;
        }

        // The Refreshed handler, and the host's only render: every pass runs
        // inside the queue entry that fired it, so a command a pass submits
        // lands behind that entry and runs at the drain, never inside the pass.
        // The first render of a bound screen is the session's own refresh entry
        // (UIRoot.Bind), since a fresh game runs no transaction at all (12.11).
        public void Render()
        {
            var phase = session.Phase;
            collect.Root.style.display =
                phase == SessionPhase.AwaitingIdleClaim ? DisplayStyle.Flex : DisplayStyle.None;
            if (phase == SessionPhase.AwaitingIdleClaim)
                collect.Refresh();

            var chapter = session.ForegroundChapter;
            if (phase != SessionPhase.Live || chapter == null)
            {
                topBar.Root.style.display = DisplayStyle.None;
                ClearLiveRequests();
                if (phase == SessionPhase.NoChapter)
                    select.Show(false);
                else
                    select.Hide();
                // The select and the dialog are whole screens of their own,
                // and the sections stay down under the dialog: a phase that
                // never ticks must not interpolate a display on a report
                // measured before the switch.
                container.Clear();
                sections.Clear();
                builtFor = null;
                // The card belongs to a live chapter, so a phase leaving
                // Live takes the request down with the sections. The beat's
                // mark was written when its card opened (section 10);
                // nothing here writes.
                return;
            }

            topBar.Root.style.display = DisplayStyle.Flex;
            topBar.Refresh();

            if (chapter != builtFor)
                Build(chapter);

            foreach (var section in sections)
            {
                var ctx = new GameContext(section.Scope, clock.RealTimeUtc);
                section.Visible = section.Definition.visibleWhen.Evaluate(ctx);
                section.Root.style.display = section.Visible ? DisplayStyle.Flex : DisplayStyle.None;
                // A hidden section's modules are neither evaluated nor
                // refreshed: nothing on screen depends on them
                // (requirement 3).
                if (section.Visible)
                    RenderModules(section);
            }

            // After the rows, so the pass that reads a latch as set is the
            // pass that put the card up. The sections stay UP beneath it:
            // the chapter is live and ticking, so nothing here interpolates
            // a stale report.
            ShowLiveOverlay(chapter);
        }

        // Presentation between refreshes, on every visible widget. Nothing here
        // reads Time.* or DateTime - the clock the widgets hold is the one
        // source (12.11).
        public void Interpolate()
        {
            foreach (var section in sections)
            {
                if (!section.Visible)
                    continue;
                foreach (var module in section.Modules)
                    if (module.Visible && module.Widget != null)
                        module.Widget.Interpolate();
            }
            if (session.Phase != SessionPhase.Live)
                return;
            topBar.Interpolate();
            if (requestedOverlay == LiveOverlay.Encore)
                encore.Interpolate();
        }

        public void Dispose() => session.Refreshed -= Render;

        // The one way a card goes up, for a row's button and for the
        // marked-beat walk alike: the request is held, and the beat is READ the
        // moment its card opens (section 10). The mark is an ordinary command -
        // issued outside any transaction it runs at the call and its own
        // Refreshed renders the card; issued from inside one, from the walk
        // below, it runs at the next drain (12.9) and the held request keeps the
        // card up until then. With the latch already set nothing changes but
        // the card, so the card alone is shown - no pass of the host's own, and
        // no state read that a refresh would owe. A requested overlay keeps
        // standing beneath the card: the log opens a card over itself, and
        // the card's close hands the screen back to it.
        public void OpenStory(StoryBeatDefinition beat, ScopeState scope)
        {
            HideRequestedOverlay();
            requestedBeat = beat;
            var ctx = new GameContext(scope, clock.RealTimeUtc);
            if (!ctx.IsFlagSet(beat.seenFlag))
                session.AcknowledgeStory(ctx, beat);
            else
                story.Show(beat);
        }

        // What the card's button calls: dropping the request takes the card
        // down and the overlay that stood beneath it, if any, comes back - the
        // log, for a card opened from it. The beat was read when the card
        // opened (section 10), so closing writes nothing, and a marked beat
        // still waiting shows at the next refresh.
        public void CloseStory()
        {
            requestedBeat = null;
            story.Hide();
            ShowRequestedOverlay();
        }

        public void OpenEncore() => OpenOverlay(LiveOverlay.Encore);

        public void OpenStoryLog() => OpenOverlay(LiveOverlay.StoryLog);

        public void OpenChapterSelect() => OpenOverlay(LiveOverlay.ChapterSelect);

        public void OpenSettings() => OpenOverlay(LiveOverlay.Settings);

        public void OpenRoadies() => OpenOverlay(LiveOverlay.Roadies);

        public void SelectChapter(ChapterScopeState chapter) => select.Select(chapter);

        public void CloseOverlay()
        {
            requestedOverlay = LiveOverlay.None;
            HideRequestedOverlay();
        }

        private void OpenOverlay(LiveOverlay overlay)
        {
            if (session.Phase != SessionPhase.Live || session.ForegroundChapter == null)
                return;
            requestedBeat = null;
            story.Hide();
            HideRequestedOverlay();
            requestedOverlay = overlay;
            if (overlay == LiveOverlay.Roadies)
                allocation.Open();
            else
                ShowRequestedOverlay();
        }

        private void BeforeChapterSelection()
        {
            if (session.Phase == SessionPhase.Live)
                CloseOverlay();
            else
                select.Hide();
        }

        private void ClearLiveRequests()
        {
            requestedBeat = null;
            story.Hide();
            requestedOverlay = LiveOverlay.None;
            HideRequestedOverlay();
        }

        private void HideRequestedOverlay()
        {
            select.Hide();
            settings.Hide();
            allocation.Hide();
            encore.Hide();
            storyLog.Hide();
        }

        private void ShowRequestedOverlay()
        {
            switch (requestedOverlay)
            {
                case LiveOverlay.ChapterSelect:
                    select.Show(true);
                    break;
                case LiveOverlay.Settings:
                    settings.Show();
                    break;
                case LiveOverlay.Roadies:
                    allocation.Refresh();
                    break;
                case LiveOverlay.Encore:
                    encore.Show();
                    break;
                case LiveOverlay.StoryLog:
                    storyLog.Show();
                    break;
            }
        }

        private void ShowLiveOverlay(ChapterScopeState chapter)
        {
            if (requestedBeat != null)
            {
                // A held card is the top of the stack whatever stands beneath
                // it; the overlay comes back when the card closes.
                HideRequestedOverlay();
                story.Show(requestedBeat);
                return;
            }
            if (requestedOverlay != LiveOverlay.None)
            {
                // Modal dialogs block automatic stories. Leave the beat unseen
                // so the state-based walk finds it after the overlay closes.
                story.Hide();
                ShowRequestedOverlay();
                return;
            }
            HideRequestedOverlay();
            ShowStory(chapter);
        }

        // What the card shows this pass: the standing request, and when none
        // stands whatever the marked-beat walk finds - opened through the same
        // path a button takes, because a beat is read when its card opens.
        // Nothing else opens a card.
        private void ShowStory(ChapterScopeState chapter)
        {
            if (requestedBeat == null)
            {
                var popped = Popped(chapter);
                if (popped != null)
                    OpenStory(popped, chapter);
            }
            if (requestedBeat != null)
                story.Show(requestedBeat);
            else
                story.Hide();
        }

        // The marked-beat walk (section 10): over the foreground chapter's own
        // storyBeats list - a declaration list of a scope the host already
        // holds - the first beat that is marked, available at the chapter's
        // context, and unseen; the scope is the chapter. State, never a
        // transition: such a beat pops on every pass until its card opens and
        // the mark is written, so a crash before the card ever went up shows it
        // on the next launch. Unmarked beats never pop; their button is the way
        // in.
        private StoryBeatDefinition Popped(ChapterScopeState chapter)
        {
            var ctx = new GameContext(chapter, clock.RealTimeUtc);
            foreach (var candidate in ((ChapterDefinition)chapter.Definition).storyBeats)
            {
                if (candidate == null || !candidate.opensWhenAvailable)
                    continue;
                if (ctx.IsFlagSet(candidate.seenFlag) || !candidate.IsAvailable(ctx))
                    continue;
                return candidate;
            }
            return null;
        }

        // The chapter's authored sections, in order, hidden until the pass
        // below judges them. Every evaluation scope was resolved when the tree
        // was built and is READ off the chapter node by the section or module
        // that names it (12.11/12.14.8) - the 12.11 reach check lives at the
        // link, so nothing is searched for here and a rebuild costs a
        // dictionary hit per view.
        private void Build(ChapterScopeState chapter)
        {
            container.Clear();
            sections.Clear();
            builtFor = chapter;

            foreach (var section in ((ChapterDefinition)chapter.Definition).sections)
            {
                var view = new SectionView(section, chapter.Link(section));
                foreach (var module in section.modules)
                    view.AddModule(new ModuleView(module, chapter.Link(module)));
                view.Root.style.display = DisplayStyle.None;
                container.Add(view.Root);
                sections.Add(view);
            }
        }

        private void RenderModules(SectionView section)
        {
            for (var i = 0; i < section.Modules.Count; i++)
            {
                var module = section.Modules[i];
                var ctx = new GameContext(module.Scope, clock.RealTimeUtc);
                // An absent module gate means always visible (12.11).
                module.Visible = module.Definition.visibleWhen == null
                    || module.Definition.visibleWhen.Evaluate(ctx);
                if (module.Visible && module.Widget == null)
                    CreateWidget(section, i, module);
                if (module.Widget == null)
                    continue;
                module.Widget.Root.style.display = module.Visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (module.Visible)
                    module.Widget.Refresh();
            }
        }

        // Instantiate, construct, bind - and the caller refreshes, in the same
        // pass. The registry's whole graph is loaded, so this is synchronous
        // mid-refresh by construction (12.11).
        private void CreateWidget(SectionView section, int index, ModuleView module)
        {
            var root = registry.Resolve(module.Definition.prefabId).Instantiate();
            module.Widget = ModuleWidgetFactory.Create(module.Definition.prefabId, root, this);
            module.Widget.Bind(session, module.Scope, module.Definition.content, clock);
            section.ModulesContainer.Insert(PlacedBefore(section, index), root);
        }

        // How many of this module's predecessors already hold a widget, which is
        // where its root goes: a module that turns visible late still lands in
        // authored order rather than at the end.
        private static int PlacedBefore(SectionView section, int index)
        {
            var placed = 0;
            for (var i = 0; i < index; i++)
                if (section.Modules[i].Widget != null)
                    placed++;
            return placed;
        }

        // The named element Screen.uxml promises, for the host and for the two
        // screen classes it owns. Static content cannot legitimately be
        // unresolvable (requirement 7), so a miss names the element rather than
        // leaving a null to surface later.
        internal static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"Screen.uxml has no {typeof(T).Name} named '{name}' (design doc 12.11).");
            return element;
        }
    }
}
