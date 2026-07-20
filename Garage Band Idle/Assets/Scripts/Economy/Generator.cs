using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime state for one generator, wrapping its GeneratorDefinition asset.
    // State lives here keyed by the definition; the definition itself is
    // immutable content.
    public class Generator
    {
        public GeneratorDefinition Definition { get; }

        public int Owned { get; private set; }

        // set once by GeneratorSystem when the definition's unlock conditions are met
        public bool Unlocked { get; private set; }

        // fires after a successful purchase changes Owned; code-only subscribers
        public event Action OwnedChanged;

        public Generator(GeneratorDefinition definition)
        {
            Definition = definition;
        }

        public BigNumber NextCost => CostCalculator.Cost(Definition, Owned);

        // base production before global multipliers (those apply in ProductionCalculator)
        public BigNumber ProductionPerSecond => (BigNumber)Definition.BaseOutput * Owned;

        // buys one unit if affordable; deducts the cost and bumps Owned
        public bool TryBuy(CurrencyManager currencies)
        {
            var cost = NextCost;
            if (currencies.Get(Definition.ProducesCurrencyId) < cost)
                return false;

            currencies.Add(Definition.ProducesCurrencyId, -cost);
            Owned++;
            OwnedChanged?.Invoke();
            return true;
        }

        internal void MarkUnlocked() => Unlocked = true;
    }
}
