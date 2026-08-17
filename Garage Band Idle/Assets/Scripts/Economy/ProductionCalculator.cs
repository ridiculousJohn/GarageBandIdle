namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Income math per the design doc sections 3 and 5. This is formula only:
    // the modifiers that scale production are composed by ModifierSystem, so
    // catalog/roadie/encore arrive as more derived modifiers rather than more
    // parameters here.
    public static class ProductionCalculator
    {
        // permanent global buff: 1 + buffPerRecord x records (additive per
        // Record), the value RecordsIncomeModifier reports
        public static BigNumber IncomeMultiplier(BigNumber records, double buffPerRecord)
            => BigNumber.One + records * buffPerRecord;

        // The album payout deliberately does NOT live here: it is authored
        // content (RootOfBalanceFormula on the album rung, design doc section
        // 5), and a second copy in code is how a formula ends up with two homes
        // and one of them stale.

        // No per-second helper lives here: a currency's rate is its producer's
        // (rule 13), summed and composed in one place, so a sum over generators
        // alone would be half of a rate and could only disagree with the whole.
    }
}
