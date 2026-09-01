using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The generic half of the feedback contract (design doc 12.11), over any
    // gate in a context: the rung buttons and the event rows share one
    // implementation of the leg rules. Pure - reads the same condition objects
    // the operations enforce, so what the screen explains is what refused.
    public static class GateFeedback
    {
        // A gate's top-level legs: an All's list, else the gate itself (12.11).
        // A Not or an Any is one leg carrying its own uiText, never decomposed.
        // A null gate has no legs - the load pass refuses one, and IsOffered
        // and IsAvailable already fail closed on it.
        public static IReadOnlyList<Condition> Legs(Condition gate)
        {
            if (gate == null)
                return System.Array.Empty<Condition>();
            if (gate is All all)
                return all.conditions;
            return new[] { gate };
        }

        // The top-level legs that do not hold, in authored order - each judged
        // on its own, so the rendering names every unmet leg and not just the
        // first one the All would have stopped at (12.11).
        public static List<Condition> UnmetLegs(Condition gate, GameContext ctx)
        {
            var unmet = new List<Condition>();
            var legs = Legs(gate);
            for (var i = 0; i < legs.Count; i++)
                if (!legs[i].Evaluate(ctx))
                    unmet.Add(legs[i]);
            return unmet;
        }
    }
}
