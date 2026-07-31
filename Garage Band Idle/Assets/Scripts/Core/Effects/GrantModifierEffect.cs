using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Grants one stat modifier per target the effect names, composed on read by
    // ModifierSystem. This is every numeric effect in the game: a flat tap bonus,
    // one generator's output multiplier, the fan-rate multiplier a cover bar pays,
    // an income multiplier over named currencies. The address is a
    // ModifierTargetKey, so the closed half (which stat) stays code and the open
    // half (which generator or currency) stays content - one class rather than one
    // per stat, and a new stat is a ModifierTarget value plus a composer.
    //
    // The affected ids are declared data, never implied by the effect: a generator
    // producing fans or merch must not inherit a cash income buff just because the
    // buff exists (design doc section 3, the rule the Records buff also follows).
    [Serializable]
    public class GrantModifierEffect : GameEffect
    {
        [SerializeField]
        [Tooltip("Which stat this modifies. The global kinds (TapValue, FanRate) take no qualifiers.")]
        private ModifierTarget _target;

        [SerializeField]
        [Tooltip("Generator or currency ids the modifier applies to, for the kinds that name one. Anything not listed is untouched.")]
        private List<string> _qualifiers = new();

        [SerializeField]
        private ModifierOperation _operation;

        [SerializeField]
        [Tooltip("Multiply: 1.5 for +50%. Add: a flat amount.")]
        private double _value;

        public ModifierTarget Target => _target;
        public IReadOnlyList<string> Qualifiers => _qualifiers;
        public ModifierOperation Operation => _operation;
        public double Value => _value;

        public GrantModifierEffect() { }

        public GrantModifierEffect(ModifierTarget target, ModifierOperation operation, double value,
            List<string> qualifiers = null)
        {
            _target = target;
            _operation = operation;
            _value = value;
            _qualifiers = qualifiers ?? new List<string>();
        }

        // one grant per declared qualifier, so the effect reaches exactly what it
        // names; a global kind is a single grant carrying no qualifier
        public override void Apply(EffectContext context, ContentScope scope)
        {
            if (!ModifierTargetKey.RequiresQualifier(_target))
            {
                context.Modifiers.Grant(ModifierTargetKey.Global(_target), _operation, scope, _value);
                return;
            }

            foreach (var qualifier in _qualifiers)
                context.Modifiers.Grant(ModifierTargetKey.Of(_target, qualifier), _operation, scope, _value);
        }

        public override void Validate(ConditionContext context, string source)
        {
            if (_target == ModifierTarget.None)
                Debug.LogError($"GameEffect: {source} names no modifier target (uninitialized).");
            if (_operation == ModifierOperation.None)
                Debug.LogError($"GameEffect: {source} names no modifier operation (uninitialized).");

            // an Add of a negative amount subtracts with nothing to restore it, and
            // a non-positive Multiply zeroes or negates the whole product it lands
            // in. The registry refuses both at runtime; this catches the asset.
            if (_operation == ModifierOperation.Multiply && _value <= 0)
                Debug.LogError($"GameEffect: {source} has a non-positive multiplier ({_value}).");
            else if (_operation == ModifierOperation.Add && _value < 0)
                Debug.LogError($"GameEffect: {source} adds a negative amount ({_value}) to {_target}.");

            ValidateQualifiers(context, source);
        }

        // A qualified kind naming nothing addresses nothing, and a qualifier on a
        // global kind would silently address nothing - ModifierTargetKey draws that
        // line, so both mistakes report here instead of at grant time.
        private void ValidateQualifiers(ConditionContext context, string source)
        {
            if (!ModifierTargetKey.RequiresQualifier(_target))
            {
                if (_qualifiers.Count > 0)
                    Debug.LogError($"GameEffect: {source} targets {_target}, which takes no qualifiers, but names {_qualifiers.Count}.");
                return;
            }

            if (_qualifiers.Count == 0)
            {
                Debug.LogError($"GameEffect: {source} targets {_target} but names nothing to affect - the modifier could never apply.");
                return;
            }

            foreach (var qualifier in _qualifiers)
            {
                if (_target == ModifierTarget.CurrencyProduction)
                {
                    context.Currencies.ValidateReference(qualifier, source);
                    continue;
                }

                // prefer the content registry (it covers ids outside the running
                // chapter); unit tests have no database and validate against the
                // live system instead
                var resolves = context.Database != null
                    ? context.Database.Generators.Contains(qualifier)
                    : context.Generators != null && context.Generators.TryGet(qualifier, out _);
                if (!resolves)
                    Debug.LogError($"GameEffect: {source} targets unknown generator id '{qualifier}'.");
            }
        }
    }
}
