using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RidiculousGaming.GarageBandIdle
{
    // Runtime discovery of every definition ScriptableObject (design doc section
    // 12, rule 10): each type loads by its Addressables label into an id-keyed
    // registry, so the content set stays open — new assets are picked up with no
    // code or registration changes and nothing holds a direct asset reference.
    // Label load order is arbitrary; display/processing order comes from the
    // chapter's id lists, never from a registry.
    public class ContentDatabase
    {
        public Registry<CurrencyGroupDefinition> CurrencyGroups { get; }
        public Registry<CurrencyDefinition> Currencies { get; }
        public Registry<ChapterDefinition> Chapters { get; }
        public Registry<SectionDefinition> Sections { get; }
        public Registry<GeneratorDefinition> Generators { get; }
        public Registry<UpgradeDefinition> Upgrades { get; }
        public Registry<BarDefinition> Bars { get; }
        public Registry<BarGroupDefinition> BarGroups { get; }
        public Registry<EventDefinition> Events { get; }
        public Registry<RewardDefinition> Rewards { get; }

        public ContentDatabase()
        {
            CurrencyGroups = Load<CurrencyGroupDefinition>(ContentLabels.CurrencyGroup, d => d.Id);
            Currencies = Load<CurrencyDefinition>(ContentLabels.Currency, d => d.Id);
            Chapters = Load<ChapterDefinition>(ContentLabels.Chapter, d => d.Id);
            Sections = Load<SectionDefinition>(ContentLabels.Section, d => d.Id);
            Generators = Load<GeneratorDefinition>(ContentLabels.Generator, d => d.Id);
            Upgrades = Load<UpgradeDefinition>(ContentLabels.Upgrade, d => d.Id);
            Bars = Load<BarDefinition>(ContentLabels.Bar, d => d.Id);
            BarGroups = Load<BarGroupDefinition>(ContentLabels.BarGroup, d => d.Id);
            Events = Load<EventDefinition>(ContentLabels.Event, d => d.Id);
            Rewards = Load<RewardDefinition>(ContentLabels.Reward, d => d.Id);
        }

        // Synchronous label load, held for the app's lifetime (definitions are
        // needed as long as the game runs, so handles are never released).
        // WaitForCompletion keeps bootstrap simple; this becomes async behind a
        // loading screen in a later slice.
        private static Registry<T> Load<T>(string label, Func<T, string> idSelector) where T : ScriptableObject
        {
            IList<T> assets;
            try
            {
                assets = Addressables.LoadAssetsAsync<T>(label, null).WaitForCompletion();
            }
            catch (Exception exception)
            {
                // Addressables throws InvalidKeyException when a label has no
                // entries yet, i.e. content was never imported/marked
                Debug.LogError($"ContentDatabase: loading addressable content with label '{label}' failed — " +
                    $"run 'GarageBandIdle → Import Chapter 1 JSON' (it marks all content addressable), then press Play again. ({exception.Message})");
                assets = Array.Empty<T>();
            }

            return new Registry<T>(label, assets, idSelector);
        }

        // Id-keyed lookup for one definition type. Content errors (empty or
        // duplicate ids) are reported at load so they surface immediately.
        public class Registry<T> where T : ScriptableObject
        {
            private readonly string _label;
            private readonly List<T> _all = new();
            private readonly Dictionary<string, T> _byId = new();

            public IReadOnlyList<T> All => _all;
            public int Count => _all.Count;

            public Registry(string label, IEnumerable<T> assets, Func<T, string> idSelector)
            {
                _label = label;

                foreach (var asset in assets)
                {
                    if (asset == null)
                        continue;

                    var id = idSelector(asset);
                    if (string.IsNullOrEmpty(id))
                    {
                        Debug.LogError($"ContentDatabase: {typeof(T).Name} asset '{asset.name}' has an empty id. Skipping it.");
                        continue;
                    }
                    if (_byId.TryGetValue(id, out var existing))
                    {
                        Debug.LogError($"ContentDatabase: duplicate {typeof(T).Name} id '{id}' on assets '{asset.name}' and '{existing.name}'. Keeping '{existing.name}'.");
                        continue;
                    }

                    _all.Add(asset);
                    _byId.Add(id, asset);
                }
            }

            public bool Contains(string id) => !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);

            // silent lookup for probing callers (validation, gates)
            public bool TryGet(string id, out T definition) => _byId.TryGetValue(id ?? "", out definition);

            public T Get(string id)
            {
                if (TryGet(id, out var definition))
                    return definition;

                Debug.LogError($"ContentDatabase: unknown {typeof(T).Name} id '{id}' (label '{_label}').");
                return null;
            }
        }
    }
}
