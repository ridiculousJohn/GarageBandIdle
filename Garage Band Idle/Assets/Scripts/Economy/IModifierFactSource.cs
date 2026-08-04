namespace RidiculousGaming.GarageBandIdle.Economy
{
    // A system holding facts that produce modifiers, able to re-apply their
    // effects from the facts it currently holds (design doc section 12, rule 6).
    //
    // Implementers are the fact classes rule 6's totality obligation names: the
    // purchase latches in UpgradeSystem, the completed bars in BarSystem, and
    // the cleared event tiers slice 8 adds. Nothing else grants - the projection
    // is the only door a modifier enters through - so implementing this is the
    // whole registration a new fact class needs. EconomyContext derives its
    // projection list by filtering the systems it holds for this interface,
    // which is what makes "a fact class added later gets silently skipped"
    // inexpressible rather than merely unlikely: a system the context does not
    // hold cannot hold facts, and one it does hold is projected.
    //
    // Projecting must be idempotent with respect to non-modifier effects: a
    // payload that also sets a flag re-sets a flag already set, which the
    // registry absorbs. Modifiers are not idempotent, which is why every
    // projection is preceded by ModifierSystem.ResetGranted - callers go through
    // EconomyContext.ProjectModifiers, which does both halves.
    public interface IModifierFactSource
    {
        // named in the projection's own error reporting, so a mis-wired source
        // is identifiable without a stack trace
        string FactSourceName { get; }

        // re-applies the effects of every fact this system currently holds
        void ProjectModifiers();
    }
}
