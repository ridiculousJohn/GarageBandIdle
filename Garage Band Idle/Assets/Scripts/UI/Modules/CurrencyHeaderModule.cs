using TMPro;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the currency readout header. Shows Cash for now; grows Fans and
    // Records displays with their slices.
    public class CurrencyHeaderModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private TextMeshProUGUI _cashLabel;

        private ChapterContext _context;
        private CurrencyDefinition _cashDefinition;

        public void Initialize(ChapterContext context)
        {
            _context = context;
            _cashDefinition = context.Game.Currencies.GetDefinition(GameManager.CashCurrencyId);
            context.Game.Currencies.BalanceChanged += HandleBalanceChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Game.Currencies.BalanceChanged -= HandleBalanceChanged;
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            if (currencyId == GameManager.CashCurrencyId)
                Refresh();
        }

        private void Refresh()
        {
            var cash = _context.Game.Currencies.Get(GameManager.CashCurrencyId);
            _cashLabel.text = $"{_cashDefinition.DisplayName}: {NumberFormatter.Format(cash, _cashDefinition)}";
        }
    }
}
