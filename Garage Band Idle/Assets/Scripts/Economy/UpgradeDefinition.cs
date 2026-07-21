using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What kind of upgrade this is — a closed, code-defined set (ContentScope
    // has the rationale). The chapter JSON spells these "buff" / "contentUnlock".
    // Explicit values: the numbers are the serialization contract, and zero is
    // reserved for the uninitialized state (see ContentScope). Append with new
    // values only.
    public enum UpgradeType
    {
        None = 0,

        // run-scoped stat buff, re-bought each run
        Buff = 1,

        // reveals a system/currency/generator; permanent within the chapter
        ContentUnlock = 2,
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
        [SubclassPicker]
        [Tooltip("Must hold for the upgrade to become available. Gates may reference any currency, not only the cost currency.")]
        private Condition _gate;

        [SerializeReference]
        [SubclassPicker]
        private UpgradePayload _payload;

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
}
