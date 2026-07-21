using System;

namespace RidiculousGaming.GarageBandIdle
{
    // One gate/unlock/visibility/availability rule (design doc section 12, rule 8).
    // A single polymorphic family serialized via [SerializeReference]: each subclass
    // declares exactly the fields its kind needs and implements Evaluate, so a
    // condition type can never exist without its handler (the same shape as the
    // RewardDefinition family). Callers go through ConditionEvaluator; a null
    // condition means "no gate" and is always met.
    [Serializable]
    public abstract class Condition
    {
        // true when the rule currently holds against the running game
        public abstract bool Evaluate(ConditionContext context);

        // load-time check that every id this rule references resolves; failures
        // are reported loudly with the referencing content named in source
        public abstract void Validate(ConditionContext context, string source);
    }
}
