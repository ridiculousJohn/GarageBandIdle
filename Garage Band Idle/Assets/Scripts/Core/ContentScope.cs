namespace RidiculousGaming.GarageBandIdle
{
    // Lifetime of a piece of content's effect or state (design doc section 4).
    // A closed, code-defined set: reset logic dispatches on it (one behavior per
    // value), so unlike currency/group ids it is not designer-extensible data.
    // The chapter JSON spells these as "run" / "permanentInChapter"; the importer
    // maps them and fails loudly on anything else.
    public enum ContentScope
    {
        // resets on album release, re-earned each run
        Run,

        // survives album releases within its chapter
        PermanentInChapter,
    }
}
