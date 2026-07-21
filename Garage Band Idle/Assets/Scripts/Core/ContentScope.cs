namespace RidiculousGaming.GarageBandIdle
{
    // Lifetime of a piece of content's effect or state (design doc section 4).
    // A closed, code-defined set: reset logic dispatches on it (one behavior per
    // value), so unlike currency/group ids it is not designer-extensible data.
    // The chapter JSON spells these as "run" / "permanentInChapter"; the importer
    // maps them and fails loudly on anything else.
    // Values are explicit because Unity serializes enum fields as their integral
    // value: the numbers are a stable contract with saved assets (and later,
    // saves), independent of declaration order. Append with new values only.
    // Zero is reserved for the uninitialized state so a hand-created asset or an
    // un-migrated field is detectable (boot validation flags None), never a
    // silent default.
    public enum ContentScope
    {
        None = 0,

        // resets on album release, re-earned each run
        Run = 1,

        // survives album releases within its chapter
        PermanentInChapter = 2,
    }
}
