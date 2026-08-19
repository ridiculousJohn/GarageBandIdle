using System;
using System.Collections.Generic;
using System.Linq;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // List-backed IDefinitionSource test double. ContentDatabase is the
    // production implementation (Addressables discovery); this stays for tests
    // that want incremental Add chaining and no asset pipeline.
    public class FakeDefs : IDefinitionSource
    {
        private readonly List<Definition> definitions = new();

        public FakeDefs Add(Definition definition)
        {
            definitions.Add(definition);
            return this;
        }

        public T Get<T>(string id) where T : Definition =>
            definitions.OfType<T>().FirstOrDefault(d => d.Id == id);

        public IEnumerable<T> All<T>() where T : Definition => definitions.OfType<T>();
    }

    // The standing test tree mirrors Chapter 1's shape: root -> ch1 -> tier1,
    // currencies and flags filed exactly as the content doc files them, and the
    // economy declarations carrying the content doc's own numbers so a
    // resolution test reads like the walkthroughs.
    public class TestTree
    {
        public readonly DateTime Now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        public readonly FakeDefs Defs = new();
        public readonly ScopeDefinition RootDef;
        public readonly ScopeDefinition Ch1Def;
        public readonly ScopeDefinition Tier1Def;
        public readonly ScopeState Root;
        public readonly ScopeState Ch1;
        public readonly ScopeState Tier1;

        public readonly ProducerDefinition TapProducer;
        public readonly ProducerDefinition Band;
        public readonly GeneratorDefinition PracticeAmp;
        public readonly GeneratorDefinition Drummer;
        public readonly UpgradeDefinition StagePresence;
        public readonly UpgradeDefinition AmpStrings;
        public readonly UpgradeDefinition TightSet;
        public readonly CareerEffectDefinition RecordsIncome;
        public readonly CareerEffectDefinition RoadieTotal;
        public readonly CareerEffectDefinition RoadieActive;
        public readonly RoadieVenueDefinition Garage;
        public readonly ModifierDefinition GjTap1;
        public readonly TriggerDefinition Tier1Trigger;

        public TestTree()
        {
            Tier1Def = MakeScope("tier1");
            Tier1Def.declaredCurrencyIds.AddRange(new[] { "cash", "fans", "rehearsal" });
            Tier1Def.declaredFlags.AddRange(new[] { "fans_revealed", "rehearsal_revealed" });
            Tier1Trigger = MakeDefinition<TriggerDefinition>("tier1_trigger");
            Tier1Def.triggers.Add(Tier1Trigger);

            Ch1Def = MakeScope("ch1");
            Ch1Def.declaredCurrencyIds.Add("ch1_records");
            Ch1Def.declaredFlags.AddRange(new[] { "album", "gj1_done" });
            Ch1Def.children.Add(Tier1Def);

            RootDef = MakeScope("root");
            RootDef.declaredCurrencyIds.AddRange(new[] { "records", "roadies" });
            RootDef.declaredFlags.Add("ch1_complete");
            RootDef.children.Add(Ch1Def);

            // The Jam: two cash yield entries (the second reads the upgrade
            // latch) plus the reveal-gated rehearsal pair.
            TapProducer = MakeDefinition<ProducerDefinition>("tap_producer", "production");
            TapProducer.produces.Add(Entry("cash", Stat.Yield, 1));
            TapProducer.produces.Add(Entry("cash", Stat.Yield, 1, new UpgradePurchased { upgradeId = "stage_presence" }));
            TapProducer.produces.Add(Entry("rehearsal", Stat.Yield, 1, new FlagSet { flagId = "rehearsal_revealed" }));
            TapProducer.produces.Add(Entry("rehearsal", Stat.Rate, 0.5, new FlagSet { flagId = "rehearsal_revealed" }));

            Band = MakeDefinition<ProducerDefinition>("band", "production");
            Band.produces.Add(Entry("fans", Stat.Rate, 0.35, new FlagSet { flagId = "fans_revealed" }));

            PracticeAmp = MakeDefinition<GeneratorDefinition>("practice_amp", "gear", "production");
            PracticeAmp.availableWhen = new EarnedTotalAtLeast { currencyId = "cash", threshold = 100 };
            PracticeAmp.costCurrencyId = "cash";
            PracticeAmp.baseCost = 60;
            PracticeAmp.growth = 1.15;
            PracticeAmp.produces.Add(Entry("cash", Stat.Rate, 0.5));

            Drummer = MakeDefinition<GeneratorDefinition>("drummer", "gear", "bandmate", "production");
            Drummer.availableWhen = new OwnedCountAtLeast { generatorId = "practice_amp", count = 3 };
            Drummer.costCurrencyId = "cash";
            Drummer.baseCost = 250;
            Drummer.growth = 1.15;
            Drummer.produces.Add(Entry("cash", Stat.Rate, 3));
            Drummer.produces.Add(Entry("fans", Stat.Rate, 0.02));

            StagePresence = MakeDefinition<UpgradeDefinition>("stage_presence");
            StagePresence.gate = new EarnedTotalAtLeast { currencyId = "cash", threshold = 250 };
            StagePresence.costCurrencyId = "cash";
            StagePresence.cost = 250;

            AmpStrings = MakeDefinition<UpgradeDefinition>("amp_strings");
            AmpStrings.gate = new EarnedTotalAtLeast { currencyId = "cash", threshold = 500 };
            AmpStrings.costCurrencyId = "cash";
            AmpStrings.cost = 500;
            AmpStrings.effects.Add(new Effect { target = "practice_amp", multiplier = 2 });

            // A currency-total effect narrowed to one stat: it lifts the cash
            // rate and leaves the tap yield alone.
            TightSet = MakeDefinition<UpgradeDefinition>("tight_set");
            TightSet.gate = new CurrencyAtLeast { currencyId = "fans", threshold = 30 };
            TightSet.costCurrencyId = "cash";
            TightSet.cost = 20000;
            TightSet.effects.Add(new Effect { target = "cash", stat = Stat.Rate, multiplier = 1.5 });

            Tier1Def.producers.AddRange(new[] { TapProducer, Band });
            Tier1Def.generators.AddRange(new[] { PracticeAmp, Drummer });
            Tier1Def.upgrades.AddRange(new[] { StagePresence, AmpStrings, TightSet });

            // 1 + 0.02 * records, on the income tag cash carries.
            RecordsIncome = MakeDefinition<CareerEffectDefinition>("records_income");
            RecordsIncome.target = "income";
            RecordsIncome.formula = new LinearOnBalance { currencyId = "records", coefficient = 0.02 };
            RootDef.careerEffects.Add(RecordsIncome);

            // The two roadie effects, composed at different levels: the global
            // product on the income currency, the per-chapter factor on the
            // SOURCES, narrowed to income so a bandmate's fans line stays out.
            RoadieTotal = MakeDefinition<CareerEffectDefinition>("roadie_total");
            RoadieTotal.target = "income";
            RoadieTotal.stat = Stat.Rate;
            RoadieTotal.formula = new RoadieTotalBoost();
            RootDef.careerEffects.Add(RoadieTotal);

            RoadieActive = MakeDefinition<CareerEffectDefinition>("roadie_active");
            RoadieActive.target = "production";
            RoadieActive.currencyId = "income";
            RoadieActive.stat = Stat.Rate;
            RoadieActive.formula = new RoadieActiveBoost();
            RootDef.careerEffects.Add(RoadieActive);

            Garage = MakeDefinition<RoadieVenueDefinition>("garage_venue");
            Garage.chapterScopeId = "ch1";
            Garage.perRoadie = 0.05;
            Garage.cap = 5;

            // The Garage Jam reward: +25% tap for the rest of the chapter, so
            // it is granted at ch1 and outlives the tier resets.
            GjTap1 = MakeDefinition<ModifierDefinition>("gj_tap_1");
            GjTap1.effects.Add(new Effect { target = "tap_producer", multiplier = 1.25 });

            Defs.Add(RootDef).Add(Ch1Def).Add(Tier1Def)
                .Add(MakeDefinition<CurrencyDefinition>("cash", "income"))
                .Add(MakeDefinition<CurrencyDefinition>("fans"))
                .Add(MakeDefinition<CurrencyDefinition>("rehearsal"))
                .Add(MakeDefinition<CurrencyDefinition>("ch1_records"))
                .Add(MakeDefinition<CurrencyDefinition>("records"))
                .Add(MakeDefinition<CurrencyDefinition>("roadies"))
                .Add(TapProducer).Add(Band)
                .Add(PracticeAmp).Add(Drummer)
                .Add(StagePresence).Add(AmpStrings).Add(TightSet)
                .Add(RecordsIncome).Add(RoadieTotal).Add(RoadieActive).Add(Garage).Add(GjTap1)
                .Add(Tier1Trigger);

            Root = ScopeState.Build(RootDef);
            Ch1 = Root.FindInSubtree("ch1");
            Tier1 = Root.FindInSubtree("tier1");
        }

        public static ProducesEntry Entry(string currencyId, string stat, double value, Condition condition = null) =>
            new ProducesEntry { currencyId = currencyId, stat = stat, value = value, condition = condition };

        public GameContext Ctx(ScopeState scope) => new GameContext(scope, Defs, Now);

        public static ScopeDefinition MakeScope(string id)
        {
            var def = ScriptableObject.CreateInstance<ScopeDefinition>();
            def.EditorInit(id);
            return def;
        }

        public static T MakeDefinition<T>(string id, params string[] tags) where T : Definition
        {
            var def = ScriptableObject.CreateInstance<T>();
            def.EditorInit(id, tags);
            return def;
        }
    }
}
