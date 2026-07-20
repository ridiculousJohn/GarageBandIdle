using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Module: the Jam tap button. Tap value text comes from chapter data.
    public class TapModule : MonoBehaviour, IChapterModule
    {
        [SerializeField] private Button _jamButton;
        [SerializeField] private TextMeshProUGUI _jamLabel;

        private ChapterContext _context;

        public void Initialize(ChapterContext context)
        {
            _context = context;
            var cashDefinition = context.Game.Currencies.GetDefinition(GameManager.CashCurrencyId);
            _jamLabel.text = $"JAM\n<size=44>+{NumberFormatter.Format(context.Chapter.TapBaseValue, cashDefinition)} per tap</size>";
            _jamButton.onClick.AddListener(HandleJamClicked);
        }

        private void OnDestroy()
        {
            _jamButton.onClick.RemoveListener(HandleJamClicked);
        }

        private void HandleJamClicked()
        {
            _context.Game.Jam();
        }
    }
}
