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
    //
    // A generator purchase is a count (12.2): CostOf is the one function that
    // computes a generator cost, MaxAffordable searches over it, and a buy is
    // one spend of the series sum and one write of the new count. The count is
    // required at every leg, so what the row prints and what the bank pays are
    // the same number by construction.
    public static class Purchasing
    {
        public static bool CanBuy(GameContext ctx, GeneratorDefinition generator, int count)
        {
            var declaringCtx = ctx.Rebase(Producer.DeclaringScope<ScopeState>(ctx.Scope, generator));
            return generator.IsAvailable(declaringCtx)
                && declaringCtx.CanSpend(generator.costCurrency.Id, CostOf(generator, declaringCtx, count));
        }

        // The payload takes the action-list runner's question like every other
        // list (12.5): an upgrade whose actions would clear a scope holding an
        // armed reward is unbuyable rather than half-applied.
        public static bool CanBuy(GameContext ctx, UpgradeDefinition upgrade)
        {
            var declaringCtx = ctx.Rebase(Producer.DeclaringScope<ScopeState>(ctx.Scope, upgrade));
            return upgrade.IsOffered(declaringCtx)
                && !declaringCtx.Scope.purchasedUpgrades.Contains(upgrade.Id)   // the latch IS the one-shot; a reset re-arms it
                && declaringCtx.CanSpend(upgrade.costCurrency.Id, upgrade.cost)
                && ActionList.Refuses(upgrade.actions, declaringCtx) == null;
        }

        // Performs the purchase. Calling either when Can answers false is a
        // caller bug, so the guard throws rather than no-oping.
        // One spend of the series sum and one write of the new count, never a
        // loop of unit buys: nothing in the effect vocabulary observes a unit
        // landing and counts scale on read, so the count lands at once and the
        // trigger sweep runs once at the transaction's close (12.9).
        public static void Buy(GameContext ctx, GeneratorDefinition generator, int count)
        {
            var declaring = Producer.DeclaringScope<ScopeState>(ctx.Scope, generator);
            var declaringCtx = ctx.Rebase(declaring);
            var cost = CostOf(generator, declaringCtx, count);
            if (!generator.IsAvailable(declaringCtx) || !declaringCtx.CanSpend(generator.costCurrency.Id, cost))
                throw new InvalidOperationException($"Buy: generator '{generator.Id}' is not currently buyable - ask CanBuy first.");

            declaring.generatorCounts.TryGetValue(generator.Id, out var owned);
            declaringCtx.Spend(generator.costCurrency.Id, cost);
            declaring.generatorCounts[generator.Id] = owned + count;
        }

        public static void Buy(GameContext ctx, UpgradeDefinition upgrade)
        {
            var declaring = Producer.DeclaringScope<ScopeState>(ctx.Scope, upgrade);
            var declaringCtx = ctx.Rebase(declaring);
            if (!upgrade.IsOffered(declaringCtx) || declaring.purchasedUpgrades.Contains(upgrade.Id)
                || !declaringCtx.CanSpend(upgrade.costCurrency.Id, upgrade.cost)
                || ActionList.Refuses(upgrade.actions, declaringCtx) != null)
                throw new InvalidOperationException($"Buy: upgrade '{upgrade.Id}' is not currently buyable - ask CanBuy first.");

            declaringCtx.Spend(upgrade.costCurrency.Id, upgrade.cost);

            // Latch before payload: the effects are live for anything the actions
            // read. A payload that clears the latch's own scope re-arms the
            // upgrade for another purchase; no load-time check refuses that
            // shape, so the content's walkthrough is what would show it.
            declaring.purchasedUpgrades.Add(upgrade.Id);
            ActionList.Run(upgrade.actions, declaringCtx);
        }

        public static bool TryBuy(GameContext ctx, GeneratorDefinition generator, int count)
        {
            if (!CanBuy(ctx, generator, count))
                return false;
            Buy(ctx, generator, count);
            return true;
        }

        public static bool TryBuy(GameContext ctx, UpgradeDefinition upgrade)
        {
            if (!CanBuy(ctx, upgrade))
                return false;
            Buy(ctx, upgrade);
            return true;
        }

        // The cost of count units from the current owned count: the geometric
        // series, as the unit cost times a factor (12.2). Runtime backstop on
        // the cost curve: validation refuses a nonpositive baseCost and a
        // nonpositive growth, but that pass is dev-only, and generator
        // purchases REPEAT - a free one is an unbounded rate printer.
        public static BigNumber CostOf(GeneratorDefinition generator, GameContext declaringCtx, int count)
        {
            if (count < 1)
                throw new InvalidOperationException(
                    $"CostOf: generator '{generator.Id}' asked for count {count}; a purchase is at least one unit.");

            declaringCtx.Scope.generatorCounts.TryGetValue(generator.Id, out var owned);
            var unit = generator.CostAt(owned);
            if (unit <= BigNumber.Zero)
                throw new InvalidOperationException(
                    $"Generator '{generator.Id}' computed cost {unit} at owned={owned}.");

            // The factor is its own quotient, multiplied onto the unit cost
            // afterwards: at count 1 it is x / x, exactly 1 in IEEE, so a single
            // buy costs the unit cost bit for bit. Growth 1 is authorable -
            // validation refuses only a nonpositive growth - and there the
            // closed form is 0 / 0, so the count IS the factor.
            var factor = generator.growth == BigNumber.One
                ? (BigNumber)count
                : (BigNumber.Pow(generator.growth, count) - BigNumber.One) / (generator.growth - BigNumber.One);
            return unit * factor;
        }

        // M, the largest count the gate and the balance allow at the declaring
        // scope, or zero (12.2). Doubling then bisection over CostOf, with no
        // logarithm: every probe IS the affordability check, so there is no
        // closed-form estimate left needing a fix-up against the last digit.
        public static int MaxAffordable(GameContext ctx, GeneratorDefinition generator)
        {
            var declaring = Producer.DeclaringScope<ScopeState>(ctx.Scope, generator);
            var declaringCtx = ctx.Rebase(declaring);
            if (!generator.IsAvailable(declaringCtx))
                return 0;

            declaring.generatorCounts.TryGetValue(generator.Id, out var owned);

            // owned + count has to fit the count field, so the bracket stops at
            // the headroom left in that int rather than at int.MaxValue.
            var cap = int.MaxValue - owned;

            bool Affordable(int n) =>
                declaringCtx.CanSpend(generator.costCurrency.Id, CostOf(generator, declaringCtx, n));

            if (cap < 1 || !Affordable(1))
                return 0;

            // The invariant through both loops: lo is a count Affordable was
            // evaluated true for, and the answer is always lo. So a non-monotone
            // last bit in the power can hide a larger affordable count, never
            // offer a count the command would refuse on its own arithmetic.
            var lo = 1;
            var hi = 0;
            for (var probe = 2L; ; probe *= 2)
            {
                var n = (int)Math.Min(probe, cap);
                if (!Affordable(n))
                {
                    hi = n;
                    break;
                }
                lo = n;
                if (n == cap)
                    return cap;
            }

            // hi is the first unaffordable count above lo; the bracket closes
            // on lo, which is the largest count that was found affordable.
            while (hi - lo > 1)
            {
                var mid = lo + (hi - lo) / 2;
                if (Affordable(mid))
                    lo = mid;
                else
                    hi = mid;
            }
            return lo;
        }
    }
}
