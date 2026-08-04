using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle
{
    // Everything a chapter module needs at Initialize time. The systems come
    // from the EconomyContext, not from GameManager: a module reads the economy
    // it is showing, so the same module prefab works for an event sandbox or a
    // replay economy (design doc section 12, rule 12) without knowing there is
    // more than one economy.
    //
    // Game is still here because the UI's player actions route through it - a
    // button press means "act on whatever has focus", which is GameManager's
    // question, not this economy's.
    public class ChapterContext
    {
        public GameManager Game { get; }
        public EconomyContext Economy { get; }
        public ChapterDefinition Chapter { get; }
        public FlagSystem Flags { get; }

        public ChapterContext(GameManager game, EconomyContext economy)
        {
            Game = game;
            Economy = economy;
            Chapter = economy?.Chapter;
            Flags = economy?.Flags;
        }
    }
}
