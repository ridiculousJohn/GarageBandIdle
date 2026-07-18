using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Holds balances for whatever CurrencyDefinition assets exist, keyed by
    // currency id. Knows nothing about specific currencies or groups; behavior
    // is driven by group flags, never by named ids.
    public class CurrencyManager
    {
        private readonly Dictionary<string, CurrencyDefinition> _definitions = new();
        private readonly Dictionary<string, CurrencyGroupDefinition> _groups = new();
        private readonly Dictionary<string, BigNumber> _balances = new();

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
            }
        }

        public CurrencyDefinition GetDefinition(string id)
        {
            if (_definitions.TryGetValue(id, out var definition))
                return definition;

            Debug.LogError($"CurrencyManager: unknown currency id '{id}'. No CurrencyDefinition asset with that id was loaded.");
            return null;
        }

        public BigNumber Get(string id)
        {
            if (_balances.TryGetValue(id, out var balance))
                return balance;

            Debug.LogError($"CurrencyManager: Get on unknown currency id '{id}'. Returning zero.");
            return BigNumber.Zero;
        }

        public void Add(string id, BigNumber amount) => Set(id, Get(id) + amount);

        public void Set(string id, BigNumber value)
        {
            if (!_balances.ContainsKey(id))
            {
                Debug.LogError($"CurrencyManager: Set on unknown currency id '{id}'. Ignoring.");
                return;
            }

            _balances[id] = value;
            BalanceChanged?.Invoke(id, value);
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

                if (group.ResetsOnAlbumRelease)
                    Set(definition.Id, definition.StartingValue);
            }
        }
    }
}
