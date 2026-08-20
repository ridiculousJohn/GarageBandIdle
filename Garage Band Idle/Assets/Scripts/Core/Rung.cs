using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The one shape behind the album release and the capstone (design doc 12.5):
    // an offer condition and an action list - a rung of the prestige ladder.
    // Every invocation - the UI's try-rung command or ExecuteRung from another
    // rung's action list - is fail-closed against the offer condition; there is
    // no bypass. (Renamed from Press 2026-08-18: that name collided with UI
    // button vocabulary in a codebase where the UI owns nothing.)
    [Serializable]
    public class Rung
    {
        [SerializeReference, SubclassPicker] public Condition offerCondition;
        [SerializeReference, SubclassPicker] public List<GameAction> actions = new();

        // True when the offer condition holds in the rung's own scope. A null
        // condition never offers - an unauthored gate is closed, not open.
        public bool IsOffered(GameContext ctx) =>
            offerCondition != null && offerCondition.Evaluate(ctx);

        // Runs the action list. Calling this while the gate is closed is a
        // caller bug - every path is fail-closed, so ask IsOffered first.
        // ctx must already be rebased to the rung's declaring scope.
        public void Execute(GameContext ctx)
        {
            if (!IsOffered(ctx))
                throw new InvalidOperationException("Rung.Execute called while the offer condition is unmet - ask IsOffered first.");
            foreach (var action in actions)
                action.Execute(ctx);
        }

        // Convenience over the two: runs the list iff the gate holds, and
        // reports whether it did. An unmet gate is an ordinary answer here -
        // ExecuteRung against a closed gate no-ops by design (12.5).
        public bool TryExecute(GameContext ctx)
        {
            if (!IsOffered(ctx))
                return false;
            Execute(ctx);
            return true;
        }
    }
}
