using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Where one member's automation stands (LeveledGroupUI.Automation):
    // Purchased when the bar goes again by itself or its upgrade is bought,
    // Buy when the purchase would succeed now, Price otherwise. The names are
    // not the window's wording.
    public enum AutomationState
    {
        Purchased,
        Buy,
        Price,
    }

    // What the cycle button at the top of a Cycle card says each row's Hire
    // button buys (LeveledGroupUI.Mode): one level, the most the balance
    // affords, or the levels still needed to reach the next count threshold.
    // The names are not the button's wording.
    public enum BuyMode
    {
        One,
        Max,
        Next,
    }

    // What every row of a levelled card prints inside its fill
    // (LeveledGroupUI.Figures): each currency's earnings per second, or what
    // one completion pays. The names are not the toggle's wording.
    public enum FigureMode
    {
        PerSecond,
        Raw,
    }

    // The levelled group (design doc 12.11): the module over ONE group whose
    // members are all bars naming a level, bound by contentId exactly as the
    // group module is. Each available bar is one combined row - its name and
    // level, its fill with its earnings inside, the level's buy control. While
    // no bar is available the first member shows as a row at level 0, whose
    // buy control hires its first level and starts it as Book does. The
    // card ends in two buttons: Book, which buys the first level of the
    // next gig not yet on the calendar and starts it, and Auto, which buys the
    // automation of the first visible gig that does not yet go again by
    // itself; holding Auto opens the automation window over the group, where
    // any visible gig's automation is bought. There is no select control:
    // tapping a bar is how it starts. Every card opens with the figures
    // toggle at its top left, which switches what every row prints inside its
    // fill between Per Second and Raw. A card whose module authors the Cycle
    // buy control opens with one more button at its top right, the cycle,
    // which steps every row's Hire button through Buy One, Buy Max and Buy
    // Next. Both modes live on the card for as long as the card is built and
    // are never saved. The
    // group module stays the layout for a group that is not levelled (the
    // split one, bars in one list and their generators in another).
    // The rows are built once and toggled, because a group's members are a
    // fixed set under a gate rather than a changing set.
    public sealed class LeveledGroupUI : ModuleWidget
    {
        private readonly IGeneratorInfoOpener infos;
        private readonly IAutomationOpener automations;
        private readonly IToastOpener toasts;
        private readonly VisualElement rows;
        private readonly Button figures;
        private readonly Button cycle;
        private readonly Button book;
        private readonly Button auto;
        private readonly List<LeveledBarRowUI> views = new();

        private GroupDefinition group;

        // The scope declaring the group, resolved once by the outward walk
        // (12.3): the active set and every member's own facts live there.
        private ScopeState declaring;

        // The bar the Book button books: the first member in group order that
        // is not available, is not shown at level 0, and whose level is
        // offered. Null hides the button.
        public BarDefinition BookTarget { get; private set; }

        // The price printed on Book: one unit of the target's level.
        public BigNumber ShownBookCost { get; private set; } = BigNumber.Zero;

        public bool BookEnabled { get; private set; }

        // The bar the Auto button automates: the first available member in
        // group order that names an automation and whose automation reads
        // anything but Purchased (Automation below). Null hides the button.
        public BarDefinition AutoTarget { get; private set; }

        // True when the target's automation reads Buy.
        public bool AutoEnabled { get; private set; }

        // Which buy control every row draws: the module's authored choice,
        // which the host hands over before the bind that builds the rows
        // (12.11). Pair when nothing sets it.
        public BuyControlKind BuyControl { get; set; } = BuyControlKind.Pair;

        // What the cycle button reads and every Hire button buys by. Starts at
        // One whenever the card is built; meaningless on a Pair card, which
        // draws no cycle button.
        public BuyMode Mode { get; private set; } = BuyMode.One;

        // What the figures toggle reads and every row prints inside its fill.
        // Starts at PerSecond whenever the card is built, on a Pair card and a
        // Cycle card alike.
        public FigureMode Figures { get; private set; } = FigureMode.PerSecond;

        public LeveledGroupUI(VisualElement root, IGeneratorInfoOpener infos, IAutomationOpener automations,
                              IToastOpener toasts)
            : base(root)
        {
            rows = Require<VisualElement>(root, "rows", "LeveledGroup.uxml");
            figures = Require<Button>(root, "figures", "LeveledGroup.uxml");
            cycle = Require<Button>(root, "cycle", "LeveledGroup.uxml");
            book = Require<Button>(root, "book", "LeveledGroup.uxml");
            auto = Require<Button>(root, "auto", "LeveledGroup.uxml");
            // A row with nowhere to send the hold or the crossing would render
            // a dead gesture, and the host is the only caller (requirement 7).
            this.infos = infos ?? throw new ArgumentNullException(nameof(infos));
            this.automations = automations ?? throw new ArgumentNullException(nameof(automations));
            this.toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
            figures.clicked += ToggleFigures;
            cycle.clicked += Cycle;
            book.clicked += Book;
            auto.clicked += Auto;
        }

        protected override void OnBound()
        {
            group = Content as GroupDefinition
                ?? throw new InvalidOperationException(
                    $"The leveled_group module binds '{Content?.Id}', which is not a GroupDefinition (12.11).");
            // Every member is a bar naming a level, or the layout has no row to
            // draw it with: a content fault, refused before anything is built.
            foreach (var member in group.members)
            {
                if (member is not BarDefinition bar)
                    throw new InvalidOperationException(
                        $"The leveled group '{group.Id}' lists '{member?.Id}', a {member?.GetType().Name ?? "null"}, which is not a bar (12.11).");
                if (bar.level == null)
                    throw new InvalidOperationException(
                        $"The leveled group '{group.Id}' lists bar '{bar.Id}', which names no level (12.11).");
            }
            declaring = Producer.DeclaringScope<ScopeState>(Scope, group);

            foreach (var member in group.members)
            {
                var row = new LeveledBarRowUI(Session, declaring, Clock, group, (BarDefinition)member, infos, toasts,
                    Activate, BuyControl, () => Mode, () => Figures);
                rows.Add(row.Root);
                views.Add(row);
            }
            cycle.style.display = BuyControl == BuyControlKind.Cycle ? DisplayStyle.Flex : DisplayStyle.None;

            // A tap on Auto buys and a hold opens the window. The hold's
            // manipulator lets go of the button's pointer when it fires, so the
            // release that ends a hold is never also a click (LongPressManipulator).
            auto.AddManipulator(new LongPressManipulator(OpenAutomation, Session.Config.longPressSeconds));
        }

        public override void Refresh()
        {
            figures.text = Figures == FigureMode.Raw ? "Raw" : "Per Second";
            cycle.text = Mode switch
            {
                BuyMode.Max => "Buy Max",
                BuyMode.Next => "Buy Next",
                _ => "Buy One",
            };
            var ctx = Context().Rebase(declaring);
            var first = LevelZero(ctx);
            foreach (var view in views)
                view.Refresh(view == first);
            RefreshBook(ctx);
            RefreshAuto(ctx);
        }

        public override void Interpolate()
        {
            foreach (var view in views)
                if (view.Visible)
                    view.Interpolate();
        }

        // The row drawn for one member.
        public LeveledBarRowUI Row(BarDefinition bar)
        {
            foreach (var view in views)
                if (view.Bar == bar)
                    return view;
            throw new InvalidOperationException(
                $"The leveled group '{group?.Id}' does not list '{bar?.Id}'.");
        }

        // What the cycle button presses, public so a headless test presses what
        // the pointer presses: Buy One, then Buy Max, then Buy Next, then Buy
        // One again. A press changes no state the session holds, so no command
        // runs and no refresh follows; the card repaints itself.
        public void Cycle()
        {
            Mode = Mode switch
            {
                BuyMode.One => BuyMode.Max,
                BuyMode.Max => BuyMode.Next,
                _ => BuyMode.One,
            };
            Refresh();
        }

        // What the figures toggle presses, public for the reason Cycle is: Per
        // Second, then Raw, then Per Second again. Like the cycle, a press
        // changes no state the session holds, so the card repaints itself.
        public void ToggleFigures()
        {
            Figures = Figures == FigureMode.PerSecond ? FigureMode.Raw : FigureMode.PerSecond;
            Refresh();
        }

        // What the Book button presses, public so a headless test presses what
        // the pointer presses: one unit of the target's level, then the target
        // added to the active set, so the gig starts with the purchase that
        // made it available. Both are submitted commands in order, and the
        // second is fail-closed - a refused purchase leaves the bar
        // unavailable, so the set is refused whole and nothing changes.
        public void Book()
        {
            var target = BookTarget;
            if (target == null)
                return;
            Session.TryBuy(Context().Rebase(Row(target).LevelHome), target.level, 1);
            Activate(target);
        }

        // What the Auto button presses: the purchase the upgrade row issues, on
        // the target's automation. The button stays pressable while dimmed, so
        // a tap that could not buy does nothing here.
        public void Auto()
        {
            var target = AutoTarget;
            if (target == null || !AutoEnabled)
                return;
            Session.TryBuy(Context(), target.automation);
        }

        // What the hold on Auto calls, public for the reason OpenInfo is: the
        // automation window over this group, at the declaring scope the card
        // resolved at bind.
        public void OpenAutomation() => automations.OpenAutomation(group, declaring);

        // One member's automation, the ONE answer the Auto button and the
        // automation window both read, so the two cannot disagree. `ctx` is at
        // the group's declaring scope, which is the bar's home (12.7), and the
        // bar names an automation. Whether a bar goes again by itself is its
        // repeatWhen holding there (12.7), never whether the upgrade is
        // bought; an owned automation (Purchasing.IsOwned) reads Purchased
        // too, since buying it again is refused. Otherwise it is Buy exactly when the purchase the
        // upgrade row issues would succeed, and Price when it would not.
        public static AutomationState Automation(GameContext ctx, BarDefinition bar)
        {
            if (bar.repeatWhen != null && bar.repeatWhen.Evaluate(ctx))
                return AutomationState.Purchased;
            if (Purchasing.IsOwned(ctx, bar.automation))
                return AutomationState.Purchased;
            return Purchasing.CanBuy(ctx, bar.automation) ? AutomationState.Buy : AutomationState.Price;
        }

        // The price the purchase would spend, read as the upgrade row reads it
        // (12.2): at the upgrade's declaring scope.
        public static BigNumber AutomationCost(GameContext ctx, BarDefinition bar) =>
            Purchasing.CostOf(bar.automation, AutomationContext(ctx, bar));

        private static GameContext AutomationContext(GameContext ctx, BarDefinition bar) =>
            ctx.Rebase(Producer.DeclaringScope<ScopeState>(ctx.Scope, bar.automation));

        // The row shown at level 0: the first member in group order, while no
        // member's bar is available and that member's level is offered - the
        // card is never a Book button alone. Null once any bar is available.
        private LeveledBarRowUI LevelZero(GameContext ctx)
        {
            foreach (var view in views)
                if (view.Available(ctx))
                    return null;
            if (views.Count == 0)
                return null;
            var first = views[0];
            return first.Bar.level.IsAvailable(ctx.Rebase(first.LevelHome)) ? first : null;
        }

        // Book: the next gig not on the calendar whose level can be bought at
        // all, priced at one unit of that level. A row at level 0 is visible,
        // so Book never offers the bar it shows. Its text is the bar's own gate
        // text when it has one - the gate is what booking satisfies - else the
        // bar's name.
        private void RefreshBook(GameContext ctx)
        {
            BookTarget = null;
            foreach (var view in views)
            {
                var levelCtx = ctx.Rebase(view.LevelHome);
                if (view.Visible || !view.Bar.level.IsAvailable(levelCtx))
                    continue;
                BookTarget = view.Bar;
                ShownBookCost = Purchasing.CostOf(view.Bar.level, levelCtx, 1);
                BookEnabled = Purchasing.CanBuy(levelCtx, view.Bar.level, 1);
                var gate = view.Bar.availableWhen?.uiText;
                var name = string.IsNullOrEmpty(gate) ? view.Bar.displayName : gate;
                book.text = name + " (" + NumberFormatter.Format(ShownBookCost) + " "
                            + view.Bar.level.costCurrency.displayName + ")";
                break;
            }
            if (BookTarget == null)
            {
                ShownBookCost = BigNumber.Zero;
                BookEnabled = false;
            }
            book.style.display = BookTarget != null ? DisplayStyle.Flex : DisplayStyle.None;
            book.SetEnabled(BookEnabled);
        }

        // Auto: the first visible gig whose automation is not Purchased, a
        // row at level 0 aside since its gig is not hired yet. So the button
        // leaves when every visible gig repeats and comes back when another
        // is booked. Its face is the word alone, from the UXML; the
        // prices are the window's.
        private void RefreshAuto(GameContext ctx)
        {
            AutoTarget = null;
            AutoEnabled = false;
            foreach (var view in views)
            {
                var bar = view.Bar;
                if (!view.Visible || view.ShownLevelZero || bar.automation == null)
                    continue;
                var state = Automation(ctx, bar);
                if (state == AutomationState.Purchased)
                    continue;
                AutoTarget = bar;
                AutoEnabled = state == AutomationState.Buy;
                break;
            }
            auto.style.display = AutoTarget != null ? DisplayStyle.Flex : DisplayStyle.None;
            // Dimmed by class and never disabled: a disabled element receives
            // no pointer events, and the hold is how a player who cannot
            // afford the target reads its price.
            auto.EnableInClassList("leveled-auto-unaffordable", !AutoEnabled);
        }

        // The group's current set with this bar added, mapped back to the
        // definitions the command takes in the group's own declaration order.
        // Add only: a levelled bar is never taken off the calendar from here.
        // SetActiveMembers is fail-closed, so a refusal changes nothing.
        private void Activate(BarDefinition bar)
        {
            var chosen = new HashSet<string>();
            if (declaring.activeMembers.TryGetValue(group.Id, out var active))
                foreach (var id in active)
                    chosen.Add(id);
            chosen.Add(bar.Id);

            var wanted = new List<Definition>();
            foreach (var listed in group.members)
                if (listed != null && chosen.Contains(listed.Id))
                    wanted.Add(listed);
            Session.SetActiveMembers(Context(), group, wanted);
        }
    }
}
