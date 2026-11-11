using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The automation window (design doc 12.11), opened by a hold on a levelled
    // group's Auto button: one row per member the card shows that names an
    // automation, in group order, each the bar's name and where its automation
    // stands - Purchased, a Buy button, or the price. Buy issues the upgrade
    // row's purchase for that bar, so any gig is automated from here in any
    // order while the card's tap keeps buying the first. Every state is
    // LeveledGroupUI.Automation's answer, the one the card's button reads.
    // No state beyond the group and the scope the card's hold handed over.
    public sealed class AutomationWindowUI
    {
        // One row: the bar, and the state and price it shows.
        public sealed class Row
        {
            public BarDefinition Bar { get; }

            public AutomationState State { get; internal set; }

            // The automation's price, printed in the Price state; read in
            // every state, since it is what a Buy spends.
            public BigNumber ShownCost { get; internal set; } = BigNumber.Zero;

            internal VisualElement Root { get; }
            internal Button ActionButton { get; }

            internal Row(BarDefinition bar, Action<BarDefinition> buy)
            {
                Bar = bar;
                Root = new VisualElement();
                Root.AddToClassList("automation-row");
                var name = new Label(bar.displayName);
                name.AddToClassList("automation-name");
                ActionButton = new Button();
                ActionButton.AddToClassList("automation-action");
                ActionButton.clicked += () => buy(Bar);
                Root.Add(name);
                Root.Add(ActionButton);
            }
        }

        public VisualElement Root { get; }

        private readonly GameSession session;
        private readonly GameClock clock;
        private readonly Action close;
        private readonly VisualElement rowsElement;

        // A row per member naming an automation, built when a group is held
        // and toggled after: the members are a fixed set under a gate, and a
        // button rebuilt under a press would lose it.
        private readonly List<Row> built = new();
        private readonly List<Row> shownRows = new();

        private GroupDefinition group;

        // The scope the card resolved as the group's declaring one, which is
        // every member's home (12.7), so nothing here walks for it.
        private ScopeState scope;

        public bool IsOpen { get; private set; }

        // Null when nothing is held: the pair is the request, and the host asks
        // for the window only after handing one over.
        public GroupDefinition ShownGroup => group;

        public ScopeState HeldScope => scope;

        // The rows on screen, in group order: the available members naming an
        // automation.
        public IReadOnlyList<Row> Rows => shownRows;

        public AutomationWindowUI(VisualElement rootElement, GameSession session, GameClock clock, Action close)
        {
            Root = rootElement;
            this.session = session;
            this.clock = clock;
            this.close = close;
            rowsElement = ScreenHost.Require<VisualElement>(rootElement, "automation-rows");
            ScreenHost.Require<Button>(rootElement, "automation-close").clicked += Dismiss;
        }

        public void Hold(GroupDefinition group, ScopeState scope)
        {
            if (group != this.group)
            {
                rowsElement.Clear();
                built.Clear();
                shownRows.Clear();
                if (group != null)
                    foreach (var member in group.members)
                        if (member is BarDefinition bar && bar.automation != null)
                        {
                            var row = new Row(bar, Buy);
                            built.Add(row);
                            rowsElement.Add(row.Root);
                        }
            }
            this.group = group;
            this.scope = scope;
        }

        public void Show()
        {
            if (group == null)
                throw new InvalidOperationException(
                    "AutomationWindowUI.Show with no group held (design doc 12.11).");
            Root.style.display = DisplayStyle.Flex;
            IsOpen = true;
            Refresh();
        }

        // The host calls this through Show on every refresh while the window
        // stands, so a purchase made here, a booking beneath it, or Cash
        // arriving moves a row with no reopen.
        public void Refresh()
        {
            var ctx = new GameContext(scope, clock.RealTimeUtc);
            shownRows.Clear();
            foreach (var row in built)
            {
                var available = LeveledBarRowUI.Available(row.Bar, ctx);
                row.Root.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                if (!available)
                    continue;
                shownRows.Add(row);
                row.State = LeveledGroupUI.Automation(ctx, row.Bar);
                row.ShownCost = LeveledGroupUI.AutomationCost(ctx, row.Bar);
                row.ActionButton.EnableInClassList("automation-buy", row.State == AutomationState.Buy);
                row.ActionButton.SetEnabled(row.State == AutomationState.Buy);
                row.ActionButton.text = row.State switch
                {
                    AutomationState.Purchased => "Purchased",
                    AutomationState.Buy => "Buy",
                    _ => NumberFormatter.Format(row.ShownCost) + " " + row.Bar.automation.costCurrency.displayName,
                };
            }
        }

        // What a row's Buy calls, public so a headless test presses what the
        // pointer presses: the purchase the upgrade row issues, for that bar's
        // automation, at the held scope. The window stays up; the command's
        // own refresh repaints it through the host.
        public void Buy(BarDefinition bar)
        {
            if (!built.Exists(row => row.Bar == bar))
                throw new InvalidOperationException(
                    $"The automation window over '{group?.Id}' has no row for '{bar?.Id}'.");
            session.TryBuy(new GameContext(scope, clock.RealTimeUtc), bar.automation);
        }

        // What the Dismiss button calls: the host's close, which drops the
        // request.
        public void Dismiss() => close();

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
            IsOpen = false;
        }

        // The request is dropped, so the pair goes with it and the window
        // comes down.
        public void Drop()
        {
            Hold(null, null);
            Hide();
        }
    }
}
