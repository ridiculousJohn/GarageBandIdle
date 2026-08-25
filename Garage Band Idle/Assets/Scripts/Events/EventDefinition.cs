using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Events
{
    // A scoped challenge (design doc 6.1, 12.8): declared on the interior scope
    // that hosts it, entered and ended by commands rather than actions, leaving
    // exactly one ActiveEvent record on the host while it runs.
    [CreateAssetMenu(menuName = "Garage Band Idle/Event")]
    public class EventDefinition : Definition
    {
        // A null gate refuses entry - the fail-closed backstop behind the
        // load-time check, which refuses a null gate outright (12.12).
        [SerializeReference, SubclassPicker] public Condition availableWhen;

        // Latched by the sweep while the event runs. Null is dismiss-only:
        // validation warns, because the event can never reward.
        [SerializeReference, SubclassPicker] public Condition goal;

        // Real seconds; zero means untimed. Expiry ends nothing but the chance
        // to latch the goal (12.8).
        public double timeLimitSeconds;

        // Applied by the host's multiplier gather while its record names this
        // event - existence is the whole test, expiry does not lift them (12.6).
        public List<Effect> handicaps = new();

        [SerializeReference, SubclassPicker] public List<GameAction> onEntry = new();

        // The two ending lists (6.1): rewards runs only when the goal latched,
        // onEnd runs either way, in that order, in one transaction.
        [SerializeReference, SubclassPicker] public List<GameAction> rewards = new();
        [SerializeReference, SubclassPicker] public List<GameAction> onEnd = new();

        // Fail-closed, and the domain owns the gate - never the UI's visibility
        // (the same ruling as a generator's). ctx must be rebased to the host.
        public bool IsAvailable(GameContext hostCtx) =>
            availableWhen != null && availableWhen.Evaluate(hostCtx);

        public bool GoalHolds(GameContext hostCtx) =>
            goal != null && goal.Evaluate(hostCtx);

        // Derived, so the idle path asks one question and never inspects a
        // timer; a bool becomes another term here if one is ever wanted (12.9).
        public bool BlocksIdle => timeLimitSeconds > 0;
    }
}
