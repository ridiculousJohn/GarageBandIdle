using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // Sets a progress flag — the reward kind that opens content, since sections,
    // generator unlocks, and gates all observe flags (FlagSetCondition).
    // Lets any reward source (an event, a bar, a special song) reveal new
    // content with no new plumbing.
    [CreateAssetMenu(
        fileName = "NewSetFlagReward",
        menuName = "GarageBandIdle/Rewards/Set Flag")]
    public class SetFlagReward : RewardDefinition
    {
        [SerializeField]
        [Tooltip("Progress flag to latch on (FlagSystem).")]
        private string _flagId;

        public string FlagId => _flagId;

        public override void Apply(RewardContext context) => context.Flags.Set(_flagId);

#if UNITY_EDITOR
        public void EditorInitialize(string id, string displayName, string flagId)
        {
            EditorInitializeBase(id, displayName);
            _flagId = flagId;
        }
#endif
    }
}
