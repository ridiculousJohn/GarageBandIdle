using System.Threading.Tasks;

namespace RidiculousGaming.GarageBandIdle.Monetization
{
    // Where a rewarded ad is offered. A closed, code-defined set, so an enum
    // rather than a string: nothing authors a placement.
    public enum AdPlacement
    {
        EncoreExtension,
        IdleDouble
    }

    // What the SDK reports back. Only Rewarded pays; the other two are ordinary
    // answers, not faults (design doc 9: all ads are opt-in).
    public enum AdResult
    {
        Rewarded,
        Aborted,
        Failed
    }

    // What a rewarded-ad SDK presents: request now, result later. The manager
    // polls the task from the main thread, so a result lands only in the
    // driver's Update and never inside a running command (12.9).
    public interface IAdService
    {
        Task<AdResult> ShowRewarded(AdPlacement placement);
    }
}
