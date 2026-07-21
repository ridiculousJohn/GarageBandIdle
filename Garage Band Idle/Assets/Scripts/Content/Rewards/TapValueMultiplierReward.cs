using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // Multiplies the Jam tap value (event-tier rewards, e.g. garage_jam).
    [CreateAssetMenu(
        fileName = "NewTapValueMultiplierReward",
        menuName = "GarageBandIdle/Rewards/Tap Value Multiplier")]
    public class TapValueMultiplierReward : RewardDefinition
    {
        [SerializeField]
        [Tooltip("Tap multiplier, e.g. 1.25 for +25%.")]
        private double _value;

        [SerializeField]
        [Tooltip("Reset logic acts on this field.")]
        private ContentScope _scope;

        public double Value => _value;
        public ContentScope Scope => _scope;

        public override void Apply(RewardContext context)
        {
            // nothing can grant this yet: events (the only tap-reward source)
            // arrive with the events slice, which adds the tap-buff stack
            Debug.LogError($"TapValueMultiplierReward '{Id}': tap-value application arrives with the events slice.");
        }

#if UNITY_EDITOR
        public void EditorInitialize(string id, string displayName, double value, ContentScope scope)
        {
            EditorInitializeBase(id, displayName);
            _value = value;
            _scope = scope;
        }
#endif
    }
}
