using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // How fill currency moves from the group's pool into its bars (design doc
    // 12.7). Behavior classes carry their own config; future modes (tap a fixed
    // chunk, dump the pool) are sibling classes. The fill logic itself lands
    // with the bar system step - these are the authored shapes.
    [Serializable]
    public abstract class BarFillBehavior
    {
    }

    // Drains the pool currency into the active bars as it arrives; the pool only
    // holds a balance when no bar is selected.
    [Serializable]
    public class ContinuousDelivery : BarFillBehavior
    {
        // On completion the stream stops (choosing is the mechanic in Ch. 1);
        // a later chapter's automation can grant auto-advance.
        public bool autoAdvance;
    }

    // Fills from time alone - no pool.
    [Serializable]
    public class TimedFill : BarFillBehavior
    {
    }
}
