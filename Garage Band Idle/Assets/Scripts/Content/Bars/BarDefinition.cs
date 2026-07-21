using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // One generic fillable bar (design doc section 3): declares the currency
    // that fills it, a fill requirement, and a reward granted on completion.
    // The fill logic reads FillCurrencyId and works for any currency — Learn
    // Covers is just the Chapter 1 instance, nothing here is covers-specific.
    // Fill/reward behavior arrives with the bars slice; this stores the data.
    [CreateAssetMenu(
        fileName = "NewBar",
        menuName = "GarageBandIdle/Bar")]
    public class BarDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency id spent into the bar (Rehearsal in Chapter 1).")]
        private string _fillCurrencyId;

        [SerializeField]
        [Tooltip("Amount of the fill currency required to complete the bar.")]
        private double _fillRequirement;

        [SerializeField]
        [DefinitionId(typeof(RewardDefinition))]
        [Tooltip("Reward pool id applied on completion (RewardManager).")]
        private string _rewardId;

        public string Id => _id;
        public string DisplayName => _displayName;
        public string FillCurrencyId => _fillCurrencyId;
        public double FillRequirement => _fillRequirement;
        public string RewardId => _rewardId;

#if UNITY_EDITOR
        // importer-only: bar assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, string fillCurrencyId,
            double fillRequirement, string rewardId)
        {
            _id = id;
            _displayName = displayName;
            _fillCurrencyId = fillCurrencyId;
            _fillRequirement = fillRequirement;
            _rewardId = rewardId;
        }
#endif
    }
}
