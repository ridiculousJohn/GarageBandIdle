using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime home of a chapter's generators: builds Generator state from the
    // chapter's definition list and produces into CurrencyManager on tick.
    // State is keyed by generator id, so the generator set stays open - new
    // generators are new assets, not code. Unlock conditions are validated by
    // the boot validation pass (ContentValidator), not here.
    //
    // Reveal is deliberately absent: whether a generator is on offer is a live
    // read of its unlock condition (Generator.IsUnlocked), asked by whoever
    // renders it, so nothing here has to remember - or fail to forget - that a
    // row was once shown.
    public class GeneratorSystem
    {
        private readonly List<Generator> _generators = new();
        private readonly Dictionary<string, Generator> _byId = new();
        private readonly List<string> _producedCurrencyIds = new();
        private readonly ICurrencies _currencies;
        private readonly ModifierSystem _modifiers;

        // fires whenever any generator's owned count changes (purchases, run
        // resets, restores) - the signal behind ownedCount conditions
        public event Action<Generator> GeneratorOwnedChanged;

        public IReadOnlyList<Generator> All => _generators;

        // Content errors (duplicate/empty ids, unresolvable produces currencies)
        // are reported at load so they surface immediately instead of as
        // silently-never-producing rows.
        public GeneratorSystem(IEnumerable<GeneratorDefinition> definitions, ICurrencies currencies,
            ModifierSystem modifiers)
        {
            _currencies = currencies;
            _modifiers = modifiers;

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
                _currencies.ValidateReference(definition.CostCurrencyId, $"Generator '{definition.Id}' (cost)");

                var generator = new Generator(definition, _modifiers);
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

        // One economy tick: each produced currency gets its generators' summed
        // output, composed with the modifiers targeting that currency's
        // production. A currency nothing targets composes to identity, so a
        // multiplier only ever reaches the currencies it was granted against -
        // a fans or merch producer never inherits a cash buff.
        public void Tick(double seconds)
        {
            foreach (var currencyId in _producedCurrencyIds)
            {
                var composition = _modifiers.For(
                    ModifierTargetKey.Of(ModifierTarget.CurrencyRate, currencyId));
                var perSecond = composition.ApplyTo(ProductionCalculator.TotalPerSecond(_generators, currencyId));
                if (perSecond > BigNumber.Zero)
                    _currencies.Add(currencyId, perSecond * seconds);
            }
        }

        // Run reset (album release, event baseline; design doc section 7):
        // gear and bandmates are re-bought each run, so every owned count
        // returns to zero. All state settles before any notification fires -
        // a subscriber may never observe one generator reset while another
        // still holds its old count (state, then notify).
        public void ResetOwned()
        {
            var changed = new List<Generator>();
            foreach (var generator in _generators)
            {
                if (generator.ResetOwned())
                    changed.Add(generator);
            }
            foreach (var generator in changed)
                generator.NotifyOwnedChanged();
        }

        // Save/load: re-establishes saved counts as one atomic operation -
        // every count settles before any notification fires, so a subscriber
        // never observes a half-restored fleet (state, then notify). An
        // unknown id is stale save data: reported and skipped.
        //
        // REPLACEMENT, not a merge: a generator the snapshot omits is restored to
        // zero, not left holding whatever this fleet had. A new run's empty seed
        // and a load are then the same operation with different data, which is
        // what lets EconomyContext.Restore be the only place the order lives.
        //
        // notify: false defers publication to the context-wide restore, which
        // announces one settled state after projection. The default is unchanged,
        // so every existing caller behaves exactly as before.
        public void RestoreOwned(IReadOnlyDictionary<string, int> ownedById, bool notify = true)
        {
            if (ownedById == null)
            {
                Debug.LogError("GeneratorSystem: RestoreOwned with no saved counts.");
                return;
            }

            var changed = new List<Generator>();
            foreach (var entry in ownedById)
            {
                if (!_byId.TryGetValue(entry.Key, out var generator))
                {
                    Debug.LogError($"GeneratorSystem: RestoreOwned with unknown generator id '{entry.Key}'. Skipping it.");
                    continue;
                }
                if (generator.RestoreOwned(entry.Value))
                    changed.Add(generator);
            }

            // the replacement half: anything the snapshot did not name goes to zero
            foreach (var generator in _generators)
            {
                if (ownedById.ContainsKey(generator.Definition.Id))
                    continue;
                if (generator.RestoreOwned(0))
                    changed.Add(generator);
            }

            if (!notify)
                return;

            foreach (var generator in changed)
                generator.NotifyOwnedChanged();
        }

        // Re-announces every owned count. The notification half of a silent
        // restore: OwnedChanged carries no delta, so replaying it for the whole
        // fleet is a full refresh, which is what a restore is.
        public void RepublishOwned()
        {
            foreach (var generator in _generators)
                generator.NotifyOwnedChanged();
        }

        // Owned counts for a capture, in the chapter's declaration order. Only
        // non-zero counts are recorded: zero is what an absent entry restores to,
        // so writing it would be stating the default twice.
        public IReadOnlyDictionary<string, int> CaptureOwned()
        {
            var owned = new Dictionary<string, int>();
            foreach (var generator in _generators)
            {
                if (generator.Owned > 0)
                    owned.Add(generator.Definition.Id, generator.Owned);
            }
            return owned;
        }
    }
}
