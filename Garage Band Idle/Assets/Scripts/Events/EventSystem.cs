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
        // refuses, the fail-closed backstop behind the load-time check. The
        // entry list takes the runner's question like every other list (12.5):
        // an onEntry that would clear a scope holding an armed reward closes
        // the Start button rather than half-running.
        public static bool CanStart(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            var hostCtx = ctx.Rebase(host);
            return host.activeEvent == null && evt.IsAvailable(hostCtx)
                && ActionList.Refuses(evt.onEntry, hostCtx) == null;
        }

        public static void Start(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            var hostCtx = ctx.Rebase(host);
            if (host.activeEvent != null || !evt.IsAvailable(hostCtx)
                || ActionList.Refuses(evt.onEntry, hostCtx) != null)
                throw new InvalidOperationException(
                    $"Start: event '{evt.Id}' is not currently startable - ask CanStart first.");

            // onEntry first, the record after: an entry list that resets the
            // host swaps the payload, and writing through the accessor then is
            // what puts the record in the fresh one (6.1's banked run).
            ActionList.Run(evt.onEntry, hostCtx);
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
        // produced (12.8). Both ending lists take the runner's question, asked
        // as if this host's record were already GONE - dismissal removes it
        // before either list runs, so asking with it still there would refuse
        // an ending on the very reward it is about to pay. `ignoring: host` is
        // that, and it is the only place the argument is ever non-null.
        public static bool CanDismiss(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            var record = host.activeEvent;
            return record != null && record.eventId == evt.Id && EndingsRun(ctx, evt, host, record);
        }

        // Whether the two ending lists would both run: rewards only when the
        // goal latched, onEnd always.
        private static bool EndingsRun(GameContext ctx, EventDefinition evt, InteriorScopeState host, ActiveEvent record)
        {
            var hostCtx = ctx.Rebase(host);
            if (record.goalReached && ActionList.Refuses(evt.rewards, hostCtx, ignoring: host) != null)
                return false;
            return ActionList.Refuses(evt.onEnd, hostCtx, ignoring: host) == null;
        }

        public static void Dismiss(GameContext ctx, EventDefinition evt)
        {
            var host = Producer.DeclaringScope<InteriorScopeState>(ctx.Scope, evt);
            var record = host.activeEvent;
            // A refused dismissal changes nothing, which is why the whole guard
            // is answered before the record is touched.
            if (record == null || record.eventId != evt.Id || !EndingsRun(ctx, evt, host, record))
                throw new InvalidOperationException(
                    $"Dismiss: event '{evt.Id}' is not currently dismissable at its host - ask CanDismiss first.");

            // Remove FIRST (12.8): it opens a rung whose own list clears this
            // host, so an onEnd carrying RestartScope(host) banks instead of
            // being refused by its own reward. Nothing can observe the gap - no
            // action reads a multiplier, and starting is a command no list can
            // reach. The record is gone, so the lists run with no exclusion.
            var goalReached = record.goalReached;
            host.activeEvent = null;
            var hostCtx = ctx.Rebase(host);
            if (goalReached)
                ActionList.Run(evt.rewards, hostCtx);
            ActionList.Run(evt.onEnd, hostCtx);
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
