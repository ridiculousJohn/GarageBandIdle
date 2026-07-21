using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "compound": every condition in All must hold, and at least one in
    // Any must hold (an empty list places no requirement). Children are Condition
    // references themselves, so compounds nest.
    [Serializable]
    public class CompoundCondition : Condition
    {
        [SerializeReference]
        private List<Condition> _all = new();

        [SerializeReference]
        private List<Condition> _any = new();

        public IReadOnlyList<Condition> All => _all;
        public IReadOnlyList<Condition> Any => _any;

        public CompoundCondition() { }

        public CompoundCondition(List<Condition> all, List<Condition> any)
        {
            _all = all ?? new List<Condition>();
            _any = any ?? new List<Condition>();
        }

        public override bool Evaluate(ConditionContext context)
        {
            foreach (var condition in _all)
            {
                // a null child is a content mistake (reported by Validate); it
                // must fail closed rather than accidentally pass
                if (condition == null || !condition.Evaluate(context))
                    return false;
            }

            if (_any.Count > 0)
            {
                foreach (var condition in _any)
                {
                    if (condition != null && condition.Evaluate(context))
                        return true;
                }
                return false;
            }

            return true;
        }

        public override void Validate(ConditionContext context, string source)
        {
            if (_all.Count == 0 && _any.Count == 0)
                Debug.LogError($"Condition: {source} has a compound condition with no children.");

            ValidateChildren(_all, context, source, "all");
            ValidateChildren(_any, context, source, "any");
        }

        private static void ValidateChildren(List<Condition> children, ConditionContext context, string source, string listName)
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] == null)
                    Debug.LogError($"Condition: {source} compound '{listName}' entry {i} is null. It will never pass.");
                else
                    children[i].Validate(context, $"{source} (compound {listName}[{i}])");
            }
        }
    }
}
