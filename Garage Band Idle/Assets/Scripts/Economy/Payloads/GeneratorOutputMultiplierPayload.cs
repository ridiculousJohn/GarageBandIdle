using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // JSON effect "generatorOutputMultiplier": multiplies one generator's
    // output (amp_strings, kit_upgrade). Run-scoped buff.
    [Serializable]
    public class GeneratorOutputMultiplierPayload : UpgradePayload
    {
        [SerializeField]
        [DefinitionId(typeof(GeneratorDefinition))]
        [Tooltip("Target generator id.")]
        private string _generatorId;

        [SerializeField]
        [Tooltip("Output multiplier, e.g. 2 for x2.")]
        private double _value;

        public string GeneratorId => _generatorId;
        public double Value => _value;

        public GeneratorOutputMultiplierPayload() { }

        public GeneratorOutputMultiplierPayload(string generatorId, double value)
        {
            _generatorId = generatorId;
            _value = value;
        }

        // the target names one generator, so the buff can never reach another's
        // output however many generators produce the same currency
        public override void Apply(UpgradePayloadContext context, ContentScope scope)
            => context.Modifiers.Grant(ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, _generatorId),
                ModifierOperation.Multiply, scope, _value);

        public override void Validate(ConditionContext context, string source)
        {
            // a non-positive multiplier would zero or negate the generator's
            // whole output product (the registry refuses it at runtime)
            if (_value <= 0)
                Debug.LogError($"UpgradePayload: {source} has a non-positive multiplier ({_value}).");

            // prefer the content registry (covers ids outside the running chapter);
            // unit tests have no database and validate against the live system
            var resolves = context.Database != null
                ? context.Database.Generators.Contains(_generatorId)
                : context.Generators != null && context.Generators.TryGet(_generatorId, out _);
            if (!resolves)
                Debug.LogError($"UpgradePayload: {source} targets unknown generator id '{_generatorId}'.");
        }
    }
}
