using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Meta
{
    // The one writer for the root allocation fact (design doc 8.2/12.11).
    // Validation completes before the live map is touched, so a refused
    // replacement leaves the standing allocation whole.
    public static class RoadieAllocation
    {
        public static bool TrySet(GameContext ctx, IReadOnlyDictionary<string, int> requested)
        {
            if (ctx.Scope is not RootScopeState root)
                throw new InvalidOperationException(
                    $"RoadieAllocation must run at root, not '{ctx.Scope.ScopeId}'.");

            var chapterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in root.Children)
                if (((ChapterScopeState)child).IsUnlocked(ctx.NowUtc))
                    chapterIds.Add(child.ScopeId);

            var total = BigNumber.Zero;
            foreach (var pair in requested)
            {
                if (pair.Value < 0 || !chapterIds.Contains(pair.Key))
                    return false;
                total += pair.Value;
            }

            // Roadies are a currency, so the affordability comparison stays in
            // BigNumber even though each authored station count is an int.
            if (total > ctx.GetBalance(Roadies.CurrencyId))
                return false;

            root.roadieAllocation.Clear();
            foreach (var pair in requested)
                if (pair.Value != 0)
                    root.roadieAllocation.Add(pair.Key, pair.Value);
            return true;
        }
    }
}
