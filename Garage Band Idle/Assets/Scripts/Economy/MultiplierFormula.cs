using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The formula half of "an effect's factor is a constant or a formula"
    // (design doc 12.2): a factor computed from stored facts on every read,
    // returning BigNumber because the results are unbounded products of live
    // facts rather than authored doubles.
    //
    // Compute receives the GATHER-ORIGIN context - the source scope in stage 1,
    // the currency home in stage 2 - never a context rebased to the declaring
    // scope. That is a deliberate asymmetry with 12.4, where conditions and
    // action lists evaluate in their declaring scope: a multiplier is addressed
    // to a NUMBER, and the number's identity includes the chain it resolves on.
    // RoadieActiveBoost is unimplementable from a root-rebased context, since
    // root's chain holds no chapter. Reads stay chain-only either way, so no
    // sibling reach opens up.
    [Serializable]
    public abstract class MultiplierFormula
    {
        public abstract BigNumber Compute(GameContext ctx);

        // Load-time reference and range checks (design doc 12.12), driven
        // through the owning definition's validation.
        public virtual void Validate(ValidationContext ctx) { }
    }

    // 1 + coefficient * balance: additive within the term, so Demo Tapes at 20
    // with 0.02 per Demo Tape gives 1.4x (chapter 1's demo_tapes_income).
    [Serializable]
    public class LinearOnBalance : MultiplierFormula
    {
        public CurrencyDefinition currency;
        public BigNumber coefficient;

        public override BigNumber Compute(GameContext ctx) =>
            BigNumber.One + coefficient * ctx.GetBalance(currency.Id);

        public override void Validate(ValidationContext ctx)
        {
            ctx.RequireOnChain(currency, "LinearOnBalance");
            if (coefficient < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"LinearOnBalance coefficient is {coefficient} - a formula factor never shrinks with the fact it derives from.");
        }
    }

    // 1 + coefficient * the generator's count, read outward from the gather
    // origin - the source scope in stage 1, the target's home in stage 2 - like
    // every count read (design doc 12.2). A generator that multiplies another by
    // how many of it are owned is a permanent modifier carrying this; the flag
    // picks the purchased count alone or purchased plus granted, since the two
    // are separate facts.
    [Serializable]
    public class LinearOnOwnedCount : MultiplierFormula
    {
        public GeneratorDefinition generator;
        public BigNumber coefficient;
        public bool purchasedOnly;

        public override BigNumber Compute(GameContext ctx) =>
            BigNumber.One + coefficient * (purchasedOnly
                ? (BigNumber)ctx.GetPurchasedCount(generator.Id)
                : ctx.GetOwnedCount(generator.Id));

        public override void Validate(ValidationContext ctx)
        {
            ctx.RequireOnChain(generator, "LinearOnOwnedCount");
            if (coefficient < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"LinearOnOwnedCount coefficient is {coefficient} - a formula factor never shrinks with the fact it derives from.");
        }
    }

    // How a debt factor combines the balances it reads: their sum, or the
    // largest of them.
    public enum DebtCombine { Sum, Maximum }

    // max(0, 1 - coefficient * debt), where debt is the listed balances read
    // outward from the gather origin and combined by the mode: a factor that
    // shrinks as debt grows and never goes below zero (design doc 12.2). A
    // show authors coefficient as one over its own base yield, which makes the
    // factor equal to subtracting the debt from that show's yield before every
    // other factor applies. The mode is fixed per effect; content changes mode
    // with two modifiers, one per mode, switched by appliesWhen. Relief is an
    // optional balance that divides the combined debt once it passes one, so a
    // relief at or below one changes nothing.
    [Serializable]
    public class DebtFactor : MultiplierFormula
    {
        public List<CurrencyDefinition> currencies = new();
        public DebtCombine combine = DebtCombine.Sum;
        public BigNumber coefficient;
        public CurrencyDefinition relief;     // optional; null means none

        public override BigNumber Compute(GameContext ctx)
        {
            var debt = BigNumber.Zero;
            foreach (var currency in currencies)
            {
                var balance = ctx.GetBalance(currency.Id);
                debt = combine == DebtCombine.Sum ? debt + balance : BigNumber.Max(debt, balance);
            }
            if (relief != null)
            {
                var r = ctx.GetBalance(relief.Id);
                if (r > BigNumber.One)
                    debt = debt / r;
            }
            return BigNumber.Max(BigNumber.Zero, BigNumber.One - coefficient * debt);
        }

        public override void Validate(ValidationContext ctx)
        {
            if (currencies.Count == 0)
                ctx.AddError(ValidationCheck.NullEntry,
                    "DebtFactor lists no currencies - a debt factor reading no debt is inert.");
            for (var i = 0; i < currencies.Count; i++)
            {
                if (currencies[i] == null)
                    ctx.AddError(ValidationCheck.NullEntry, $"DebtFactor has a null currencies[{i}] entry.");
                else
                    ctx.RequireOnChain(currencies[i], "DebtFactor");
            }
            if (coefficient <= BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"DebtFactor coefficient is {coefficient} - a nonpositive coefficient leaves the factor at one or raises production with debt.");
            if (relief != null)
                ctx.RequireOnChain(relief, "DebtFactor");
        }
    }

    // 1 + coefficient * log10(1 + balance): a factor that keeps growing with
    // the balance at a falling rate, and is exactly one at a zero balance
    // (design doc 12.2).
    [Serializable]
    public class LogOnBalance : MultiplierFormula
    {
        public CurrencyDefinition currency;
        public BigNumber coefficient;

        public override BigNumber Compute(GameContext ctx) =>
            BigNumber.One + coefficient * BigNumber.Log10(BigNumber.One + ctx.GetBalance(currency.Id));

        public override void Validate(ValidationContext ctx)
        {
            ctx.RequireOnChain(currency, "LogOnBalance");
            if (coefficient < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"LogOnBalance coefficient is {coefficient} - a formula factor never shrinks with the fact it derives from.");
        }
    }

    // One over the balance, read outward from the gather origin (design doc
    // 12.2). Zero while the balance is zero, so a value that has not been
    // recorded pays nothing rather than dividing by zero. A rate entry whose
    // value is an amount per run, under this factor over a recorded run time,
    // is that amount per second.
    [Serializable]
    public class InverseOfBalance : MultiplierFormula
    {
        public CurrencyDefinition currency;

        public override BigNumber Compute(GameContext ctx)
        {
            var balance = ctx.GetBalance(currency.Id);
            return balance > BigNumber.Zero ? BigNumber.One / balance : BigNumber.Zero;
        }

        public override void Validate(ValidationContext ctx) => ctx.RequireOnChain(currency, "InverseOfBalance");
    }

    // The product over chapters of (1 + perRoadie * stationed there) - additive
    // within a chapter, multiplicative across them (design doc 8.2). The
    // concavity is the point: the next Roadie is worth more in a chapter that
    // holds fewer, so spreading beats stacking for the global factor, and
    // Roadies on a cleared chapter still help the one being played.
    [Serializable]
    public class RoadieTotalBoost : MultiplierFormula
    {
        public BigNumber perRoadie;

        public override BigNumber Compute(GameContext ctx)
        {
            var product = BigNumber.One;
            foreach (var stationed in ctx.Scope.Root().roadieAllocation.Values)
                product *= BigNumber.One + perRoadie * stationed;
            return product;
        }

        public override void Validate(ValidationContext ctx)
        {
            if (perRoadie < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"RoadieTotalBoost perRoadie is {perRoadie} - a formula factor never shrinks with the fact it derives from.");
        }
    }

    // The played chapter's own factor, counted on top of the global one (design
    // doc 8.2's double-count), which is what makes stationing Roadies speed the
    // chapter being worked. "Active" means the chapter on the resolution chain -
    // the gather-origin ruling above is what makes it derivable at all.
    [Serializable]
    public class RoadieActiveBoost : MultiplierFormula
    {
        public BigNumber perRoadie;

        public override BigNumber Compute(GameContext ctx)
        {
            var chapter = ctx.Scope.Chapter();
            if (chapter == null)
                return BigNumber.One;           // resolving off any chapter's chain: no local factor
            if (!ctx.Scope.Root().roadieAllocation.TryGetValue(chapter.ScopeId, out var stationed))
                return BigNumber.One;
            return BigNumber.One + perRoadie * stationed;
        }

        public override void Validate(ValidationContext ctx)
        {
            if (perRoadie < BigNumber.Zero)
                ctx.AddError(ValidationCheck.NumericRange,
                    $"RoadieActiveBoost perRoadie is {perRoadie} - a formula factor never shrinks with the fact it derives from.");
        }
    }
}
