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
        public readonly IDefinitionSource Defs;
        public readonly DateTime NowUtc;

        public GameContext(ScopeState scope, IDefinitionSource defs, DateTime nowUtc)
        {
            Scope = scope;
            Defs = defs;
            NowUtc = nowUtc;
        }

        // A nested invocation runs in the owning object's scope (design doc 12.4).
        public GameContext Rebase(ScopeState scope) => new GameContext(scope, Defs, NowUtc);

        // ---- reads: chain walk outward from the acting scope ----

        // A currency has one home; the holder is the scope whose dictionary
        // carries the key. Absent everywhere = 0 (undeclared reads are a load-time
        // validation concern, not a runtime branch).
        public BigNumber GetBalance(string currencyId)
        {
            for (var node = Scope; node != null; node = node.Parent)
                if (node.balances.TryGetValue(currencyId, out var value))
                    return value;
            return BigNumber.Zero;
        }

        public BigNumber GetEarnedTotal(string currencyId)
        {
            for (var node = Scope; node != null; node = node.Parent)
                if (node.earnedTotals.TryGetValue(currencyId, out var value))
                    return value;
            return BigNumber.Zero;
        }

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
        // A deposit is a grant; spending is TrySpend below, which moves the
        // balance alone.
        public void Deposit(string currencyId, BigNumber amount)
        {
            for (var node = Scope; node != null; node = node.Parent)
            {
                if (node.balances.ContainsKey(currencyId))
                {
                    node.balances[currencyId] += amount;
                    node.earnedTotals[currencyId] += amount;
                    return;
                }
            }
            Debug.LogError($"Deposit: no scope on the chain from '{Scope.ScopeId}' holds currency '{currencyId}'.");
        }

        // Decrements at the currency's home iff the balance covers the amount.
        // NEVER touches earnedTotals: spending is not earning, and section 2's
        // strobe-proofing - a threshold met once stays met - stands on that.
        public bool TrySpend(string currencyId, BigNumber amount)
        {
            // A negative amount would pass the affordability check and then ADD
            // through the subtraction, minting currency. Refused before anything
            // is located or touched; zero stays legal, since cut_demo costs 0.
            if (amount < BigNumber.Zero)
            {
                Debug.LogError($"TrySpend: refused a negative amount ({amount}) for currency '{currencyId}'.");
                return false;
            }

            for (var node = Scope; node != null; node = node.Parent)
            {
                if (!node.balances.TryGetValue(currencyId, out var balance))
                    continue;
                if (balance < amount)
                    return false;
                node.balances[currencyId] = balance - amount;
                return true;
            }
            Debug.LogError($"TrySpend: no scope on the chain from '{Scope.ScopeId}' holds currency '{currencyId}'.");
            return false;
        }

        // Writes to the flag's declared home (design doc 12.3). Setting a flag no
        // scope on the chain declares is a load-time error; at runtime it no-ops
        // loudly rather than inventing a home.
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
            Debug.LogError($"SetFlag: no scope on the chain from '{Scope.ScopeId}' declares flag '{flagId}'.");
        }
    }
}
