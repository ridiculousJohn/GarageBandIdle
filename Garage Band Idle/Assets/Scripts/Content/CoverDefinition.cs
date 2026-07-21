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
        [SerializeField]
        [Tooltip("Stable string id. Never rename once saves exist.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        [Tooltip("Rehearsal points required to complete the bar.")]
        private double _fillRequirement;

        [SerializeField]
        [Tooltip("Applied when the bar completes; a direct reference, so it loads with the chapter.")]
        private RewardDefinition _reward;

        public string Id => _id;
        public string DisplayName => _displayName;
        public double FillRequirement => _fillRequirement;
        public RewardDefinition Reward => _reward;

#if UNITY_EDITOR
        // importer-only: cover assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, double fillRequirement,
            RewardDefinition reward)
        {
            _id = id;
            _displayName = displayName;
            _fillRequirement = fillRequirement;
            _reward = reward;
        }
#endif
    }
}
