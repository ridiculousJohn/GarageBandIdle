using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Load-time companion to GateEvaluator: every id a condition references must
    // resolve, and every condition type must have an evaluator, reported with
    // the referencing context named. Shared by generator unlocks and upgrade
    // gates so the handled-type list lives in one place.
    public static class GateValidator
    {
        public static void Validate(IReadOnlyList<GateCondition> conditions, string context,
            CurrencyManager currencies, GeneratorSystem generators)
        {
            foreach (var condition in conditions)
            {
                switch (condition.Type)
                {
                    case GateCondition.TypeCurrencyBalance:
                    case GateCondition.TypeCurrencyEarnedTotal:
                        currencies.ValidateReference(condition.CurrencyId, context);
                        break;
                    case GateCondition.TypeOwnedCount:
                        if (generators == null || string.IsNullOrEmpty(condition.GeneratorId)
                            || !generators.TryGet(condition.GeneratorId, out _))
                            Debug.LogError($"GateValidator: {context} references unknown generator id '{condition.GeneratorId}'.");
                        break;
                    case GateCondition.TypeFlagSet:
                        if (string.IsNullOrEmpty(condition.FlagId))
                            Debug.LogError($"GateValidator: {context} has a flagSet condition with an empty flag id.");
                        break;
                    case GateCondition.TypeCoversCompleted:
                        // valid content; its evaluator arrives with the covers
                        // slice, so until then it holds the gate unmet by design
                        break;
                    default:
                        Debug.LogError($"GateValidator: {context} uses gate type '{condition.Type}', which has no handler. It will never pass.");
                        break;
                }
            }
        }
    }
}
