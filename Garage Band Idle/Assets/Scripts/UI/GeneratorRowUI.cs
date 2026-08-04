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

        // A modifier on this generator changes the rate the row advertises, and
        // nothing else would repaint it: Refresh is otherwise driven by
        // OwnedChanged alone, so a bought buff (amp_strings, kit_upgrade) left
        // the old "+X/sec" standing until the next purchase - the same
        // staleness ProductionSystem.TapValueChanged cures for the Jam label. A run reset
        // clearing those grants arrives through here too.
        public void HandleModifierChanged(ModifierTargetKey target)
        {
            if (gameObject.activeSelf && target.Equals(Generator.OutputTarget))
                Refresh();
        }

        private void Refresh()
        {
            // both figures come from the composed output, never the raw
            // BaseOutput: a buffed total beside an unbuffed "each" reads as a
            // bug even when each number is defensible on its own
            _info.text = $"{Generator.Definition.DisplayName} x{Generator.Owned}\n" +
                $"+{NumberFormatter.Format(Generator.ProductionPerSecond)} {_producesDefinition.DisplayName}/sec ({NumberFormatter.Format(Generator.PerUnitProduction)} each)";
            _buyLabel.text = $"Buy {NumberFormatter.Format(Generator.NextCost, _costDefinition)}";
            RefreshAffordability();
        }

        private void RefreshAffordability()
        {
            // mirrors TryBuy exactly, including its fail-closed refusal of a
            // non-positive cost - the button is never enabled for a buy that
            // would be refused
            _buyButton.interactable = Generator.NextCost > BigNumber.Zero
                && _game.Currencies.Get(Generator.Definition.CostCurrencyId) >= Generator.NextCost;
        }
    }
}
