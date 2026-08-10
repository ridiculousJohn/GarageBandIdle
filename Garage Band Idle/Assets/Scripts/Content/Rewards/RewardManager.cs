using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // The shared reward pool (design doc section 6.1): bars and event tiers name a
    // reward by id, and Apply forwards to that asset's GameEffect carrying the scope
    // of whatever applied it. One pool, so a reward is reusable across content, and a
    // new reward kind is a new GameEffect subclass plus assets - RewardDefinition is
    // concrete, because a reward is the named wrapper and the effect is the behavior.
    public class RewardManager
    {
        private readonly Dictionary<string, RewardDefinition> _byId = new();

        public RewardManager(IEnumerable<RewardDefinition> definitions)
        {
            foreach (var definition in definitions)
            {
                if (definition == null)
                    continue;
                if (string.IsNullOrEmpty(definition.Id))
                {
                    Debug.LogError($"RewardManager: RewardDefinition asset '{definition.name}' has an empty id. Skipping it.");
                    continue;
                }
                if (!_byId.TryAdd(definition.Id, definition))
                    Debug.LogError($"RewardManager: duplicate reward id '{definition.Id}' on asset '{definition.name}'. Keeping the first.");
            }
        }

        // startup check for content holding a reward id (bars, event tiers)
        public bool Contains(string id) => !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);

        // display lookup for UI naming a payoff; boot validation already
        // reported unknown ids, so a miss here returns null quietly
        public RewardDefinition Get(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var reward) ? reward : null;

        // The scope belongs to the content applying the reward (a bar group, an
        // event tier), not to the reward asset - which is what keeps one reward
        // reusable across sources whose lifetimes differ.
        //
        // One entry point: a reward is re-applicable state by construction (its
        // effect is a GameEffect, and one-shot awards are GameActions no reward
        // can hold), so the first completion and every rebuild over recorded
        // completions run the same call.
        public void Apply(string rewardId, EffectContext context, ContentScope scope)
        {
            if (TryResolve(rewardId, "Apply", out var reward))
                reward.Apply(context, scope);
        }

        private bool TryResolve(string rewardId, string source, out RewardDefinition reward)
        {
            if (_byId.TryGetValue(rewardId ?? "", out reward))
                return true;

            Debug.LogError($"RewardManager: {source} on unknown reward id '{rewardId}'. Nothing granted.");
            return false;
        }
    }
}
