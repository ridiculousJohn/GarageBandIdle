using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Fan accrual (design doc sections 3 and 6): fans are a function of band
    // size and time ONLY — never Cash or Cash/sec — so income alone cannot
    // shortcut the album payout (section 11). Dormant until the activation flag
    // (set by the chapter's play_for_crowd-style content unlock) latches on.
    public class FanSystem
    {
        private readonly FansConfig _config;
        private readonly CurrencyManager _currencies;
        private readonly GeneratorSystem _generators;
        private readonly FlagSystem _flags;

        // the accrual currency and activation flag come from the chapter's fans
        // config (JSON), not from code
        public FanSystem(FansConfig config, CurrencyManager currencies, GeneratorSystem generators, FlagSystem flags)
        {
            _config = config;
            _currencies = currencies;
            _generators = generators;
            _flags = flags;

            _currencies.ValidateReference(config.CurrencyId, "FanSystem (accrual)");
        }

        public bool Active => _flags.IsSet(_config.RevealFlagId);

        // Fan-rate rewards stack multiplicatively, tracked PER SCOPE so scoped
        // effects stay resettable: an album release clears the run-scoped stack
        // and keeps the permanent-in-chapter one. Collapsing scopes into one
        // number would make "reset run-scoped effects" unimplementable.
        private BigNumber _runRateMultiplier = BigNumber.One;
        private BigNumber _permanentRateMultiplier = BigNumber.One;

        public void MultiplyRate(double factor, ContentScope scope)
        {
            switch (scope)
            {
                case ContentScope.Run:
                    _runRateMultiplier *= factor;
                    break;
                case ContentScope.PermanentInChapter:
                    _permanentRateMultiplier *= factor;
                    break;
                default:
                    // fail closed on broken content: boot validation reports a
                    // None scope; an unscoped multiplier must never apply
                    Debug.LogError($"FanSystem: MultiplyRate with scope '{scope}'. Ignoring.");
                    break;
            }
        }

        // the run reset (album release now, event baseline later) clears only
        // the run-scoped stack; permanent-in-chapter rewards survive
        public void ResetRunScopedMultipliers() => _runRateMultiplier = BigNumber.One;

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
            ? (BigNumber)(_config.BaseFansPerSec + _config.PerBandmateOwnedBonus * BandmateCount)
                * _runRateMultiplier * _permanentRateMultiplier
            : BigNumber.Zero;

        public void Tick(double seconds)
        {
            var rate = RatePerSecond;
            if (rate > BigNumber.Zero)
                _currencies.Add(_config.CurrencyId, rate * seconds);
        }
    }
}
