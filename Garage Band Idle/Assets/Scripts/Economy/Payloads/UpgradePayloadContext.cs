namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Everything an upgrade payload may touch when applied. Grows as the buff
    // slice lands (tap-value stack, generator output buffs) — the same shape as
    // RewardContext, kept separate because the rewards pool and upgrade payloads
    // are distinct systems in the design doc.
    public class UpgradePayloadContext
    {
        public FlagSystem Flags { get; }

        public UpgradePayloadContext(FlagSystem flags)
        {
            Flags = flags;
        }
    }
}
