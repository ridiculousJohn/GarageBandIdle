namespace RidiculousGaming.GarageBandIdle.UI
{
    // Contract for a chapter module prefab. ChapterScreen instantiates module
    // prefabs by addressable address and calls Initialize - it never knows
    // concrete types, so a new module kind is a new script + prefab + address,
    // with no framework changes. Cleanup belongs in OnDestroy; modules die with
    // their section objects.
    public interface IChapterModule
    {
        // What this module presents, so boot validation can check the section entry
        // that names it. Declared HERE rather than mapped from the address in the
        // validator, because the module is the thing that actually requires it - a
        // table of addresses in ContentValidator would be a second declaration of
        // what a prefab already knows, able to disagree with it.
        ModuleDefinitionKind RequiredDefinition { get; }

        // context: the economy this module both shows AND acts on - never
        // "whatever has focus" (see ChapterContext).
        //
        // definitionId: which definition this INSTANCE presents, from the section's
        // module entry, or null/empty for a module that renders a whole roster.
        // Most modules ignore it, and that asymmetry is the point: a list module
        // asks the chapter what to show, while a module presenting exactly one
        // thing has no other way to be told which - two story-beat cards from one
        // prefab, or a tap button that fires ITS producer rather than every tap
        // producer in the chapter.
        void Initialize(ChapterContext context, string definitionId);
    }
}
