using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Holds balances for whatever CurrencyDefinition definitions it is given,
    // keyed by currency id. Knows nothing about specific currencies or groups;
    // behavior is driven by group flags, never by named ids.
    //
    // ONE pool, with no scope concept inside it (design doc section 12, rule
    // 12): a currency's lifetime comes from who created the instance holding
    // it, never from a flag read here. The startup pool and each economy
    // context's pool are the same class with different owners - which is what
    // makes an event sandbox or a replay economy a second instantiation rather
    // than a second code path. Consumers reach one or several pools through
    // ICurrencies (see CurrencyRouter) and cannot tell which.
    public class CurrencyManager : ICurrencies
    {
        private readonly Dictionary<string, CurrencyDefinition> _definitions = new();
        private readonly Dictionary<string, CurrencyGroupDefinition> _groups = new();
        private readonly Dictionary<string, BigNumber> _balances = new();
        // total earned per currency, backing the earned-total gates. Deliberately
        // NOT named for a lifetime: how long it lives is the currency GROUP's
        // call, exactly as the balance's is. For a group that resets on release
        // this is the run's earnings; for a permanent group (Records) it is the
        // lifetime total. One field, one place the scope is declared.
        private readonly Dictionary<string, BigNumber> _earned = new();

        // fires on every balance change with the currency id and new balance;
        // UI listens here, nothing polls
        public event Action<string, BigNumber> BalanceChanged;

        public IReadOnlyCollection<CurrencyDefinition> Definitions => _definitions.Values;

        // Builds balances from whatever definitions exist. Content errors (duplicate
        // or empty ids, groupIds that resolve to no loaded group) are logged at load
        // so they surface immediately instead of as silent zeros mid-run.
        public CurrencyManager(IEnumerable<CurrencyGroupDefinition> groupDefinitions, IEnumerable<CurrencyDefinition> currencyDefinitions)
        {
            foreach (var group in groupDefinitions)
            {
                if (string.IsNullOrEmpty(group.Id))
                {
                    Debug.LogError($"CurrencyManager: CurrencyGroupDefinition asset '{group.name}' has an empty id. Skipping it.");
                    continue;
                }
                if (_groups.TryGetValue(group.Id, out var existing))
                {
                    Debug.LogError($"CurrencyManager: duplicate currency group id '{group.Id}' on assets '{group.name}' and '{existing.name}'. Keeping '{existing.name}'.");
                    continue;
                }
                _groups.Add(group.Id, group);
            }

            foreach (var definition in currencyDefinitions)
            {
                if (string.IsNullOrEmpty(definition.Id))
                {
                    Debug.LogError($"CurrencyManager: CurrencyDefinition asset '{definition.name}' has an empty id. Skipping it.");
                    continue;
                }
                if (_definitions.TryGetValue(definition.Id, out var existing))
                {
                    Debug.LogError($"CurrencyManager: duplicate currency id '{definition.Id}' on assets '{definition.name}' and '{existing.name}'. Keeping '{existing.name}'.");
                    continue;
                }

                // register the currency even on a bad group reference so balances still
                // work; the currency just won't participate in group-driven resets
                if (string.IsNullOrEmpty(definition.GroupId) || !_groups.ContainsKey(definition.GroupId))
                    Debug.LogError($"CurrencyManager: currency '{definition.Id}' references unknown group id '{definition.GroupId}'.");

                _definitions.Add(definition.Id, definition);
                _balances.Add(definition.Id, definition.StartingValue);
                _earned.Add(definition.Id, BigNumber.Zero);
            }
        }

        // Whether this pool holds the currency. Silent, unlike GetDefinition:
        // the caller asking is deciding WHICH pool owns an id (CurrencyRouter's
        // ownership map, boot's shadowing check), so a miss is the expected
        // answer for every id another pool owns.
        public bool Contains(string id) => !string.IsNullOrEmpty(id) && _definitions.ContainsKey(id);

        // The group a currency is filed in, or null if the currency or its
        // group reference does not resolve (both already reported at load).
        // Placement and reset behavior are read from here, so validation asks
        // one question of the asset rather than re-deriving the lookup.
        public CurrencyGroupDefinition GetGroup(string currencyId)
            => !string.IsNullOrEmpty(currencyId)
               && _definitions.TryGetValue(currencyId, out var definition)
               && _groups.TryGetValue(definition.GroupId ?? "", out var group)
                ? group
                : null;

        public CurrencyDefinition GetDefinition(string id)
        {
            if (_definitions.TryGetValue(id, out var definition))
                return definition;

            Debug.LogError($"CurrencyManager: unknown currency id '{id}'. No CurrencyDefinition asset with that id was loaded.");
            return null;
        }

        public BigNumber Get(string id)
        {
            // null/empty guards on every id entry point: content mistakes must
            // report loudly, never throw out of a Dictionary
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("CurrencyManager: Get with a null or empty currency id. Returning zero.");
                return BigNumber.Zero;
            }

            if (_balances.TryGetValue(id, out var balance))
                return balance;

            Debug.LogError($"CurrencyManager: Get on unknown currency id '{id}'. Returning zero.");
            return BigNumber.Zero;
        }

        public void Add(string id, BigNumber amount)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("CurrencyManager: Add with a null or empty currency id. Ignoring.");
                return;
            }

            // positive additions accrue into the earned stat backing
            // earned-total unlock gates; spends (negative adds) never lower it
            if (amount > BigNumber.Zero && _earned.ContainsKey(id))
                _earned[id] += amount;

            Set(id, Get(id) + amount);
        }

        // Total earned over the scope the currency's group declares (starting
        // value excluded); used by earned-total gates. Spending never lowers it,
        // so a gate on it holds for as long as that scope does.
        public BigNumber GetEarned(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("CurrencyManager: GetEarned with a null or empty currency id. Returning zero.");
                return BigNumber.Zero;
            }

            if (_earned.TryGetValue(id, out var earned))
                return earned;

            Debug.LogError($"CurrencyManager: GetEarned on unknown currency id '{id}'. Returning zero.");
            return BigNumber.Zero;
        }

        public void Set(string id, BigNumber value)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("CurrencyManager: Set with a null or empty currency id. Ignoring.");
                return;
            }

            if (!_balances.ContainsKey(id))
            {
                Debug.LogError($"CurrencyManager: Set on unknown currency id '{id}'. Ignoring.");
                return;
            }

            _balances[id] = value;
            BalanceChanged?.Invoke(id, value);
        }

        // Restore (save load, event-sandbox seeding): assigns BOTH halves of a
        // currency's state absolutely. The earned total is a separate fact from
        // the balance and there was no way to write it - Set moves the balance
        // alone - which made every earned-total gate unrestorable. That is not an
        // abstract gap: cumulative Records IS this field for the Records
        // currency, so a load that restored the balance alone would come back
        // with the capstone re-locked and the permanent income multiplier at 1.0,
        // both reading zero while the number on screen looked right.
        //
        // Absolute, not additive, because restore is REPLACEMENT: a currency
        // absent from a snapshot is restored to its starting value by the caller,
        // never left holding whatever this pool happened to have.
        //
        // notify: false is for a context-wide restore, which publishes one settled
        // set of notifications after projection instead of a storm of partial ones
        // (see Scope.Restore). The default keeps every existing caller's
        // behavior.
        public void Restore(string id, BigNumber balance, BigNumber earnedTotal, bool notify = true)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("CurrencyManager: Restore with a null or empty currency id. Ignoring.");
                return;
            }

            if (!_balances.ContainsKey(id))
            {
                Debug.LogError($"CurrencyManager: Restore on unknown currency id '{id}'. Ignoring - stale saved state naming a currency this pool does not hold.");
                return;
            }

            // Neither half can legitimately be negative: a balance below zero puts
            // the currency in debt and the release reset would preserve it, and an
            // earned total is monotonic by construction, so a negative one could
            // only come from corrupted state. Clamped rather than refused, because
            // refusing one currency mid-restore leaves the pool half-replaced.
            if (balance < BigNumber.Zero)
            {
                Debug.LogError($"CurrencyManager: Restore of '{id}' has a negative balance. Clamping to zero.");
                balance = BigNumber.Zero;
            }
            if (earnedTotal < BigNumber.Zero)
            {
                Debug.LogError($"CurrencyManager: Restore of '{id}' has a negative earned total. Clamping to zero.");
                earnedTotal = BigNumber.Zero;
            }

            // earned first, for the same reason the release reset writes it first:
            // the balance write is what publishes, so a subscriber must never
            // observe the new balance beside the old earned total (state, then
            // notify)
            _earned[id] = earnedTotal;
            _balances[id] = balance;

            if (notify)
                BalanceChanged?.Invoke(id, balance);
        }

        // Every currency this pool holds, both halves. The capture half of the
        // contract, and the reason it lives HERE rather than on any one owner: a
        // pool is a pool, so the chapter's and the permanent one are captured
        // through the same call and only the caller decides which it owns. The
        // permanent block (Records, Roadies) is captured by whoever created that
        // pool - exactly once - and an economy context captures its own.
        //
        // Total rather than sparse, unlike the per-system captures: a pool's
        // currency set is fixed at construction, so there is no "absent means
        // default" to encode, and RestoreAll below is a straight replacement.
        public IReadOnlyDictionary<string, CurrencyState> CaptureAll()
        {
            var state = new Dictionary<string, CurrencyState>(_balances.Count);
            foreach (var entry in _balances)
                state.Add(entry.Key, new CurrencyState(entry.Value, GetEarned(entry.Key)));
            return state;
        }

        // REPLACEMENT over everything this pool holds: a currency the snapshot names
        // takes that state, and one it omits returns to its starting value with a
        // zeroed earned total. The walk is over the POOL rather than over the
        // snapshot's keys, which is what makes an empty snapshot a legitimate "new
        // run" instead of a no-op - the same reason the generator and bar restores
        // walk their own content.
        //
        // Every currency settles before any notification fires, and notify: false
        // defers the whole set to a context-wide restore (state, then notify).
        public void RestoreAll(IReadOnlyDictionary<string, CurrencyState> state, bool notify = true)
        {
            if (state == null)
            {
                Debug.LogError("CurrencyManager: RestoreAll with no saved state. Ignoring - returning every balance to its starting value was more likely a missing snapshot than an authored empty one.");
                return;
            }

            // Every currency settles SILENTLY first, whatever the caller asked for.
            // Passing notify straight through published after the first currency
            // while the rest still held pre-restore state - a subscriber could read
            // Records restored beside Roadies not yet touched, which is exactly the
            // half-applied observation this method's contract denies.
            foreach (var definition in _definitions.Values)
            {
                var restored = state.TryGetValue(definition.Id, out var saved)
                    ? saved
                    : new CurrencyState(definition.StartingValue, BigNumber.Zero);
                Restore(definition.Id, restored.Balance, restored.EarnedTotal, notify: false);
            }

            // A key this pool does not own is stale saved state or a snapshot handed
            // to the wrong pool - the permanent block restored into a chapter's, say.
            // Silently dropping it loses the balance AND the evidence, so it reports
            // after the replacement rather than interrupting it.
            foreach (var entry in state)
            {
                if (!_balances.ContainsKey(entry.Key))
                    Debug.LogError($"CurrencyManager: RestoreAll was given state for currency '{entry.Key}', which this pool does not hold. Ignoring it - stale saved state, or a snapshot restored into the wrong pool.");
            }

            if (notify)
                RepublishAll();
        }

        // Re-announces every balance this pool holds. The notification half of a
        // silent restore: BalanceChanged carries the CURRENT value rather than a
        // delta, so replaying it for everything is a full refresh with no
        // double-counting - and after a restore everything did move, which is why
        // the replay is total rather than a computed set of changes.
        public void RepublishAll()
        {
            foreach (var entry in _balances)
                BalanceChanged?.Invoke(entry.Key, entry.Value);
        }

        // Startup check for any system holding a currency id (generators, UI): a
        // reference to a currency that has no definition asset gets reported at load
        // with the referencing context named, not mid-run.
        public bool ValidateReference(string id, string context)
        {
            if (!string.IsNullOrEmpty(id) && _definitions.ContainsKey(id))
                return true;

            Debug.LogError($"CurrencyManager: {context} references currency id '{id}', which resolves to no CurrencyDefinition asset.");
            return false;
        }

        // Whether a currency's group opts into the album-release reset. A system
        // whose correctness depends on a currency surviving a release can assert
        // on the group flag instead of trusting the asset to be filed correctly.
        // An unresolvable group answers false - that broken reference is already
        // reported at load, and it is not this question's job to repeat it.
        public bool ResetsOnAlbumRelease(string currencyId)
            => !string.IsNullOrEmpty(currencyId)
               && _definitions.TryGetValue(currencyId, out var definition)
               && _groups.TryGetValue(definition.GroupId, out var group)
               && group.ResetsOnAlbumRelease;

        // Album release (prestige) reset: every currency whose group opts in returns
        // to its starting value. Driven purely by the group flag, so new currencies
        // and new groups participate with no code changes.
        public void ResetCurrenciesOnAlbumRelease()
        {
            foreach (var definition in _definitions.Values)
            {
                // bad group references were already reported at load; skip quietly here
                if (!_groups.TryGetValue(definition.GroupId, out var group))
                    continue;

                if (!group.ResetsOnAlbumRelease)
                    continue;

                // The earned total is the same fact as the balance, measured
                // differently, so it resets on the same group decision. Leaving
                // it standing is what kept earned-total gates (the band section,
                // the practice amp) met forever after the first demo.
                //
                // Cleared BEFORE the balance, because Set publishes: a
                // BalanceChanged subscriber must never observe this currency
                // with its balance back at the start and its earned total still
                // standing (state, then notify).
                _earned[definition.Id] = BigNumber.Zero;
                Set(definition.Id, definition.StartingValue);
            }
        }
    }
}
