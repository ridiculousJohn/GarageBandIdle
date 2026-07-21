namespace RidiculousGaming.GarageBandIdle
{
    // Completed-bar counts consumed by BarsCompletedCondition. Implemented by
    // the bar system (bars slice); until one is wired into the ConditionContext,
    // every barsCompleted condition evaluates as unmet.
    public interface IBarCompletionSource
    {
        // completed bars in the given bar group this run
        int CompletedCount(string groupId);
    }
}
