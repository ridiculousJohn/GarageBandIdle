namespace RidiculousGaming.GarageBandIdle
{
    // Completed-bar counts consumed by BarsCompletedCondition, implemented by
    // BarSystem. Where the ConditionContext carries none, every barsCompleted
    // condition evaluates as unmet.
    public interface IBarCompletionSource
    {
        // completed bars in the given bar group this run
        int CompletedCount(string groupId);
    }
}
