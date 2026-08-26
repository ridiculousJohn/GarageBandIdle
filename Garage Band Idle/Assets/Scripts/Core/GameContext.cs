using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The explicit scope every condition evaluates in and every action list
    // executes in (design doc 12.4) - never inferred, never inherited from a
    // caller; nested invocations rebase. Reads walk the chain outward from the
    // acting scope; sibling scopes are unreachable by construction.
    public class GameContext
    {
        public readonly ScopeState Scope;
        public readonly DateTime NowUtc;

        // The claim's circumstance (design doc 12.5/12.9): the idle claim builds
        // its contexts with this set, everything else builds without, and
        // nothing mutates it - a rebase carries it, so one gather runs whole
        // under one circumstance. Read by the IdleAccumulation condition kind.
        public readonly bool IdleAccumulation;

        // No definition source: every reference an authored object holds is the
        // object itself, and every id a FACT holds resolves by walking this
        // scope outward. Nothing needs a catalogue.
        public GameContext(ScopeState scope, DateTime nowUtc, bool idleAccumulation = false)
        {
            Scope = scope;
            NowUtc = nowUtc;
            IdleAccumulation = idleAccumulation;
        }

        // A nested invocation runs in the owning object's scope (design doc 12.4).
        public GameContext Rebase(ScopeState scope) => new GameContext(scope, NowUtc, IdleAccumulation);

        // ---- reads: chain walk outward from the acting scope ----

        // The currency's home on this chain. Absent means the content addresses
        // a currency it cannot reach - refused at load, so reaching it here is a
        // bug and nothing sensible follows from continuing.
        private ScopeState HomeOf(string currencyId)
        {
            for (var node = Scope; node != null; node = node.Parent)
                if (node.balances.ContainsKey(currencyId))
                    return node;
            throw new InvalidOperationException(
                $"No scope on the chain from '{Scope.ScopeId}' holds currency '{currencyId}'.");
        }

        // A currency has one home; the holder is the scope whose dictionary
        // carries the key. Every declared currency gets its keys when the tree is
        // built, so a missing key means the content read a currency off this
        // chain - a load-time error, and answering 0 would be a wrong number the
        // caller cannot tell from a real one.
        public BigNumber GetBalance(string currencyId) => HomeOf(currencyId).balances[currencyId];

        public BigNumber GetEarnedTotal(string currencyId) => HomeOf(currencyId).earnedTotals[currencyId];

        public int GetOwnedCount(string generatorId)
        {
            for (var node = Scope; node != null; node = node.Parent)
                if (node.generatorCounts.TryGetValue(generatorId, out var count))
                    return count;
            return 0;
        }

        // Set anywhere on the chain = set (design doc 12.3).
        public bool IsFlagSet(string flagId)
        {
            for (var node = Scope; node != null; node = node.Parent)
                if (node.flags.Contains(flagId))
                    return true;
            return false;
        }

        public bool IsUpgradePurchased(string upgradeId)
        {
            for (var node = Scope; node != null; node = node.Parent)
                if (node.purchasedUpgrades.Contains(upgradeId))
                    return true;
            return false;
        }

        public BigNumber GetBarProgress(string barId)
        {
            for (var node = Scope; node != null; node = node.Parent)
                if (node.barProgress.TryGetValue(barId, out var value))
                    return value;
            return BigNumber.Zero;
        }

        // ---- writes: each lands at the fact's home ----

        // Deposits at the currency's home: balance and earned total together.
        // A deposit is a grant; spending moves the balance alone. A negative
        // amount would drive an earned total DOWNWARD, and section 2's
        // strobe-proofing - a threshold met once stays met - stands on that
        // never happening; authored negatives are refused at load.
        public void Deposit(string currencyId, BigNumber amount)
        {
            if (amount < BigNumber.Zero)
                throw new InvalidOperationException(
                    $"Deposit of {amount} for currency '{currencyId}': a grant is never negative.");
            var home = HomeOf(currencyId);
            home.balances[currencyId] += amount;
            home.earnedTotals[currencyId] += amount;
        }

        // Whether the balance covers the amount right now - a question about
        // mutable state, which is the only kind a bool answers here. A negative
        // amount would pass this and then ADD through the subtraction, minting
        // currency, so it throws rather than reporting false; zero stays legal,
        // since cut_demo costs 0.
        public bool CanSpend(string currencyId, BigNumber amount)
        {
            if (amount < BigNumber.Zero)
                throw new InvalidOperationException(
                    $"CanSpend of {amount} for currency '{currencyId}': a cost is never negative.");
            return HomeOf(currencyId).balances[currencyId] >= amount;
        }

        // Decrements at the currency's home. NEVER touches earnedTotals:
        // spending is not earning, and section 2's strobe-proofing stands on
        // that. Callers ask CanSpend first; calling this when it answers false
        // is a caller bug.
        public void Spend(string currencyId, BigNumber amount)
        {
            if (!CanSpend(currencyId, amount))
                throw new InvalidOperationException(
                    $"Spend of {amount} for currency '{currencyId}': the balance does not cover it - ask CanSpend first.");
            var home = HomeOf(currencyId);
            home.balances[currencyId] -= amount;
        }

        // Convenience over the two: the caller that does not need the reason.
        public bool TrySpend(string currencyId, BigNumber amount)
        {
            if (!CanSpend(currencyId, amount))
                return false;
            Spend(currencyId, amount);
            return true;
        }

        // Writes to the flag's declared home (design doc 12.3). A flag no scope
        // on the chain declares is refused at load; reaching it here means the
        // content or the code is broken, and a silently skipped write would be
        // saved as if it had happened.
        public void SetFlag(string flagId)
        {
            for (var node = Scope; node != null; node = node.Parent)
            {
                if (node.Definition.DeclaresFlag(flagId))
                {
                    node.flags.Add(flagId);
                    return;
                }
            }
            throw new InvalidOperationException(
                $"No scope on the chain from '{Scope.ScopeId}' declares flag '{flagId}'.");
        }
    }
}
