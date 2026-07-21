using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // One reward (design doc section 6.1): the payoff of an event tier or a
    // completed cover bar, and reusable anywhere content grants something.
    // Rewards are a ScriptableObject family — each subclass declares exactly
    // the fields its kind needs and implements Apply, so a reward type can
    // never exist without its handler. A new reward kind is a new subclass
    // plus assets; referencing content points at reward assets directly.
    public abstract class RewardDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id, referenced by the chapter JSON's rewards list.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        public string Id => _id;
        public string DisplayName => _displayName;

        // grants the reward to the running game
        public abstract void Apply(RewardContext context);

#if UNITY_EDITOR
        // importer-only: reward assets are generated from chapter JSON
        protected void EditorInitializeBase(string id, string displayName)
        {
            _id = id;
            _displayName = displayName;
        }
#endif
    }
}
