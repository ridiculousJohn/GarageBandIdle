using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime home of a chapter's upgrades. Implements the general content-unlock
    // mechanism (design doc sections 2 and 4): a contentUnlock upgrade whose gate
    // is met applies its payload — setFlag latches the named flag in the single
    // reveal registry, which sections and other gates observe. Buff upgrades
    // (purchase, tap/output payloads) arrive in the buff slice; their definitions
    // load and validate but are never auto-applied here. Gate conditions are
    // validated by the boot validation pass (ContentValidator), not here.
    public class UpgradeSystem
    {
        private readonly List<Upgrade> _upgrades = new();
        private readonly Dictionary<string, Upgrade> _byId = new();
        private readonly CurrencyManager _currencies;
        private readonly UpgradePayloadContext _payloadContext;

        // fires once per upgrade when its payload is applied
        public event Action<Upgrade> UpgradeApplied;

        public IReadOnlyList<Upgrade> All => _upgrades;

        public UpgradeSystem(IEnumerable<UpgradeDefinition> definitions, CurrencyManager currencies, FlagSystem flags)
        {
            _currencies = currencies;
            _payloadContext = new UpgradePayloadContext(flags);

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    Debug.LogError("UpgradeSystem: chapter upgrade list contains a null entry. Skipping it.");
                    continue;
                }
                if (string.IsNullOrEmpty(definition.Id))
                {
                    Debug.LogError($"UpgradeSystem: UpgradeDefinition asset '{definition.name}' has an empty id. Skipping it.");
                    continue;
                }
                if (_byId.TryGetValue(definition.Id, out var existing))
                {
                    Debug.LogError($"UpgradeSystem: duplicate upgrade id '{definition.Id}' on assets '{definition.name}' and '{existing.Definition.name}'. Keeping '{existing.Definition.name}'.");
                    continue;
                }

                if (!string.IsNullOrEmpty(definition.CostCurrencyId))
                    _currencies.ValidateReference(definition.CostCurrencyId, $"Upgrade '{definition.Id}' (cost)");
                ValidatePayload(definition);

                var upgrade = new Upgrade(definition);
                _upgrades.Add(upgrade);
                _byId.Add(definition.Id, upgrade);
            }
        }

        public Upgrade Get(string id)
        {
            if (_byId.TryGetValue(id, out var upgrade))
                return upgrade;

            Debug.LogError($"UpgradeSystem: unknown upgrade id '{id}'.");
            return null;
        }

        // applies any content unlock whose gate now holds; called on tick and
        // after purchases (an ownedCount gate can trip mid-tick)
        public void EvaluateContentUnlocks(ConditionContext context)
        {
            foreach (var upgrade in _upgrades)
            {
                if (upgrade.Applied)
                    continue;
                if (upgrade.Definition.Type != UpgradeType.ContentUnlock)
                    continue;
                if (!ConditionEvaluator.IsMet(upgrade.Definition.Gate, context))
                    continue;

                Apply(upgrade);
            }
        }

        private void Apply(Upgrade upgrade)
        {
            var payload = upgrade.Definition.Payload;
            if (payload == null)
            {
                // marked applied anyway so a content mistake reports once, not per tick
                Debug.LogError($"UpgradeSystem: upgrade '{upgrade.Definition.Id}' has no payload. Nothing to apply.");
            }
            else
            {
                payload.Apply(_payloadContext);
            }

            upgrade.MarkApplied();
            UpgradeApplied?.Invoke(upgrade);
        }

        private void ValidatePayload(UpgradeDefinition definition)
        {
            if (definition.Type != UpgradeType.ContentUnlock)
                return;

            // content unlocks auto-apply on gate met; a price needs the purchase
            // flow that arrives with the buff-upgrades slice
            if (definition.CostAmount > 0)
                Debug.LogError($"UpgradeSystem: content unlock '{definition.Id}' has a non-zero cost, but content unlocks are applied automatically in this slice. Its cost will be ignored.");
        }
    }
}
