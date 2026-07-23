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

        // Base production before global multipliers (those apply in
        // ProductionCalculator). Fails closed on a negative base output —
        // invalid data, boot validation reports it: production must never
        // drain a currency.
        public BigNumber ProductionPerSecond => Definition.BaseOutput < 0
            ? BigNumber.Zero
            : (BigNumber)Definition.BaseOutput * Owned;

        // buys one unit if affordable; deducts the declared cost currency —
        // never the produced currency — and bumps Owned
        public bool TryBuy(CurrencyManager currencies)
        {
            var cost = NextCost;

            // fail closed on broken content: a non-positive cost is invalid
            // data (boot validation reports it) and must never behave as an
            // endless free purchase
            if (cost <= BigNumber.Zero)
                return false;

            if (currencies.Get(Definition.CostCurrencyId) < cost)
                return false;

            // Owned settles before the spend: Add fires BalanceChanged
            // synchronously, and no subscriber may ever observe the cost
            // deducted with the purchase not yet counted (state, then notify)
            Owned++;
            currencies.Add(Definition.CostCurrencyId, -cost);
            OwnedChanged?.Invoke();
            return true;
        }

        internal void MarkUnlocked() => Unlocked = true;
    }
}
