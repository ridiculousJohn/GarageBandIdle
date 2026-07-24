using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the Jam tap button. The label advertises what a tap actually
    // grants - TapSystem.Value, the chapter base times the multiplier stacks -
    // and refreshes whenever the tap value moves (reward applied, run reset).
    public class TapModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private Button _jamButton;
        [SerializeField] private TextMeshProUGUI _jamLabel;

        private ChapterContext _context;
        private CurrencyDefinition _cashDefinition;

        public void Initialize(ChapterContext context)
        {
            _context = context;
            _cashDefinition = context.Game.Currencies.GetDefinition(GameManager.CashCurrencyId);
            RefreshLabel();
            context.Game.Tap.ValueChanged += RefreshLabel;
            _jamButton.onClick.AddListener(HandleJamClicked);
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Game.Tap.ValueChanged -= RefreshLabel;
            _jamButton.onClick.RemoveListener(HandleJamClicked);
        }

        private void RefreshLabel()
        {
            _jamLabel.text = $"JAM\n<size=44>+{NumberFormatter.Format(_context.Game.Tap.Value, _cashDefinition)} per tap</size>";
        }

        private void HandleJamClicked()
        {
            _context.Game.Jam();
        }
    }
}
