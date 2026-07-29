using System;
using UnityEngine;

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

        // The threshold rule every "at least N" condition shares, stated here so
        // the five types cannot disagree about it. A threshold of zero or less is
        // already satisfied by an empty balance, an unowned generator, or an
        // untouched bar group, so the gate stands open before play starts - a
        // buff buyable at boot, a section revealed from the start, a capstone
        // reachable without playing. That is broken content, so it fails closed
        // here (never met) rather than passing, the same way a null compound
        // child does, and ValidateThreshold reports it at load.
        protected static bool ThresholdIsMet(double threshold, BigNumber actual)
            => threshold > 0 && actual >= threshold;

        protected static bool ThresholdIsMet(double threshold, int actual)
            => threshold > 0 && actual >= threshold;

        // Reports the JSON key rather than the C# field the threshold landed in,
        // because the fix is in the chapter data. Every condition spells its
        // threshold `value` - a condition compares against one, while `amount`
        // belongs to a cost block, where the number is a price - so the key is
        // fixed here instead of passed in by each type.
        protected static void ValidateThreshold(double threshold, string source)
        {
            if (threshold <= 0)
                Debug.LogError($"Condition: {source} has a non-positive value ({threshold}) - the gate would be met before play starts.");
        }
    }
}
