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
    // Which permanent pool an economy routes global currency ids to. A second,
    // ORTHOGONAL axis to the projection filter below, and it has to be: withholding
    // the Records income modifier makes a sandbox's baseline fixed, but it does
    // nothing to stop a sandbox WRITING Records. Only the pool decides that.
    public enum PermanentPoolRouting
    {
        // the real permanent pool: the frontier chapter, whose Records and Roadies
        // are the player's permanent progress
        Shared = 0,

        // a private pool built from the same Global currency definitions, at their
        // starting values. An event sandbox's earnings and awards die with the
        // context, so a stray Add("records") in a challenge cannot touch the run -
        // and it cannot be reached by accident, because the sandbox never holds a
        // reference to the real pool at all.
        Isolated = 1,
    }

    // Only the frontier recipe exists today. The factory takes a recipe anyway,
    // so slice 8 adds a value and a recipe rather than a parameter and every
    // call site that passes it.
    public class EconomyRecipe
    {
        // the frontier chapter: the one economy that reads the permanent pool's
        // Records total, through the chapter's own recordBuff declaration
        public static readonly EconomyRecipe FrontierChapter =
            new EconomyRecipe(EconomyRecipeKind.FrontierChapter);

        // an event's fixed baseline (design doc section 6.1): the chapter's
        // permanent facts, no run facts, no Records income derivation, and a pool of
        // its own. Built here rather than by slice 8 so the isolation is testable
        // before there is an event to run in it.
        public static readonly EconomyRecipe EventSandbox =
            new EconomyRecipe(EconomyRecipeKind.EventSandbox);

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

        // Which permanent pool this economy may reach. An event sandbox gets its
        // own; everything else shares the player's.
        //
        // Note the consequence rather than leaving it to be discovered: inside an
        // isolated economy, cumulative Records reads ZERO, because the pool backing
        // the earned total is fresh. That is not a bug to work around - it IS the
        // fixed baseline (design doc section 6.1), arrived at by construction rather
        // than by filtering anything.
        public PermanentPoolRouting PoolRouting
            => Kind == EconomyRecipeKind.EventSandbox
                ? PermanentPoolRouting.Isolated
                : PermanentPoolRouting.Shared;
    }
}
