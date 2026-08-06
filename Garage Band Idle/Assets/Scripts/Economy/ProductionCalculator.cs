using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Income math per the design doc sections 3 and 5. This is formula only:
    // the modifiers that scale production are composed by ModifierSystem, so
    // catalog/roadie/encore arrive as more derived modifiers rather than more
    // parameters here.
    public static class ProductionCalculator
    {
        // the album payout's tuning divisor (design doc section 5, the JSON's
        // recordsFormula): 50 fans banks the first meaningful payout (3)
        private const double FansPerRecordsUnit = 5;

        // permanent global buff: 1 + buffPerRecord x records (additive per
        // Record), the value RecordsIncomeModifier reports
        public static BigNumber IncomeMultiplier(BigNumber records, double buffPerRecord)
            => BigNumber.One + records * buffPerRecord;

        // The album payout (design doc section 5): floor((fansThisRun / 5) ^ 0.5),
        // the early-chapter f(fansThisRun) - the Ch. 6+ variant that reads catalog
        // quality is a different function, not a parameter of this one. Clamped at
        // zero so a sub-zero balance (impossible today: production never drains
        // fans) can never produce a NaN payout.
        public static BigNumber RecordsEarned(BigNumber fansThisRun)
            => BigNumber.Floor(BigNumber.Pow(
                BigNumber.Max(fansThisRun, BigNumber.Zero) / FansPerRecordsUnit, 0.5));

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
