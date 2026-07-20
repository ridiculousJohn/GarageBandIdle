using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Shared evaluation for GateCondition lists (all must hold). Used by
    // generator unlocks, section visibility, and later upgrade gates — one
    // handler per condition type lives here, so a new type is one new case.
    public static class GateEvaluator
    {
        public static bool AllMet(IReadOnlyList<GateCondition> conditions, CurrencyManager currencies, GeneratorSystem generators, FlagSystem flags)
        {
            foreach (var condition in conditions)
            {
                if (!IsMet(condition, currencies, generators, flags))
                    return false;
            }
            return true;
        }

        private static bool IsMet(GateCondition condition, CurrencyManager currencies, GeneratorSystem generators, FlagSystem flags)
        {
            switch (condition.Type)
            {
                case GateCondition.TypeCurrencyBalance:
                    return currencies.Get(condition.CurrencyId) >= condition.Amount;
                case GateCondition.TypeCurrencyEarnedTotal:
                    return currencies.GetLifetimeEarned(condition.CurrencyId) >= condition.Amount;
                case GateCondition.TypeOwnedCount:
                    return generators != null
                        && generators.TryGet(condition.GeneratorId, out var generator)
                        && generator.Owned >= condition.Amount;
                case GateCondition.TypeFlagSet:
                    return flags != null && flags.IsSet(condition.FlagId);
                default:
                    // unsupported types are reported by the owning system at
                    // load; an unknown condition never passes
                    return false;
            }
        }
    }
}
