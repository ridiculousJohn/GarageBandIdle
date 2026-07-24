using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Fill-currency engagement earn (design doc section 3): a fill currency
    // OWNS its earn config — a passive tick plus Jam taps, never Cash, so bar
    // progress comes from playing, not income. The caller hands over the
    // OWNING chapter's currencies only (flag ids may repeat across chapters,
    // so flags cannot scope this); each earner is then dormant until its own
    // reveal flag latches. No earn-configured currencies leaves the system
    // inert.
    public class EngagementEarnSystem
    {
        private readonly List<CurrencyDefinition> _earners = new();
        private readonly CurrencyManager _currencies;
        private readonly FlagSystem _flags;

        public EngagementEarnSystem(IEnumerable<CurrencyDefinition> definitions, CurrencyManager currencies, FlagSystem flags)
        {
            _currencies = currencies;
            _flags = flags;

            foreach (var definition in definitions)
            {
                if (definition != null && definition.Earn.Configured)
                    _earners.Add(definition);
            }
        }

        public bool HasEarn(string currencyId) => Find(currencyId) != null;

        // zero while dormant; a negative rate never earns (fail closed — the
        // importer refuses it and boot validation reports stale assets)
        public BigNumber RatePerSecond(string currencyId)
        {
            var earner = Find(currencyId);
            return earner != null && _flags.IsSet(earner.Earn.RevealFlagId) && earner.Earn.PerSec > 0
                ? (BigNumber)earner.Earn.PerSec
                : BigNumber.Zero;
        }

        // the configured per-tap yield, for display; accrual itself stays
        // gated by the reveal flag
        public double PerTap(string currencyId) => Find(currencyId)?.Earn.PerTap ?? 0;

        public void Tick(double seconds)
        {
            foreach (var earner in _earners)
            {
                if (_flags.IsSet(earner.Earn.RevealFlagId) && earner.Earn.PerSec > 0)
                    _currencies.Add(earner.Id, (BigNumber)earner.Earn.PerSec * seconds);
            }
        }

        // the Jam tap's engagement yield; the caller is the one tap action
        public void OnJamTap()
        {
            foreach (var earner in _earners)
            {
                if (_flags.IsSet(earner.Earn.RevealFlagId) && earner.Earn.PerTap > 0)
                    _currencies.Add(earner.Id, earner.Earn.PerTap);
            }
        }

        private CurrencyDefinition Find(string currencyId)
        {
            foreach (var earner in _earners)
            {
                if (earner.Id == currencyId)
                    return earner;
            }
            return null;
        }
    }
}
