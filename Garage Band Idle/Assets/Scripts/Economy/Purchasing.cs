using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The one buy entry point (design doc 12.11's TryBuy(generator | upgrade)).
    // Ids are unique tree-wide, so the id itself picks the path. Every leg is
    // fail-closed: the domain owns the gate, never the UI's visibility, and an
    // unauthored gate is closed rather than open. The command boundary
    // (foreground-subtree rejection) layers on with GameSession.
    //
    // CanBuy answers the mutable-state question - gate met, affordable, not
    // already owned - and Buy performs the purchase; TryBuy is the convenience
    // wrapper for a caller that does not need the reason. Anything CONTENT
    // derived - an id resolving to nothing, no declaring scope on the chain, a
    // nonpositive computed cost - throws from either path: static content
    // cannot legitimately be in that state, and reporting it as "no" hides a
    // bug behind an answer the player's state could have produced.
    public static class Purchasing
    {
        public static bool CanBuy(GameContext ctx, string definitionId)
        {
            var generator = ctx.Defs.Get<GeneratorDefinition>(definitionId);
            if (generator != null)
            {
                var declaringCtx = ctx.Rebase(Producer.DeclaringScope(ctx.Scope, generator, s => s.generators));
                return generator.IsAvailable(declaringCtx)
                    && declaringCtx.CanSpend(generator.costCurrencyId, CostOf(generator, declaringCtx));
            }

            var upgrade = ctx.Defs.Get<UpgradeDefinition>(definitionId)
                ?? throw new InvalidOperationException($"CanBuy: '{definitionId}' resolves to no generator or upgrade.");
            var upgradeCtx = ctx.Rebase(Producer.DeclaringScope(ctx.Scope, upgrade, s => s.upgrades));
            return upgrade.IsOffered(upgradeCtx)
                && !upgradeCtx.Scope.purchasedUpgrades.Contains(upgrade.Id)   // the latch IS the one-shot; a reset re-arms it
                && upgradeCtx.CanSpend(upgrade.costCurrencyId, upgrade.cost);
        }

        // Performs the purchase. Calling this when CanBuy answers false is a
        // caller bug, so the guard throws rather than no-oping.
        public static void Buy(GameContext ctx, string definitionId)
        {
            if (!CanBuy(ctx, definitionId))
                throw new InvalidOperationException($"Buy: '{definitionId}' is not currently buyable - ask CanBuy first.");

            var generator = ctx.Defs.Get<GeneratorDefinition>(definitionId);
            if (generator != null)
            {
                var declaring = Producer.DeclaringScope(ctx.Scope, generator, s => s.generators);
                var declaringCtx = ctx.Rebase(declaring);
                declaring.generatorCounts.TryGetValue(generator.Id, out var owned);
                declaringCtx.Spend(generator.costCurrencyId, CostOf(generator, declaringCtx));
                declaring.generatorCounts[generator.Id] = owned + 1;
                return;
            }

            var upgrade = ctx.Defs.Get<UpgradeDefinition>(definitionId);
            var upgradeScope = Producer.DeclaringScope(ctx.Scope, upgrade, s => s.upgrades);
            var upgradeCtx = ctx.Rebase(upgradeScope);
            upgradeCtx.Spend(upgrade.costCurrencyId, upgrade.cost);

            // Latch before payload: the effects are live for anything the actions
            // read, and a payload resetting the latch's own scope is refused at
            // load (set-then-wiped) rather than silently re-armed here.
            upgradeScope.purchasedUpgrades.Add(upgrade.Id);
            foreach (var action in upgrade.actions)
                action?.Execute(upgradeCtx);
        }

        public static bool TryBuy(GameContext ctx, string definitionId)
        {
            if (!CanBuy(ctx, definitionId))
                return false;
            Buy(ctx, definitionId);
            return true;
        }

        // Runtime backstop on the cost curve. Validation refuses a nonpositive
        // baseCost and a nonpositive growth, but that pass is dev-only, and
        // generator purchases REPEAT - a free one is an unbounded rate printer.
        private static BigNumber CostOf(GeneratorDefinition generator, GameContext declaringCtx)
        {
            declaringCtx.Scope.generatorCounts.TryGetValue(generator.Id, out var owned);
            var cost = generator.CostAt(owned);
            if (cost <= BigNumber.Zero)
                throw new InvalidOperationException(
                    $"Generator '{generator.Id}' computed cost {cost} at owned={owned}.");
            return cost;
        }
    }
}
