namespace RidiculousGaming.GarageBandIdle.UI
{
    // The one thing a story row asks of the host (12.11): open the card for a
    // beat, at the scope the beat is declared on. The host is the card's owner;
    // the row never sees the overlay.
    public interface IStoryOpener
    {
        void OpenStory(Story.StoryBeatDefinition beat, ScopeState scope);
    }
}
