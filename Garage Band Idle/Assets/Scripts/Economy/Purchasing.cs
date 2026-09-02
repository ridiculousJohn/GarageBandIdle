using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The buy entry points (design doc 12.11). Every leg is fail-closed: the
    // domain owns the gate, never the UI's visibility, and an unauthored gate is
    // closed rather than open. The command boundary (foreground-subtree
    // rejection) layers on with GameSession.
    //
    // Can* answers the mutable-state question - gate met, affordable, not
    // already owned - and Buy performs the purchase; TryBuy is the wrapper for a
    // caller that does not need the reason. Content-derived faults throw from
    // either path: static content cannot legitimately be in that state, and
    // reporting one as "no" hides a bug behind an answer the player's own state
    // could have produced.
    public static class Purchasing
    {
        public static bool CanBuy(GameContext ctx, GeneratorDefinition generator)
        {
            var declaringCtx = ctx.Rebase(Producer.DeclaringScope<ScopeState>(ctx.Scope, generator));
            return generator.IsAvailable(declaringCtx)
                && declaringCtx.CanSpend(generator.costCurrency.Id, CostOf(generator, declaringCtx));
        }

        public static bool CanBuy(GameContext ctx, UpgradeDefinition upgrade)
        {
            var declaringCtx = ctx.Rebase(Producer.DeclaringScope<ScopeState>(ctx.Scope, upgrade));
            return upgrade.IsOffered(declaringCtx)
                && !declaringCtx.Scope.purchasedUpgrades.Contains(upgrade.Id)   // the latch IS the one-shot; a reset re-arms it
                && declaringCtx.CanSpend(upgrade.costCurrency.Id, upgrade.cost);
        }

        // Performs the purchase. Calling either when Can answers false is a
        // caller bug, so the guard throws rather than no-oping.
        public static void Buy(GameContext ctx, GeneratorDefinition generator)
        {
            var declaring = Producer.DeclaringScope<ScopeState>(ctx.Scope, generator);
            var declaringCtx = ctx.Rebase(declaring);
            var cost = CostOf(generator, declaringCtx);
            if (!generator.IsAvailable(declaringCtx) || !declaringCtx.CanSpend(generator.costCurrency.Id, cost))
                throw new InvalidOperationException($"Buy: generator '{generator.Id}' is not currently buyable - ask CanBuy first.");

            declaring.generatorCounts.TryGetValue(generator.Id, out var owned);
            declaringCtx.Spend(generator.costCurrency.Id, cost);
            declaring.generatorCounts[generator.Id] = owned + 1;
        }

        public static void Buy(GameContext ctx, UpgradeDefinition upgrade)
        {
            var declaring = Producer.DeclaringScope<ScopeState>(ctx.Scope, upgrade);
            var declaringCtx = ctx.Rebase(declaring);
            if (!upgrade.IsOffered(declaringCtx) || declaring.purchasedUpgrades.Contains(upgrade.Id)
                || !declaringCtx.CanSpend(upgrade.costCurrency.Id, upgrade.cost))
                throw new InvalidOperationException($"Buy: upgrade '{upgrade.Id}' is not currently buyable - ask CanBuy first.");

            declaringCtx.Spend(upgrade.costCurrency.Id, upgrade.cost);

            // Latch before payload: the effects are live for anything the actions
            // read, and a payload resetting the latch's own scope is refused at
            // load (set-then-wiped) rather than silently re-armed here.
            declaring.purchasedUpgrades.Add(upgrade.Id);
            foreach (var action in upgrade.actions)
                action?.Execute(declaringCtx);
        }

        public static bool TryBuy(GameContext ctx, GeneratorDefinition generator)
        {
            if (!CanBuy(ctx, generator))
                return false;
            Buy(ctx, generator);
            return true;
        }

        public static bool TryBuy(GameContext ctx, UpgradeDefinition upgrade)
        {
            if (!CanBuy(ctx, upgrade))
                return false;
            Buy(ctx, upgrade);
            return true;
        }

        // Runtime backstop on the cost curve. Validation refuses a nonpositive
        // baseCost and a nonpositive growth, but that pass is dev-only, and
        // generator purchases REPEAT - a free one is an unbounded rate printer.
        public static BigNumber CostOf(GeneratorDefinition generator, GameContext declaringCtx)
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
