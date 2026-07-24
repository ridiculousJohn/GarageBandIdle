using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Fan accrual (design doc sections 3 and 6): fans are a function of band
    // size and time ONLY - never Cash or Cash/sec - so income alone cannot
    // shortcut the album payout (section 11). Dormant until the activation flag
    // (set by the chapter's play_for_crowd-style content unlock) latches on.
    // Rate modifiers (cover-bar rewards) live in ModifierSystem under FanRate.
    public class FanSystem
    {
        private static readonly ModifierTargetKey RateTarget = ModifierTargetKey.Global(ModifierTarget.FanRate);

        private readonly FansConfig _config;
        private readonly CurrencyManager _currencies;
        private readonly GeneratorSystem _generators;
        private readonly FlagSystem _flags;
        private readonly ModifierSystem _modifiers;

        // the accrual currency and activation flag come from the chapter's fans
        // config (JSON), not from code
        public FanSystem(FansConfig config, CurrencyManager currencies, GeneratorSystem generators,
            FlagSystem flags, ModifierSystem modifiers)
        {
            _config = config;
            _currencies = currencies;
            _generators = generators;
            _flags = flags;
            _modifiers = modifiers;

            _currencies.ValidateReference(config.CurrencyId, "FanSystem (accrual)");
        }

        public bool Active => _flags.IsSet(_config.RevealFlagId);

        // owned units across bandmate generators (IsBandmate - gear never counts)
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
            ? _modifiers.For(RateTarget)
                .ApplyTo(_config.BaseFansPerSec + _config.PerBandmateOwnedBonus * BandmateCount)
            : BigNumber.Zero;

        public void Tick(double seconds)
        {
            var rate = RatePerSecond;
            if (rate > BigNumber.Zero)
                _currencies.Add(_config.CurrencyId, rate * seconds);
        }
    }
}
