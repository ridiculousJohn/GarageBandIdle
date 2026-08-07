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
        // Two entry points, mirroring the effect family (design doc section 12,
        // rule 6): a bar completing or a tier clearing is an ACQUISITION, while
        // rebuilding the store from the completions already on record is a
        // PROJECTION. Only the former may pay currency, and which is which is the
        // calling site's knowledge - BarGroupRuntime.NotifyCompleted against
        // ProjectCompletedRewards - so it is expressed as two methods rather than
        // a flag nobody would read.
        public void ApplyOnAcquisition(string rewardId, EffectContext context, ContentScope scope)
        {
            if (TryResolve(rewardId, "ApplyOnAcquisition", out var reward))
                reward.ApplyOnAcquisition(context, scope);
        }

        public void Project(string rewardId, EffectContext context, ContentScope scope)
        {
            if (TryResolve(rewardId, "Project", out var reward))
                reward.Project(context, scope);
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
