using System;

namespace RidiculousGaming.GarageBandIdle
{
    // Computes an amount from readable state (design doc 12.5). Pure functions,
    // so UI previews call the same code the press runs - one implementation, no
    // drift.
    [Serializable]
    public abstract class PayoutFormula
    {
        public abstract BigNumber Compute(GameContext ctx);
        public virtual void Validate(IDefinitionSource defs) { }
    }

    [Serializable]
    public class ConstantFormula : PayoutFormula
    {
        public BigNumber value;

        public override BigNumber Compute(GameContext ctx) => value;
    }

    // floor((balance / divisor) ^ exponent) - Chapter 1's album payout is
    // RootCurve(fans, 5, 0.5) (design doc 5).
    [Serializable]
    public class RootCurveFormula : PayoutFormula
    {
        [DefinitionId(typeof(Economy.CurrencyDefinition))] public string currencyId;
        public BigNumber divisor = 1;
        public double exponent = 1;   // BigDouble.Pow's power is a double by the library's own signature

        public override BigNumber Compute(GameContext ctx) =>
            BigNumber.Floor(BigNumber.Pow(ctx.GetBalance(currencyId) / divisor, exponent));
    }
}
