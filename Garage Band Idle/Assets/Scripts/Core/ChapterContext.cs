using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle
{
    // Everything a chapter module needs at Initialize time. The systems come
    // from the EconomyContext, not from GameManager: a module reads the economy
    // it is showing AND acts on it, so the same module prefab works for an
    // event sandbox or a replay economy (design doc section 12, rule 12)
    // without knowing there is more than one economy.
    //
    // Deliberately NO GameManager here. Routing a button press to "whatever has
    // focus" lets a module display one economy's numbers and mutate another's the
    // moment two contexts exist (a frontier release button resetting an event
    // sandbox). Displayed and mutated are the same object by construction: a
    // module cannot reach any economy but the one it shows. Focus is what rule 7
    // says it is -
    // who receives the tick - not who receives button presses.
    public class ChapterContext
    {
        public EconomyContext Economy { get; }
        public ChapterDefinition Chapter { get; }
        public FlagSystem Flags { get; }

        public ChapterContext(EconomyContext economy)
        {
            Economy = economy;
            Chapter = economy?.Chapter;
            Flags = economy?.Flags;
        }
    }
}
