using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Latches a progress flag, once, at the player-action moment that earns it -
    // the action counterpart of SetFlagEffect, and the two are different
    // categories on purpose (design doc section 4). An effect re-runs at every
    // rebuild, which is right when the flag re-derives from a more primitive
    // saved fact (an upgrade's reveal flag, from its latch) and wrong for a
    // completion, which has nothing more primitive behind it: the flag IS the
    // fact, set here and persisted, and projection re-applies onComplete FROM
    // it rather than ever re-running this.
    //
    // A rung's completionLatch slot is typed to this class concretely, so "some
    // other action in the latch slot" is not authorable - and the slot stays
    // readable: the already-completed refusal reads FlagId without executing
    // anything.
    [Serializable]
    public class SetFlagAction : GameAction
    {
        [SerializeField]
        [Tooltip("Flag to latch on (FlagSystem). Must be declared by the owning scope - a setter writes its own scope's registry.")]
        private string _flagId;

        public string FlagId => _flagId;

        // Unity's serializer needs a parameterless constructor on plain classes
        public SetFlagAction() { }

        public SetFlagAction(string flagId)
        {
            _flagId = flagId;
        }

        // A flag the registry does not declare has nowhere to latch (Set would
        // refuse and report), and the asking operation must refuse BEFORE any
        // payout lands - a completion whose latch cannot execute after the
        // awards have been paid is exactly the stranding preflight exists to
        // prevent. Deliberately scope-own, like Validate below: a setter writes
        // its own scope's registry, only observers resolve outward.
        public override bool CanExecute(EffectContext context)
            => !string.IsNullOrEmpty(_flagId) && context.Flags != null && context.Flags.IsKnown(_flagId);

        public override void Execute(EffectContext context) => context.Flags.Set(_flagId);

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_flagId))
            {
                Debug.LogError($"GameAction: {source} has a setFlag action with an empty flag id.");
                return;
            }

            // scope-own on purpose, unlike a gate's chain-wide check: mutation
            // targets the owning scope's registry, observation resolves outward
            if (context.Flags != null && !context.Flags.IsKnown(_flagId))
                Debug.LogError($"GameAction: {source} references flag '{_flagId}', which the owning scope does not declare.");

            // this action's own disclosure to the boot flag-setter sweep, when
            // one is listening - the same path SetFlagEffect uses, so the sweep
            // finds a rung's latch with no validator special case
            context.FlagSetterReport?.Invoke(_flagId);
        }
    }
}
