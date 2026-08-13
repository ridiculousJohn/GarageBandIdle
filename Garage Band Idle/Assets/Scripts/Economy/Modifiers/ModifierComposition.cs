namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The composed effect of every modifier reaching one number: their product.
    //
    // There is no Add beside the Multiply, so there is no application order for two
    // systems to disagree about. A flat bonus is a contribution to the number rather
    // than a modifier on it (design doc section 12, rule 11), which means the base
    // already IS the sum of the flat parts.
    public readonly struct ModifierComposition
    {
        public static readonly ModifierComposition Identity = new(BigNumber.One);

        public BigNumber Multiply { get; }

        public ModifierComposition(BigNumber multiply)
        {
            Multiply = multiply;
        }

        public BigNumber ApplyTo(BigNumber baseValue) => baseValue * Multiply;
    }
}
