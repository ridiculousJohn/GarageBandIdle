using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "flagSet": a progress flag has latched on (FlagSystem). The
    // observing half of the single reveal registry — content-unlock upgrades and
    // setFlag rewards set flags, and anything that appears when a system exists
    // gates on one of these.
    [Serializable]
    public class FlagSetCondition : Condition
    {
        [SerializeField]
        private string _flagId;

        public string FlagId => _flagId;

        public FlagSetCondition() { }

        public FlagSetCondition(string flagId)
        {
            _flagId = flagId;
        }

        public override bool Evaluate(ConditionContext context)
            => context.Flags != null && context.Flags.IsSet(_flagId);

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_flagId))
                Debug.LogError($"Condition: {source} has a flagSet condition with an empty flag id.");
            else if (context.Flags != null && !context.Flags.IsKnown(_flagId))
                Debug.LogError($"Condition: {source} references flag '{_flagId}', which no chapter declares.");
        }
    }
}
