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

    // Which of a currency's three totals a payout reads (design doc 12.5): what
    // this round earned, the balance standing now, or everything ever earned
    // across rounds. Earned this round is the default because a prestige payout
    // is about the run just played; the balance is what a spend can have moved.
    public enum PayoutTotal
    {
        EarnedThisRound,
        Balance,
        Lifetime
    }

    // floor((total / divisor) ^ exponent) over the total `reads` names - chapter
    // 1's album is RootCurve over fans earned this round, the same number as its
    // balance since fans are never spent and both clear with the tier (design
    // doc 5).
    [Serializable]
    public class RootCurveFormula : PayoutFormula
    {
        public Economy.CurrencyDefinition currency;
        public PayoutTotal reads = PayoutTotal.EarnedThisRound;
        public BigNumber divisor = 1;
        public double exponent = 1;   // BigDouble.Pow's power is a double by the library's own signature

        public override BigNumber Compute(GameContext ctx)
        {
            var total = reads switch
            {
                PayoutTotal.Balance => ctx.GetBalance(currency.Id),
                PayoutTotal.Lifetime => ctx.GetLifetimeTotal(currency.Id),
                _ => ctx.GetEarnedTotal(currency.Id),
            };
            return BigNumber.Floor(BigNumber.Pow(total / divisor, exponent));
        }

        public override void Validate(ValidationContext ctx)
        {
            ctx.RequireOnChain(currency, "RootCurveFormula");
            // A negative exponent makes 0^n infinite, and the total IS zero on
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
