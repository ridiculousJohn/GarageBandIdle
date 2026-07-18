using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime state for one generator. Constructed from hardcoded values in this
    // slice; a data-driven GeneratorDefinition supplies the same parameters in a
    // later slice. Cost follows the standard idle curve: baseCost * costGrowth^owned.
    public class Generator
    {
        private readonly BigNumber _baseCost;
        private readonly double _costGrowth;
        private readonly BigNumber _ratePerSecondPerUnit;

        public string DisplayName { get; }

        // currency this generator produces and is bought with
        public string CurrencyId { get; }

        public int Owned { get; private set; }

        // fires after a successful purchase changes Owned; code-only subscribers
        public event Action OwnedChanged;

        public Generator(string displayName, string currencyId, BigNumber baseCost, double costGrowth, BigNumber ratePerSecondPerUnit)
        {
            DisplayName = displayName;
            CurrencyId = currencyId;
            _baseCost = baseCost;
            _costGrowth = costGrowth;
            _ratePerSecondPerUnit = ratePerSecondPerUnit;
        }

        public BigNumber NextCost => _baseCost * BigNumber.Pow(_costGrowth, Owned);
        public BigNumber RatePerUnit => _ratePerSecondPerUnit;
        public BigNumber ProductionPerSecond => _ratePerSecondPerUnit * Owned;

        public BigNumber Produce(double seconds) => ProductionPerSecond * seconds;

        // buys one unit if affordable; deducts the cost and bumps Owned
        public bool TryBuy(CurrencyManager currencies)
        {
            var cost = NextCost;
            if (currencies.Get(CurrencyId) < cost)
                return false;

            currencies.Add(CurrencyId, -cost);
            Owned++;
            OwnedChanged?.Invoke();
            return true;
        }
    }
}
