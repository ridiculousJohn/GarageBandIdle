using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One generator (gear or bandmate). Produces is a currency string id routed
    // through CurrencyManager, and runtime state is keyed by generator id, so
    // adding a generator is a new asset + JSON row with no code change.
    [CreateAssetMenu(
        fileName = "NewGenerator",
        menuName = "GarageBandIdle/Generator")]
    public class GeneratorDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id used as the state key. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Currency id this generator produces — and is bought with.")]
        private string _producesCurrencyId;

        [Header("Economy")]
        [SerializeField]
        private double _baseCost;

        [SerializeField]
        [Tooltip("Cost multiplier per owned unit: cost = baseCost × growth^owned.")]
        private double _costGrowth;

        [SerializeField]
        [Tooltip("Production per second per owned unit.")]
        private double _baseOutput;

        [SerializeField]
        [Tooltip("All conditions must hold for the generator to reveal.")]
        private List<GateCondition> _unlock = new();

        public string Id => _id;
        public string DisplayName => _displayName;
        public string ProducesCurrencyId => _producesCurrencyId;
        public double BaseCost => _baseCost;
        public double CostGrowth => _costGrowth;
        public double BaseOutput => _baseOutput;
        public IReadOnlyList<GateCondition> Unlock => _unlock;

#if UNITY_EDITOR
        // importer-only: generator assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string producesCurrencyId,
            double baseCost, double costGrowth, double baseOutput, List<GateCondition> unlock)
        {
            _id = id;
            _displayName = displayName;
            _producesCurrencyId = producesCurrencyId;
            _baseCost = baseCost;
            _costGrowth = costGrowth;
            _baseOutput = baseOutput;
            _unlock = unlock;
        }
#endif
    }
}
