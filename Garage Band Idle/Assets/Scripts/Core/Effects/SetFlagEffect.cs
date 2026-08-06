using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON effect "setFlag": latches a progress flag in the single reveal
    // registry. Everything that appears when a system exists gates on the flag
    // (FlagSetCondition), so this is how a content unlock and a reward alike
    // reveal content - one handler for both, since the mutation is the same.
    [Serializable]
    public class SetFlagEffect : GameEffect
    {
        [SerializeField]
        [Tooltip("Flag to latch on (FlagSystem), e.g. fans / covers / album.")]
        private string _flagId;

        public string FlagId => _flagId;

        // Unity's serializer needs a parameterless constructor on plain classes
        public SetFlagEffect() { }

        public SetFlagEffect(string flagId)
        {
            _flagId = flagId;
        }

        // The scope parameter is deliberately unused: a flag's lifetime is
        // declared exactly once, on its FlagDeclaration (rule 11), never by
        // whoever sets it - two setters could otherwise give one flag two
        // lifetimes. The setter's own scope still matters indirectly: it
        // decides whether the projection re-asserts this flag after a release
        // clears it.
        public override void Apply(EffectContext context, ContentScope scope) => context.Flags.Set(_flagId);

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_flagId))
                Debug.LogError($"GameEffect: {source} has a setFlag effect with an empty flag id.");
            else if (context.Flags != null && !context.Flags.IsKnown(_flagId))
                Debug.LogError($"GameEffect: {source} references flag '{_flagId}', which the chapter does not declare.");
        }
    }
}
