using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The one thing a levelled row asks of the host when its level crosses a
    // count threshold (12.11): show the toast naming one effect the threshold
    // switched on. The host owns the toast and its queue; the row never sees
    // either.
    public interface IToastOpener
    {
        void OpenToast(ThresholdEffect effect);
    }
}
