using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // One Learn Covers bar (design doc sections 5-6). Run-scoped: bars reset on
    // album release. Fill/reward behavior arrives with the covers slice; this
    // slice only stores the data.
    [CreateAssetMenu(
        fileName = "NewCover",
        menuName = "GarageBandIdle/Cover")]
    public class CoverDefinition : ScriptableObject
    {
        public const string RewardFanRateMultiplier = "fanRateMultiplier";

        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Rehearsal points required to complete the bar.")]
        private double _fillRequirement;

        [Header("Reward")]
        [SerializeField]
        [Tooltip("Reward effect key, e.g. fanRateMultiplier.")]
        private string _rewardEffect;

        [SerializeField]
        private double _rewardValue;

        public string Id => _id;
        public string DisplayName => _displayName;
        public double FillRequirement => _fillRequirement;
        public string RewardEffect => _rewardEffect;
        public double RewardValue => _rewardValue;

#if UNITY_EDITOR
        // importer-only: cover assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, double fillRequirement,
            string rewardEffect, double rewardValue)
        {
            _id = id;
            _displayName = displayName;
            _fillRequirement = fillRequirement;
            _rewardEffect = rewardEffect;
            _rewardValue = rewardValue;
        }
#endif
    }
}
