namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Band size raises the fan rate (design doc section 6): each owned unit of a
    // bandmate generator adds the chapter's per-bandmate bonus to fans/sec. Gear
    // never counts, which is what IsBandmate declares - the rule is read off the
    // generator as data, not from a drummer/bassist/guitarist name list.
    //
    // Derived rather than granted, for the same reason RecordsIncomeModifier is:
    // nothing applies it, it is on from boot, and its value is a function of a
    // fact that already exists (owned counts). Its lifetime is that fact's, which
    // is why it carries no scope - an album release resets owned counts and this
    // follows for free, with no second mechanism deciding whether the fan rate
    // survives (rule 11).
    //
    // Adds rather than multiplies, so the composed rate is
    // (baseFansPerSec + perBandmate x bandmates) x coverBarRewards - the
    // composition ModifierComposition already defines.
    public class BandmateFanRateModifier : DerivedModifier
    {
        private readonly GeneratorSystem _generators;
        private readonly double _perBandmateOwnedBonus;
        private readonly ModifierTargetKey _target;

        // The fans currency is named by the chapter, never known here: this
        // raises the rate of whichever currency that chapter treats as fans, so
        // a chapter calling it something else needs no code.
        public BandmateFanRateModifier(GeneratorSystem generators, double perBandmateOwnedBonus,
            string fansCurrencyId)
        {
            _generators = generators;
            _perBandmateOwnedBonus = perBandmateOwnedBonus;
            _target = ModifierTargetKey.Of(ModifierTarget.CurrencyRate, fansCurrencyId);
        }

        public override ModifierTargetKey Target => _target;

        public override ModifierOperation Operation => ModifierOperation.Add;

        public override BigNumber Value => (BigNumber)(_perBandmateOwnedBonus * BandmateCount);

        // owned units across bandmate generators (IsBandmate - gear never counts)
        private int BandmateCount
        {
            get
            {
                var count = 0;
                foreach (var generator in _generators.All)
                {
                    if (generator.Definition.IsBandmate)
                        count += generator.Owned;
                }
                return count;
            }
        }
    }
}
