using System;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Events
{
    // The event lifecycle (design doc 6.1, 12.8, 12.11). Start and Dismiss are
    // commands, never GameAction kinds, so no authored list can start or end an
    // event and one event cannot spawn another. The shape is Purchasing's: Can*
    // answers the mutable-state question, the command mutates and throws when
    // its guard says no, Try* wraps the two. Each resolves the host by the
    // outward walk - declaration is ownership, asked for an InteriorScopeState
    // so root is not a candidate - and rebases to it, so the gate, the goal and
    // all three action lists evaluate in the HOST's scope (12.4).
    public static class EventSystem
    {
        // Start refuses an occupied host - ANY record, running or expired-but-
        // undismissed, blocks entry (12.8) - and a closed gate; a null gate
        // refuses, the fail-closed backstop behind the load-time check.
        public static bool CanStart(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            return host.activeEvent == null && evt.IsAvailable(ctx.Rebase(host));
        }

        public static void Start(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            var hostCtx = ctx.Rebase(host);
            if (host.activeEvent != null || !evt.IsAvailable(hostCtx))
                throw new InvalidOperationException(
                    $"Start: event '{evt.Id}' is not currently startable - ask CanStart first.");

            // onEntry first, the record after: an entry list that resets the
            // host swaps the payload, and writing through the accessor then is
            // what puts the record in the fresh one (6.1's banked run).
            foreach (var action in evt.onEntry)
                action?.Execute(hostCtx);
            host.activeEvent = new ActiveEvent { eventId = evt.Id, remainingSeconds = evt.timeLimitSeconds };
        }

        public static bool TryStart(GameContext ctx, EventDefinition evt)
        {
            if (!CanStart(ctx, evt))
                return false;
            Start(ctx, evt);
            return true;
        }

        // Dismiss needs a record FOR THIS EVENT: a sibling's record is an
        // ordinary refusal, since which event is running is state the player
        // produced (12.8).
        public static bool CanDismiss(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            return host.activeEvent != null && host.activeEvent.eventId == evt.Id;
        }

        public static void Dismiss(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            var record = host.activeEvent;
            if (record == null || record.eventId != evt.Id)
                throw new InvalidOperationException(
                    $"Dismiss: event '{evt.Id}' holds no record at its host - ask CanDismiss first.");

            // Remove FIRST (12.8): it opens a rung gated on
            // Not(EventRewardPending(host)), so an onEnd carrying
            // RestartScope(host) banks instead of no-oping against its own
            // reward. Nothing can observe the gap - no action reads a
            // multiplier, and starting is a command no list can reach.
            var goalReached = record.goalReached;
            host.activeEvent = null;
            var hostCtx = ctx.Rebase(host);
            if (goalReached)
                foreach (var action in evt.rewards)
                    action?.Execute(hostCtx);
            foreach (var action in evt.onEnd)
                action?.Execute(hostCtx);
        }

        public static bool TryDismiss(GameContext ctx, EventDefinition evt)
        {
            if (!CanDismiss(ctx, evt))
                return false;
            Dismiss(ctx, evt);
            return true;
        }

        // The one latch rule (12.8), shared by its two callers - the timer
        // phase below and the sweep: the goal latches while the attempt is
        // RUNNING (untimed, or time remaining), timed and untimed alike, and
        // expiry does exactly one thing - stop this. Read through the
        // declaration list, so a stray record id never latches.
        internal static void LatchGoal(InteriorScopeState host, DateTime nowUtc)
        {
            var record = host.activeEvent;
            if (record == null || record.goalReached)
                return;
            foreach (var evt in ((InteriorDefinition)host.Definition).events)
            {
                if (evt == null || evt.Id != record.eventId)
                    continue;
                if (evt.timeLimitSeconds > 0 && record.remainingSeconds <= 0)
                    return;
                if (evt.GoalHolds(new GameContext(host, nowUtc)))
                    record.goalReached = true;
                return;
            }
        }

        // The tick's wall-clock timer phase (12.9), over the same set the sweep
        // walks - root plus the foreground chapter's subtree. Root cannot hold
        // a record, so the parameter states the set and the type discharges its
        // half; a dormant chapter's timer pauses because this never walks it.
        // Latch BEFORE decrement is what sends the tie - a goal met by the
        // segment that also expires the timer - to the player. A record never
        // removes itself: expiry ends nothing but the chance to latch. The
        // timestamp is passed, never read from the clock, for the same reason
        // BarSystem takes its segment boundaries as arguments.
        public static void AdvanceTimers(RootScopeState root, ChapterScopeState foregroundChapter,
                                         double realSeconds, DateTime segmentEndUtc)
        {
            if (foregroundChapter == null || realSeconds <= 0)
                return;
            Walk(foregroundChapter);

            void Walk(ScopeState node)
            {
                if (node is InteriorScopeState host && host.activeEvent != null)
                {
                    LatchGoal(host, segmentEndUtc);
                    // Only a timed record still running has seconds to burn; an
                    // untimed one sits at zero and is untouched.
                    if (host.activeEvent.remainingSeconds > 0)
                        host.activeEvent.remainingSeconds =
                            Math.Max(0, host.activeEvent.remainingSeconds - realSeconds);
                }
                foreach (var child in node.Children)
                    Walk(child);
            }
        }
    }
}
