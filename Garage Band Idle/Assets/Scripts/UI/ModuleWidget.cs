using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The behavior side of a module (design doc 12.11): a plain C# controller
    // over the element tree its UXML instantiated, because a UXML carries no
    // behavior. Plain also means an EditMode test builds one over imported
    // content with no panel at all.
    public abstract class ModuleWidget
    {
        public VisualElement Root { get; }

        protected GameSession Session { get; private set; }

        // The module's evaluation scope's STATE node - where its reads walk
        // outward from and where its commands act.
        protected ScopeState Scope { get; private set; }

        // What the module binds; null for a list module, whose content is the
        // evaluation scope's own declaration lists.
        protected Definition Content { get; private set; }

        protected GameClock Clock { get; private set; }

        protected ModuleWidget(VisualElement root) => Root = root;

        // The host calls Bind once at creation and Refresh on every pass while
        // visible. Bind does not refresh: the host does, so a widget created
        // mid-refresh is refreshed by the same pass that created it (12.11).
        public void Bind(GameSession session, ScopeState scope, Definition content, GameClock clock)
        {
            Session = session;
            Scope = scope;
            Content = content;
            Clock = clock;
            OnBound();
        }

        protected virtual void OnBound() { }

        public abstract void Refresh();

        // Per frame while visible, driven by the host. Presentation only (12.11).
        public virtual void Interpolate() { }

        // Every read and command runs in the module's scope at the clock's time
        // (12.4/12.11) - never Time.* and never DateTime.
        protected GameContext Context() => new GameContext(Scope, Clock.RealTimeUtc);

        // The named element a widget's UXML promises it. Static content cannot
        // legitimately be unresolvable (requirement 7), so a miss names both the
        // element and the document rather than leaving a null to surface later.
        protected static T Require<T>(VisualElement root, string name, string uxml) where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"{uxml} has no {typeof(T).Name} named '{name}' (design doc 12.11).");
            return element;
        }
    }
}
