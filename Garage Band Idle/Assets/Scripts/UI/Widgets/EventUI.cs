using System;
using System.Collections.Generic;
using System.Text;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound event's row (design doc 12.11). Its state is runtime fact,
    // never a widget decision: ACTIVE when the host's record names this event,
    // STARTABLE when EventSystem.CanStart says so - the same gate StartEvent
    // enforces - else disabled, rendering its unmet legs. Nothing here
    // interpolates: the timer snaps per tick, four times a second at the
    // authored cadence, and the interpolated slopes are measured deltas, which
    // a wall clock is not.
    public sealed class EventUI : ModuleWidget
    {
        private readonly Label nameLabel;
        private readonly Label statusLabel;
        private readonly VisualElement legs;
        private readonly Button start;
        private readonly Button dismiss;

        // The legs paired with the conditions they render, as the rung button
        // pairs them: the gate's top-level list is fixed, so the labels are
        // built once and matched against the unmet set by identity.
        private readonly List<(Condition condition, Label label)> legViews = new();

        private EventDefinition evt;

        // The host, resolved once: the record lives at the scope that DECLARES
        // the event, and declaration is ownership (12.3/12.8).
        private InteriorScopeState host;

        public EventUI(VisualElement root) : base(root)
        {
            nameLabel = Require<Label>(root, "name", "EventRow.uxml");
            statusLabel = Require<Label>(root, "status", "EventRow.uxml");
            legs = Require<VisualElement>(root, "legs", "EventRow.uxml");
            start = Require<Button>(root, "start", "EventRow.uxml");
            dismiss = Require<Button>(root, "dismiss", "EventRow.uxml");
        }

        protected override void OnBound()
        {
            evt = (EventDefinition)Content;
            host = Producer.DeclaringScope<InteriorScopeState>(Scope, evt);
            nameLabel.text = evt.displayName;
            start.text = "Start";
            start.clicked += () => Session.TryStartEvent(Context(), evt);
            dismiss.clicked += () => Session.TryDismissEvent(Context(), evt);
            foreach (var leg in GateFeedback.Legs(evt.availableWhen))
            {
                var label = new Label();
                label.AddToClassList("leg");
                legViews.Add((leg, label));
                legs.Add(label);
            }
        }

        public override void Refresh()
        {
            var ctx = Context();
            // The gate and the goal evaluate in the HOST's scope, exactly as
            // EventSystem rebases before asking them (12.4/12.8).
            var hostCtx = ctx.Rebase(host);
            var record = host.activeEvent;
            var active = record != null && record.eventId == evt.Id;

            start.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            dismiss.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (active)
            {
                // A running attempt has no gate left to explain, and its one
                // button is the ending - which pays when the goal latched.
                dismiss.text = record.goalReached ? "Claim reward" : "Dismiss";
                statusLabel.text = Status(record, hostCtx);
                foreach (var (_, label) in legViews)
                    label.style.display = DisplayStyle.None;
                return;
            }

            start.SetEnabled(EventSystem.CanStart(ctx, evt));
            statusLabel.text = "";
            var unmet = GateFeedback.UnmetLegs(evt.availableWhen, hostCtx);
            foreach (var (condition, label) in legViews)
            {
                var visible = unmet.Contains(condition);
                label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    label.text = GateFeedback.LegText(condition, hostCtx);
            }
        }

        // The attempt as the player reads it: the timer when the event is
        // timed, then the goal, which is the same leg rendering the gates use.
        // Expiry ends nothing but the chance to latch (12.8), so a spent timer
        // sits beside the goal line rather than replacing the row.
        private string Status(ActiveEvent record, GameContext hostCtx)
        {
            var text = new StringBuilder();
            if (evt.timeLimitSeconds > 0)
                text.Append(record.remainingSeconds > 0
                    ? Math.Ceiling(record.remainingSeconds) + "s left"
                    : "Time's up");
            // A goalless event is dismiss-only (12.12), so it has no goal line.
            if (evt.goal != null)
            {
                if (text.Length > 0)
                    text.Append(" - ");
                text.Append(record.goalReached ? "Goal reached" : "Goal " + GateFeedback.LegText(evt.goal, hostCtx));
            }
            return text.ToString();
        }
    }
}
