using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What kind of upgrade this is - a closed, code-defined set (ContentScope
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
    // a content unlock's payload is setFlag - the single reveal registry. A buff
    // is bought through UpgradeSystem.TryBuy, a content unlock applies when its
    // gate holds, and both grant the payload with this asset's Scope.
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
        [Tooltip("What the upgrade grants. Its lifetime is this asset's Scope, never a second declaration on the effect.")]
        private GameEffect _payload;

        public string Id => _id;
        public string DisplayName => _displayName;
        public UpgradeType Type => _type;
        public ContentScope Scope => _scope;
        public string CostCurrencyId => _costCurrencyId;
        public double CostAmount => _costAmount;
        public Condition Gate => _gate;
        public GameEffect Payload => _payload;

        // Whether this upgrade would pay out more than once (design doc section 12,
        // rule 6). A content unlock applies AUTOMATICALLY whenever its gate holds and
        // its latch is absent, and both halves are reachable at any scope: a release
        // clears run-scoped latches, and a restore clears any latch its snapshot omits
        // because restore is replacement. So a payout behind one is granted again
        // every time that happens.
        //
        // The rule lives here, on the definition, because two callers must not be able
        // to disagree about it: ContentValidator reports it at boot, and UpgradeSystem
        // refuses to register the upgrade at all - and the refusal is what makes the
        // report safe to arrive late. Validation runs AFTER the frontier economy is
        // built and settled, so a validator-only rule would report a payout the boot
        // settle had already banked.
        //
        // A bought buff is exempt: TryBuy charges the cost again, so re-buying and
        // re-paying is coherent rather than free.
        public bool CarriesRepeatablePayout
            => _type == UpgradeType.ContentUnlock && _payload != null && _payload.ContainsOneShot;

#if UNITY_EDITOR
        // importer-only: upgrade assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, UpgradeType type, ContentScope scope,
            string costCurrencyId, double costAmount, Condition gate, GameEffect payload)
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
