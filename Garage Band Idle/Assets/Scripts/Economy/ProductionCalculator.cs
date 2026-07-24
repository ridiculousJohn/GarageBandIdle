using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Income math per the design doc section 3. This is formula only: the
    // modifiers that scale production are composed by ModifierSystem, so
    // catalog/roadie/encore arrive as more derived modifiers rather than more
    // parameters here.
    public static class ProductionCalculator
    {
        // permanent global buff: 1 + buffPerRecord x records (additive per
        // Record), the value RecordsIncomeModifier reports
        public static BigNumber IncomeMultiplier(BigNumber records, double buffPerRecord)
            => BigNumber.One + records * buffPerRecord;

        // sum of one produced currency's generator output, each generator
        // already composed with the modifiers targeting it
        public static BigNumber TotalPerSecond(IReadOnlyList<Generator> generators, string currencyId)
        {
            var sum = BigNumber.Zero;
            foreach (var generator in generators)
            {
                if (generator.Definition.ProducesCurrencyId == currencyId)
                    sum += generator.ProductionPerSecond;
            }
            return sum;
        }
    }
}
