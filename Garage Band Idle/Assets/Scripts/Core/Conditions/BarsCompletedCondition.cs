using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "barsCompleted": at least Value bars completed in a bar group
    // this run. Unmet until a bar system is wired into the ConditionContext
    // (bars slice), which is the staged-content behavior the chapter data wants.
    [Serializable]
    public class BarsCompletedCondition : Condition
    {
        [SerializeField]
        private string _groupId;

        [SerializeField]
        private double _value;

        public string GroupId => _groupId;
        public double Value => _value;

        public BarsCompletedCondition() { }

        public BarsCompletedCondition(string groupId, double value)
        {
            _groupId = groupId;
            _value = value;
        }

        public override bool Evaluate(ConditionContext context)
            => context.Bars != null && context.Bars.CompletedCount(_groupId) >= _value;

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_groupId))
                Debug.LogError($"Condition: {source} has a barsCompleted condition with an empty group id.");
            else if (context.Database != null && !context.Database.BarGroups.Contains(_groupId))
                Debug.LogError($"Condition: {source} references unknown bar group id '{_groupId}'.");
        }
    }
}
