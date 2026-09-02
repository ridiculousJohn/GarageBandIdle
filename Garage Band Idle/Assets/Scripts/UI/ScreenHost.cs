using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The screen's structure logic (design doc 12.11) - the authored sections
    // while Live, the select while NoChapter, the collect dialog while
    // AwaitingIdleClaim - plain C# so an EditMode test builds it over imported
    // content with no panel; UIRoot is the MonoBehaviour shell around it. This
    // is the ONE Refreshed subscriber - widgets subscribe to nothing, so there
    // is one dispatch order.
    public sealed class ScreenHost : IDisposable
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
        private readonly List<SectionView> sections = new();

        // The chapter the section views describe. Identity, not id: a switch
        // rebuilds, and a same-definition node from another tree is a different
        // screen.
        private ChapterScopeState builtFor;

        public IReadOnlyList<SectionView> Sections => sections;

        // Over the screen's own root: the host owns all three screens, so it is
        // the one place that knows which named elements Screen.uxml promises.
        public ScreenHost(VisualElement screenRoot, ModuleRegistry registry, GameSession session, GameClock clock)
        {
            container = Require<VisualElement>(screenRoot, "sections");
            this.registry = registry;
            this.session = session;
            this.clock = clock;
            select = new ChapterSelectUI(Require<VisualElement>(screenRoot, "select"), session, clock);
            collect = new CollectScreenUI(Require<VisualElement>(screenRoot, "collect"), session, clock);
            session.Refreshed += Render;
        }

        // The unconditional first render and the Refreshed handler are the same
        // method: a fresh game runs no transaction at all, so waiting for the
        // first event would leave the screen permanently blank (12.11).
        public void Render()
        {
            var phase = session.Phase;
            select.Root.style.display = phase == SessionPhase.NoChapter ? DisplayStyle.Flex : DisplayStyle.None;
            collect.Root.style.display = phase == SessionPhase.AwaitingIdleClaim ? DisplayStyle.Flex : DisplayStyle.None;
            if (phase == SessionPhase.AwaitingIdleClaim)
                collect.Refresh();

            var chapter = session.ForegroundChapter;
            if (phase != SessionPhase.Live || chapter == null)
            {
                // The select and the dialog are whole screens of their own, and
                // the sections stay down under the dialog: a phase that never
                // ticks must not interpolate a display on a report measured
                // before the switch.
                container.Clear();
                sections.Clear();
                builtFor = null;
                return;
            }

            if (chapter != builtFor)
                Build(chapter);

            foreach (var section in sections)
            {
                var ctx = new GameContext(section.Scope, clock.RealTimeUtc);
                section.Visible = section.Definition.visibleWhen.Evaluate(ctx);
                section.Root.style.display = section.Visible ? DisplayStyle.Flex : DisplayStyle.None;
                // A hidden section's modules are neither evaluated nor
                // refreshed: nothing on screen depends on them (requirement 3).
                if (section.Visible)
                    RenderModules(section);
            }
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
        }

        public void Dispose() => session.Refreshed -= Render;

        // The chapter's authored sections, in order, hidden until the pass
        // below judges them. Every scope resolves downward through the one
        // named subtree from the chapter node the host already holds - the
        // legitimate walk (12.14.8) - and a miss throws, since validated
        // content cannot address a scope outside the chapter.
        private void Build(ChapterScopeState chapter)
        {
            container.Clear();
            sections.Clear();
            builtFor = chapter;

            foreach (var section in ((ChapterDefinition)chapter.Definition).sections)
            {
                var view = new SectionView(section, Resolve(chapter, section.scope));
                foreach (var module in section.modules)
                    view.AddModule(new ModuleView(module, Resolve(chapter, module.scope)));
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
            module.Widget = ModuleWidgetFactory.Create(module.Definition.prefabId, root);
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

        private static ScopeState Resolve(ChapterScopeState chapter, ScopeDefinition scope)
        {
            var found = scope == null ? null : chapter.FindInSubtree(scope);
            if (found == null)
                throw new InvalidOperationException(
                    $"Evaluation scope '{(scope == null ? "<none>" : scope.Id)}' is not inside chapter "
                    + $"'{chapter.ScopeId}' (design doc 12.11).");
            return found;
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
