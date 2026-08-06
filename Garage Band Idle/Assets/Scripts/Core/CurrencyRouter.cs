using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Resolves a currency id to the pool that owns it, over an economy's own
    // pool plus the startup pool holding the global currencies (design doc
    // section 12, rule 12). The map is built once, at construction, so no call
    // site ever chooses a pool: a system asks for 'records' and a system asks
    // for 'cash' through the identical surface, and which instance answers was
    // decided here by placement data rather than there by a currency name.
    //
    // It is IDisposable because it aggregates both pools' BalanceChanged into
    // one event. The startup pool outlives every context, so a discarded
    // router that kept listening would keep a dead economy's subscribers alive
    // and deliver them balance changes for a chapter nobody is playing - the
    // same failure ConditionContext.Dispose exists to prevent, one layer down.
    //
    // Shadowing is refused rather than resolved. An id in both pools has two
    // balances and every read would silently pick whichever this class happened
    // to check first, which is a coin flip decided by code order: a spend could
    // charge one balance while the UI reads the other. Boot validation reports
    // it and the router keeps the local pool, so the failure is loud and the
    // chapter still plays.
    public class CurrencyRouter : ICurrencies, IDisposable
    {
        private readonly Dictionary<string, CurrencyManager> _owners = new();
        private readonly CurrencyManager _local;
        private readonly CurrencyManager _global;

        // one aggregated signal for every pool reachable here, so a consumer
        // holds one subscription no matter how many pools back it
        public event Action<string, BigNumber> BalanceChanged;

        // the pools behind the map, for the operations that are explicitly
        // about one of them (a release resets the local pool, never the global)
        public CurrencyManager Local => _local;
        public CurrencyManager Global => _global;

        public CurrencyRouter(CurrencyManager local, CurrencyManager global)
        {
            _local = local;
            _global = global;

            // the global pool is claimed first so that a shadowed id reports
            // against the local one, naming the pool the chapter authored and
            // can fix, rather than against the startup pool it collided with
            Claim(_global);
            Claim(_local);

            if (_local != null)
                _local.BalanceChanged += HandleBalanceChanged;
            if (_global != null)
                _global.BalanceChanged += HandleBalanceChanged;
        }

        public void Dispose()
        {
            if (_local != null)
                _local.BalanceChanged -= HandleBalanceChanged;
            if (_global != null)
                _global.BalanceChanged -= HandleBalanceChanged;
        }

        // Which pool owns the id, or null if neither does. Public because the
        // shadowing and roster checks ask it directly; nothing routes on the
        // answer outside this class.
        public CurrencyManager OwnerOf(string id)
            => !string.IsNullOrEmpty(id) && _owners.TryGetValue(id, out var owner) ? owner : null;

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

                // last claim wins, which is why Claim(_local) runs second: a
                // shadowed id resolves to the chapter's own balance, the one
                // its producers and bars were authored against. Boot validation
                // is what reports the collision; silently preferring a pool is
                // not a fix, it just makes the choice deterministic.
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
