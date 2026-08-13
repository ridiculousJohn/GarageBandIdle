using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Builders for in-memory definition instances so unit tests don't depend on
    // imported assets. Everything created here is tracked and torn down via
    // DestroyAll from a fixture's [OneTimeTearDown].
    internal static class TestContent
    {
        private static readonly List<Object> Created = new();

        public static void DestroyAll()
        {
            foreach (var created in Created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            Created.Clear();
        }

        // Placement defaults to Chapter because that is what almost every
        // fixture wants: one flat pool standing in for a chapter's economy.
        // A fixture testing placement itself passes it explicitly.
        public static CurrencyGroupDefinition MakeGroup(string id, bool resetsOnAlbumRelease,
            CurrencyPlacement placement = CurrencyPlacement.Chapter)
        {
            var definition = Track(ScriptableObject.CreateInstance<CurrencyGroupDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = id;
            serialized.FindProperty("_resetsOnAlbumRelease").boolValue = resetsOnAlbumRelease;
            // intValue, not enumValueIndex: the serialized form is the enum's
            // VALUE (a save contract, explicitly numbered), and index-vs-value
            // only coincide while no value is skipped
            serialized.FindProperty("_placement").intValue = (int)placement;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        public static CurrencyDefinition MakeCurrency(string id, string groupId, double startingValue = 0)
        {
            var definition = Track(ScriptableObject.CreateInstance<CurrencyDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = id;
            serialized.FindProperty("_groupId").stringValue = groupId;
            serialized.FindProperty("_startingValue").doubleValue = startingValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        public static ProducerDefinition MakeProducer(string id, List<ProductionContribution> contributions)
        {
            var definition = Track(ScriptableObject.CreateInstance<ProducerDefinition>());
            definition.EditorInitialize(id, contributions);
            return definition;
        }

        // One production line. The id defaults to "<holder>_<currency>", the
        // convention chapter 1 authors, so a fixture only spells one out when the
        // test is about the id itself.
        public static ProductionContribution Line(string holderId, string currencyId, double amount,
            ProductionFeed feeds, Condition gate = null, string id = null, string[] tags = null)
            => new(id ?? $"{holderId}_{currencyId}", currencyId, amount, feeds, gate, tags);

        // A producer straight from its lines, which is how almost every fixture
        // wants to say it. Ids are derived here rather than authored per line -
        // "<producer>_<currency>", falling back to "<producer>_<currency>_<feeds>"
        // when one producer feeds both of a currency's numbers - so a fixture never
        // silently gives two numbers one id, which rule 11 forbids and which would
        // make a selector reach the wrong one.
        public static ProducerDefinition MakeProducer(string id,
            params (string currency, double amount, ProductionFeed feeds, Condition gate)[] lines)
        {
            // counted first, so BOTH of a doubled currency's lines get the suffix -
            // one plain and one suffixed would read as if the plain id named the
            // currency rather than one of its two numbers
            var perCurrency = new Dictionary<string, int>();
            foreach (var line in lines)
                perCurrency[line.currency] = perCurrency.TryGetValue(line.currency, out var n) ? n + 1 : 1;

            var contributions = new List<ProductionContribution>();
            foreach (var line in lines)
            {
                var lineId = perCurrency[line.currency] > 1
                    ? $"{id}_{line.currency}_{line.feeds}".ToLowerInvariant()
                    : $"{id}_{line.currency}";
                contributions.Add(Line(id, line.currency, line.amount, line.feeds, line.gate, lineId));
            }
            return MakeProducer(id, contributions);
        }

        // a jam producer whose single line feeds cash's YIELD - the probe for the
        // per-firing modifier stack (the shape TapSystem was)
        public static ProductionSystem MakeTapProduction(double baseAmount, ModifierSystem modifiers,
            CurrencyManager currencies = null, FlagSystem flags = null)
        {
            currencies ??= MakeEconomy();
            var producer = MakeProducer("jam", new List<ProductionContribution>
            {
                Line("jam", "cash", baseAmount, ProductionFeed.Yield),
            });
            return new ProductionSystem(new[] { producer }, null, null, currencies, modifiers,
                MakeContext(currencies, flags: flags));
        }

        // The fan-accrual path as the game authors it: a passive producer (no
        // module) holding the BASE fans rate, plus whatever fans lines the
        // generators carry. Both halves together, because either alone is not the
        // fan rate - the composed value is (base + each bandmate's line) x rewards,
        // and the per-bandmate half is now a contribution rather than a derived Add.
        public static ProductionSystem MakeFanProduction(ModifierSystem modifiers,
            GeneratorSystem generators, ICurrencies currencies, ConditionContext conditions,
            Condition gate = null, double baseFansPerSec = 0.2)
        {
            var producer = MakeProducer("band", new List<ProductionContribution>
            {
                Line("band", "fans", baseFansPerSec, ProductionFeed.Rate, gate),
            });
            return new ProductionSystem(new[] { producer }, generators, null, currencies, modifiers,
                conditions);
        }

        // A generator with ONE rate line, named "<id>_<currency>" - the shape
        // almost every fixture wants, and the convention chapter 1 authors.
        //
        // isBandmate is no longer a field on the definition: it was a tag that never
        // got the concept (design doc rule 10), and the fan bonus it drove is now an
        // ordinary fans line on the generator. The parameter survives as fixture
        // shorthand for exactly that - the `bandmate` tag plus a fans rate line -
        // because what the tests using it mean is "a bandmate as the game authors
        // one", not "a bool is set".
        public static GeneratorDefinition MakeGenerator(string id, string produces,
            double baseCost, double costGrowth, double baseOutput, Condition unlock = null,
            bool isBandmate = false, string costCurrency = "cash", double fansPerUnit = 0.02)
        {
            var contributions = new List<ProductionContribution>
            {
                Line(id, produces, baseOutput, ProductionFeed.Rate),
            };
            if (isBandmate)
                contributions.Add(Line(id, "fans", fansPerUnit, ProductionFeed.Rate));

            var definition = Track(ScriptableObject.CreateInstance<GeneratorDefinition>());
            definition.EditorInitialize(id, id, costCurrency, baseCost, costGrowth, contributions, unlock,
                isBandmate ? new[] { "bandmate" } : null);
            return definition;
        }

        // What one generator's line for a currency is worth right now, and what one
        // unit of it is. A generator has a LIST of lines now, so a fixture names the
        // currency it means rather than asking for "the" output.
        public static BigNumber LineValue(Generator generator, string currencyId = "cash")
            => generator.ValueOf(LineFor(generator, currencyId));

        public static BigNumber PerUnitLineValue(Generator generator, string currencyId = "cash")
            => generator.PerUnitValueOf(LineFor(generator, currencyId));

        public static ProductionContribution LineFor(Generator generator, string currencyId)
        {
            foreach (var contribution in generator.Contributions)
            {
                if (contribution != null && contribution.CurrencyId == currencyId)
                    return contribution;
            }
            return null;
        }

        // The tick as the game runs it: a generator does not pay a currency, it
        // contributes to the producer that does, so a fixture asserting what a fleet
        // earns has to go through one. Built per call because these fixtures are
        // about the numbers, not about the assembly.
        public static void AccrueGenerators(GeneratorSystem generators, ICurrencies currencies,
            ModifierSystem modifiers, double seconds, ConditionContext conditions = null)
        {
            var production = new ProductionSystem(System.Array.Empty<ProducerDefinition>(), generators, null,
                currencies, modifiers, conditions ?? MakeContext(currencies, generators));
            production.Accrue(seconds);
        }

        public static UpgradeDefinition MakeUpgrade(string id, UpgradeType type, ContentScope scope,
            Condition gate, GameEffect payload,
            string costCurrencyId = "cash", double costAmount = 0,
            List<GameAction> actions = null, List<ProductionContribution> contributions = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<UpgradeDefinition>());
            definition.EditorInitialize(id, id, type, scope, costCurrencyId, costAmount,
                gate, payload, actions, contributions);
            return definition;
        }

        public static StoryBeatDefinition MakeStoryBeat(string id, string text, string readFlagId = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<StoryBeatDefinition>());
            definition.EditorInitialize(id, text, readFlagId);
            return definition;
        }

        public static BarDefinition MakeBar(string id, string fillCurrencyId,
            double fillRequirement, string rewardId = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<BarDefinition>());
            definition.EditorInitialize(id, id, fillCurrencyId, fillRequirement, rewardId);
            return definition;
        }

        public static BarGroupDefinition MakeBarGroup(string id, Condition visibleWhen,
            List<string> barIds, BarFillBehavior fillBehavior = null,
            ContentScope scope = ContentScope.Run)
        {
            var definition = Track(ScriptableObject.CreateInstance<BarGroupDefinition>());
            definition.EditorInitialize(id, id, visibleWhen,
                fillBehavior ?? new PerBarContinuousFill(), scope, barIds);
            return definition;
        }

        // Defaults to one real module address: a section with none is a reported
        // content error, so a fixture that only cares about visibility still has to
        // be a coherent section. The address has to resolve through Addressables
        // like the running game's would.
        //
        // The default is a ROSTER module (the generator list), because those present
        // no single definition - a section entry for one names no id, which is what a
        // fixture wants when it is not about bindings at all. The tap module would
        // report a missing producer id on every such fixture, since 6.5 made a
        // module's requirement something boot validation enforces.
        //
        // Addresses are still the fixture's parameter, since almost no test cares
        // which definition a module presents; each becomes a SectionModule with no
        // definition id.
        public static SectionDefinition MakeSection(string id, Condition visibleWhen = null,
            List<string> moduleAddresses = null, List<SectionModule> modules = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<SectionDefinition>());
            definition.EditorInitialize(id, id,
                modules ?? ToModules(moduleAddresses ?? new List<string> { "module/generator-list" }), visibleWhen);
            return definition;
        }

        private static List<SectionModule> ToModules(List<string> addresses)
        {
            var modules = new List<SectionModule>(addresses.Count);
            foreach (var address in addresses)
                modules.Add(new SectionModule(address));
            return modules;
        }

        // a minimal coherent chapter: declared flags plus the id lists that
        // form its content closure. Fan accrual itself is a production config on
        // a producer, so a chapter fixture declares only which currency is fans
        // and the per-bandmate tuning - see MakeFanProduction for the accrual.
        public static ChapterDefinition MakeChapter(string id, List<string> flagIds,
            List<string> sectionIds = null, List<string> generatorIds = null,
            List<string> upgradeIds = null, List<string> barGroupIds = null,
            List<string> eventIds = null, List<string> currencyIds = null,
            List<string> producerIds = null,
            double recordBuffPerRecord = 0.02,
            int index = 1, string fansCurrencyId = "fans",
            List<string> recordBuffAffects = null, Condition albumReleaseWhen = null,
            List<FlagDeclaration> flags = null, CapstoneConfig capstone = null,
            List<string> storyBeatIds = null)
        {
            // string ids remain the fixture-friendly spelling: each becomes a
            // permanent-latch declaration, the same default absent JSON gets.
            // A fixture testing flag lifetimes passes declarations instead.
            var declarations = flags;
            if (declarations == null)
            {
                declarations = new List<FlagDeclaration>();
                foreach (var flagId in flagIds ?? new List<string>())
                    declarations.Add(new FlagDeclaration(flagId));
            }

            var definition = Track(ScriptableObject.CreateInstance<ChapterDefinition>());
            definition.EditorInitialize(id, index, id, "",
                new RecordBuffConfig(recordBuffPerRecord, recordBuffAffects ?? new List<string> { "cash" }),
                new FansConfig(fansCurrencyId),
                // null (the default) is a legal album gate: always offered
                new AlbumConfig(albumReleaseWhen),
                // an unauthored capstone by default: not every chapter declares one,
                // and validation asks IsAuthored before demanding any of its parts
                capstone ?? new CapstoneConfig(),
                // the chapter-local half of the standard economy; records is
                // global, so no chapter declares it
                declarations, currencyIds ?? new List<string> { "cash", "fans" }, producerIds ?? new List<string>(),
                sectionIds ?? new List<string>(), generatorIds ?? new List<string>(),
                upgradeIds ?? new List<string>(), barGroupIds ?? new List<string>(),
                eventIds ?? new List<string>(), storyBeatIds ?? new List<string>());
            return definition;
        }

        public static EventDefinition MakeEvent(string id, List<EventTier> tiers,
            Condition availableWhen = null, bool baselineReset = true)
        {
            var definition = Track(ScriptableObject.CreateInstance<EventDefinition>());
            definition.EditorInitialize(id, id, availableWhen, baselineReset, tiers);
            return definition;
        }

        // One tier, defaulting to the coherent shape: timed and failable together,
        // paying a reward. Goal is required rather than defaulted, because "no
        // goal" is one of the states a tier fixture needs to be able to express.
        public static EventTier MakeTier(int tier, string rewardId, Condition goal,
            double timerSeconds = 60, bool failable = true,
            ContentScope scope = ContentScope.PermanentInChapter)
            => new(tier, new AutomationDisabledDebuff(), goal, timerSeconds, failable, rewardId, scope);

        // Reward fixtures carry no scope: the content applying one declares the
        // lifetime, so a bar group's Scope or a tier's Scope supplies it at Apply.
        // The named helpers are just the friendly effect vocabulary, same as the
        // importer's - one reward type underneath.
        public static RewardDefinition MakeFanRateReward(string id, double value)
            => MakeReward(id, new GrantModifierEffect(Sel("fans_rate"), ModifierOperation.Multiply, value));

        public static RewardDefinition MakeTapValueReward(string id, double value)
            => MakeReward(id, new GrantModifierEffect(Sel("cash_yield"), ModifierOperation.Multiply, value));

        public static RewardDefinition MakeSetFlagReward(string id, string flagId)
            => MakeReward(id, new SetFlagEffect(flagId));

        public static RewardDefinition MakeReward(string id, GameEffect effect)
        {
            var definition = Track(ScriptableObject.CreateInstance<RewardDefinition>());
            definition.EditorInitialize(id, id, effect);
            return definition;
        }

        // The standard two-group, three-currency economy, as DEFINITIONS. Records
        // is placed Global like the real game's: it lives in the permanent pool,
        // which every economy routes to, so it is reachable from every chapter
        // without appearing in any chapter's roster.
        // A modifier's address and a number's identity are two different things
        // (rule 11): a grant carries a SELECTOR, a reader offers a SUBJECT. They
        // used to be one ModifierTargetKey, which is why a fixture that granted
        // and read through the same value now needs both.
        public static ModifierSelector Sel(params string[] terms) => new(terms);

        public static ModifierSubject Num(string id, params string[] tags) => new(id, tags);

        // One of a currency's two producer numbers, by the id the runtime derives:
        // `cash_rate`, `cash_yield`. Fixtures ask for it rather than spelling the
        // string, so a change to how the id is formed cannot leave the tests
        // agreeing with themselves and disagreeing with the game.
        public static ModifierSubject RateOf(string currencyId)
            => new(CurrencyProducer.NumberId(currencyId, ProductionFeed.Rate), null, currencyId);

        public static ModifierSubject YieldOf(string currencyId)
            => new(CurrencyProducer.NumberId(currencyId, ProductionFeed.Yield), null, currencyId);

        public static CurrencyGroupDefinition[] StandardGroups()
            => new[] { MakeGroup("run", true), MakeGroup("permanent", false, CurrencyPlacement.Global) };

        public static CurrencyDefinition[] StandardCurrencies()
            => new[]
            {
                MakeCurrency("cash", "run"),
                MakeCurrency("fans", "run"),
                MakeCurrency("records", "permanent"),
            };

        // the standard two-group, three-currency economy most fixtures need
        public static CurrencyManager MakeEconomy()
            => new(StandardGroups(), StandardCurrencies());

        // A ContentDatabase for fixtures: the injection constructor, except the
        // currencies and their groups default to the standard set instead of to
        // empty. Boot validation resolves a chapter's currency references against
        // the DATABASE (ChapterCurrencies), so a fixture registering currencies
        // only in a CurrencyManager describes content where nothing resolves -
        // and every test would drown in that rather than in what it is asserting.
        public static ContentDatabase MakeDatabase(
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
            => new(chapters, sections, generators, upgrades, bars, barGroups, events, rewards,
                currencies ?? StandardCurrencies(), currencyGroups ?? StandardGroups(), producers, storyBeats);

        // evaluation context over live test systems; no ContentDatabase, which
        // makes Validate fall back to the systems themselves
        public static ConditionContext MakeContext(ICurrencies currencies,
            GeneratorSystem generators = null, FlagSystem flags = null)
            => new(currencies, generators, flags);

        // The run reset exactly as slice 6's release will perform it (design doc
        // section 12, rule 6): reset the FACTS that declare a run lifetime, then
        // rebuild the modifier store by re-projecting from whatever facts
        // survived. Nothing filters the store - a run-scoped effect disappears
        // because the fact behind it is gone, not because anything went looking
        // for grants to remove.
        //
        // This is the shape every test asserting "what a release keeps" now
        // uses, so the tests exercise the mechanism the release will use rather
        // than a reset call that only existed for them.
        public static void RunReset(ModifierSystem modifiers, UpgradeSystem upgrades = null,
            BarSystem bars = null, GeneratorSystem generators = null, FlagSystem flags = null)
        {
            // facts first, all of them, before the store is touched: a
            // projection that ran against half-reset facts would rebuild
            // effects the release is in the middle of removing
            upgrades?.ResetRunScoped();
            bars?.ResetRunScopedGroups();
            generators?.ResetOwned();
            flags?.ResetRunScoped();

            modifiers.ResetGranted();
            upgrades?.ProjectModifiers();
            bars?.ProjectModifiers();
        }

        // grants exactly enough of the cost currency for each purchase so tests
        // control balances
        public static void BuyTimes(Generator generator, CurrencyManager currencies, int times)
        {
            for (var i = 0; i < times; i++)
            {
                currencies.Add(generator.Definition.CostCurrencyId, generator.NextCost);
                Assert.IsTrue(generator.TryBuy(currencies),
                    $"TryBuy failed for '{generator.Definition.Id}' at owned {generator.Owned}.");
            }
        }

        private static T Track<T>(T created) where T : Object
        {
            created.hideFlags = HideFlags.HideAndDontSave;
            Created.Add(created);
            return created;
        }
    }

    // The same two reads as extensions, for the call sites whose receiver is an
    // expression rather than a local (`system.Get("drummer").LineValue()`).
    internal static class GeneratorTestExtensions
    {
        public static BigNumber LineValue(this Generator generator, string currencyId = "cash")
            => TestContent.LineValue(generator, currencyId);

        public static BigNumber PerUnitLineValue(this Generator generator, string currencyId = "cash")
            => TestContent.PerUnitLineValue(generator, currencyId);
    }
}
