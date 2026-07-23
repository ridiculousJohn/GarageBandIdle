using RidiculousGaming.GarageBandIdle.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One generator row in the list. Lives in the GeneratorRow prefab;
    // GeneratorListModule instantiates one per chapter generator and binds it.
    // Hidden while its generator is locked.
    public class GeneratorRowUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _info;
        [SerializeField] private Button _buyButton;
        [SerializeField] private TextMeshProUGUI _buyLabel;

        private GameManager _game;
        private CurrencyDefinition _producesDefinition;
        private CurrencyDefinition _costDefinition;

        public Generator Generator { get; private set; }

        public void Bind(GameManager game, Generator generator)
        {
            _game = game;
            Generator = generator;
            _producesDefinition = game.Currencies.GetDefinition(generator.Definition.ProducesCurrencyId);
            _costDefinition = game.Currencies.GetDefinition(generator.Definition.CostCurrencyId);

            _buyButton.onClick.AddListener(HandleBuyClicked);
            Generator.OwnedChanged += Refresh;

            gameObject.SetActive(Generator.Unlocked);
            Refresh();
        }

        private void OnDestroy()
        {
            _buyButton.onClick.RemoveListener(HandleBuyClicked);
            if (Generator != null)
                Generator.OwnedChanged -= Refresh;
        }

        private void HandleBuyClicked()
        {
            _game.BuyGenerator(Generator);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            Refresh();
        }

        // affordability moves whenever the cost currency's balance moves
        public void HandleBalanceChanged(string currencyId)
        {
            if (gameObject.activeSelf && Generator.Definition.CostCurrencyId == currencyId)
                RefreshAffordability();
        }

        private void Refresh()
        {
            _info.text = $"{Generator.Definition.DisplayName} x{Generator.Owned}\n" +
                $"+{NumberFormatter.Format(Generator.ProductionPerSecond)} {_producesDefinition.DisplayName}/sec ({NumberFormatter.Format(Generator.Definition.BaseOutput)} each)";
            _buyLabel.text = $"Buy {NumberFormatter.Format(Generator.NextCost, _costDefinition)}";
            RefreshAffordability();
        }

        private void RefreshAffordability()
        {
            // mirrors TryBuy exactly, including its fail-closed refusal of a
            // non-positive cost — the button is never enabled for a buy that
            // would be refused
            _buyButton.interactable = Generator.NextCost > BigNumber.Zero
                && _game.Currencies.Get(Generator.Definition.CostCurrencyId) >= Generator.NextCost;
        }
    }
}
