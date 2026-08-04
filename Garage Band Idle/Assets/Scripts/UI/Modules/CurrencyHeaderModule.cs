using TMPro;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the currency readout header. Shows Cash always; the Fans meter
    // stays hidden until the fans content unlock (play_for_crowd) sets its
    // flag. Records display grows in with its slice.
    public class CurrencyHeaderModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private TextMeshProUGUI _cashLabel;
        [SerializeField] private TextMeshProUGUI _fansLabel;

        private ChapterContext _context;
        private CurrencyDefinition _cashDefinition;
        private CurrencyDefinition _fansDefinition;

        public void Initialize(ChapterContext context)
        {
            _context = context;
            _cashDefinition = context.Economy.Currencies.GetDefinition(GameManager.CashCurrencyId);
            _fansDefinition = context.Economy.Currencies.GetDefinition(GameManager.FansCurrencyId);

            context.Economy.Currencies.BalanceChanged += HandleBalanceChanged;
            context.Flags.FlagSet += HandleFlagSet;

            _fansLabel.gameObject.SetActive(context.Flags.IsSet(GameManager.FansUnlockFlagId));
            RefreshCash();
            RefreshFans();
        }

        private void OnDestroy()
        {
            if (_context == null)
                return;

            _context.Economy.Currencies.BalanceChanged -= HandleBalanceChanged;
            _context.Flags.FlagSet -= HandleFlagSet;
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            if (currencyId == GameManager.CashCurrencyId)
                RefreshCash();
            else if (currencyId == GameManager.FansCurrencyId)
                RefreshFans();
        }

        private void HandleFlagSet(string flagId)
        {
            if (flagId != GameManager.FansUnlockFlagId)
                return;

            _fansLabel.gameObject.SetActive(true);
            RefreshFans();
        }

        private void RefreshCash()
        {
            var cash = _context.Economy.Currencies.Get(GameManager.CashCurrencyId);
            _cashLabel.text = $"{_cashDefinition.DisplayName}: {NumberFormatter.Format(cash, _cashDefinition)}";
        }

        // shows the rate so it's visible that fan accrual moves with band size,
        // never with Cash
        private void RefreshFans()
        {
            if (!_fansLabel.gameObject.activeSelf)
                return;

            var fans = _context.Economy.Currencies.Get(GameManager.FansCurrencyId);
            _fansLabel.text = $"{_fansDefinition.DisplayName}: {NumberFormatter.Format(fans, _fansDefinition)}" +
                $"  (+{NumberFormatter.Format(_context.Economy.Fans.RatePerSecond)}/s)";
        }
    }
}
