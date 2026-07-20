namespace RidiculousGaming.GarageBandIdle.UI
{
    // Contract for a chapter module prefab. ChapterScreen instantiates module
    // prefabs by addressable address and calls Initialize — it never knows
    // concrete types, so a new module kind is a new script + prefab + address,
    // with no framework changes. Cleanup belongs in OnDestroy; modules die with
    // their section objects.
    public interface IChapterModule
    {
        void Initialize(ChapterContext context);
    }
}
