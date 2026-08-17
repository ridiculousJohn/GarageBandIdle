namespace RidiculousGaming.GarageBandIdle.UI
{
    // What KIND of definition a module presents, declared by the module itself
    // (IChapterModule.RequiredDefinition) and enforced at boot.
    //
    // The module is the only honest authority here. A section's module entry pairs
    // an address with a definition id, and nothing about the id says which family it
    // belongs to - so validation that only asked "does this chapter declare that id
    // anywhere" accepted a tap button presenting a story beat while an unrelated
    // card presented the jam producer. Both entries resolve, the producer looks
    // presented, and the Jam button is dead.
    //
    // A closed, code-defined set for the usual reason: each value corresponds to a
    // module script that exists. A new module kind adds a value and its resolution
    // rule together, which is what keeps authored data from outrunning implemented
    // code (the same discipline the bar fillMode/delivery pair follows).
    public enum ModuleDefinitionKind
    {
        // renders a whole roster resolved from the chapter (the currency header,
        // the generator/upgrade/bar lists), so it names no single definition - and
        // an entry that gives one anyway is a content mistake, since nothing would
        // read it
        None = 0,

        // a contributor that authors yield lines: the surface a firing pays from.
        // Not merely "Producer", because a button naming the passive band
        // producer would resolve and still pay nothing.
        FireableContributor = 1,

        // one story beat's text, presented on a card
        StoryBeat = 2,

        // one prestige rung, named by id among the scope's filed rungs - a
        // button naming a rung nothing files would be dead, exactly the
        // producer failure above one family over
        PrestigeRung = 3,
    }
}
