using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The one thing a levelled group's Auto button asks of the host (12.11):
    // open the automation window for a group, at the scope that declares it.
    // The host is the overlay's owner; the card never sees it.
    public interface IAutomationOpener
    {
        void OpenAutomation(GroupDefinition group, ScopeState scope);
    }
}
