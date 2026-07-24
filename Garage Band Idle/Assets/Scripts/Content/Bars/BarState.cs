using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // runtime state for one bar; progress is spent fill currency, so it never
    // exceeds the requirement and never refunds. Completion latching is
    // meaningful for every fill mode, so the state is shared, not per-mode,
    // and completion derives from progress inside this class's own
    // transitions (accrue, run-reset, restore) - progress and completion can
    // never diverge no matter which path establishes state.
    public class BarState
    {
        public BarDefinition Definition { get; }
        public BarGroupDefinition Group { get; }
        public BigNumber Progress { get; private set; }
        public bool Completed { get; private set; }

        public BigNumber Remaining => (BigNumber)Definition.FillRequirement - Progress;

        // requirement > 0 is enforced where bars are accepted (BarSystem
        // rejects non-positive requirements), so a new bar is never
        // already complete
        public BarState(BarDefinition definition, BarGroupDefinition group)
        {
            Definition = definition;
            Group = group;
            Progress = BigNumber.Zero;
        }

        // The ONLY accrual mutation a bar's state has: progress moves and
        // completion derives from it in one operation, so the two can
        // never diverge regardless of the caller. Clamps to the
        // requirement. Returns true when this call completed the bar.
        internal bool AddProgress(BigNumber amount)
        {
            if (Completed || amount <= BigNumber.Zero)
                return false;

            Progress = BigNumber.Min(Progress + amount, Definition.FillRequirement);
            if (Remaining > BigNumber.Zero)
                return false;

            Completed = true;
            return true;
        }

        // run reset: back to an empty bar. State-only, no notification -
        // BarSystem notifies after every run-scoped bar has settled.
        // Returns whether anything changed.
        internal bool ResetForRun()
        {
            if (Progress <= BigNumber.Zero && !Completed)
                return false;

            Progress = BigNumber.Zero;
            Completed = false;
            return true;
        }

        // save/load: re-establishes saved progress through the same
        // clamp-and-derive rule as accrual, so completion can never
        // diverge from progress on this path either. Negative progress is
        // corrupt save data and fails closed to an empty bar.
        internal void RestoreProgress(BigNumber progress)
        {
            if (progress < BigNumber.Zero)
            {
                Debug.LogError($"BarSystem: RestoreProgress with negative progress for bar '{Definition.Id}'. Restoring an empty bar.");
                progress = BigNumber.Zero;
            }

            Progress = BigNumber.Min(progress, Definition.FillRequirement);
            Completed = Remaining <= BigNumber.Zero;
        }
    }
}
