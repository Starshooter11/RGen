using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RhythmGame
{
    /// <summary>
    /// The MainMenu scene's root UI: Standard (play) / Options / Volume / About, each with a
    /// back button, built procedurally at runtime (same approach as LaneSetup/SongSelectMenu).
    /// Auto-creates a Canvas/EventSystem if the scene doesn't already have one, so the
    /// MainMenu scene itself can stay nearly empty — just one GameObject with this component.
    ///
    /// Options is intentionally empty for now. Game-mode selection (2D lanes vs. the planned
    /// 3D-ish "driving" mode) will live there once that mode exists — kept deliberately
    /// separate from song selection (SongSelectMenu) so picking a song never implies a mode.
    /// </summary>
    // Must create the Canvas/EventSystem before SongSelectMenu's own Awake() looks for a
    // Canvas — Unity doesn't guarantee Awake() order between components otherwise, and
    // SongSelectMenu has no fallback creation path of its own (see LaneSetup for the same
    // "runs before GameManager" pattern in the gameplay scene).
    [DefaultExecutionOrder(-10)]
    public class StartMenu : MonoBehaviour
    {
        [Header("References — auto-filled/auto-created if left empty")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private SongSelectMenu _songSelectMenu;

        [Header("Layout")]
        [SerializeField] private Color _backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
        [SerializeField] private Color _itemColor = new Color(0.2f, 0.2f, 0.25f, 1f);
        [SerializeField] private Color _itemHoverColor = new Color(0.3f, 0.3f, 0.4f, 1f);
        [SerializeField] private float _itemHeight = 60f;
        [SerializeField] private float _itemSpacing = 12f;

        [Header("About text")]
        [TextArea(4, 12)]
        [SerializeField] private string _aboutText =
            "RGen\n\nA rhythm game with auto-generated beatmaps.\n\n(About text placeholder — fill in later.)";

        private GameObject _mainPanel;
        private GameObject _optionsPanel;
        private GameObject _volumePanel;
        private GameObject _aboutPanel;

        private Slider _bgmSlider;
        private Slider _uiSlider;

        private void Awake()
        {
            EnsureEventSystem();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
            if (_canvas == null) _canvas = CreateCanvas();
            if (_songSelectMenu == null) _songSelectMenu = FindFirstObjectByType<SongSelectMenu>();

            Build();
        }

        private void Start()
        {
            ShowMain();
        }

        // -------------------------------------------------------------------------
        // Navigation
        // -------------------------------------------------------------------------

        private void HideAll()
        {
            _mainPanel.SetActive(false);
            _optionsPanel.SetActive(false);
            _volumePanel.SetActive(false);
            _aboutPanel.SetActive(false);
        }

        public void ShowMain()
        {
            HideAll();
            _mainPanel.SetActive(true);
        }

        private void OnStandardClicked()
        {
            _mainPanel.SetActive(false);
            if (_songSelectMenu != null) _songSelectMenu.Show(ShowMain);
        }

        private void ShowOptions()
        {
            HideAll();
            _optionsPanel.SetActive(true);
        }

        private void ShowVolume()
        {
            HideAll();
            if (SoundSettings.Instance != null)
            {
                _bgmSlider.SetValueWithoutNotify(SoundSettings.Instance.BgmVolume);
                _uiSlider.SetValueWithoutNotify(SoundSettings.Instance.UiVolume);
            }
            _volumePanel.SetActive(true);
        }

        private void ShowAbout()
        {
            HideAll();
            _aboutPanel.SetActive(true);
        }

        // -------------------------------------------------------------------------
        // Build UI
        // -------------------------------------------------------------------------

        private void Build()
        {
            BuildMainPanel();
            BuildOptionsPanel();
            BuildVolumePanel();
            BuildAboutPanel();
        }

        private void BuildMainPanel()
        {
            _mainPanel = RuntimeUI.CreatePanel(_canvas.transform, "MainMenuPanel", _backgroundColor);

            TextMeshProUGUI title = RuntimeUI.CreateText(_mainPanel.transform, "Title", "RGen", 48, TextAlignmentOptions.Center);
            RectTransform titleRT = title.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.anchoredPosition = new Vector2(0f, -60f);
            titleRT.sizeDelta = new Vector2(0f, 80f);

            var buttonsGO = new GameObject("Buttons", typeof(RectTransform));
            buttonsGO.transform.SetParent(_mainPanel.transform, false);
            RectTransform buttonsRT = buttonsGO.GetComponent<RectTransform>();
            buttonsRT.anchorMin = new Vector2(0.5f, 0.5f);
            buttonsRT.anchorMax = new Vector2(0.5f, 0.5f);
            buttonsRT.pivot = new Vector2(0.5f, 0.5f);
            buttonsRT.anchoredPosition = Vector2.zero;
            buttonsRT.sizeDelta = new Vector2(320f, 4 * _itemHeight + 3 * _itemSpacing);

            var layout = buttonsGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = _itemSpacing;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            RuntimeUI.CreateButton(buttonsGO.transform, "StandardButton", "Standard", _itemColor, _itemHoverColor, _itemHeight, OnStandardClicked);
            RuntimeUI.CreateButton(buttonsGO.transform, "OptionsButton", "Options", _itemColor, _itemHoverColor, _itemHeight, ShowOptions);
            RuntimeUI.CreateButton(buttonsGO.transform, "VolumeButton", "Volume", _itemColor, _itemHoverColor, _itemHeight, ShowVolume);
            RuntimeUI.CreateButton(buttonsGO.transform, "AboutButton", "About", _itemColor, _itemHoverColor, _itemHeight, ShowAbout);

            _mainPanel.SetActive(false);
        }

        private void BuildOptionsPanel()
        {
            _optionsPanel = RuntimeUI.CreatePanel(_canvas.transform, "OptionsPanel", _backgroundColor);
            BuildTitledBackPanel(_optionsPanel, "Options");

            // Intentionally empty for now — see class doc comment above.
            TextMeshProUGUI placeholder = RuntimeUI.CreateText(_optionsPanel.transform, "Placeholder",
                "Nothing here yet.", 22, TextAlignmentOptions.Center);
            RectTransform placeholderRT = placeholder.rectTransform;
            placeholderRT.anchorMin = new Vector2(0.1f, 0.4f);
            placeholderRT.anchorMax = new Vector2(0.9f, 0.6f);
            placeholderRT.offsetMin = Vector2.zero;
            placeholderRT.offsetMax = Vector2.zero;

            _optionsPanel.SetActive(false);
        }

        private void BuildVolumePanel()
        {
            _volumePanel = RuntimeUI.CreatePanel(_canvas.transform, "VolumePanel", _backgroundColor);
            BuildTitledBackPanel(_volumePanel, "Volume");

            TextMeshProUGUI bgmLabel = RuntimeUI.CreateText(_volumePanel.transform, "BgmLabel", "BGM Volume", 24, TextAlignmentOptions.Center);
            RectTransform bgmLabelRT = bgmLabel.rectTransform;
            bgmLabelRT.anchorMin = new Vector2(0.1f, 0.5f);
            bgmLabelRT.anchorMax = new Vector2(0.9f, 0.5f);
            bgmLabelRT.pivot = new Vector2(0.5f, 0.5f);
            bgmLabelRT.anchoredPosition = new Vector2(0f, 90f);
            bgmLabelRT.sizeDelta = new Vector2(0f, 40f);

            _bgmSlider = RuntimeUI.CreateSlider(_volumePanel.transform, "BgmSlider",
                _itemColor, _itemHoverColor, Color.white, 0f, 1f, wholeNumbers: false);
            RectTransform bgmSliderRT = _bgmSlider.GetComponent<RectTransform>();
            bgmSliderRT.anchorMin = new Vector2(0.2f, 0.5f);
            bgmSliderRT.anchorMax = new Vector2(0.8f, 0.5f);
            bgmSliderRT.pivot = new Vector2(0.5f, 0.5f);
            bgmSliderRT.anchoredPosition = new Vector2(0f, 50f);
            bgmSliderRT.sizeDelta = new Vector2(0f, 30f);
            _bgmSlider.onValueChanged.AddListener(v => SoundSettings.Instance?.SetBgmVolume(v));

            TextMeshProUGUI uiLabel = RuntimeUI.CreateText(_volumePanel.transform, "UiLabel", "UI Volume", 24, TextAlignmentOptions.Center);
            RectTransform uiLabelRT = uiLabel.rectTransform;
            uiLabelRT.anchorMin = new Vector2(0.1f, 0.5f);
            uiLabelRT.anchorMax = new Vector2(0.9f, 0.5f);
            uiLabelRT.pivot = new Vector2(0.5f, 0.5f);
            uiLabelRT.anchoredPosition = new Vector2(0f, -30f);
            uiLabelRT.sizeDelta = new Vector2(0f, 40f);

            _uiSlider = RuntimeUI.CreateSlider(_volumePanel.transform, "UiSlider",
                _itemColor, _itemHoverColor, Color.white, 0f, 1f, wholeNumbers: false);
            RectTransform uiSliderRT = _uiSlider.GetComponent<RectTransform>();
            uiSliderRT.anchorMin = new Vector2(0.2f, 0.5f);
            uiSliderRT.anchorMax = new Vector2(0.8f, 0.5f);
            uiSliderRT.pivot = new Vector2(0.5f, 0.5f);
            uiSliderRT.anchoredPosition = new Vector2(0f, -70f);
            uiSliderRT.sizeDelta = new Vector2(0f, 30f);
            _uiSlider.onValueChanged.AddListener(v =>
            {
                SoundSettings.Instance?.SetUiVolume(v);
                SoundSettings.Instance?.PlayClick(); // immediate feedback at the new volume
            });

            _volumePanel.SetActive(false);
        }

        private void BuildAboutPanel()
        {
            _aboutPanel = RuntimeUI.CreatePanel(_canvas.transform, "AboutPanel", _backgroundColor);
            BuildTitledBackPanel(_aboutPanel, "About");

            TextMeshProUGUI body = RuntimeUI.CreateText(_aboutPanel.transform, "Body", _aboutText, 22, TextAlignmentOptions.TopLeft);
            body.enableWordWrapping = true;
            body.enableAutoSizing = false;
            body.fontSize = 22;
            RectTransform bodyRT = body.rectTransform;
            bodyRT.anchorMin = new Vector2(0.1f, 0.1f);
            bodyRT.anchorMax = new Vector2(0.9f, 0.85f);
            bodyRT.offsetMin = Vector2.zero;
            bodyRT.offsetMax = Vector2.zero;

            _aboutPanel.SetActive(false);
        }

        // Title text + back-to-main button, the shape shared by Options/Volume/About.
        private void BuildTitledBackPanel(GameObject panel, string title)
        {
            TextMeshProUGUI titleText = RuntimeUI.CreateText(panel.transform, "Title", title, 32, TextAlignmentOptions.Center);
            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -30f);
            titleRect.sizeDelta = new Vector2(0f, 60f);

            RuntimeUI.CreateBackButton(panel.transform, _itemColor, _itemHoverColor, ShowMain);
        }

        // -------------------------------------------------------------------------
        // Scene bootstrap — lets the MainMenu scene stay nearly empty
        // -------------------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // The project runs on the new Input System package (see InputHandler) rather than
            // the legacy Input Manager, so UI needs InputSystemUIInputModule, not
            // StandaloneInputModule, to actually receive clicks.
            go.AddComponent<InputSystemUIInputModule>();
        }

        // Matches SampleScene's Canvas exactly (Screen Space - Overlay, Constant Pixel Size) —
        // the project targets a fixed landscape orientation (see Player Settings > Resolution
        // and Presentation), so MainMenu's canvas should behave the same way, not assume
        // portrait like a Scale-With-Screen-Size setup would.
        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }
    }
}
