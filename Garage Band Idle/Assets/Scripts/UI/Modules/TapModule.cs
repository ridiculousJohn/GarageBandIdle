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

        public void Initialize(ChapterContext context)
        {
            var cashDefinition = context.Game.Currencies.GetDefinition(GameManager.CashCurrencyId);
            _jamLabel.text = $"JAM\n<size=44>+{NumberFormatter.Format(context.Chapter.TapBaseValue, cashDefinition)} per tap</size>";
            _jamButton.onClick.AddListener(() => context.Game.Jam());
        }
    }
}
