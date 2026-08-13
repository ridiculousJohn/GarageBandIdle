namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The composed effect of every modifier reaching one number: their product.
    //
    // It used to carry an Add beside the Multiply and define the order the two
    // applied in - (base + adds) x multipliers - which was a rule two systems
    // could disagree about only because there were two kinds of thing to order.
    // A flat bonus is now a contribution to the number rather than a modifier on
    // it (design doc section 12, rule 11), so the base already IS the sum of the
    // adds and there is no ordering left to state.
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
