using System;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // JSON type "ownedCount": the owned count of a generator is at least Value.
    [Serializable]
    public class OwnedCountCondition : Condition
    {
        [SerializeField]
        [DefinitionId(typeof(GeneratorDefinition))]
        private string _generatorId;

        [SerializeField]
        private double _value;

        public string GeneratorId => _generatorId;
        public double Value => _value;

        public OwnedCountCondition() { }

        public OwnedCountCondition(string generatorId, double value)
        {
            _generatorId = generatorId;
            _value = value;
        }

        public override bool Evaluate(ConditionContext context)
            => context.Generators != null
                && context.Generators.TryGet(_generatorId, out var generator)
                && generator.Owned >= _value;

        public override void Validate(ConditionContext context, string source)
        {
            // prefer the content registry (covers ids outside the running chapter);
            // unit tests have no database and validate against the live system
            var resolves = context.Database != null
                ? context.Database.Generators.Contains(_generatorId)
                : context.Generators != null && context.Generators.TryGet(_generatorId, out _);
            if (!resolves)
                Debug.LogError($"Condition: {source} references unknown generator id '{_generatorId}'.");
        }
    }
}
