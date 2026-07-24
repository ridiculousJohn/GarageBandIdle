using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Jam tap value (design doc section 3): the chapter's base composed with
    // every modifier targeting TapValue. The stacks live in ModifierSystem, not
    // here - flat adds (stage_presence) and multipliers (event-tier rewards)
    // compose by the one rule, per scope, and the run reset that clears the
    // run-scoped ones is that system's single entry point.
    public class TapSystem
    {
        private static readonly ModifierTargetKey Target = ModifierTargetKey.Global(ModifierTarget.TapValue);

        private readonly double _baseValue;
        private readonly ModifierSystem _modifiers;

        public TapSystem(double baseValue, ModifierSystem modifiers)
        {
            _baseValue = baseValue;
            _modifiers = modifiers;
            _modifiers.Changed += HandleModifierChanged;
        }

        // UI listens here, nothing polls; fires when the composed value moves
        public event Action ValueChanged;

        // Fails closed on tuning that would make a tap drain cash: a negative
        // base (invalid data - boot validation reports it) or any composition
        // landing below zero yields nothing, and no multiplier resurrects it.
        public BigNumber Value
        {
            get
            {
                var value = _modifiers.For(Target).ApplyTo(_baseValue);
                return value < BigNumber.Zero ? BigNumber.Zero : value;
            }
        }

        private void HandleModifierChanged(ModifierTargetKey target)
        {
            if (target.Equals(Target))
                ValueChanged?.Invoke();
        }
    }
}
