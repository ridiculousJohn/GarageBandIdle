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
    // registry, so the content set stays open - new assets are picked up with no
    // code or registration changes and nothing holds a direct asset reference.
    // Label load order is arbitrary; display/processing order comes from the
    // chapter's id lists, never from a registry.
    public class ContentDatabase
    {
        public Registry<CurrencyGroupDefinition> CurrencyGroups { get; }
        public Registry<CurrencyDefinition> Currencies { get; }
        public Registry<ProducerDefinition> Producers { get; }
        public Registry<ChapterDefinition> Chapters { get; }
        public Registry<SectionDefinition> Sections { get; }
        public Registry<GeneratorDefinition> Generators { get; }
        public Registry<UpgradeDefinition> Upgrades { get; }
        public Registry<BarDefinition> Bars { get; }
        public Registry<BarGroupDefinition> BarGroups { get; }
        public Registry<EventDefinition> Events { get; }
        public Registry<RewardDefinition> Rewards { get; }
        public Registry<StoryBeatDefinition> StoryBeats { get; }

        // Every family, for the questions that are about the DATABASE rather than
        // about one registry - a modifier selector's terms are open content across
        // all of them (rule 11), so resolving one cannot start by picking a family.
        // IReadOnlyList<T> is covariant, so each registry's All lands here as-is.
        private readonly List<IEnumerable<Definition>> _families = new();

        public ContentDatabase()
        {
            CurrencyGroups = Load<CurrencyGroupDefinition>(ContentLabels.CurrencyGroup);
            Currencies = Load<CurrencyDefinition>(ContentLabels.Currency);
            Producers = Load<ProducerDefinition>(ContentLabels.Producer);
            Chapters = Load<ChapterDefinition>(ContentLabels.Chapter);
            Sections = Load<SectionDefinition>(ContentLabels.Section);
            Generators = Load<GeneratorDefinition>(ContentLabels.Generator);
            Upgrades = Load<UpgradeDefinition>(ContentLabels.Upgrade);
            Bars = Load<BarDefinition>(ContentLabels.Bar);
            BarGroups = Load<BarGroupDefinition>(ContentLabels.BarGroup);
            Events = Load<EventDefinition>(ContentLabels.Event);
            Rewards = Load<RewardDefinition>(ContentLabels.Reward);
            StoryBeats = Load<StoryBeatDefinition>(ContentLabels.StoryBeat);
            CollectFamilies();
        }

        // direct-injection alternative to Addressables discovery: tests (and
        // tooling) hand over an explicit content set instead of loading labels
        public ContentDatabase(
            IEnumerable<ChapterDefinition> chapters = null,
            IEnumerable<SectionDefinition> sections = null,
            IEnumerable<GeneratorDefinition> generators = null,
            IEnumerable<UpgradeDefinition> upgrades = null,
            IEnumerable<BarDefinition> bars = null,
            IEnumerable<BarGroupDefinition> barGroups = null,
            IEnumerable<EventDefinition> events = null,
            IEnumerable<RewardDefinition> rewards = null,
            IEnumerable<CurrencyDefinition> currencies = null,
            IEnumerable<CurrencyGroupDefinition> currencyGroups = null,
            IEnumerable<ProducerDefinition> producers = null,
            IEnumerable<StoryBeatDefinition> storyBeats = null)
        {
            CurrencyGroups = new Registry<CurrencyGroupDefinition>(ContentLabels.CurrencyGroup, currencyGroups ?? Array.Empty<CurrencyGroupDefinition>());
            Currencies = new Registry<CurrencyDefinition>(ContentLabels.Currency, currencies ?? Array.Empty<CurrencyDefinition>());
            Producers = new Registry<ProducerDefinition>(ContentLabels.Producer, producers ?? Array.Empty<ProducerDefinition>());
            Chapters = new Registry<ChapterDefinition>(ContentLabels.Chapter, chapters ?? Array.Empty<ChapterDefinition>());
            Sections = new Registry<SectionDefinition>(ContentLabels.Section, sections ?? Array.Empty<SectionDefinition>());
            Generators = new Registry<GeneratorDefinition>(ContentLabels.Generator, generators ?? Array.Empty<GeneratorDefinition>());
            Upgrades = new Registry<UpgradeDefinition>(ContentLabels.Upgrade, upgrades ?? Array.Empty<UpgradeDefinition>());
            Bars = new Registry<BarDefinition>(ContentLabels.Bar, bars ?? Array.Empty<BarDefinition>());
            BarGroups = new Registry<BarGroupDefinition>(ContentLabels.BarGroup, barGroups ?? Array.Empty<BarGroupDefinition>());
            Events = new Registry<EventDefinition>(ContentLabels.Event, events ?? Array.Empty<EventDefinition>());
            Rewards = new Registry<RewardDefinition>(ContentLabels.Reward, rewards ?? Array.Empty<RewardDefinition>());
            StoryBeats = new Registry<StoryBeatDefinition>(ContentLabels.StoryBeat, storyBeats ?? Array.Empty<StoryBeatDefinition>());
            CollectFamilies();
        }

        // Synchronous label load, held for the app's lifetime (definitions are
        // needed as long as the game runs, so handles are never released).
        // WaitForCompletion keeps bootstrap simple; this becomes async behind a
        // loading screen in a later slice.
        private static Registry<T> Load<T>(string label) where T : Definition
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
                Debug.LogError($"ContentDatabase: loading addressable content with label '{label}' failed - " +
                    $"run 'GarageBandIdle > Import Chapter 1 JSON' (it marks all content addressable), then press Play again. ({exception.Message})");
                assets = Array.Empty<T>();
            }

            return new Registry<T>(label, assets);
        }

        private void CollectFamilies()
        {
            _families.Add(CurrencyGroups.All);
            _families.Add(Currencies.All);
            _families.Add(Producers.All);
            _families.Add(Chapters.All);
            _families.Add(Sections.All);
            _families.Add(Generators.All);
            _families.Add(Upgrades.All);
            _families.Add(Bars.All);
            _families.Add(BarGroups.All);
            _families.Add(Events.All);
            _families.Add(Rewards.All);
            _families.Add(StoryBeats.All);
        }

        // Whether anything in the content set answers to one selector term (rule
        // 11) - a definition's id, a tag it declares, or a produced number's feed
        // name. This is what turns a typo into a reported error instead of a
        // modifier filed forever and read by nobody, which is the guard the old
        // closed ModifierTarget enum gave for free by refusing to compile.
        //
        // Deliberately a question about the whole database: a term does not say
        // which family it belongs to, and requiring it to would be re-introducing
        // the kind that could not name one of a generator's two output lines.
        public bool ResolvesModifierTerm(string term)
        {
            if (string.IsNullOrEmpty(term))
                return false;

            foreach (var family in _families)
            {
                foreach (var definition in family)
                {
                    if (definition.Id == term || definition.HasTag(term))
                        return true;
                }
            }

            // A producer's two numbers are named but not authored: their ids are
            // derived from the currency, so nothing in a family carries them
            // (CurrencyProducer.NumberId). `cash_rate` is what a currency-wide
            // income buff selects, and it must not be reported as a typo.
            foreach (var currency in Currencies.All)
            {
                if (term == CurrencyProducer.NumberId(currency.Id, ProductionFeed.Rate)
                    || term == CurrencyProducer.NumberId(currency.Id, ProductionFeed.Yield))
                    return true;
            }

            // Contribution lines are modifiable numbers with their own ids and tags
            // (rule 11), and they live INSIDE the definitions holding them rather
            // than in a family of their own - so `drummer_cash` resolves here or
            // nowhere. This is the term form the whole addressing change exists for:
            // one line of a contributor that holds several.
            foreach (var generator in Generators.All)
            {
                if (ResolvesContribution(generator.Contributions, term))
                    return true;
            }
            foreach (var producer in Producers.All)
            {
                if (ResolvesContribution(producer.Contributions, term))
                    return true;
            }
            foreach (var upgrade in Upgrades.All)
            {
                if (ResolvesContribution(upgrade.Contributions, term))
                    return true;
            }

            return false;
        }

        private static bool ResolvesContribution(IReadOnlyList<ProductionContribution> contributions, string term)
        {
            foreach (var contribution in contributions)
            {
                if (contribution == null)
                    continue;
                if (contribution.Id == term)
                    return true;

                foreach (var tag in contribution.Tags)
                {
                    if (tag == term)
                        return true;
                }
            }
            return false;
        }

        // Id-keyed lookup for one definition type. Content errors (empty or
        // duplicate ids) are reported at load so they surface immediately.
        public class Registry<T> where T : Definition
        {
            private readonly string _label;
            private readonly List<T> _all = new();
            private readonly Dictionary<string, T> _byId = new();

            public IReadOnlyList<T> All => _all;
            public int Count => _all.Count;

            // The id comes off the definition base rather than a per-type accessor
            // the caller supplies (rule 10): twelve `d => d.Id` lambdas were twelve
            // chances to hand this the wrong one.
            public Registry(string label, IEnumerable<T> assets)
            {
                _label = label;

                foreach (var asset in assets)
                {
                    if (asset == null)
                        continue;

                    var id = asset.Id;
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
