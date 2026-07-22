namespace RidiculousGaming.GarageBandIdle.Content
{
    // runtime state for one bar; progress is spent fill currency, so it never
    // exceeds the requirement and never refunds. Completion latching is
    // meaningful for every fill mode, so the state is shared, not per-mode.
    public class BarState
    {
        public BarDefinition Definition { get; }
        public BarGroupDefinition Group { get; }
        public BigNumber Progress { get; internal set; }
        public bool Completed { get; internal set; }

        public BigNumber Remaining => (BigNumber)Definition.FillRequirement - Progress;

        public BarState(BarDefinition definition, BarGroupDefinition group)
        {
            Definition = definition;
            Group = group;
            Progress = BigNumber.Zero;
        }
    }
}
