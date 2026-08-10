using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON effect "compound": applies several effects as one payload, mirroring
    // CompoundCondition. Children are GameEffect references themselves, so
    // compounds nest.
    //
    // It exists because a single event can grant several things and the sources
    // hold ONE effect each: UpgradeDefinition.Payload and RewardDefinition.Effect
    // are single fields, deliberately, so the lifetime travelling with a grant
    // cannot fork - and so group-ness stays this class's private business rather
    // than a list every consumer iterates. The alternative, making every source
    // hold a list, would put the same composition in three places instead of one.
    //
    // Ordering is list order, and it matters: two grants on one target with a mix
    // of adds and multiplies compose differently if reordered, and the projection
    // must produce the same store every time it runs (design doc section 12, rule
    // 6). No de-duplication, for the same reason BarSystem projects in the
    // chapter's declaration order rather than dictionary order.
    [Serializable]
    public class CompoundEffect : GameEffect
    {
        [SerializeReference]
        [SubclassPicker]
        private List<GameEffect> _effects = new();

        public IReadOnlyList<GameEffect> Effects => _effects;

        public CompoundEffect() { }

        public CompoundEffect(List<GameEffect> effects)
        {
            _effects = effects ?? new List<GameEffect>();
        }

        public override void Apply(EffectContext context, ContentScope scope)
        {
            for (var i = 0; i < _effects.Count; i++)
            {
                // a null child is a content mistake (reported by Validate); skip it
                // rather than throwing out of a payload mid-application, which
                // would leave the earlier children applied and the later ones not
                if (_effects[i] == null)
                    continue;

                _effects[i].Apply(context, scope);
            }
        }

        public override void Validate(ConditionContext context, string source)
        {
            if (_effects.Count == 0)
            {
                Debug.LogError($"GameEffect: {source} has a compound effect with no children - it would grant nothing.");
                return;
            }

            for (var i = 0; i < _effects.Count; i++)
            {
                if (_effects[i] == null)
                    Debug.LogError($"GameEffect: {source} compound entry {i} is null. It will never grant anything.");
                else
                    _effects[i].Validate(context, $"{source} (compound[{i}])");
            }
        }
    }
}
