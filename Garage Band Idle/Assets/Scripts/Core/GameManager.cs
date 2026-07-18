using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.Utilities;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Bootstrap and tick orchestration. Discovers currency content from Resources
    // so new CurrencyDefinition / CurrencyGroupDefinition assets are picked up with
    // no code or registration changes, wires the economy to the tick, and exposes
    // the player actions the UI calls.
    [RequireComponent(typeof(TickSystem))]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance => SingletonManager.GetInstance<GameManager>();
        public static bool IsAllocated => SingletonManager.IsAllocated<GameManager>();

        // the slice's hardcoded touchpoints; these stay string ids (not fields on
        // CurrencyManager) so the currency set remains open
        public const string CashCurrencyId = "cash";

        public CurrencyManager Currencies { get; private set; }
        public Generator PracticeAmp { get; private set; }

        private TickSystem _tickSystem;

        private void Awake()
        {
            if (SingletonManager.DestroyIfRegistered(this))
            {
                Debug.LogWarning($"[{GetType().Name}] Attempted to create multiple instances of {GetType().Name}. Destroying this instance.");
                return;
            }

            // LoadAll with an empty path scans every Resources folder, so content
            // discovery is by type, not by a maintained list
            var groups = Resources.LoadAll<CurrencyGroupDefinition>("");
            var currencies = Resources.LoadAll<CurrencyDefinition>("");
            Currencies = new CurrencyManager(groups, currencies);

            // hardcoded for the vertical slice; becomes a GeneratorDefinition asset later
            PracticeAmp = new Generator("Practice Amp", CashCurrencyId, baseCost: 60, costGrowth: 1.15, ratePerSecondPerUnit: 0.4);

            // every hardcoded currency reference is validated at load so a content
            // mistake gets reported here, loudly, instead of surfacing mid-run
            Currencies.ValidateReference(CashCurrencyId, "GameManager");
            Currencies.ValidateReference(PracticeAmp.CurrencyId, $"Generator '{PracticeAmp.DisplayName}'");

            _tickSystem = GetComponent<TickSystem>();
            _tickSystem.Ticked += OnTicked;
        }

        private void OnDestroy()
        {
            if (_tickSystem != null)
                _tickSystem.Ticked -= OnTicked;

            SingletonManager.Unregister(this);
        }

        private void OnTicked(double seconds)
        {
            if (PracticeAmp.Owned > 0)
                Currencies.Add(PracticeAmp.CurrencyId, PracticeAmp.Produce(seconds));
        }

        // the tap action: +1 Cash per tap in this slice
        public void Jam()
        {
            Currencies.Add(CashCurrencyId, 1);
        }

        public bool BuyPracticeAmp() => PracticeAmp.TryBuy(Currencies);
    }
}
