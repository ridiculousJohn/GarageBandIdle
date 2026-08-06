using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the Jam tap button. The label advertises what a tap actually
    // grants - ProductionSystem.TapValue, the jam producer's cash config times
    // the multiplier stacks - and refreshes whenever the tap value moves
    // (reward applied, run reset).
    public class TapModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private Button _jamButton;
        [SerializeField] private TextMeshProUGUI _jamLabel;

        private ChapterContext _context;
        private CurrencyDefinition _cashDefinition;

        public void Initialize(ChapterContext context)
        {
            _context = context;
            _cashDefinition = context.Economy.Currencies.GetDefinition(GameManager.CashCurrencyId);
            RefreshLabel();
            context.Economy.Production.TapValueChanged += RefreshLabel;
            _jamButton.onClick.AddListener(HandleJamClicked);
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.Economy.Production.TapValueChanged -= RefreshLabel;
            _jamButton.onClick.RemoveListener(HandleJamClicked);
        }

        private void RefreshLabel()
        {
            _jamLabel.text = $"JAM\n<size=44>+{NumberFormatter.Format(_context.Economy.Production.TapValue, _cashDefinition)} per tap</size>";
        }

        private void HandleJamClicked()
        {
            _context.Economy.Jam();
        }
    }
}
