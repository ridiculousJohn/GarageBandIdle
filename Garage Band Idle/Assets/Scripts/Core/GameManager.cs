using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using RidiculousGaming.Utilities;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Bootstrap and tick orchestration. All definition content is discovered
    // through the ContentDatabase (Addressables labels, see ContentLabels) so
    // new assets are picked up with no code or registration changes; the chapter
    // names its content by id and everything resolves here. Wires the economy to
    // the tick and exposes the player actions the UI calls.
    [RequireComponent(typeof(TickSystem))]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance => SingletonManager.GetInstance<GameManager>();
        public static bool IsAllocated => SingletonManager.IsAllocated<GameManager>();

        // the slice's hardcoded touchpoints; these stay string ids (not fields on
        // CurrencyManager) so the currency set remains open
        public const string CashCurrencyId = "cash";
        public const string RecordsCurrencyId = "records";

        // UI display touchpoints only (CurrencyHeaderModule/TapModule name what
        // they show); the fan SYSTEM takes its ids from the chapter's fans
        // config. The playable pass (slice 10) replaces these with a
        // data-driven currency header.
        public const string FansCurrencyId = "fans";
        public const string FansUnlockFlagId = "fans";

        public ContentDatabase Database { get; private set; }
        public CurrencyManager Currencies { get; private set; }
        public FlagSystem Flags { get; private set; }
        public ChapterDefinition CurrentChapter { get; private set; }
        public GeneratorSystem Generators { get; private set; }
        public UpgradeSystem Upgrades { get; private set; }
        public FanSystem Fans { get; private set; }
        public TapSystem Tap { get; private set; }
        public RehearsalSystem Rehearsal { get; private set; }
        public RewardManager Rewards { get; private set; }
        public BarSystem Bars { get; private set; }
        public ConditionContext Conditions { get; private set; }

        // the current chapter's sections in layout order, resolved from its id list
        public IReadOnlyList<SectionDefinition> Sections { get; private set; }

        private TickSystem _tickSystem;

        private void Awake()
        {
            if (SingletonManager.DestroyIfRegistered(this))
            {
                Debug.LogWarning($"[{GetType().Name}] Attempted to create multiple instances of {GetType().Name}. Destroying this instance.");
                return;
            }

            Database = new ContentDatabase();
            Currencies = new CurrencyManager(Database.CurrencyGroups.All, Database.Currencies.All);

            // the lowest chapter index is the starting chapter; chapter advancement
            // (ChapterManager) is a later slice
            foreach (var chapter in Database.Chapters.All)
            {
                if (CurrentChapter == null || chapter.Index < CurrentChapter.Index)
                    CurrentChapter = chapter;
            }

            if (CurrentChapter == null)
            {
                Debug.LogError("GameManager: no ChapterDefinition assets found. Run 'GarageBandIdle → Import Chapter 1 JSON' in the editor menu, then press Play again.");
                Flags = new FlagSystem();
            }
            else
            {
                // the chapter's declared flags are the known set; setting or
                // gating on anything else is reported as a content mistake
                Flags = new FlagSystem(CurrentChapter.FlagIds);
                Generators = new GeneratorSystem(Resolve(Database.Generators, CurrentChapter.GeneratorIds, "generator"), Currencies);
                Upgrades = new UpgradeSystem(Resolve(Database.Upgrades, CurrentChapter.UpgradeIds, "upgrade"), Currencies, Flags);
                Fans = new FanSystem(CurrentChapter.Fans, Currencies, Generators, Flags);
                Tap = new TapSystem(CurrentChapter.TapBaseValue);
                Rehearsal = new RehearsalSystem(CurrentChapter.Rehearsal, Currencies, Flags);
                Rewards = new RewardManager(Database.Rewards.All);
                Bars = new BarSystem(Resolve(Database.BarGroups, CurrentChapter.BarGroupIds, "bar group"),
                    Database.Bars.All, Currencies, Rewards, new RewardContext(Currencies, Flags, Fans, Tap));
                Sections = Resolve(Database.Sections, CurrentChapter.SectionIds, "section");

                Conditions = new ConditionContext(Currencies, Generators, Flags, RecordsCurrencyId, Database, Bars);

                // one boot pass covers every content reference — conditions,
                // payloads, rewards, module addresses — so a mistake gets
                // reported here, loudly, instead of surfacing mid-run
                ContentValidator.Validate(Database, Conditions, Rewards);
            }

            Currencies.ValidateReference(CashCurrencyId, "GameManager (tap)");
            Currencies.ValidateReference(RecordsCurrencyId, "GameManager (income multiplier)");

            _tickSystem = GetComponent<TickSystem>();
            _tickSystem.Ticked += OnTicked;
        }

        // maps a chapter's ordered id list to definitions, reporting any id that
        // fails to resolve (the chapter is authored against the same JSON that
        // generated the assets, so a miss means a stale import)
        private static List<T> Resolve<T>(ContentDatabase.Registry<T> registry, IReadOnlyList<string> ids, string kind)
            where T : ScriptableObject
        {
            var definitions = new List<T>(ids.Count);
            foreach (var id in ids)
            {
                if (registry.TryGet(id, out var definition))
                    definitions.Add(definition);
                else
                    Debug.LogError($"GameManager: chapter references unknown {kind} id '{id}'. Re-run the chapter import.");
            }
            return definitions;
        }

        private void OnDestroy()
        {
            if (_tickSystem != null)
                _tickSystem.Ticked -= OnTicked;

            SingletonManager.Unregister(this);
        }

        private void OnTicked(double seconds)
        {
            if (Generators == null)
                return;

            // the Records buff applies only to the currencies it declares
            // (cash in Ch1); production of anything it doesn't name is untouched
            var multiplier = ProductionCalculator.IncomeMultiplier(
                Currencies.Get(RecordsCurrencyId), CurrentChapter.RecordBuff.PerRecord);
            Generators.Tick(seconds, multiplier, CurrentChapter.RecordBuff.AffectsCurrencyIds);
            Generators.EvaluateUnlocks(Conditions);

            // content unlocks before fan accrual so a freshly-set fans flag
            // starts accruing on the same tick; fans never take the income
            // multiplier — fan rate is band size and time only
            Upgrades.EvaluateContentUnlocks(Conditions);
            Fans.Tick(seconds);

            // rehearsal accrues, then bars drain the pool into the active bar
            // in the same tick, so a selected bar advances with no pool lag
            Rehearsal.Tick(seconds);
            Bars.Tick();
        }

        // the tap action; cash per tap = chapter base × tap-reward multipliers
        // (flat tap buffs like stage_presence arrive with the buff slice)
        public void Jam()
        {
            if (CurrentChapter == null)
                return;

            Currencies.Add(CashCurrencyId, Tap.Value);

            // taps also yield the fill currency; drain immediately so the
            // active bar visibly nudges on the tap, not a tick later
            Rehearsal.OnJamTap();
            Bars.Tick();
        }

        public bool BuyGenerator(Generator generator)
        {
            if (generator == null || !generator.Unlocked)
                return false;
            if (!generator.TryBuy(Currencies))
                return false;

            // a purchase can satisfy another generator's ownedCount unlock — or a
            // content unlock's gate (play_for_crowd: own 1 Drummer) — right now
            Generators.EvaluateUnlocks(Conditions);
            Upgrades.EvaluateContentUnlocks(Conditions);
            return true;
        }
    }
}
