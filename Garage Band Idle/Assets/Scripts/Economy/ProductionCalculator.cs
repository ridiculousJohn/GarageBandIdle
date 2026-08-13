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

        // No per-second helper lives here: a currency's rate is its producer's
        // (rule 13), summed and composed in one place, so a sum over generators
        // alone would be half of a rate and could only disagree with the whole.
    }
}
