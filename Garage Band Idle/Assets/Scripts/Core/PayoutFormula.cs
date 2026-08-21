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

        public override void Validate(ValidationContext ctx)
        {
            if (value < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"ConstantFormula value is {value} - a payout never subtracts.");
        }
    }

    // floor((balance / divisor) ^ exponent) - Chapter 1's album payout is
    // RootCurve(fans, 5, 0.5) (design doc 5).
    [Serializable]
    public class RootCurveFormula : PayoutFormula
    {
        public Economy.CurrencyDefinition currency;
        public BigNumber divisor = 1;
        public double exponent = 1;   // BigDouble.Pow's power is a double by the library's own signature

        public override BigNumber Compute(GameContext ctx) =>
            BigNumber.Floor(BigNumber.Pow(ctx.GetBalance(currency.Id) / divisor, exponent));

        public override void Validate(ValidationContext ctx)
        {
            var home = ctx.RequireOnChain(currency, "RootCurveFormula");
            if (home != null)
                ctx.RecordFormulaRead(currency.Id, home); // input for the reads-zeros warn (12.12)
            // A negative exponent makes 0^n infinite, and the balance IS zero on
            // the first read after a reset - BigNumber refuses infinities at
            // construction, so this would throw on the first payout.
            if (ctx.RequireFiniteDouble(exponent, "RootCurveFormula exponent") && exponent < 0)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"RootCurveFormula exponent is {exponent} - a negative exponent is infinite at a zero balance.");
            if (divisor <= BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"RootCurveFormula divisor is {divisor} - a nonpositive divisor makes the payout infinite or undefined.");
        }
    }
}
