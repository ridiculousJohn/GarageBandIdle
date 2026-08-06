namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The permanent global income buff (design doc section 5): 1 + perRecord x
    // records, multiplying the production of one currency the chapter's
    // recordBuff declares. One instance per declared currency, so production of
    // anything undeclared is untouched.
    //
    // Derived rather than granted: nothing applies it, it is on from boot and
    // tracks the Records total. Its lifetime is that total's, which is why it
    // carries no scope, and boot validation asserts Records sit in a currency
    // group that survives an album release so the total the player reads is the
    // one driving this buff.
    //
    // Reads the lifetime-earned total, the same quantity
    // RecordsCumulativeCondition reads for the capstone gate: "cumulative
    // Records" has one answer, not one per consumer. Records are accumulated
    // and never spent (design doc section 3), so this equals the balance today
    // - the point is that a permanent buff and a chapter gate can never drift
    // apart if a sink is ever added, which would be the deliberate reason to
    // split them rather than a silent divergence.
    public class RecordsIncomeModifier : DerivedModifier
    {
        private readonly ICurrencies _currencies;
        private readonly string _recordsCurrencyId;
        private readonly double _perRecord;
        private readonly ModifierTargetKey _target;

        public RecordsIncomeModifier(ICurrencies currencies, string recordsCurrencyId,
            double perRecord, string affectedCurrencyId)
        {
            _currencies = currencies;
            _recordsCurrencyId = recordsCurrencyId;
            _perRecord = perRecord;
            _target = ModifierTargetKey.Of(ModifierTarget.CurrencyProduction, affectedCurrencyId);
        }

        public override ModifierTargetKey Target => _target;

        public override ModifierOperation Operation => ModifierOperation.Multiply;

        public override BigNumber Value
            => ProductionCalculator.IncomeMultiplier(
                _currencies.GetEarned(_recordsCurrencyId), _perRecord);
    }
}
