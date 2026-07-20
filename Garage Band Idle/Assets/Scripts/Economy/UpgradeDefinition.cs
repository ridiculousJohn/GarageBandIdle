using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One upgrade (design doc section 4). This slice only stores the data;
    // gate evaluation, purchase, and payload application arrive in later slices
    // (content unlocks in slice 3, buffs in slice 5).
    [CreateAssetMenu(
        fileName = "NewUpgrade",
        menuName = "GarageBandIdle/Upgrade")]
    public class UpgradeDefinition : ScriptableObject
    {
        // run-scoped stat buff, re-bought each run
        public const string TypeBuff = "buff";
        // reveals a system/currency/generator; permanent within the chapter
        public const string TypeContentUnlock = "contentUnlock";

        public const string ScopeRun = "run";
        public const string ScopePermanentInChapter = "permanentInChapter";

        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("One of the Type* constants: buff | contentUnlock.")]
        private string _type;

        [SerializeField]
        [Tooltip("One of the Scope* constants; reset logic acts on this field, never on the id.")]
        private string _scope;

        [Header("Cost")]
        [SerializeField]
        [Tooltip("Currency id the purchase deducts from (content unlocks cost 0).")]
        private string _costCurrencyId;

        [SerializeField]
        private double _costAmount;

        [SerializeField]
        [Tooltip("All conditions must hold for the upgrade to become available. Gates may reference any currency, not only the cost currency.")]
        private List<GateCondition> _gate = new();

        [SerializeField]
        private UpgradePayload _payload = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Type => _type;
        public string Scope => _scope;
        public string CostCurrencyId => _costCurrencyId;
        public double CostAmount => _costAmount;
        public IReadOnlyList<GateCondition> Gate => _gate;
        public UpgradePayload Payload => _payload;

#if UNITY_EDITOR
        // importer-only: upgrade assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string type, string scope,
            string costCurrencyId, double costAmount, List<GateCondition> gate, UpgradePayload payload)
        {
            _id = id;
            _displayName = displayName;
            _type = type;
            _scope = scope;
            _costCurrencyId = costCurrencyId;
            _costAmount = costAmount;
            _gate = gate;
            _payload = payload;
        }
#endif
    }

    // What an upgrade grants. Effect is a string key with one code handler each
    // (slice 5); which fields are meaningful depends on the effect.
    [Serializable]
    public class UpgradePayload
    {
        public const string EffectTapValueAdd = "tapValueAdd";
        public const string EffectGeneratorOutputMultiplier = "generatorOutputMultiplier";
        public const string EffectAllCashPerSecMultiplier = "allCashPerSecMultiplier";
        public const string EffectUnlockSystem = "unlockSystem";

        [SerializeField]
        [Tooltip("One of the Effect* constants.")]
        private string _effect;

        [SerializeField]
        private double _value;

        [SerializeField]
        [Tooltip("Target generator id, for generator-scoped effects.")]
        private string _generatorId;

        [SerializeField]
        [Tooltip("System key revealed by unlockSystem, e.g. fans / covers / album.")]
        private string _systemId;

        public string Effect => _effect;
        public double Value => _value;
        public string GeneratorId => _generatorId;
        public string SystemId => _systemId;

        public UpgradePayload() { }

#if UNITY_EDITOR
        public UpgradePayload(string effect, double value, string generatorId, string systemId)
        {
            _effect = effect;
            _value = value;
            _generatorId = generatorId;
            _systemId = systemId;
        }
#endif
    }
}
