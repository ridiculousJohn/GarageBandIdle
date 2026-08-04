using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle
{
    // Everything an effect may touch when applied: the reveal registry for
    // content unlocks, the modifier registry for every stat effect, and balances
    // for the effects that pay out. Stat effects go through Modifiers rather than
    // through the system they affect, so an effect needs no reference to
    // FanSystem or ProductionSystem to change a rate or a tap value.
    // Grows as effect kinds need more of the game (roadie pool, catalog, ...).
    public class EffectContext
    {
        public CurrencyManager Currencies { get; }
        public FlagSystem Flags { get; }
        public ModifierSystem Modifiers { get; }

        public EffectContext(CurrencyManager currencies, FlagSystem flags, ModifierSystem modifiers)
        {
            Currencies = currencies;
            Flags = flags;
            Modifiers = modifiers;
        }
    }
}
