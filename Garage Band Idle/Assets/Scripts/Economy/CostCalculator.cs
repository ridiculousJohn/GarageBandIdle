namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The standard idle cost curve: cost = baseCost × growth^owned.
    public static class CostCalculator
    {
        public static BigNumber Cost(GeneratorDefinition definition, int owned)
            => (BigNumber)definition.BaseCost * BigNumber.Pow(definition.CostGrowth, owned);
    }
}
