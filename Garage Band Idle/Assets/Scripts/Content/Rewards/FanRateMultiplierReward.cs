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

        public override void Apply(RewardContext context) => context.Fans.MultiplyRate(_value);

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
