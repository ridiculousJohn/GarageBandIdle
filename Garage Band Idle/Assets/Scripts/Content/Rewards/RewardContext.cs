using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Content
{
    // Everything a reward may touch when applied. Grows as reward kinds need
    // more of the game (roadie pool, catalog, ...).
    public class RewardContext
    {
        public CurrencyManager Currencies { get; }
        public FlagSystem Flags { get; }
        public FanSystem Fans { get; }

        public RewardContext(CurrencyManager currencies, FlagSystem flags, FanSystem fans)
        {
            Currencies = currencies;
            Flags = flags;
            Fans = fans;
        }
    }
}
