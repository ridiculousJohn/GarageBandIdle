namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime state for one upgrade, wrapping its UpgradeDefinition asset.
    // For content unlocks Applied latches when the gate is met (this slice);
    // buff purchase/ownership state arrives with the buff-upgrades slice.
    public class Upgrade
    {
        public UpgradeDefinition Definition { get; }

        public bool Applied { get; private set; }

        public Upgrade(UpgradeDefinition definition)
        {
            Definition = definition;
        }

        internal void MarkApplied() => Applied = true;
    }
}
