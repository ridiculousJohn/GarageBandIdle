using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
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
        [Tooltip("Which stat this modifies.")]
        private ModifierTarget _target;

        [SerializeField]
        [ModifierQualifierId(nameof(_target))]
        [Tooltip("Ids the modifier applies to. LEAVE EMPTY to reach every member of the target's family in scope - listing ids narrows it to exactly those.")]
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

        // One grant per declared qualifier, so the effect reaches exactly what it
        // names; NO qualifiers is a single unqualified grant, which by rule 11
        // reaches every member of the kind in scope. That is the difference
        // between "+50% to the amp" and "+50% to everything", authored as the
        // presence or absence of a list rather than as two effect kinds.
        //
        // This is the effect the rebuild boundaries exist FOR: the store is
        // cleared before every projection (ModifierSystem.ResetGranted), so
        // re-granting rebuilds rather than compounds. Grants are deliberately not
        // idempotent on their own - clearing first is what makes replaying them
        // exact.
        public override void Apply(EffectContext context, ContentScope scope)
        {
            if (_qualifiers.Count == 0)
            {
                context.Modifiers.Grant(ModifierTargetKey.All(_target), _operation, scope, _value);
                return;
            }

            foreach (var qualifier in _qualifiers)
                context.Modifiers.Grant(ModifierTargetKey.Of(_target, qualifier), _operation, scope, _value);
        }

        public override void Validate(ConditionContext context, string source)
        {
            // A serialized enum is an int, so a hand-edited or un-migrated asset can
            // hold a value no member defines. The specialized payload classes this
            // replaced could not express that - each hardcoded its target and
            // operation - so generalizing made two new broken states representable
            // and they have to be named here rather than only failing closed later.
            if (!Enum.IsDefined(typeof(ModifierTarget), _target))
                Debug.LogError($"GameEffect: {source} has modifier target {(int)_target}, which no ModifierTarget defines.");
            else if (_target == ModifierTarget.None)
                Debug.LogError($"GameEffect: {source} names no modifier target (uninitialized).");

            if (!Enum.IsDefined(typeof(ModifierOperation), _operation))
                Debug.LogError($"GameEffect: {source} has modifier operation {(int)_operation}, which no ModifierOperation defines.");
            else if (_operation == ModifierOperation.None)
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

        // An empty list is legal and means "every member in scope" (rule 11), so
        // the only mistakes left are a qualifier the target's family cannot
        // resolve and a qualifier on a kind with no family at all.
        private void ValidateQualifiers(ConditionContext context, string source)
        {
            if (_qualifiers.Count == 0)
                return;

            // which registry resolves a qualifier follows from the target's declared
            // definition family, the same mapping the inspector's dropdown reads
            var family = ModifierTargetKey.QualifierDefinitionType(_target);
            if (family == null)
            {
                Debug.LogError($"GameEffect: {source} targets {_target}, which has no id family to resolve a qualifier against, but names {_qualifiers.Count}. Leave the list empty to reach everything in scope.");
                return;
            }

            foreach (var qualifier in _qualifiers)
            {
                if (family == typeof(CurrencyDefinition))
                {
                    context.Currencies.ValidateReference(qualifier, source);
                    continue;
                }

                // prefer the content registry (it covers ids outside the running
                // chapter); unit tests have no database and validate against the
                // live system instead
                if (family == typeof(BarGroupDefinition))
                {
                    if (context.Database != null && !context.Database.BarGroups.Contains(qualifier))
                        Debug.LogError($"GameEffect: {source} targets unknown bar group id '{qualifier}'.");
                    continue;
                }

                var resolves = context.Database != null
                    ? context.Database.Generators.Contains(qualifier)
                    : context.Generators != null && context.Generators.TryGet(qualifier, out _);
                if (!resolves)
                    Debug.LogError($"GameEffect: {source} targets unknown generator id '{qualifier}'.");
            }
        }
    }
}
