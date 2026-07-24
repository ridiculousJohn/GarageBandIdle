using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime home of a chapter's upgrades. Implements the general content-unlock
    // mechanism (design doc sections 2 and 4): a contentUnlock upgrade whose gate
    // is met applies its payload - setFlag latches the named flag in the single
    // reveal registry, which sections and other gates observe. Buffs are never
    // auto-applied: they are bought through TryBuy, which charges the declared
    // cost currency and grants the payload. Gate conditions are validated by the
    // boot validation pass (ContentValidator), not here.
    public class UpgradeSystem
    {
        private readonly List<Upgrade> _upgrades = new();
        private readonly Dictionary<string, Upgrade> _byId = new();
        private readonly CurrencyManager _currencies;
        private readonly UpgradePayloadContext _payloadContext;

        // fires once per upgrade when its payload is applied
        public event Action<Upgrade> UpgradeApplied;

        public IReadOnlyList<Upgrade> All => _upgrades;

        public UpgradeSystem(IEnumerable<UpgradeDefinition> definitions, CurrencyManager currencies,
            FlagSystem flags, ModifierSystem modifiers)
        {
            _currencies = currencies;
            _payloadContext = new UpgradePayloadContext(flags, modifiers);

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

        // Whether the UI should offer this buff: its gate holds and it has not
        // been bought yet. Affordability is deliberately separate so a row can
        // show a priced, disabled button rather than vanishing.
        public bool IsAvailable(Upgrade upgrade, ConditionContext context)
            => upgrade != null
               && upgrade.Definition.Type == UpgradeType.Buff
               && !upgrade.Applied
               && ConditionEvaluator.IsMet(upgrade.Definition.Gate, context);

        public bool CanAfford(Upgrade upgrade)
            => upgrade != null
               && upgrade.Definition.CostAmount > 0
               && !string.IsNullOrEmpty(upgrade.Definition.CostCurrencyId)
               && _currencies.Get(upgrade.Definition.CostCurrencyId) >= (BigNumber)upgrade.Definition.CostAmount;

        // Buys one buff: charges the declared cost currency and grants the
        // payload. Every refusal is silent except the ones that mean broken
        // content, because the UI calls this on a button press.
        public bool TryBuy(Upgrade upgrade, ConditionContext context)
        {
            if (upgrade == null)
                return false;

            // content unlocks are free and apply on their gate; buying one is a
            // caller mistake, not a rejected purchase
            if (upgrade.Definition.Type != UpgradeType.Buff)
            {
                Debug.LogError($"UpgradeSystem: TryBuy on '{upgrade.Definition.Id}', which is a {upgrade.Definition.Type} - only buffs are bought.");
                return false;
            }

            if (upgrade.Applied)
                return false;
            if (!ConditionEvaluator.IsMet(upgrade.Definition.Gate, context))
                return false;

            // fail closed on broken content (boot validation reports all three):
            // never charge for a payload that would grant nothing, and never let
            // a missing price or currency become an endless free purchase
            var payload = upgrade.Definition.Payload;
            if (payload == null)
            {
                Debug.LogError($"UpgradeSystem: upgrade '{upgrade.Definition.Id}' has no payload. Refusing the purchase rather than charging for nothing.");
                return false;
            }

            var cost = (BigNumber)upgrade.Definition.CostAmount;
            if (cost <= BigNumber.Zero)
                return false;

            var currencyId = upgrade.Definition.CostCurrencyId;
            if (string.IsNullOrEmpty(currencyId))
                return false;
            if (_currencies.Get(currencyId) < cost)
                return false;

            // state, then notify: the latch and the effect settle before the
            // spend fires BalanceChanged, so no condition evaluator or UI
            // subscriber observes the money gone with the buff not yet granted
            upgrade.MarkApplied();
            payload.Apply(_payloadContext, upgrade.Definition.Scope);
            _currencies.Add(currencyId, -cost);
            UpgradeApplied?.Invoke(upgrade);
            return true;
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
                // the upgrade's declared scope travels with the grant, so the
                // effect's lifetime is never a second declaration
                payload.Apply(_payloadContext, upgrade.Definition.Scope);
            }

            upgrade.MarkApplied();
            UpgradeApplied?.Invoke(upgrade);
        }

        private void ValidatePayload(UpgradeDefinition definition)
        {
            if (definition.Type != UpgradeType.ContentUnlock)
                return;

            // content unlocks apply on their gate and are never bought, so a
            // price on one would never be charged - only buffs go through TryBuy
            if (definition.CostAmount > 0)
                Debug.LogError($"UpgradeSystem: content unlock '{definition.Id}' has a non-zero cost, but content unlocks are applied automatically. Its cost will be ignored.");
        }
    }
}
