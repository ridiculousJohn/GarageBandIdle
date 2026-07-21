using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using RidiculousGaming.Utilities;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RidiculousGaming.GarageBandIdle
{
    // Bootstrap and tick orchestration. Discovers content through Addressables
    // by per-type label (see ContentLabels) so new assets are picked up with no
    // code or registration changes, wires the economy to the tick, and exposes
    // the player actions the UI calls.
    [RequireComponent(typeof(TickSystem))]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance => SingletonManager.GetInstance<GameManager>();
        public static bool IsAllocated => SingletonManager.IsAllocated<GameManager>();

        // the slice's hardcoded touchpoints; these stay string ids (not fields on
        // CurrencyManager) so the currency set remains open
        public const string CashCurrencyId = "cash";
        public const string RecordsCurrencyId = "records";
        public const string FansCurrencyId = "fans";

        // the system key in the JSON's unlockSystem payloads doubles as the
        // progress flag id — play_for_crowd sets "fans", which activates FanSystem
        public const string FansUnlockFlagId = "fans";

        public CurrencyManager Currencies { get; private set; }
        public FlagSystem Flags { get; private set; }
        public ChapterDefinition CurrentChapter { get; private set; }
        public GeneratorSystem Generators { get; private set; }
        public UpgradeSystem Upgrades { get; private set; }
        public FanSystem Fans { get; private set; }

        private TickSystem _tickSystem;

        private void Awake()
        {
            if (SingletonManager.DestroyIfRegistered(this))
            {
                Debug.LogWarning($"[{GetType().Name}] Attempted to create multiple instances of {GetType().Name}. Destroying this instance.");
                return;
            }

            var groups = LoadAll<CurrencyGroupDefinition>(ContentLabels.CurrencyGroup);
            var currencies = LoadAll<CurrencyDefinition>(ContentLabels.Currency);
            Currencies = new CurrencyManager(groups, currencies);
            Flags = new FlagSystem();

            // the lowest chapter index is the starting chapter; chapter advancement
            // (ChapterManager) is a later slice
            foreach (var chapter in LoadAll<ChapterDefinition>(ContentLabels.Chapter))
            {
                if (CurrentChapter == null || chapter.Index < CurrentChapter.Index)
                    CurrentChapter = chapter;
            }

            if (CurrentChapter == null)
            {
                Debug.LogError("GameManager: no ChapterDefinition assets found. Run 'GarageBandIdle → Import Chapter 1 JSON' in the editor menu, then press Play again.");
            }
            else
            {
                Generators = new GeneratorSystem(CurrentChapter.Generators, Currencies, Flags);
                Upgrades = new UpgradeSystem(CurrentChapter.Upgrades, Currencies, Generators, Flags);
                Fans = new FanSystem(CurrentChapter.Fans, FansCurrencyId, FansUnlockFlagId, Currencies, Generators, Flags);
            }

            // every hardcoded currency reference is validated at load so a content
            // mistake gets reported here, loudly, instead of surfacing mid-run
            Currencies.ValidateReference(CashCurrencyId, "GameManager (tap)");
            Currencies.ValidateReference(RecordsCurrencyId, "GameManager (income multiplier)");

            _tickSystem = GetComponent<TickSystem>();
            _tickSystem.Ticked += OnTicked;
        }

        // Synchronous label load, held for the app's lifetime (definitions are
        // needed as long as the game runs, so handles are never released).
        // WaitForCompletion keeps bootstrap simple; this becomes async behind a
        // loading screen in a later slice.
        private static IList<T> LoadAll<T>(string label)
        {
            try
            {
                return Addressables.LoadAssetsAsync<T>(label, null).WaitForCompletion();
            }
            catch (Exception exception)
            {
                // Addressables throws InvalidKeyException when a label has no
                // entries yet, i.e. content was never imported/marked
                Debug.LogError($"GameManager: loading addressable content with label '{label}' failed — " +
                    $"run 'GarageBandIdle → Import Chapter 1 JSON' (it marks all content addressable), then press Play again. ({exception.Message})");
                return Array.Empty<T>();
            }
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

            var multiplier = ProductionCalculator.IncomeMultiplier(
                Currencies.Get(RecordsCurrencyId), CurrentChapter.RecordBuffPerRecord);
            Generators.Tick(seconds, multiplier);
            Generators.EvaluateUnlocks();

            // content unlocks before fan accrual so a freshly-set fans flag
            // starts accruing on the same tick; fans never take the income
            // multiplier — fan rate is band size and time only
            Upgrades.EvaluateContentUnlocks();
            Fans.Tick(seconds);
        }

        // the tap action; tap buffs (stage_presence etc.) arrive in the upgrades slice
        public void Jam()
        {
            if (CurrentChapter == null)
                return;

            Currencies.Add(CashCurrencyId, CurrentChapter.TapBaseValue);
        }

        public bool BuyGenerator(Generator generator)
        {
            if (generator == null || !generator.Unlocked)
                return false;
            if (!generator.TryBuy(Currencies))
                return false;

            // a purchase can satisfy another generator's ownedCount unlock — or a
            // content unlock's gate (play_for_crowd: own 1 Drummer) — right now
            Generators.EvaluateUnlocks();
            Upgrades.EvaluateContentUnlocks();
            return true;
        }
    }
}
