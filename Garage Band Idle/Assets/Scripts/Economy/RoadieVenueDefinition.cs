using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One venue's roadie scaling (design doc 8.2): which chapter it is, the
    // per-roadie rate, and how many the venue takes. Not scope-attached - it
    // names its chapter by id, so both roadie formulas read it and the
    // allocation step owns only the write command, the write-time cap, and the
    // UI, never the multiplier arithmetic.
    [CreateAssetMenu(menuName = "Garage Band Idle/Roadie Venue")]
    public class RoadieVenueDefinition : Definition
    {
        [DefinitionId(typeof(ScopeDefinition))] public string chapterScopeId;
        public double perRoadie;                // a curve ratio, not a currency value
        public int cap;

        // Stationed roadies as the formulas see them: the saved count clamped to
        // [0, cap], so a tampered save or a cap retuned downward never over-pays.
        // The save-side drop of nonpositive values makes this defense in depth
        // rather than the only line.
        public int Stationed(GameContext ctx)
        {
            if (!ctx.Scope.Root.roadieAllocation.TryGetValue(chapterScopeId, out var saved))
                return 0;
            return Mathf.Clamp(saved, 0, cap);
        }

        // 1 + perRoadie * stationed - additive within a venue (design doc 8.2).
        // perRoadie converts BEFORE the multiplication: done in double arithmetic
        // the product can overflow to infinity before the wrapper sees it.
        public BigNumber Boost(GameContext ctx) => BigNumber.One + (BigNumber)perRoadie * Stationed(ctx);
    }
}
