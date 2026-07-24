namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Everything an upgrade payload may touch when applied: the reveal registry
    // for content unlocks, and the modifier registry for every stat effect a
    // buff grants. Same shape as RewardContext, kept separate because the
    // rewards pool and upgrade payloads are distinct systems in the design doc.
    public class UpgradePayloadContext
    {
        public FlagSystem Flags { get; }
        public ModifierSystem Modifiers { get; }

        public UpgradePayloadContext(FlagSystem flags, ModifierSystem modifiers)
        {
            Flags = flags;
            Modifiers = modifiers;
        }
    }
}
