namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A modifier whose value is computed from a source rather than granted at a
    // moment. It carries no ContentScope deliberately: its lifetime is its
    // source's, so a run reset never touches it. Two independent answers to
    // "does this survive an album release" - a scope here and the source's own
    // reset rule - could disagree, and the source is the one that already
    // exists (see RecordsIncomeModifier, whose source is a currency group that
    // declares it does not reset).
    public abstract class DerivedModifier
    {
        public abstract ModifierTargetKey Target { get; }
        public abstract ModifierOperation Operation { get; }

        // read every time the target composes, never cached, so it cannot go
        // stale against the source it reads
        public abstract BigNumber Value { get; }
    }
}
