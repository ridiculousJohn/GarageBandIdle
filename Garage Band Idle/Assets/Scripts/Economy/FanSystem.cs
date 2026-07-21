using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Fan accrual (design doc sections 3 and 6): fans are a function of band
    // size and time ONLY — never Cash or Cash/sec — so income alone cannot
    // shortcut the album payout (section 11). Dormant until the activation flag
    // (set by the chapter's play_for_crowd-style content unlock) latches on.
    public class FanSystem
    {
        private readonly FansConfig _config;
        private readonly string _fansCurrencyId;
        private readonly string _activationFlagId;
        private readonly CurrencyManager _currencies;
        private readonly GeneratorSystem _generators;
        private readonly FlagSystem _flags;

        public FanSystem(FansConfig config, string fansCurrencyId, string activationFlagId,
            CurrencyManager currencies, GeneratorSystem generators, FlagSystem flags)
        {
            _config = config;
            _fansCurrencyId = fansCurrencyId;
            _activationFlagId = activationFlagId;
            _currencies = currencies;
            _generators = generators;
            _flags = flags;

            _currencies.ValidateReference(fansCurrencyId, "FanSystem (accrual)");
        }

        public bool Active => _flags.IsSet(_activationFlagId);

        private BigNumber _rateMultiplier = BigNumber.One;

        // fan-rate rewards (completed covers) stack multiplicatively on the whole
        // rate; multiplier reset on album release arrives with the prestige slice
        public void MultiplyRate(double factor) => _rateMultiplier *= factor;

        // owned units across bandmate generators (IsBandmate — gear never counts)
        public int BandmateCount
        {
            get
            {
                var count = 0;
                foreach (var generator in _generators.All)
                {
                    if (generator.Definition.IsBandmate)
                        count += generator.Owned;
                }
                return count;
            }
        }

        public BigNumber RatePerSecond => Active
            ? (BigNumber)(_config.BaseFansPerSec + _config.PerBandmateOwnedBonus * BandmateCount) * _rateMultiplier
            : BigNumber.Zero;

        public void Tick(double seconds)
        {
            var rate = RatePerSecond;
            if (rate > BigNumber.Zero)
                _currencies.Add(_fansCurrencyId, rate * seconds);
        }
    }
}
