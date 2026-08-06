using RidiculousGaming.GarageBandIdle.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One buff row in the upgrade list. Lives in the UpgradeRow prefab;
    // UpgradeListModule instantiates one per chapter buff and binds it. Hidden
    // until the buff's gate holds, and hidden again once it is bought - a buff
    // is bought once per run, so a run reset brings the row back.
    public class UpgradeRowUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _info;
        [SerializeField] private Button _buyButton;
        [SerializeField] private TextMeshProUGUI _buyLabel;

        private ChapterContext _context;
        private CurrencyDefinition _costDefinition;

        public Upgrade Upgrade { get; private set; }

        public void Bind(ChapterContext context, Upgrade upgrade)
        {
            _context = context;
            Upgrade = upgrade;
            _costDefinition = context.Economy.Currencies.GetDefinition(upgrade.Definition.CostCurrencyId);

            _buyButton.onClick.AddListener(HandleBuyClicked);

            _info.text = upgrade.Definition.DisplayName;
            // an unresolvable cost currency is broken content that boot
            // validation reports; the price still renders, without a symbol
            _buyLabel.text = _costDefinition != null
                ? $"Buy {NumberFormatter.Format(upgrade.Definition.CostAmount, _costDefinition)}"
                : $"Buy {NumberFormatter.Format(upgrade.Definition.CostAmount)}";
            Refresh();
        }

        private void OnDestroy()
        {
            _buyButton.onClick.RemoveListener(HandleBuyClicked);
        }

        private void HandleBuyClicked()
        {
            // BuyUpgrade re-evaluates content unlocks; the module refreshes every
            // row off UpgradeApplied, so this row hides itself through Refresh
            _context.Economy.BuyUpgrade(Upgrade);
        }

        // Availability and affordability are separate questions: an ungated or
        // bought buff has no row at all, while a gated-in but unaffordable one
        // shows its price with the button disabled.
        public void Refresh()
        {
            var available = _context.Economy.Upgrades.IsAvailable(Upgrade, _context.Economy.Conditions);
            gameObject.SetActive(available);
            if (!available)
                return;

            // mirrors TryBuy's refusals, so the button is never enabled for a
            // purchase that would be refused
            _buyButton.interactable = _context.Economy.Upgrades.CanAfford(Upgrade);
        }
    }
}
