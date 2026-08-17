using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The facts one economy context holds (design doc section 12, rules 6 and 12).
    // ONE state type serves every purpose an economy is built or rebuilt for - a
    // new run, an event sandbox's seed, a save load - because they are the same
    // operation with different data, and two types would be two orders of
    // application able to disagree.
    //
    // What is deliberately NOT here:
    //
    // - Any pool OUTWARD of the scope's own. A capture reads that one pool and
    //   never the router (rule 12), so every further pool the chain reaches - an
    //   ancestor scope's, and the permanent pool the chain ends at - stays with
    //   whoever created it and is captured once, by that owner. Records and Roadies
    //   are the case that shows why: a context-level capture reaching through the
    //   router would make an event sandbox a second claimant on the player's
    //   permanent progress. Every owner captures through the SAME
    //   CurrencyManager.CaptureAll/RestoreAll pair this snapshot's own currencies
    //   use, so "which pool" is a question of who calls it and never of which
    //   mechanism exists.
    // - Modifiers. They are always projected from the facts above and never stored
    //   (rule 6): a saved modifier is a second answer able to disagree with the
    //   fact that produced it.
    // - Section visibility. A pure function of the conditions these facts feed, so
    //   it resets because the facts did.
    // - Any SCOPE. How long a flag or a latch lasts is content, declared once on
    //   the FlagDeclaration or the UpgradeDefinition. Recording it here would be a
    //   copy that goes stale the moment the content is retuned, so the seed filter
    //   re-derives it from the live systems instead.
    public class EconomyLocalSnapshot
    {
        // A new economy's facts: none. Not null - a caller restoring "nothing"
        // means every latch cleared and every balance at its starting value, and
        // that has to be expressible as data rather than as a skipped call.
        public static readonly EconomyLocalSnapshot Empty = new();

        public IReadOnlyDictionary<string, CurrencyState> Currencies { get; }
        public IReadOnlyDictionary<string, int> GeneratorsOwned { get; }
        public IReadOnlyCollection<string> AppliedUpgradeIds { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, BigNumber>> BarProgress { get; }

        // Which bar each group is pouring its fill currency into. Player INTENT
        // rather than progress, and it has to be here for the same reason progress
        // does: without it a restore either keeps the selection it happened to be
        // holding - draining restored Rehearsal into an unrelated bar - or silently
        // loses a decision the player made. A group with no selection is absent.
        public IReadOnlyDictionary<string, string> ActiveBarByGroup { get; }

        public IReadOnlyCollection<string> SetFlagIds { get; }

        public EconomyLocalSnapshot(
            IReadOnlyDictionary<string, CurrencyState> currencies = null,
            IReadOnlyDictionary<string, int> generatorsOwned = null,
            IReadOnlyCollection<string> appliedUpgradeIds = null,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, BigNumber>> barProgress = null,
            IReadOnlyCollection<string> setFlagIds = null,
            IReadOnlyDictionary<string, string> activeBarByGroup = null)
        {
            Currencies = currencies ?? new Dictionary<string, CurrencyState>();
            GeneratorsOwned = generatorsOwned ?? new Dictionary<string, int>();
            AppliedUpgradeIds = appliedUpgradeIds ?? new List<string>();
            BarProgress = barProgress ?? new Dictionary<string, IReadOnlyDictionary<string, BigNumber>>();
            SetFlagIds = setFlagIds ?? new List<string>();
            ActiveBarByGroup = activeBarByGroup ?? new Dictionary<string, string>();
        }
    }
}
