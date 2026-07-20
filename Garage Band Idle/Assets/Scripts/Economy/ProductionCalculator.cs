using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Income math per the design doc section 3: sum of generator output, then
    // the global multipliers. Only the Records multiplier exists in Chapter 1;
    // catalog/roadie/encore multiply into IncomeMultiplier in later slices.
    public static class ProductionCalculator
    {
        // permanent global buff: 1 + buffPerRecord × records (additive per Record)
        public static BigNumber IncomeMultiplier(BigNumber records, double buffPerRecord)
            => BigNumber.One + records * buffPerRecord;

        // Σ(gen.baseOutput × count) for one produced currency, times the multiplier
        public static BigNumber TotalPerSecond(IReadOnlyList<Generator> generators, string currencyId, BigNumber incomeMultiplier)
        {
            var sum = BigNumber.Zero;
            foreach (var generator in generators)
            {
                if (generator.Definition.ProducesCurrencyId == currencyId)
                    sum += generator.ProductionPerSecond;
            }
            return sum * incomeMultiplier;
        }
    }
}
