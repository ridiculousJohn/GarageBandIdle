namespace RidiculousGaming.GarageBandIdle.UI
{
    // The two things a prestige card asks of the host (12.11): open the rung's
    // info window, and open its confirmation window over the scope the card
    // presses from. The host is the overlays' owner; the card never sees them.
    public interface IRungOpener
    {
        void OpenRungInfo(Rung rung);

        void OpenRungConfirm(Rung rung, ScopeState scope);
    }
}
