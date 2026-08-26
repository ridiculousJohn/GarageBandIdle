using System;
using System.Collections.Generic;
using System.Linq;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The standing test tree mirrors Chapter 1's shape: root -> ch1 -> tier1,
    // currencies and flags filed exactly as the content doc files them, and the
    // economy declarations carrying the content doc's own numbers so a
    // resolution test reads like the walkthroughs.
    public class TestTree
    {
        public readonly DateTime Now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        public readonly RootDefinition RootDef;
        public readonly ChapterDefinition Ch1Def;
        public readonly TierDefinition Tier1Def;
        public readonly RootScopeState Root;
        public readonly ChapterScopeState Ch1;
        public readonly TierScopeState Tier1;

        public readonly CurrencyDefinition Cash;
        public readonly CurrencyDefinition Fans;
        public readonly CurrencyDefinition Rehearsal;
        public readonly CurrencyDefinition Ch1Records;
        public readonly CurrencyDefinition Records;
        public readonly CurrencyDefinition Roadies;

        public readonly ProducerDefinition TapProducer;
        public readonly ProducerDefinition Band;
        public readonly GeneratorDefinition PracticeAmp;
        public readonly GeneratorDefinition Drummer;
        public readonly UpgradeDefinition StagePresence;
        public readonly UpgradeDefinition AmpStrings;
        public readonly UpgradeDefinition TightSet;
        public readonly CareerEffectDefinition RecordsIncome;
        public readonly CareerEffectDefinition RecordsIncomeYield;
        public readonly CareerEffectDefinition RoadieTotal;
        public readonly CareerEffectDefinition RoadieActive;
        public readonly ModifierDefinition GjTap1;
        public readonly TriggerDefinition Tier1Trigger;
        public readonly EventDefinition TimedGig;
        public readonly EventDefinition OpenMic;
        public readonly BarGroupDefinition LearnCovers;
        public readonly BarDefinition Cover1;
        public readonly BarDefinition Cover2;
        public readonly BarDefinition Cover3;

        public TestTree()
        {
            Tier1Def = MakeTier("tier1");
            Cash = DeclareCurrency(Tier1Def, "cash", "income");
            Fans = DeclareCurrency(Tier1Def, "fans");
            Rehearsal = DeclareCurrency(Tier1Def, "rehearsal");
            Tier1Def.declaredFlags.AddRange(new[] { "fans_revealed", "rehearsal_revealed" });
            Tier1Trigger = MakeDefinition<TriggerDefinition>("tier1_trigger");
            Tier1Def.triggers.Add(Tier1Trigger);

            Ch1Def = MakeChapter("ch1");
            Ch1Records = DeclareCurrency(Ch1Def, "ch1_records");
            Ch1Def.declaredFlags.AddRange(new[] { "album", "gj1_done" });
            Ch1Def.children.Add(Tier1Def);

            RootDef = MakeRoot("root");
            Records = DeclareCurrency(RootDef, "records");
            Roadies = DeclareCurrency(RootDef, "roadies");
            RootDef.declaredFlags.Add("ch1_complete");
            RootDef.children.Add(Ch1Def);

            // The Jam: two cash yield entries (the second reads the upgrade
            // latch) plus the reveal-gated rehearsal pair.
            TapProducer = MakeDefinition<ProducerDefinition>("tap_producer", "production");
            TapProducer.produces.Add(Entry(Cash, Stat.Yield, 1));
            TapProducer.produces.Add(Entry(Rehearsal, Stat.Yield, 1, new FlagSet { flagId = "rehearsal_revealed" }));
            TapProducer.produces.Add(Entry(Rehearsal, Stat.Rate, 0.5, new FlagSet { flagId = "rehearsal_revealed" }));

            Band = MakeDefinition<ProducerDefinition>("band", "production");
            Band.produces.Add(Entry(Fans, Stat.Rate, 0.35, new FlagSet { flagId = "fans_revealed" }));

            PracticeAmp = MakeDefinition<GeneratorDefinition>("practice_amp", "gear", "production");
            PracticeAmp.availableWhen = new EarnedTotalAtLeast { currency = Cash, threshold = 100 };
            PracticeAmp.costCurrency = Cash;
            PracticeAmp.baseCost = 60;
            PracticeAmp.growth = 1.15;
            PracticeAmp.produces.Add(Entry(Cash, Stat.Rate, 0.5));

            Drummer = MakeDefinition<GeneratorDefinition>("drummer", "gear", "bandmate", "production");
            Drummer.availableWhen = new OwnedCountAtLeast { generator = PracticeAmp, count = 3 };
            Drummer.costCurrency = Cash;
            Drummer.baseCost = 250;
            Drummer.growth = 1.15;
            Drummer.produces.Add(Entry(Cash, Stat.Rate, 3));
            Drummer.produces.Add(Entry(Fans, Stat.Rate, 0.02));

            StagePresence = MakeDefinition<UpgradeDefinition>("stage_presence");
            StagePresence.gate = new EarnedTotalAtLeast { currency = Cash, threshold = 250 };
            StagePresence.costCurrency = Cash;
            StagePresence.cost = 250;

            // The second tap entry reads the purchase latch, so it is authored
            // after the upgrade it names.
            TapProducer.produces.Insert(1, Entry(Cash, Stat.Yield, 1, new UpgradePurchased { upgrade = StagePresence }));

            AmpStrings = MakeDefinition<UpgradeDefinition>("amp_strings");
            AmpStrings.gate = new EarnedTotalAtLeast { currency = Cash, threshold = 500 };
            AmpStrings.costCurrency = Cash;
            AmpStrings.cost = 500;
            AmpStrings.effects.Add(new Effect { target = "practice_amp", stat = Stat.Rate, multiplier = 2 });

            // A currency-total effect narrowed to one stat: it lifts the cash
            // rate and leaves the tap yield alone.
            TightSet = MakeDefinition<UpgradeDefinition>("tight_set");
            TightSet.gate = new CurrencyAtLeast { currency = Fans, threshold = 30 };
            TightSet.costCurrency = Cash;
            TightSet.cost = 20000;
            TightSet.effects.Add(new Effect { target = "cash", stat = Stat.Rate, multiplier = 1.5 });

            Tier1Def.producers.AddRange(new[] { TapProducer, Band });
            Tier1Def.generators.AddRange(new[] { PracticeAmp, Drummer });
            Tier1Def.upgrades.AddRange(new[] { StagePresence, AmpStrings, TightSet });

            // 1 + 0.02 * records, on the income tag cash carries. The stat leg
            // is exact, so "rate and yield alike" is one career per stat.
            RecordsIncome = MakeDefinition<CareerEffectDefinition>("records_income");
            RecordsIncome.target = "income";
            RecordsIncome.stat = Stat.Rate;
            RecordsIncome.formula = new LinearOnBalance { currency = Records, coefficient = 0.02 };
            RootDef.careerEffects.Add(RecordsIncome);

            RecordsIncomeYield = MakeDefinition<CareerEffectDefinition>("records_income_yield");
            RecordsIncomeYield.target = "income";
            RecordsIncomeYield.stat = Stat.Yield;
            RecordsIncomeYield.formula = new LinearOnBalance { currency = Records, coefficient = 0.02 };
            RootDef.careerEffects.Add(RecordsIncomeYield);

            // The two roadie effects, composed at different levels: the global
            // product on the income currency, the per-chapter factor on the
            // SOURCES, narrowed to income so a bandmate's fans line stays out.
            RoadieTotal = MakeDefinition<CareerEffectDefinition>("roadie_total");
            RoadieTotal.target = "income";
            RoadieTotal.stat = Stat.Rate;
            RoadieTotal.formula = new RoadieTotalBoost { perRoadie = 0.05 };
            RootDef.careerEffects.Add(RoadieTotal);

            RoadieActive = MakeDefinition<CareerEffectDefinition>("roadie_active");
            RoadieActive.target = "production";
            RoadieActive.currencyId = "income";
            RoadieActive.stat = Stat.Rate;
            RoadieActive.formula = new RoadieActiveBoost { perRoadie = 0.05 };
            RootDef.careerEffects.Add(RoadieActive);

            // learn_covers: each cover drinks Rehearsal at 2/s, and the group
            // caps it to ONE at a time, since choosing the next one is the
            // mechanic. Each completion grants its own fan-rate modifier, which
            // is what a one-shot completion has instead of a cascade.
            LearnCovers = MakeDefinition<BarGroupDefinition>("learn_covers");
            LearnCovers.maxActive = 1;
            Cover1 = Cover("cover_1", "cover_bonus_1", 100, 1.15);
            Cover2 = Cover("cover_2", "cover_bonus_2", 300, 1.15);
            Cover3 = Cover("cover_3", "cover_bonus_3", 600, 1.2);
            Tier1Def.barGroups.Add(LearnCovers);

            // The Garage Jam reward: +25% tap for the rest of the chapter, so
            // it is granted at ch1 and outlives the tier resets.
            GjTap1 = MakeDefinition<ModifierDefinition>("gj_tap_1");
            GjTap1.effects.Add(new Effect { target = "tap_producer", currencyId = "cash", stat = Stat.Yield, multiplier = 1.25 });
            Ch1Def.modifiers.Add(GjTap1);

            // The two tier1 events: a timed gig that zeroes the production-
            // tagged sources while its record sits on the host, and an untimed
            // open mic that halves the fan rate. Both gates open, so entry
            // tests own their refusals.
            TimedGig = MakeDefinition<EventDefinition>("timed_gig");
            TimedGig.availableWhen = new Always();
            TimedGig.goal = new EarnedTotalAtLeast { currency = Fans, threshold = 100 };
            TimedGig.timeLimitSeconds = 300;
            TimedGig.handicaps.Add(new Effect { target = "production", stat = Stat.Rate, multiplier = 0 });
            TimedGig.handicaps.Add(new Effect { target = "production", stat = Stat.Yield, multiplier = 0 });
            Tier1Def.events.Add(TimedGig);

            OpenMic = MakeDefinition<EventDefinition>("open_mic");
            OpenMic.availableWhen = new Always();
            OpenMic.goal = new EarnedTotalAtLeast { currency = Fans, threshold = 50 };
            OpenMic.timeLimitSeconds = 0;
            OpenMic.handicaps.Add(new Effect { target = "fans", stat = Stat.Rate, multiplier = 0.5 });
            Tier1Def.events.Add(OpenMic);

            Root = ScopeState.Build(RootDef);
            Ch1 = (ChapterScopeState)Root.FindInSubtree(Ch1Def);
            Tier1 = (TierScopeState)Root.FindInSubtree(Tier1Def);
        }

        // One cover and the modifier its completion grants. Both are filed at
        // tier1, so a run reset clears the bonus along with the progress.
        private BarDefinition Cover(string id, string modifierId, double fillAmount, double bonus)
        {
            var modifier = MakeDefinition<ModifierDefinition>(modifierId);
            modifier.effects.Add(new Effect { target = "fans", stat = Stat.Rate, multiplier = bonus });
            Tier1Def.modifiers.Add(modifier);

            var bar = MakeDefinition<BarDefinition>(id);
            bar.fillCurrency = Rehearsal;
            bar.fillAmount = fillAmount;
            bar.fillRate = 2;
            bar.onComplete.Add(new AddModifier { scope = Tier1Def, modifier = modifier });
            LearnCovers.bars.Add(bar);
            return bar;
        }

        public static ProducesEntry Entry(CurrencyDefinition currency, string stat, double value, Condition condition = null) =>
            new ProducesEntry { currency = currency, stat = stat, value = value, condition = condition };

        public GameContext Ctx(ScopeState scope) => new GameContext(scope, Now);

        // A scope is authored as one of three kinds, so a fixture names the kind
        // it wants rather than making a shapeless one and hoping depth sorts it.
        public static T MakeScope<T>(string id) where T : ScopeDefinition
        {
            var def = ScriptableObject.CreateInstance<T>();
            def.EditorInit(id);
            return def;
        }

        public static RootDefinition MakeRoot(string id) => MakeScope<RootDefinition>(id);
        public static ChapterDefinition MakeChapter(string id) => MakeScope<ChapterDefinition>(id);
        public static TierDefinition MakeTier(string id) => MakeScope<TierDefinition>(id);

        // Declares a currency at its home and hands back the asset, so the
        // fixture registers the same instance the scope references.
        public static CurrencyDefinition DeclareCurrency(ScopeDefinition scope, string id, params string[] tags)
        {
            var currency = MakeDefinition<CurrencyDefinition>(id, tags);
            scope.declaredCurrencies.Add(currency);
            return currency;
        }

        public static T MakeDefinition<T>(string id, params string[] tags) where T : Definition
        {
            var def = ScriptableObject.CreateInstance<T>();
            def.EditorInit(id, tags);
            return def;
        }
    }
}
