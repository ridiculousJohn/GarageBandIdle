using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Builds the slice's single-screen HUD in code and drives it from
    // CurrencyManager / Generator events; nothing polls balances per frame.
    // A scene-authored layout can replace BuildUI without touching the wiring.
    public class GameHUD : MonoBehaviour
    {
        private GameManager _game;
        private CurrencyDefinition _cashDefinition;

        private TextMeshProUGUI _cashLabel;
        private TextMeshProUGUI _ampInfoLabel;
        private TextMeshProUGUI _buyLabel;
        private Button _buyButton;

        private void Start()
        {
            _game = GameManager.Instance;
            _cashDefinition = _game.Currencies.GetDefinition(GameManager.CashCurrencyId);

            BuildUI();

            _game.Currencies.BalanceChanged += HandleBalanceChanged;
            _game.PracticeAmp.OwnedChanged += HandleAmpOwnedChanged;

            RefreshAmp();
            RefreshCash();
        }

        private void OnDestroy()
        {
            if (_game == null)
                return;
            _game.Currencies.BalanceChanged -= HandleBalanceChanged;
            _game.PracticeAmp.OwnedChanged -= HandleAmpOwnedChanged;
        }

        private void HandleBalanceChanged(string currencyId, BigNumber balance)
        {
            if (currencyId == GameManager.CashCurrencyId)
                RefreshCash();
        }

        private void HandleAmpOwnedChanged()
        {
            RefreshAmp();
            // the next cost moved, so affordability needs re-evaluating too
            RefreshCash();
        }

        private void RefreshCash()
        {
            var cash = _game.Currencies.Get(GameManager.CashCurrencyId);
            _cashLabel.text = $"{_cashDefinition.DisplayName}: {NumberFormatter.Format(cash, _cashDefinition)}";
            _buyButton.interactable = cash >= _game.PracticeAmp.NextCost;
        }

        private void RefreshAmp()
        {
            var amp = _game.PracticeAmp;
            _ampInfoLabel.text = $"{amp.DisplayName} x{amp.Owned}\n+{NumberFormatter.Format(amp.ProductionPerSecond)} {_cashDefinition.DisplayName}/sec ({NumberFormatter.Format(amp.RatePerUnit)} each)";
            _buyLabel.text = $"Buy {NumberFormatter.Format(amp.NextCost, _cashDefinition)}";
        }

        private void BuildUI()
        {
            EnsureEventSystem();

            var canvasObject = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasObject.transform;

            _cashLabel = CreateLabel(root, "CashLabel", 88, FontStyles.Bold);
            Anchor(_cashLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -190), new Vector2(1000, 160));

            var jamButton = CreateButton(root, "JamButton", new Color(0.18f, 0.55f, 0.25f), out var jamLabel);
            Anchor((RectTransform)jamButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(560, 280));
            jamLabel.fontSize = 96;
            // the sub-line mirrors GameManager.Jam's hardcoded +1; both go data-driven in a later slice
            jamLabel.text = "JAM\n<size=44>+$1 per tap</size>";
            jamButton.onClick.AddListener(() => _game.Jam());

            _ampInfoLabel = CreateLabel(root, "AmpInfo", 52, FontStyles.Normal);
            Anchor(_ampInfoLabel.rectTransform, new Vector2(0.35f, 0f), new Vector2(0, 200), new Vector2(620, 180));

            _buyButton = CreateButton(root, "BuyAmpButton", new Color(0.2f, 0.35f, 0.65f), out _buyLabel);
            Anchor((RectTransform)_buyButton.transform, new Vector2(0.78f, 0f), new Vector2(0, 200), new Vector2(380, 180));
            _buyLabel.fontSize = 52;
            _buyButton.onClick.AddListener(() => _game.BuyPracticeAmp());
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            // the project is set to the Input System package only, so the legacy
            // StandaloneInputModule would receive no input here
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, float fontSize, FontStyles style)
        {
            var labelObject = new GameObject(name, typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, Color color, out TextMeshProUGUI label)
        {
            // no sprite: a tinted solid rect is enough for the slice and keeps the
            // build free of art assets
            var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = color;

            label = CreateLabel(buttonObject.transform, "Label", 60, FontStyles.Bold);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;

            return buttonObject.GetComponent<Button>();
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
