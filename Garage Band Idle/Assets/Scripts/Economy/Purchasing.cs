using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The one buy entry point (design doc 12.11's TryBuy(generator | upgrade)).
    // Ids are unique tree-wide, so the id itself picks the path. Every leg is
    // fail-closed: the domain owns the gate, never the UI's visibility, and an
    // unauthored gate is closed rather than open. The command boundary
    // (foreground-subtree rejection) layers on with GameSession.
    public static class Purchasing
    {
        public static bool TryBuy(GameContext ctx, string definitionId)
        {
            var generator = ctx.Defs.Get<GeneratorDefinition>(definitionId);
            if (generator != null)
                return TryBuyGenerator(ctx, generator);

            var upgrade = ctx.Defs.Get<UpgradeDefinition>(definitionId);
            if (upgrade != null)
                return TryBuyUpgrade(ctx, upgrade);

            Debug.LogError($"TryBuy: '{definitionId}' resolves to no generator or upgrade.");
            return false;
        }

        private static bool TryBuyGenerator(GameContext ctx, GeneratorDefinition generator)
        {
            var declaring = Producer.DeclaringScope(ctx.Scope, generator, s => s.generators);
            if (declaring == null)
            {
                Debug.LogError($"TryBuy: no scope declares generator '{generator.Id}'.");
                return false;
            }
            var declaringCtx = ctx.Rebase(declaring);

            if (!generator.IsAvailable(declaringCtx))
                return false;

            declaring.generatorCounts.TryGetValue(generator.Id, out var owned);
            var cost = generator.CostAt(owned);
            if (cost <= BigNumber.Zero)
            {
                // Runtime backstop. Validation refuses a nonpositive baseCost and
                // a nonpositive growth, but that pass is dev-only - this check is
                // what release builds execute, and generator purchases REPEAT, so
                // a free one is an unbounded rate printer.
                Debug.LogError($"TryBuy: generator '{generator.Id}' computed cost {cost} at owned={owned} - refused.");
                return false;
            }
            if (!declaringCtx.TrySpend(generator.costCurrencyId, cost))
                return false;

            declaring.generatorCounts[generator.Id] = owned + 1;
            return true;
        }

        private static bool TryBuyUpgrade(GameContext ctx, UpgradeDefinition upgrade)
        {
            var declaring = Producer.DeclaringScope(ctx.Scope, upgrade, s => s.upgrades);
            if (declaring == null)
            {
                Debug.LogError($"TryBuy: no scope declares upgrade '{upgrade.Id}'.");
                return false;
            }
            var declaringCtx = ctx.Rebase(declaring);

            if (!upgrade.IsOffered(declaringCtx))
                return false;
            if (declaring.purchasedUpgrades.Contains(upgrade.Id))
                return false;               // the latch IS the one-shot; a reset re-arms it
            if (!declaringCtx.TrySpend(upgrade.costCurrencyId, upgrade.cost))
                return false;

            // Latch before payload: the effects are live for anything the actions
            // read, and a payload resetting the latch's own scope is refused at
            // load (set-then-wiped) rather than silently re-armed here.
            declaring.purchasedUpgrades.Add(upgrade.Id);
            foreach (var action in upgrade.actions)
                action?.Execute(declaringCtx);
            return true;
        }
    }
}
