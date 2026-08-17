using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Resolves a currency id to the pool that owns it - ResolveCurrency of the
    // three chain resolutions (design doc section 12, rule 12): first owner
    // outward wins, one balance. Built over the ScopeChain's walk, and the map
    // is built once, at construction (a cache of the walk, not a per-read
    // walk), so no call site ever chooses a pool: a system asks for 'records'
    // and a system asks for 'cash' through the identical surface, and which
    // instance answers was decided here by placement rather than there by a
    // currency name. Step 4 must rebuild this cache when enabling or disabling
    // a scope changes what the walk yields.
    //
    // It is IDisposable because it aggregates every pool's BalanceChanged into
    // one event. The outer pools outlive every scope under them, so a
    // discarded router that kept listening would keep a dead scope's
    // subscribers alive and deliver them balance changes for a ladder nobody
    // is playing - the same failure ConditionContext.Dispose exists to
    // prevent, one layer down.
    //
    // Shadowing is refused rather than resolved. An id in two pools has two
    // balances and every read would silently pick whichever this class happened
    // to check first, which is a coin flip decided by code order: a spend could
    // charge one balance while the UI reads the other. ScopeFactory's claim map
    // refuses it at construction and boot validation reports it; the router
    // keeps the innermost claim, so the failure is loud and the scope still
    // plays.
    public class CurrencyRouter : ICurrencies, IDisposable
    {
        private readonly Dictionary<string, CurrencyManager> _owners = new();
        // what the chain's walk yielded at construction, innermost first -
        // routing and subscriptions only, and the part step 4's enablement
        // rebuild legitimately recomputes
        private readonly List<CurrencyManager> _pools = new();
        // the owning scope's own pool, captured OFF the walk: enablement
        // changes what a scope can REACH, never what it OWNS, so a disabled
        // scope's capture and reset still land in its own truth. Step 4's
        // rebuild must leave this alone (and its tests must pin that a
        // disabled scope's Pool is still its own).
        private readonly CurrencyManager _local;

        // one aggregated signal for every pool reachable here, so a consumer
        // holds one subscription no matter how many pools back it
        public event Action<string, BigNumber> BalanceChanged;

        // the owning scope's pool, for the operations that are explicitly
        // about this scope's own truth (a release resets the local pool,
        // never anything outward)
        public CurrencyManager Local => _local;

        public CurrencyRouter(ScopeChain chain)
        {
            _local = chain?.Pool;
            chain?.CollectPools(_pools);

            // claimed outermost first, so on a collision the INNERMOST claim
            // wins deterministically - the pool the colliding scope's own
            // content was authored against. The collision itself was already
            // refused and reported at construction (ScopeFactory) or by boot
            // validation; silently preferring a pool is not a fix, it just
            // keeps the choice deterministic.
            for (var i = _pools.Count - 1; i >= 0; i--)
                Claim(_pools[i]);

            foreach (var pool in _pools)
                pool.BalanceChanged += HandleBalanceChanged;
        }

        public void Dispose()
        {
            foreach (var pool in _pools)
                pool.BalanceChanged -= HandleBalanceChanged;
        }

        // Which pool owns the id, or null if neither does. Public because the
        // shadowing and roster checks ask it directly; nothing routes on the
        // answer outside this class.
        public CurrencyManager OwnerOf(string id)
            => !string.IsNullOrEmpty(id) && _owners.TryGetValue(id, out var owner) ? owner : null;

        public bool Contains(string id) => OwnerOf(id) != null;

        public BigNumber Get(string id) => Route(id, nameof(Get))?.Get(id) ?? BigNumber.Zero;

        public void Add(string id, BigNumber amount) => Route(id, nameof(Add))?.Add(id, amount);

        public void Set(string id, BigNumber value) => Route(id, nameof(Set))?.Set(id, value);

        public BigNumber GetEarned(string id)
            => Route(id, nameof(GetEarned))?.GetEarned(id) ?? BigNumber.Zero;

        public CurrencyDefinition GetDefinition(string id) => Route(id, nameof(GetDefinition))?.GetDefinition(id);

        public bool ValidateReference(string id, string context)
        {
            var owner = OwnerOf(id);
            if (owner != null)
                return owner.ValidateReference(id, context);

            Debug.LogError($"CurrencyRouter: {context} references currency id '{id}', which no reachable pool holds - it is neither in this economy's roster nor a global currency.");
            return false;
        }

        public bool ResetsOnAlbumRelease(string currencyId)
            => OwnerOf(currencyId)?.ResetsOnAlbumRelease(currencyId) ?? false;

        private void Claim(CurrencyManager pool)
        {
            if (pool == null)
                return;

            foreach (var definition in pool.Definitions)
            {
                if (string.IsNullOrEmpty(definition.Id))
                    continue;

                // last claim wins, which is why the constructor claims from the
                // outermost pool inward: a shadowed id resolves to the balance
                // its own scope's producers and bars were authored against
                _owners[definition.Id] = pool;
            }
        }

        // An unroutable id is a content mistake, not a routing outcome, so it
        // reports here once with the operation named rather than degrading into
        // whichever pool's "unknown currency" message came first.
        private CurrencyManager Route(string id, string operation)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError($"CurrencyRouter: {operation} with a null or empty currency id. Ignoring.");
                return null;
            }

            if (_owners.TryGetValue(id, out var owner))
                return owner;

            Debug.LogError($"CurrencyRouter: {operation} on currency id '{id}', which no reachable pool holds. Ignoring.");
            return null;
        }

        private void HandleBalanceChanged(string id, BigNumber balance) => BalanceChanged?.Invoke(id, balance);
    }
}
