using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The list module over the evaluation scope's own bar groups (design doc
    // 12.11): a list module binds no content, so the declaration list is what
    // it renders, in authored order - one block per group: the pools its bars
    // drink from, then the rows themselves (12.7). The section's title names
    // the band, so a group carries no heading of its own.
    // Blocks are built once and their rows toggled, because a group's bars are
    // a fixed set under a gate rather than a changing set.
    public sealed class BarGroupUI : ModuleWidget
    {
        // One group's block: what refresh and interpolation walk. The elements
        // themselves live in the tree the build added them to.
        private sealed class GroupView
        {
            public readonly List<CurrencyReadout> readouts = new();
            public readonly List<BarRowUI> rows = new();
        }

        private readonly VisualElement groups;
        private readonly List<GroupView> views = new();

        public BarGroupUI(VisualElement root) : base(root)
        {
            groups = Require<VisualElement>(root, "groups", "BarGroup.uxml");
        }

        protected override void OnBound()
        {
            foreach (var group in Scope.Definition.barGroups)
            {
                if (group == null)
                    continue;
                views.Add(Build(group));
            }
        }

        public override void Refresh()
        {
            var ctx = Context();
            var report = Session.LastTick;
            var gameTime = Clock.GameTimeSeconds;
            foreach (var view in views)
            {
                foreach (var readout in view.readouts)
                    readout.Snap(ctx, report, gameTime);
                foreach (var row in view.rows)
                {
                    row.SetVisible(row.Available(ctx));
                    if (row.Visible)
                        row.Refresh();
                }
            }
        }

        public override void Interpolate()
        {
            var gameTime = Clock.GameTimeSeconds;
            foreach (var view in views)
            {
                foreach (var readout in view.readouts)
                    readout.Interpolate(gameTime);
                foreach (var row in view.rows)
                    if (row.Visible)
                        row.Interpolate();
            }
        }

        // A block's elements: the group's authored name, one readout per
        // DISTINCT pool its bars drink from - in bar order, so the readout the
        // player watches is the one the first bar spends - then the rows.
        private GroupView Build(BarGroupDefinition group)
        {
            var view = new GroupView();
            var block = new VisualElement();

            var pools = new List<CurrencyDefinition>();
            foreach (var bar in group.bars)
            {
                // A bar with no fill currency fills from time alone, so it has
                // no pool to read out (12.7).
                if (bar == null || bar.fillCurrency == null || pools.Contains(bar.fillCurrency))
                    continue;
                pools.Add(bar.fillCurrency);
                var line = new VisualElement();
                line.AddToClassList("pool-readout");
                var poolName = new Label(bar.fillCurrency.displayName);
                var poolValue = new Label();
                line.Add(poolName);
                line.Add(poolValue);
                block.Add(line);
                view.readouts.Add(new CurrencyReadout(poolValue, Scope, bar.fillCurrency));
            }

            foreach (var bar in group.bars)
            {
                if (bar == null)
                    continue;
                var row = new BarRowUI(Session, Scope, Clock, group, bar);
                view.rows.Add(row);
                block.Add(row.Root);
            }

            groups.Add(block);
            return view;
        }
    }
}
