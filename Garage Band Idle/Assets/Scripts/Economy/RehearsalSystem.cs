using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Fill-currency accrual (design doc section 3): Rehearsal is earned from
    // engagement - a passive tick plus Jam taps - never from Cash, so bar
    // progress comes from playing, not income. Dormant until the reveal flag
    // (set by the chapter's learn_covers-style content unlock) latches on.
    // The currency id and flag come from the chapter's rehearsal config, so a
    // later chapter's fill currency reuses this system unchanged. A chapter
    // with no fill currency leaves the config empty and the system stays inert.
    public class RehearsalSystem
    {
        private readonly RehearsalConfig _config;
        private readonly CurrencyManager _currencies;
        private readonly FlagSystem _flags;

        public RehearsalSystem(RehearsalConfig config, CurrencyManager currencies, FlagSystem flags)
        {
            _config = config;
            _currencies = currencies;
            _flags = flags;

            if (Configured)
                _currencies.ValidateReference(config.CurrencyId, "RehearsalSystem (accrual)");
        }

        public bool Configured => !string.IsNullOrEmpty(_config.CurrencyId);

        public bool Active => Configured && _flags.IsSet(_config.RevealFlagId);

        public string CurrencyId => _config.CurrencyId;

        public BigNumber RatePerSecond => Active ? (BigNumber)_config.PointsPerSec : BigNumber.Zero;

        public void Tick(double seconds)
        {
            var rate = RatePerSecond;
            if (rate > BigNumber.Zero)
                _currencies.Add(_config.CurrencyId, rate * seconds);
        }

        // the Jam tap's engagement yield; the caller is the one tap action
        public void OnJamTap()
        {
            if (Active && _config.PointsPerTap > 0)
                _currencies.Add(_config.CurrencyId, _config.PointsPerTap);
        }
    }
}
