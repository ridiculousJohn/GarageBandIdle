using System;

namespace RidiculousGaming.GarageBandIdle
{
    // Computes an amount from readable state (design doc 12.5). Pure functions,
    // so UI previews call the same code the rung runs - one implementation, no
    // drift.
    [Serializable]
    public abstract class PayoutFormula
    {
        public abstract BigNumber Compute(GameContext ctx);

        // Load-time reference and reach checks (design doc 12.12), driven
        // through the owning action's Validate.
        public virtual void Validate(ValidationContext ctx) { }
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

        public override void Validate(ValidationContext ctx)
        {
            var home = ctx.RequireChainCurrency(currencyId, "RootCurveFormula");
            if (home != null)
                ctx.RecordFormulaRead(currencyId, home); // input for the reads-zeros warn (12.12)
        }
    }
}
