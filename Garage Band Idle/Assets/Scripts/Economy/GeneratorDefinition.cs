using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One generator (gear or bandmate). What it makes is a LIST of production
    // contributions (design doc section 12, rule 13), each naming the currency it
    // feeds and carrying its own id, so a generator that pays two currencies is
    // ordinary content rather than a shape the model cannot hold. Runtime state is
    // keyed by generator id, so adding a generator is a new asset + JSON row with
    // no code change.
    //
    // Each line carrying its own id is what lets a buff address one of them: "double
    // the drummer's cash" and "double the drummer" are different sentences for a
    // bandmate who drives cash and fans both. A bandmate is a TAG plus a fans line
    // (rule 10), not a bool some system branches on.
    [CreateAssetMenu(
        fileName = "NewGenerator",
        menuName = "GarageBandIdle/Generator")]
    public class GeneratorDefinition : Definition
    {
        [SerializeField]
        private string _displayName;

        [Header("Economy")]
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency id the purchase deducts from - declared independently of what the generator produces.")]
        private string _costCurrencyId;

        [SerializeField]
        private double _baseCost;

        [SerializeField]
        [Tooltip("Cost multiplier per owned unit: cost = baseCost x growth^owned.")]
        private double _costGrowth;

        [SerializeField]
        [Tooltip("What each owned unit contributes, per currency. Amounts are PER UNIT: the runtime scales by the owned count.")]
        private List<ProductionContribution> _contributions = new();

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("Must hold for the generator to reveal; none = visible from start.")]
        private Condition _unlock;

        public string DisplayName => _displayName;
        public string CostCurrencyId => _costCurrencyId;
        public double BaseCost => _baseCost;
        public double CostGrowth => _costGrowth;
        public IReadOnlyList<ProductionContribution> Contributions => _contributions;
        public Condition Unlock => _unlock;

#if UNITY_EDITOR
        // importer-only: generator assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string costCurrencyId, double baseCost,
            double costGrowth, List<ProductionContribution> contributions, Condition unlock, string[] tags = null)
        {
            SetIdentity(id, tags);
            _displayName = displayName;
            _costCurrencyId = costCurrencyId;
            _baseCost = baseCost;
            _costGrowth = costGrowth;
            _contributions = contributions ?? new List<ProductionContribution>();
            _unlock = unlock;
        }
#endif
    }
}
