using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // One reward (design doc section 6.1): the payoff of an event tier or a
    // completed cover bar, and reusable anywhere content grants something. A
    // reward is the named, displayable wrapper around a GameEffect - it answers
    // what the payoff is CALLED, the effect answers what mutation happens, and the
    // applying source answers how long it lives. So a new reward kind is a new
    // effect subclass plus assets, never a new RewardDefinition subclass, and the
    // effect a reward holds is the same family an upgrade's payload holds.
    [CreateAssetMenu(
        fileName = "NewReward",
        menuName = "GarageBandIdle/Reward")]
    public class RewardDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Stable string id, referenced by the chapter JSON's rewards list.")]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeReference]
        [SubclassPicker]
        [Tooltip("What this reward grants. It inherits the durability of the fact that applied it - a bar completion, a tier clear - and declares no lifetime of its own.")]
        private GameEffect _effect;

        public string Id => _id;
        public string DisplayName => _displayName;
        public GameEffect Effect => _effect;

        // Grants the reward with the APPLYING SOURCE's scope: a cover bar passes
        // its group's, an event tier passes its own. That is what lets one asset be
        // a run-scoped payoff in one place and a permanent one in another - a scope
        // field on the asset could not express it, and could disagree with the
        // source that already declares one.
        //
        // One entry point, because a reward IS re-applicable state (a GameEffect):
        // the first completion and every rebuild over the completions already on
        // record run the same mutation. A one-shot award cannot be a reward at all
        // - that is a GameAction, and no reward or projection path holds one.
        public void Apply(EffectContext context, ContentScope scope)
        {
            if (!HasEffect())
                return;

            _effect.Apply(context, scope);
        }

        private bool HasEffect()
        {
            if (_effect != null)
                return true;

            Debug.LogError($"RewardDefinition: reward '{_id}' has no effect. Nothing granted.");
            return false;
        }

#if UNITY_EDITOR
        // importer-only: reward assets are generated from chapter JSON
        public void EditorInitialize(string id, string displayName, GameEffect effect)
        {
            _id = id;
            _displayName = displayName;
            _effect = effect;
        }
#endif
    }
}
