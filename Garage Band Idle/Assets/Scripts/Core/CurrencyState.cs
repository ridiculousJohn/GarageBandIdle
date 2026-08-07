namespace RidiculousGaming.GarageBandIdle
{
    // Both halves of one currency's state, together (design doc section 12, rule
    // 6). They travel as a pair because they are one fact measured two ways, and
    // separating them is the exact failure this type exists to prevent: cumulative
    // Records IS the earned total, so a balance-only restore comes back with the
    // capstone re-locked and the permanent income multiplier at 1.0 while the
    // number on screen looks right.
    //
    // Filed beside CurrencyManager rather than inside a snapshot type, because it
    // is a property of a currency rather than of any one economy - the chapter's
    // pool and the permanent pool are captured and restored through the same pair,
    // and only the CALLER decides which pool it owns.
    public readonly struct CurrencyState
    {
        public BigNumber Balance { get; }
        public BigNumber EarnedTotal { get; }

        public CurrencyState(BigNumber balance, BigNumber earnedTotal)
        {
            Balance = balance;
            EarnedTotal = earnedTotal;
        }
    }
}
