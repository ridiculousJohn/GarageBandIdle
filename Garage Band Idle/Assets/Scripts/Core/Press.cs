using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The one shape behind the album release and the capstone (design doc 12.5):
    // an offer condition and an action list. Every invocation - TryPress from the
    // UI or ExecuteRung from another press - is fail-closed against the offer
    // condition; there is no bypass.
    [Serializable]
    public class Press
    {
        [SerializeReference, SubclassPicker] public Condition offerCondition;
        [SerializeReference, SubclassPicker] public List<GameAction> actions = new();

        // True when the offer condition holds in the press's own scope. A null
        // condition never offers - an unauthored gate is closed, not open.
        public bool IsOffered(GameContext ctx) =>
            offerCondition != null && offerCondition.Evaluate(ctx);

        // Runs the action list iff the gate holds; returns whether it executed.
        // ctx must already be rebased to the press's declaring scope.
        public bool TryExecute(GameContext ctx)
        {
            if (!IsOffered(ctx))
                return false;
            foreach (var action in actions)
                action.Execute(ctx);
            return true;
        }
    }
}
