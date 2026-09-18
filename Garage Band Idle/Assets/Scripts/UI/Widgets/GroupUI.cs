using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The module over ONE group (design doc 12.11), bound by contentId exactly
    // as a generator row binds one generator: the currencies its bars consume,
    // then every member rendered with the row its own kind already has and one
    // control that makes it active. The section's title names the band, so the
    // group draws no heading of its own.
    // The rows are built once and toggled, because a group's members are a
    // fixed set under a gate rather than a changing set.
    public sealed class GroupUI : ModuleWidget
    {
        // One member's strip: the wrapper holding it, the row its kind renders
        // with - a bar's own, or a widget instantiated from the registry - and
        // the control that turns it on and off.
        private sealed class MemberView
        {
            public Definition Member;
            public VisualElement Wrapper;
            public BarRowUI Row;                // a bar member's, null for the rest
            public ModuleWidget Widget;         // every other kind's, null for a bar
            public Button Select;
        }

        private readonly ModuleRegistry registry;
        private readonly IStoryOpener stories;
        private readonly IGeneratorInfoOpener infos;
        private readonly VisualElement members;
        private readonly List<CurrencyReadout> readouts = new();
        private readonly List<MemberView> views = new();

        private GroupDefinition group;

        // The scope declaring the group, resolved once by the outward walk
        // (12.3): the active set and every member's own facts live there.
        private ScopeState declaring;

        public GroupUI(VisualElement root, ModuleRegistry registry, IStoryOpener stories,
                       IGeneratorInfoOpener infos) : base(root)
        {
            this.registry = registry;
            this.stories = stories;
            this.infos = infos;
            members = Require<VisualElement>(root, "members", "Group.uxml");
        }

        protected override void OnBound()
        {
            group = Content as GroupDefinition
                ?? throw new InvalidOperationException(
                    $"The group module binds '{Content?.Id}', which is not a GroupDefinition (12.11).");
            declaring = Producer.DeclaringScope<ScopeState>(Scope, group);

            // One readout per DISTINCT currency the group's bars consume, in
            // member order - so the readout the player watches is the one the
            // first bar spends (12.7).
            var consumed = new List<CurrencyDefinition>();
            foreach (var member in group.members)
            {
                if (member is not BarDefinition bar)
                    continue;
                foreach (var entry in bar.consumes)
                {
                    if (entry == null || entry.currency == null || consumed.Contains(entry.currency))
                        continue;
                    consumed.Add(entry.currency);
                    var line = new VisualElement();
                    line.AddToClassList("consumed-readout");
                    var name = new Label(entry.currency.displayName);
                    var value = new Label();
                    line.Add(name);
                    line.Add(value);
                    members.Add(line);
                    readouts.Add(new CurrencyReadout(value, Scope, entry.currency));
                }
            }

            foreach (var member in group.members)
                if (member != null)
                    views.Add(Build(member));
        }

        public override void Refresh()
        {
            var ctx = Context();
            var report = Session.LastTick;
            var gameTime = Clock.GameTimeSeconds;
            foreach (var readout in readouts)
                readout.Snap(ctx, report, gameTime);

            declaring.activeMembers.TryGetValue(group.Id, out var active);
            foreach (var view in views)
            {
                // A member that is a bar shows only while it is available, and
                // its control goes with it: a lone button for a row nobody can
                // see is a control over nothing (12.7/12.11).
                if (view.Row != null)
                {
                    var available = view.Row.Available(ctx);
                    view.Row.SetVisible(available);
                    view.Wrapper.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                    if (available)
                        view.Row.Refresh();
                }
                view.Widget?.Refresh();

                // A fill-once bar at full is Done and nothing more: it has
                // nothing left to run, which is the one refusal SetActiveMembers
                // makes that a player can reach (12.7). Everything else toggles.
                var complete = view.Member is BarDefinition bar
                               && bar.repeatWhen == null && ctx.GetBarProgress(bar.Id) >= bar.fillAmount;
                var on = active != null && active.Contains(view.Member.Id);
                view.Select.text = complete ? "Done" : on ? "Selected" : "Select";
                view.Select.SetEnabled(!complete);
            }
        }

        public override void Interpolate()
        {
            var gameTime = Clock.GameTimeSeconds;
            foreach (var readout in readouts)
                readout.Interpolate(gameTime);
            foreach (var view in views)
            {
                if (view.Row != null && view.Row.Visible)
                    view.Row.Interpolate();
                view.Widget?.Interpolate();
            }
        }

        // One member's strip: its own row, then the control. The row for a bar
        // is built in code like the group's readouts; every other kind already
        // has a widget and a layout in the registry, so this instantiates that
        // one rather than drawing a second version of it (12.11).
        private MemberView Build(Definition member)
        {
            var view = new MemberView { Member = member };
            view.Wrapper = new VisualElement();
            view.Wrapper.AddToClassList("member-row");

            if (member is BarDefinition bar)
            {
                view.Row = new BarRowUI(Session, Scope, Clock, bar);
                view.Wrapper.Add(view.Row.Root);
            }
            else
            {
                var prefabId = PrefabIdOf(member);
                var root = registry.Resolve(prefabId).Instantiate();
                view.Widget = ModuleWidgetFactory.Create(prefabId, root, registry, stories, infos);
                view.Widget.Bind(Session, Scope, member, Clock);
                view.Wrapper.Add(root);
            }

            view.Select = new Button();
            view.Select.AddToClassList("member-select");
            // The control TOGGLES: pressing an off member adds it and pressing
            // an on member removes it, so adding into a full group is refused
            // and the player deselects first - one behavior for every cap
            // (12.7). SetActiveMembers is fail-closed, so a refusal changes
            // nothing.
            view.Select.clicked += () => Toggle(member);
            view.Wrapper.Add(view.Select);

            members.Add(view.Wrapper);
            return view;
        }

        // The group's current set with this member flipped, mapped back to the
        // definitions the command takes - in the group's own declaration order,
        // since a set has no order of its own.
        private void Toggle(Definition member)
        {
            var chosen = new HashSet<string>();
            if (declaring.activeMembers.TryGetValue(group.Id, out var active))
                foreach (var id in active)
                    chosen.Add(id);
            if (!chosen.Remove(member.Id))
                chosen.Add(member.Id);

            var wanted = new List<Definition>();
            foreach (var listed in group.members)
                if (listed != null && chosen.Contains(listed.Id))
                    wanted.Add(listed);
            Session.SetActiveMembers(Context(), group, wanted);
        }

        // Which widget renders a member of this kind. The set is closed, so an
        // unanswered kind throws rather than leaving a hole on the screen
        // (requirement 7).
        private static string PrefabIdOf(Definition member) => member switch
        {
            CurrencyDefinition => "currency_line",
            ProducerDefinition => "jam_button",
            GeneratorDefinition => "generator_row",
            UpgradeDefinition => "upgrade_row",
            _ => throw new InvalidOperationException(
                $"A group lists '{member.Id}', a {member.GetType().Name}, which no widget renders (12.11).")
        };
    }
}
