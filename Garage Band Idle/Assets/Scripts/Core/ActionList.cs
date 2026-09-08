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
        public static void Run(IReadOnlyList<GameAction> list, GameContext ctx, ScopeState ignoring = null)
        {
            var refusal = Refuses(list, ctx, ignoring);
            if (refusal != null)
                throw new InvalidOperationException(
                    $"An action list was run past a refusal: scope '{refusal.Host.ScopeId}' holds the unclaimed reward of event "
                    + $"'{(refusal.Event == null ? refusal.Record.eventId : refusal.Event.Id)}' (design doc 12.5).");
            ExecuteAll(list, ctx);
        }

        private static void ExecuteAll(IReadOnlyList<GameAction> list, GameContext ctx)
        {
            if (list == null)
                return;
            for (var i = 0; i < list.Count; i++)
                list[i]?.Execute(ctx);
        }
    }
}
