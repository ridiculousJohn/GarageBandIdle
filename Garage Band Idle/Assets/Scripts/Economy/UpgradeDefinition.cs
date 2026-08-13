using System;
using System.Collections.Generic;
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
    public class UpgradeDefinition : Definition
    {
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
        [Tooltip("What the upgrade grants: re-applicable state (modifiers, flags). Its lifetime is this asset's Scope, never a second declaration on the effect.")]
        private GameEffect _payload;

        // One-shot awards belong here rather than in the payload, and the split is
        // the safety rule (design doc section 12, rule 6): a content unlock applies
        // AUTOMATICALLY whenever its gate holds and its latch is absent - a release
        // clears run-scoped latches, a restore clears any latch its snapshot omits -
        // so anything one-shot in a payload would be paid again every time. Actions
        // run only from TryBuy, the one purchase moment, and the auto-apply path
        // never reads this field, so the repeat is inexpressible rather than
        // refused. A bought buff re-paying is coherent: TryBuy charges the cost
        // again.
        [SerializeReference]
        [SubclassPicker]
        [Tooltip("One-shot awards the PURCHASE pays (buffs only - content unlocks are never bought, so theirs would never run).")]
        private List<GameAction> _actions = new();

        // A flat bonus is a CONTRIBUTION, not a modifier (design doc section 12,
        // rule 11): "+1 Cash per press" is a line feeding cash's yield, authored by
        // the upgrade that pays it, and it sums with every other line rather than
        // being an Add composed over their total. That distinction is what makes it
        // addressable - the line has an id, so a later buff can double THIS bonus -
        // and what removes the question a flat add against a set could not answer,
        // +1 to the total or +1 to each.
        //
        // These are live exactly while the upgrade is applied, which is why nothing
        // here declares a lifetime: the latch is the fact, Scope says how long the
        // latch lasts, and production re-assembles when it changes.
        [SerializeField]
        [Tooltip("Flat production this upgrade adds while applied. A bonus is a contribution, never an Add modifier.")]
        private List<ProductionContribution> _contributions = new();

        public string DisplayName => _displayName;
        public UpgradeType Type => _type;
        public ContentScope Scope => _scope;
        public string CostCurrencyId => _costCurrencyId;
        public double CostAmount => _costAmount;
        public Condition Gate => _gate;
        public GameEffect Payload => _payload;
        public IReadOnlyList<GameAction> Actions => _actions;
        public IReadOnlyList<ProductionContribution> Contributions => _contributions;

        // Whether buying this would grant anything at all. Asked by TryBuy, which
        // refuses to charge for nothing: a buff may coherently be all-payload,
        // all-contributions, all-actions or any mix, and the one broken state is
        // none of them.
        public bool GrantsAnything => _payload != null || _contributions.Count > 0;

#if UNITY_EDITOR
        // importer-only: upgrade assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, UpgradeType type, ContentScope scope,
            string costCurrencyId, double costAmount, Condition gate, GameEffect payload,
            List<GameAction> actions = null, List<ProductionContribution> contributions = null)
        {
            SetIdentity(id);
            _displayName = displayName;
            _type = type;
            _scope = scope;
            _costCurrencyId = costCurrencyId;
            _costAmount = costAmount;
            _gate = gate;
            _payload = payload;
            _actions = actions ?? new List<GameAction>();
            _contributions = contributions ?? new List<ProductionContribution>();
        }
#endif
    }
}
