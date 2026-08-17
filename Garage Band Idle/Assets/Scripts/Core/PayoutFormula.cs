using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // The AMOUNT a computed-grant action awards (design doc section 5, rule 14):
    // polymorphic and subclass-picked like Condition, because a payout curve is
    // authored content, not code a chapter branches to. A formula is math over
    // balances the executing scope can reach - its own and outward, the same
    // surface the action pays through - and nothing else: no flags, no counts,
    // no state of its own.
    //
    // Two halves, deliberately. Evaluate is the press and the preview - one
    // implementation, so a button's promise and the payout it banks cannot
    // disagree. InputCurrencyIds is the INSPECTION contract: boot validation
    // enforces where a rung may be filed (its formulas' inputs must be readable
    // from its scope, and must live in scopes its reset clears) without ever
    // switching on a concrete formula type - a new curve states what it reads
    // and the spatial rules apply to it unchanged.
    [Serializable]
    public abstract class PayoutFormula
    {
        // the computed amount, read through the executing scope's own surface;
        // zero is a legal answer (a release at zero fans still resets the run),
        // a negative one is broken math the caller refuses
        public abstract BigNumber Evaluate(EffectContext context);

        // every currency id this formula reads - the complete list, because the
        // spatial validation walks exactly these
        public abstract IReadOnlyList<string> InputCurrencyIds { get; }

        // load-time check that every id resolves and the tuning is sane;
        // failures report loudly with the owning content named in source
        public abstract void Validate(ConditionContext context, string source);
    }

    // The early-chapter album payout (design doc section 5, the JSON's
    // recordsFormula): floor((balance / divisor) ^ 0.5) over one source
    // currency. Chapter 1 reads fans with divisor 5, so 50 fans banks the first
    // meaningful payout (3). The Ch. 6+ variant that reads catalog quality is a
    // different formula class, not a parameter of this one.
    [Serializable]
    public class RootOfBalanceFormula : PayoutFormula
    {
        [SerializeField]
        [DefinitionId(typeof(CurrencyDefinition))]
        [Tooltip("Currency whose balance the curve reads - Ch1: the run's fans.")]
        private string _currencyId;

        [SerializeField]
        [Tooltip("Balance per payout unit before the root - Ch1: 5.")]
        private double _divisor = 5;

        public string CurrencyId => _currencyId;
        public double Divisor => _divisor;

        // Unity's serializer needs a parameterless constructor on plain classes
        public RootOfBalanceFormula() { }

        public RootOfBalanceFormula(string currencyId, double divisor = 5)
        {
            _currencyId = currencyId;
            _divisor = divisor;
        }

        // Clamped at zero before the root so a sub-zero balance (impossible
        // today: production never drains) can never produce a NaN payout. A
        // non-positive divisor is broken tuning (Validate reports it) and fails
        // closed to zero rather than dividing by it.
        public override BigNumber Evaluate(EffectContext context)
        {
            if (_divisor <= 0)
                return BigNumber.Zero;

            var balance = context.Currencies.Get(_currencyId);
            return BigNumber.Floor(BigNumber.Pow(
                BigNumber.Max(balance, BigNumber.Zero) / _divisor, 0.5));
        }

        public override IReadOnlyList<string> InputCurrencyIds => new[] { _currencyId };

        public override void Validate(ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(_currencyId))
            {
                Debug.LogError($"PayoutFormula: {source} has a rootOfBalance formula with an empty currency id.");
                return;
            }

            context.Currencies?.ValidateReference(_currencyId, source);

            if (_divisor <= 0)
                Debug.LogError($"PayoutFormula: {source} has a rootOfBalance formula with a non-positive divisor ({_divisor}) - the payout would be nothing, always.");
        }
    }
}
