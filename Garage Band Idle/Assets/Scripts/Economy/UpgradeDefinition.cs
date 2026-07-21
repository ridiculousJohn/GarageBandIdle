using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What kind of upgrade this is — a closed, code-defined set (ContentScope
    // has the rationale). The chapter JSON spells these "buff" / "contentUnlock".
    public enum UpgradeType
    {
        // run-scoped stat buff, re-bought each run
        Buff,

        // reveals a system/currency/generator; permanent within the chapter
        ContentUnlock,
    }

    // One upgrade (design doc section 4). Gates are the shared Condition type;
    // a content unlock's payload is setFlag — the single reveal registry.
    // Buff purchase and payload application arrive in the buff slice.
    [CreateAssetMenu(
        fileName = "NewUpgrade",
        menuName = "GarageBandIdle/Upgrade")]
    public class UpgradeDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        private UpgradeType _type;

        [SerializeField]
        [Tooltip("Reset logic acts on this field, never on the id.")]
        private ContentScope _scope;

        [Header("Cost")]
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency id the purchase deducts from (content unlocks cost 0).")]
        private string _costCurrencyId;

        [SerializeField]
        private double _costAmount;

        [SerializeReference]
        [Tooltip("Must hold for the upgrade to become available. Gates may reference any currency, not only the cost currency.")]
        private Condition _gate;

        [SerializeField]
        private UpgradePayload _payload = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public UpgradeType Type => _type;
        public ContentScope Scope => _scope;
        public string CostCurrencyId => _costCurrencyId;
        public double CostAmount => _costAmount;
        public Condition Gate => _gate;
        public UpgradePayload Payload => _payload;

#if UNITY_EDITOR
        // importer-only: upgrade assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, UpgradeType type, ContentScope scope,
            string costCurrencyId, double costAmount, Condition gate, UpgradePayload payload)
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
    // (buffs in slice 5); which fields are meaningful depends on the effect.
    [Serializable]
    public class UpgradePayload
    {
        public const string EffectTapValueAdd = "tapValueAdd";
        public const string EffectGeneratorOutputMultiplier = "generatorOutputMultiplier";
        public const string EffectAllCashPerSecMultiplier = "allCashPerSecMultiplier";
        public const string EffectSetFlag = "setFlag";

        [SerializeField]
        [Tooltip("One of the Effect* constants.")]
        private string _effect;

        [SerializeField]
        private double _value;

        [SerializeField]
        [DefinitionId(typeof(GeneratorDefinition))]
        [Tooltip("Target generator id, for generator-scoped effects.")]
        private string _generatorId;

        [SerializeField]
        [Tooltip("Flag latched by setFlag — the single reveal registry (FlagSystem), e.g. fans / covers / album.")]
        private string _flagId;

        public string Effect => _effect;
        public double Value => _value;
        public string GeneratorId => _generatorId;
        public string FlagId => _flagId;

        public UpgradePayload() { }

#if UNITY_EDITOR
        public UpgradePayload(string effect, double value, string generatorId, string flagId)
        {
            _effect = effect;
            _value = value;
            _generatorId = generatorId;
            _flagId = flagId;
        }
#endif
    }
}
