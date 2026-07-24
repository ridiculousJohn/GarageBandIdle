using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // JSON effect "setFlag": latches a progress flag in the single reveal
    // registry — the content-unlock payload. Everything that appears when a
    // system exists gates on the flag (FlagSetCondition).
    [Serializable]
    public class SetFlagPayload : UpgradePayload
    {
        [SerializeField]
        [Tooltip("Flag to latch on (FlagSystem), e.g. fans / covers / album.")]
        private string _flagId;

        public string FlagId => _flagId;

        // Unity's serializer needs a parameterless constructor on plain classes
        public SetFlagPayload() { }

        public SetFlagPayload(string flagId)
        {
            _flagId = flagId;
        }

        public override void Apply(UpgradePayloadContext context) => context.Flags.Set(_flagId);

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_flagId))
                Debug.LogError($"UpgradePayload: {source} has a setFlag payload with an empty flag id.");
            else if (context.Flags != null && !context.Flags.IsKnown(_flagId))
                Debug.LogError($"UpgradePayload: {source} references flag '{_flagId}', which the chapter does not declare.");
        }
    }
}
