namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Which economy is being built (design doc section 12, rule 12). A closed,
    // code-defined set: each value names a row of rule 12's projection table,
    // and the context dispatches on it, so this is not designer-extensible data.
    // Values are explicit and appended-only for the same reason ContentScope's
    // are - they will be a save contract once a replay economy is stored as its
    // own state block (rule 7). Zero is the uninitialized state so a recipe that
    // was never given a kind is detectable rather than silently a frontier.
    public enum EconomyRecipeKind
    {
        None = 0,

        // the chapter being played forward: global facts + chapter-permanent
        // facts + run facts
        FrontierChapter = 1,

        // an event's fixed baseline (slice 8): chapter-permanent facts only
        EventSandbox = 2,

        // a cleared chapter replayed for Roadies (rule 7): Roadie allocation +
        // replay-local facts
        ReplayEconomy = 3,
    }

    // The declaration of which global derivations an economy registers when it
    // is built. This exists as a parameter rather than as a branch inside the
    // context because the interesting recipes are the ones that register LESS:
    // an event sandbox not registering the Records income buff is exactly what
    // makes it a fixed baseline, and an absence that is declared can be read,
    // while an absence produced by an `if` somewhere is only discovered.
    //
    // Only the frontier recipe exists today. The factory takes a recipe anyway,
    // so slice 8 adds a value and a recipe rather than a parameter and every
    // call site that passes it.
    public class EconomyRecipe
    {
        // the frontier chapter: the one economy that reads the permanent pool's
        // Records total, through the chapter's own recordBuff declaration
        public static readonly EconomyRecipe FrontierChapter =
            new EconomyRecipe(EconomyRecipeKind.FrontierChapter);

        public EconomyRecipeKind Kind { get; }

        public EconomyRecipe(EconomyRecipeKind kind)
        {
            Kind = kind;
        }

        // Whether the Records income derivations register (design doc section 5:
        // one derived modifier per currency the chapter's recordBuff names).
        // Derived rather than granted, so it is registered once at construction
        // and never re-projected: its lifetime is the Records total's, and the
        // total lives in a pool no run operation holds.
        public bool RegistersRecordsIncome => Kind == EconomyRecipeKind.FrontierChapter;
    }
}
