using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // Everything a reward may touch when applied. Stat effects go through
    // Modifiers rather than through the system they affect, so a reward needs no
    // reference to FanSystem or TapSystem to change a rate or a tap value.
    // Grows as reward kinds need more of the game (roadie pool, catalog, ...).
    public class RewardContext
    {
        public CurrencyManager Currencies { get; }
        public FlagSystem Flags { get; }
        public ModifierSystem Modifiers { get; }

        public RewardContext(CurrencyManager currencies, FlagSystem flags, ModifierSystem modifiers)
        {
            Currencies = currencies;
            Flags = flags;
            Modifiers = modifiers;
        }
    }
}
