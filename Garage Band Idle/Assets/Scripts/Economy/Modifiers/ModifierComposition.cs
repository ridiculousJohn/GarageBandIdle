namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The composed effect of every modifier on one target. The composition rule
    // lives here and nowhere else - (base + adds) x multipliers - so two
    // systems can never disagree about the order their modifiers apply in.
    public readonly struct ModifierComposition
    {
        public static readonly ModifierComposition Identity = new(BigNumber.Zero, BigNumber.One);

        public BigNumber Add { get; }
        public BigNumber Multiply { get; }

        public ModifierComposition(BigNumber add, BigNumber multiply)
        {
            Add = add;
            Multiply = multiply;
        }

        public BigNumber ApplyTo(BigNumber baseValue) => (baseValue + Add) * Multiply;
    }
}
