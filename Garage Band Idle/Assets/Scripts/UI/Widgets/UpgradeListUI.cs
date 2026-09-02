using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The list module over the evaluation scope's own upgrades (design doc
    // 12.11) - the generator list's shape, with the purchased filter the row
    // carries.
    public sealed class UpgradeListUI : ModuleWidget
    {
        private readonly VisualElement rows;
        private readonly List<UpgradeRowUI> views = new();

        public UpgradeListUI(VisualElement root) : base(root)
        {
            rows = Require<VisualElement>(root, "rows", "UpgradeList.uxml");
        }

        protected override void OnBound()
        {
            foreach (var upgrade in Scope.Definition.upgrades)
            {
                if (upgrade == null)
                    continue;
                var view = new UpgradeRowUI(Session, Scope, Clock, upgrade);
                views.Add(view);
                rows.Add(view.Root);
            }
        }

        public override void Refresh()
        {
            var ctx = Context();
            foreach (var view in views)
            {
                var visible = view.Offered(ctx);
                view.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    view.Refresh();
            }
        }
    }
}
