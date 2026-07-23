using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Jam tap value (design doc section 3): cash per tap is the chapter's
    // base times the tap-reward multiplier stacks. Multipliers are tracked
    // PER SCOPE, mirroring FanSystem: the run reset (album release, event
    // baseline) clears the run-scoped stack and keeps the permanent-in-
    // chapter one — collapsing scopes into one number would make "reset
    // run-scoped effects" unimplementable. Flat tap-value adds
    // (TapValueAddPayload) arrive with the buff slice and will feed in here.
    public class TapSystem
    {
        private readonly double _baseValue;

        private BigNumber _runMultiplier = BigNumber.One;
        private BigNumber _permanentMultiplier = BigNumber.One;

        public TapSystem(double baseValue)
        {
            _baseValue = baseValue;
        }

        // fails closed on a negative base — invalid data, boot validation
        // reports it: a tap must never drain cash
        public BigNumber Value => _baseValue < 0
            ? BigNumber.Zero
            : (BigNumber)_baseValue * _runMultiplier * _permanentMultiplier;

        public void MultiplyValue(double factor, ContentScope scope)
        {
            // fail closed on broken content: a non-positive factor would zero
            // or negate the whole multiplicative stack for the rest of the run
            // (boot validation reports it)
            if (factor <= 0)
            {
                Debug.LogError($"TapSystem: MultiplyValue with non-positive factor '{factor}'. Ignoring.");
                return;
            }

            switch (scope)
            {
                case ContentScope.Run:
                    _runMultiplier *= factor;
                    break;
                case ContentScope.PermanentInChapter:
                    _permanentMultiplier *= factor;
                    break;
                default:
                    // fail closed on broken content: boot validation reports a
                    // None scope; an unscoped multiplier must never apply
                    Debug.LogError($"TapSystem: MultiplyValue with scope '{scope}'. Ignoring.");
                    break;
            }
        }

        // the run reset (album release, event baseline) clears only the
        // run-scoped stack; permanent-in-chapter rewards survive
        public void ResetRunScopedMultipliers() => _runMultiplier = BigNumber.One;
    }
}
