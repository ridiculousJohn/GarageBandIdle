using RidiculousGaming.GarageBandIdle.Loop;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Fan accrual (design doc sections 3 and 6): fans are a function of band
    // size and time ONLY - never Cash or Cash/sec - so income alone cannot
    // shortcut the album payout (section 11). Dormant until the activation gate
    // holds (Chapter 1: the flag its play_for_crowd content unlock sets).
    // Rate modifiers (cover-bar rewards) live in ModifierSystem under FanRate.
    public class FanSystem
    {
        private static readonly ModifierTargetKey RateTarget = ModifierTargetKey.Global(ModifierTarget.FanRate);

        private readonly FansConfig _config;
        private readonly ICurrencies _currencies;
        private readonly GeneratorSystem _generators;
        private readonly ConditionContext _conditions;
        private readonly ModifierSystem _modifiers;

        // the accrual currency and activation gate come from the chapter's fans
        // config (JSON), not from code
        public FanSystem(FansConfig config, ICurrencies currencies, GeneratorSystem generators,
            ConditionContext conditions, ModifierSystem modifiers)
        {
            _config = config;
            _currencies = currencies;
            _generators = generators;
            _conditions = conditions;
            _modifiers = modifiers;

            _currencies.ValidateReference(config.CurrencyId, "FanSystem (accrual)");
        }

        // a gameplay gate, not a display check: it decides whether fans accrue
        // at all, and it is an ordinary Condition, so a chapter can start
        // accrual on a balance or a completed bar as easily as on a flag
        public bool Active => ConditionEvaluator.IsMet(_config.ActiveWhen, _conditions);

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
