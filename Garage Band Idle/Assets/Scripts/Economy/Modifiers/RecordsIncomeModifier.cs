namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The permanent global income buff (design doc section 5): 1 + perRecord x
    // records, multiplying the production of one currency the chapter's
    // recordBuff declares. One instance per declared currency, so production of
    // anything undeclared is untouched.
    //
    // Derived rather than granted: nothing applies it, it is on from boot and
    // tracks the Records balance. That balance survives an album release
    // because Records sit in a currency group with resetsOnAlbumRelease false,
    // which is the single answer to how long this buff lasts.
    public class RecordsIncomeModifier : DerivedModifier
    {
        private readonly CurrencyManager _currencies;
        private readonly string _recordsCurrencyId;
        private readonly double _perRecord;
        private readonly ModifierTargetKey _target;

        public RecordsIncomeModifier(CurrencyManager currencies, string recordsCurrencyId,
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
            => ProductionCalculator.IncomeMultiplier(_currencies.Get(_recordsCurrencyId), _perRecord);
    }
}
