namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime state for one upgrade, wrapping its UpgradeDefinition asset.
    // Applied means the payload has been granted, which is the whole of an
    // upgrade's state: a content unlock applies when its gate is met, a buff
    // when the player buys it. There is no second "purchased" flag because it
    // would say the same thing for buffs and nothing for content unlocks -
    // Definition.Scope already carries how long it lasts, so the album release
    // clears exactly the run-scoped ones and the save partitions on the same
    // field.
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
