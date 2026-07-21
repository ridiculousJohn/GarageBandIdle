using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // The shared reward pool (design doc section 6.1): bars and event tiers name
    // a reward by id, and Apply dispatches to the RewardDefinition asset's own
    // handler. One pool, so a reward is reusable across content and a new reward
    // kind is a new subclass plus assets.
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

        public void Apply(string rewardId, RewardContext context)
        {
            if (!_byId.TryGetValue(rewardId ?? "", out var reward))
            {
                Debug.LogError($"RewardManager: Apply on unknown reward id '{rewardId}'. Nothing granted.");
                return;
            }

            reward.Apply(context);
        }
    }
}
