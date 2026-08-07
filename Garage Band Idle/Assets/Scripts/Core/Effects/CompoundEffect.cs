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
    // cannot fork. The capstone is the Chapter 1 case - one Roadie plus the
    // chapter-advance flag - and the alternative, making every source hold a list,
    // would put the same composition in three places instead of one class.
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

        // Projectable as a WHOLE, because projecting it is always safe: each child
        // answers for itself. That is not a claim about the contents - a compound
        // holding a payout still answers true to ContainsOneShot, which is the
        // question the content rule asks. Two members because there are two
        // questions, and collapsing them into one enum value ("Composite") would
        // answer neither: the projection would have to recurse itself, duplicating
        // the traversal below, and the validator would learn nothing about payouts.
        public override EffectProjection Projection => EffectProjection.Projectable;

        // any payout anywhere beneath this node, at any depth
        public override bool ContainsOneShot
        {
            get
            {
                foreach (var effect in _effects)
                {
                    if (effect != null && effect.ContainsOneShot)
                        return true;
                }
                return false;
            }
        }

        // acquisition applies everything, one-shots included - this is the moment
        // they are FOR
        public override void ApplyOnAcquisition(EffectContext context, ContentScope scope)
        {
            for (var i = 0; i < _effects.Count; i++)
            {
                // a null child is a content mistake (reported by Validate); skip it
                // rather than throwing out of a payload mid-application, which
                // would leave the earlier children applied and the later ones not
                if (_effects[i] == null)
                    continue;

                _effects[i].ApplyOnAcquisition(context, scope);
            }
        }

        // Projection recurses into every child and lets each answer for itself: a
        // projectable child re-applies, a one-shot child does nothing. No kind check
        // here, because GameEffect.Project already IS the filter - duplicating it
        // would be a second place for the rule to live, and this class is not the
        // authority on what a payout does.
        public override void Project(EffectContext context, ContentScope scope)
        {
            for (var i = 0; i < _effects.Count; i++)
                _effects[i]?.Project(context, scope);
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
