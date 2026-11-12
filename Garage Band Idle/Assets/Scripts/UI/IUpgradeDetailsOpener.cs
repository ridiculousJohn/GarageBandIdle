using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The one thing an upgrade tile asks of the host (12.11): open the details
    // window for an upgrade, at the scope that declares it. The host is the
    // overlay's owner; the tile never sees it.
    public interface IUpgradeDetailsOpener
    {
        void OpenUpgradeDetails(UpgradeDefinition upgrade, ScopeState scope);
    }
}
