using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // Multiplies the fan accrual rate (cover-bar rewards). Stacks
    // multiplicatively with other fan-rate rewards.
    [CreateAssetMenu(
        fileName = "NewFanRateMultiplierReward",
        menuName = "GarageBandIdle/Rewards/Fan Rate Multiplier")]
    public class FanRateMultiplierReward : RewardDefinition
    {
        [SerializeField]
        [Tooltip("Rate multiplier, e.g. 1.15 for +15%.")]
        private double _value;

        [SerializeField]
        [Tooltip("Reset logic acts on this field.")]
        private ContentScope _scope;

        public double Value => _value;
        public ContentScope Scope => _scope;

        // the reward's scope travels with the effect, so the run reset clears
        // run-scoped multipliers without touching permanent-in-chapter ones
        public override void Apply(RewardContext context)
            => context.Modifiers.Grant(ModifierTargetKey.Global(ModifierTarget.FanRate),
                ModifierOperation.Multiply, _scope, _value);

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
