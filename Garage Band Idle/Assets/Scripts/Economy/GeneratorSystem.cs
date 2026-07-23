using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime home of a chapter's generators: builds Generator state from the
    // chapter's definition list, produces into CurrencyManager on tick, and
    // reveals generators as their unlock conditions are met. State is keyed by
    // generator id, so the generator set stays open — new generators are new
    // assets, not code. Unlock conditions are validated by the boot validation
    // pass (ContentValidator), not here.
    public class GeneratorSystem
    {
        private readonly List<Generator> _generators = new();
        private readonly Dictionary<string, Generator> _byId = new();
        private readonly List<string> _producedCurrencyIds = new();
        private readonly CurrencyManager _currencies;

        // fires once per generator when its unlock conditions are first met
        public event Action<Generator> GeneratorUnlocked;

        // fires whenever any generator's owned count changes (purchases now,
        // run resets later) — the signal behind ownedCount conditions
        public event Action<Generator> GeneratorOwnedChanged;

        public IReadOnlyList<Generator> All => _generators;

        // Content errors (duplicate/empty ids, unresolvable produces currencies)
        // are reported at load so they surface immediately instead of as
        // silently-never-producing rows.
        public GeneratorSystem(IEnumerable<GeneratorDefinition> definitions, CurrencyManager currencies)
        {
            _currencies = currencies;

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    Debug.LogError("GeneratorSystem: chapter generator list contains a null entry. Skipping it.");
                    continue;
                }
                if (string.IsNullOrEmpty(definition.Id))
                {
                    Debug.LogError($"GeneratorSystem: GeneratorDefinition asset '{definition.name}' has an empty id. Skipping it.");
                    continue;
                }
                if (_byId.TryGetValue(definition.Id, out var existing))
                {
                    Debug.LogError($"GeneratorSystem: duplicate generator id '{definition.Id}' on assets '{definition.name}' and '{existing.Definition.name}'. Keeping '{existing.Definition.name}'.");
                    continue;
                }

                _currencies.ValidateReference(definition.ProducesCurrencyId, $"Generator '{definition.Id}' (produces)");

                var generator = new Generator(definition);
                generator.OwnedChanged += () => GeneratorOwnedChanged?.Invoke(generator);
                _generators.Add(generator);
                _byId.Add(definition.Id, generator);
                if (!_producedCurrencyIds.Contains(definition.ProducesCurrencyId))
                    _producedCurrencyIds.Add(definition.ProducesCurrencyId);
            }
        }

        public Generator Get(string id)
        {
            if (_byId.TryGetValue(id, out var generator))
                return generator;

            Debug.LogError($"GeneratorSystem: unknown generator id '{id}'.");
            return null;
        }

        // silent lookup for gate evaluation, which may probe ids repeatedly
        public bool TryGet(string id, out Generator generator) => _byId.TryGetValue(id, out generator);

        // one economy tick: each produced currency gets its generators' summed
        // output times the global income multiplier
        public void Tick(double seconds, BigNumber incomeMultiplier)
        {
            foreach (var currencyId in _producedCurrencyIds)
            {
                var perSecond = ProductionCalculator.TotalPerSecond(_generators, currencyId, incomeMultiplier);
                if (perSecond > BigNumber.Zero)
                    _currencies.Add(currencyId, perSecond * seconds);
            }
        }

        // reveals any still-locked generator whose condition now holds; called on
        // tick and after purchases (an ownedCount unlock can trip mid-tick)
        public void EvaluateUnlocks(ConditionContext context)
        {
            foreach (var generator in _generators)
            {
                if (generator.Unlocked)
                    continue;
                if (!ConditionEvaluator.IsMet(generator.Definition.Unlock, context))
                    continue;

                generator.MarkUnlocked();
                GeneratorUnlocked?.Invoke(generator);
            }
        }

    }
}
