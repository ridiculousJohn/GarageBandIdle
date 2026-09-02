using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The list module over the evaluation scope's own generators (design doc
    // 12.11): a list module binds no content, so the declaration list is what
    // it renders, in authored order. Rows are built once and toggled, because
    // availability is a gate on a fixed set, not a changing set.
    public sealed class GeneratorListUI : ModuleWidget
    {
        private readonly VisualElement rows;
        private readonly List<GeneratorRowUI> views = new();

        public GeneratorListUI(VisualElement root) : base(root)
        {
            rows = Require<VisualElement>(root, "rows", "GeneratorList.uxml");
        }

        protected override void OnBound()
        {
            foreach (var generator in Scope.Definition.generators)
            {
                if (generator == null)
                    continue;
                var view = new GeneratorRowUI(Session, Scope, Clock, generator);
                views.Add(view);
                rows.Add(view.Root);
            }
        }

        public override void Refresh()
        {
            var ctx = Context();
            foreach (var view in views)
            {
                var visible = view.Available(ctx);
                view.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    view.Refresh();
            }
        }
    }
}
