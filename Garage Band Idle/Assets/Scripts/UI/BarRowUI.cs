using RidiculousGaming.GarageBandIdle.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One fillable bar in the list. Lives in the BarRow prefab; BarListModule
    // instantiates one per bar and binds it. Clicking toggles this bar as the
    // group's active target (player-directed fill, design doc section 6).
    public class BarRowUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _info;
        [SerializeField] private Image _progressFill;
        [SerializeField] private Button _selectButton;
        [SerializeField] private TextMeshProUGUI _selectLabel;

        private GameManager _game;

        public BarSystem.BarState Bar { get; private set; }

        public void Bind(GameManager game, BarSystem.BarState bar)
        {
            _game = game;
            Bar = bar;

            _selectButton.onClick.AddListener(HandleSelectClicked);
            Refresh();
        }

        private void OnDestroy()
        {
            _selectButton.onClick.RemoveListener(HandleSelectClicked);
        }

        // toggle: selecting the active bar again clears the target and lets the
        // pool accumulate until the player picks the next bar
        private void HandleSelectClicked()
        {
            var groupId = Bar.Group.Id;
            var isActive = _game.Bars.GetActiveBar(groupId) == Bar;
            _game.Bars.SetActiveBar(groupId, isActive ? null : Bar.Definition.Id);
        }

        public void Refresh()
        {
            var definition = Bar.Definition;
            var reward = _game.Rewards.Get(definition.RewardId);
            var rewardText = reward != null ? $" | {reward.DisplayName}" : "";
            _info.text = $"{definition.DisplayName}\n" +
                $"{NumberFormatter.Format(Bar.Progress)} / {NumberFormatter.Format(definition.FillRequirement)}{rewardText}";

            _progressFill.fillAmount = definition.FillRequirement > 0
                ? Mathf.Clamp01((float)(Bar.Progress.ToDouble() / definition.FillRequirement))
                : 0f;

            if (Bar.Completed)
            {
                _selectButton.interactable = false;
                _selectLabel.text = "Done";
            }
            else
            {
                _selectButton.interactable = true;
                _selectLabel.text = _game.Bars.GetActiveBar(Bar.Group.Id) == Bar ? "Rehearsing..." : "Rehearse";
            }
        }
    }
}
