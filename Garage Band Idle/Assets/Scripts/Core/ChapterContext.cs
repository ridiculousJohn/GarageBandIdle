using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle
{
    // Everything a chapter module needs at Initialize time. Kept deliberately
    // lean: new systems hang off GameManager, so the module contract never
    // changes when the game grows.
    public class ChapterContext
    {
        public GameManager Game { get; }
        public ChapterDefinition Chapter { get; }
        public FlagSystem Flags { get; }

        public ChapterContext(GameManager game, ChapterDefinition chapter, FlagSystem flags)
        {
            Game = game;
            Chapter = chapter;
            Flags = flags;
        }
    }
}
