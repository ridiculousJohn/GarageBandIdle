using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // The one place an authored action list runs (design doc 12.5). The rule it
    // enforces - a scope holding an armed, unclaimed reward refuses to be
    // cleared, so a list containing that clear does not run at all - is a rule
    // about RUNNING A LIST, which is why it lives here and nowhere else: a rung
    // asking on its own while the trigger, event, upgrade and bar loops never
    // asked is the defect this class exists to forbid. A seventh kind of list
    // is added to the definition's own enumeration and calls this; there is no
    // other step, so there is no other step to forget.
    public static class ActionList
    {
        // The first refusal any action in the list reports, in list order, or
        // null. Null entries are skipped - the validator reports them, and a
        // hole cannot refuse. This is the ONLY code that asks an action whether
        // it is refused.
        public static Refusal Refuses(IReadOnlyList<GameAction> list, GameContext ctx, ScopeState ignoring = null)
        {
            if (list == null)
                return null;
            for (var i = 0; i < list.Count; i++)
            {
                var refusal = list[i]?.Refuses(ctx, ignoring);
                if (refusal != null)
                    return refusal;
            }
            return null;
        }

        // A list runs whole or not at all: false without executing anything
        // when something refuses, otherwise every action in order.
        public static bool TryRun(IReadOnlyList<GameAction> list, GameContext ctx, ScopeState ignoring = null)
        {
            if (Refuses(list, ctx, ignoring) != null)
                return false;
            ExecuteAll(list, ctx);
            return true;
        }

        // For a caller whose own guard already asked. Forced past a refusal it
        // throws (requirement 7): the guard and the run read the same objects,
        // so disagreeing means the code is broken, not the player's state.
        //
        // `skip` names actions this caller has already performed by another
        // route - the idle claim's payment lines are its completions' yields
        // (section 9) - and the refusal check still covers the WHOLE list,
        // because a list runs whole or not at all whichever actions execute.
        public static void Run(IReadOnlyList<GameAction> list, GameContext ctx, ScopeState ignoring = null,
                               Predicate<GameAction> skip = null)
        {
            RequireUnrefused(list, ctx, ignoring);
            ExecuteAll(list, ctx, skip);
        }

        // Whether the list run `times` times in a row from one snapshot is
        // exactly one RunTimes (design doc 12.5/12.7): every action that would
        // execute repeats as one. Null and skipped entries execute nothing, so
        // they repeat as anything does, and an empty list is a list every
        // action of which repeats as one.
        public static bool RepeatsAsOne(IReadOnlyList<GameAction> list, Predicate<GameAction> skip = null)
        {
            if (list == null)
                return true;
            for (var i = 0; i < list.Count; i++)
            {
                var action = list[i];
                if (action == null || (skip != null && skip(action)))
                    continue;
                if (!action.RepeatsAsOne)
                    return false;
            }
            return true;
        }

        // What one run of the list changes that a condition can read, each
        // action describing its own execution against the one state the call
        // sees (12.4/12.5).
        public static void Describe(IReadOnlyList<GameAction> list, GameContext ctx, CompletionDelta delta)
        {
            if (list == null)
                return;
            for (var i = 0; i < list.Count; i++)
                list[i]?.Describe(ctx, delta);
        }

        // The list run `times` times as one: the refusal check once, exactly
        // as Run makes it - a list runs whole or not at all, whatever the
        // count - and then each action's ExecuteTimes in list order. For a
        // caller that asked RepeatsAsOne; an action that does not repeat as
        // one refuses a count other than one.
        public static void RunTimes(IReadOnlyList<GameAction> list, GameContext ctx, BigNumber times,
                                    ScopeState ignoring = null, Predicate<GameAction> skip = null)
        {
            RequireUnrefused(list, ctx, ignoring);
            if (list == null)
                return;
            for (var i = 0; i < list.Count; i++)
            {
                var action = list[i];
                if (action != null && (skip == null || !skip(action)))
                    action.ExecuteTimes(ctx, times);
            }
        }

        private static void RequireUnrefused(IReadOnlyList<GameAction> list, GameContext ctx, ScopeState ignoring)
        {
            var refusal = Refuses(list, ctx, ignoring);
            if (refusal != null)
                throw new InvalidOperationException(
                    $"An action list was run past a refusal: scope '{refusal.Host.ScopeId}' holds the unclaimed reward of event "
                    + $"'{(refusal.Event == null ? refusal.Record.eventId : refusal.Event.Id)}' (design doc 12.5).");
        }

        private static void ExecuteAll(IReadOnlyList<GameAction> list, GameContext ctx,
                                       Predicate<GameAction> skip = null)
        {
            if (list == null)
                return;
            for (var i = 0; i < list.Count; i++)
            {
                var action = list[i];
                if (action != null && (skip == null || !skip(action)))
                    action.Execute(ctx);
            }
        }
    }
}
