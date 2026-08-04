using System;

namespace RidiculousGaming.GarageBandIdle
{
    // Completed-bar counts consumed by BarsCompletedCondition, implemented by
    // BarSystem. Where the ConditionContext carries none, every barsCompleted
    // condition evaluates as unmet.
    //
    // The count and the signal that it moved belong together: this is the
    // condition side's whole view of bars, so a context reading counts through
    // it must also be able to learn when to re-read them without holding the
    // concrete system.
    public interface IBarCompletionSource
    {
        // completed bars in the given bar group this run
        int CompletedCount(string groupId);

        // fires as a bar completes, after the completion and its reward have
        // settled - the signal behind barsCompleted conditions
        event Action<Content.BarState> BarCompleted;
    }
}
