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
        [SerializeField] private LatencyCalibrator _latencyCalibrator;

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
        private GameObject _syncPanel;

        private Slider _bgmSlider;
        private Slider _uiSlider;

        private TextMeshProUGUI _syncStatusText;
        private TextMeshProUGUI _syncOffsetText;
        private GameObject _syncStartButton;
        private GameObject _syncTapButton;

        private GameObject[] _laneCountButtons; // indexed 0..(MaxLaneCount-MinLaneCount), for highlighting the current choice

        private void Awake()
        {
            EnsureEventSystem();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
            if (_canvas == null) _canvas = CreateCanvas();
            if (_songSelectMenu == null) _songSelectMenu = FindFirstObjectByType<SongSelectMenu>();
            if (_latencyCalibrator == null) _latencyCalibrator = FindFirstObjectByType<LatencyCalibrator>();
            if (_latencyCalibrator == null) _latencyCalibrator = gameObject.AddComponent<LatencyCalibrator>();

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
            _syncPanel.SetActive(false);
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
            RefreshLaneCountHighlight();
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

        private void ShowSync()
        {
            HideAll();
            RefreshSyncOffsetLabel();
            _syncStatusText.text = "Tap in time with the clicks to measure your audio latency.";
            _syncTapButton.SetActive(false);
            _syncPanel.SetActive(true);
        }

        // Sync panel is nested one level under Options (unlike Volume/About, which are direct
        // children of Main), so its back button returns there instead of going through
        // BuildTitledBackPanel's hardcoded ShowMain — and cancels a test in progress so it
        // doesn't keep scheduling clicks after the panel is gone.
        private void OnSyncBack()
        {
            _latencyCalibrator.CancelCalibration();
            ShowOptions();
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
            BuildSyncPanel();
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

            TextMeshProUGUI laneLabel = RuntimeUI.CreateText(_optionsPanel.transform, "LaneCountLabel", "Lane Count", 24, TextAlignmentOptions.Center);
            RectTransform laneLabelRT = laneLabel.rectTransform;
            laneLabelRT.anchorMin = new Vector2(0.1f, 0.5f);
            laneLabelRT.anchorMax = new Vector2(0.9f, 0.5f);
            laneLabelRT.pivot = new Vector2(0.5f, 0.5f);
            laneLabelRT.anchoredPosition = new Vector2(0f, 100f);
            laneLabelRT.sizeDelta = new Vector2(0f, 40f);

            int laneOptionCount = LaneSettings.MaxLaneCount - LaneSettings.MinLaneCount + 1;
            _laneCountButtons = new GameObject[laneOptionCount];
            float laneBtnWidth = 70f;
            float laneBtnSpacing = 16f;
            float laneRowWidth = laneOptionCount * laneBtnWidth + (laneOptionCount - 1) * laneBtnSpacing;
            float laneStartX = -laneRowWidth / 2f + laneBtnWidth / 2f;
            for (int i = 0; i < laneOptionCount; i++)
            {
                int count = LaneSettings.MinLaneCount + i;
                GameObject btn = RuntimeUI.CreateButton(_optionsPanel.transform, $"LaneCount_{count}", count.ToString(),
                    _itemColor, _itemHoverColor, 44f, () => OnLaneCountChosen(count));
                RectTransform btnRT = btn.GetComponent<RectTransform>();
                btnRT.anchorMin = new Vector2(0.5f, 0.5f);
                btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot = new Vector2(0.5f, 0.5f);
                btnRT.anchoredPosition = new Vector2(laneStartX + i * (laneBtnWidth + laneBtnSpacing), 50f);
                btnRT.sizeDelta = new Vector2(laneBtnWidth, 44f);
                _laneCountButtons[i] = btn;
            }

            // Game-mode selection (see class doc comment) will join these once that mode exists.
            GameObject syncButton = RuntimeUI.CreateButton(_optionsPanel.transform, "AudioSyncButton",
                "Audio Sync", _itemColor, _itemHoverColor, _itemHeight, ShowSync);
            RectTransform syncRT = syncButton.GetComponent<RectTransform>();
            syncRT.anchorMin = new Vector2(0.3f, 0.5f);
            syncRT.anchorMax = new Vector2(0.7f, 0.5f);
            syncRT.pivot = new Vector2(0.5f, 0.5f);
            syncRT.anchoredPosition = new Vector2(0f, -30f);
            syncRT.sizeDelta = new Vector2(0f, _itemHeight);

            _optionsPanel.SetActive(false);
        }

        private void OnLaneCountChosen(int count)
        {
            LaneSettings.SetLaneCount(count);
            RefreshLaneCountHighlight();
        }

        private void RefreshLaneCountHighlight()
        {
            if (_laneCountButtons == null) return;
            int selected = LaneSettings.LaneCount;
            for (int i = 0; i < _laneCountButtons.Length; i++)
            {
                int count = LaneSettings.MinLaneCount + i;
                bool isSelected = count == selected;
                _laneCountButtons[i].GetComponent<Image>().color = isSelected ? _itemHoverColor : _itemColor;
            }
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

        // Tap-to-the-beat audio latency calibration — see LatencyCalibrator for the actual
        // scheduling/measurement logic. This panel is just the front end: a status/progress
        // label, the current saved offset, a Start button that kicks off the test, a Tap button
        // that only appears once it's running, and ±10ms fine-tune buttons for adjusting by
        // feel afterward without a full retest.
        private void BuildSyncPanel()
        {
            _syncPanel = RuntimeUI.CreatePanel(_canvas.transform, "SyncPanel", _backgroundColor);

            TextMeshProUGUI titleText = RuntimeUI.CreateText(_syncPanel.transform, "Title", "Audio Sync", 32, TextAlignmentOptions.Center);
            RectTransform titleRT = titleText.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.anchoredPosition = new Vector2(0f, -30f);
            titleRT.sizeDelta = new Vector2(0f, 60f);

            RuntimeUI.CreateBackButton(_syncPanel.transform, _itemColor, _itemHoverColor, OnSyncBack);

            _syncStatusText = RuntimeUI.CreateText(_syncPanel.transform, "StatusText",
                "Tap in time with the clicks to measure your audio latency.", 22, TextAlignmentOptions.Center);
            _syncStatusText.enableWordWrapping = true;
            RectTransform statusRT = _syncStatusText.rectTransform;
            statusRT.anchorMin = new Vector2(0.1f, 0.66f);
            statusRT.anchorMax = new Vector2(0.9f, 0.82f);
            statusRT.offsetMin = Vector2.zero;
            statusRT.offsetMax = Vector2.zero;

            _syncOffsetText = RuntimeUI.CreateText(_syncPanel.transform, "OffsetText", "", 20, TextAlignmentOptions.Center);
            RectTransform offsetRT = _syncOffsetText.rectTransform;
            offsetRT.anchorMin = new Vector2(0.1f, 0.56f);
            offsetRT.anchorMax = new Vector2(0.9f, 0.66f);
            offsetRT.offsetMin = Vector2.zero;
            offsetRT.offsetMax = Vector2.zero;

            _syncStartButton = RuntimeUI.CreateButton(_syncPanel.transform, "StartButton", "Start Test",
                _itemColor, _itemHoverColor, _itemHeight, OnSyncStartClicked);
            RectTransform startRT = _syncStartButton.GetComponent<RectTransform>();
            startRT.anchorMin = new Vector2(0.3f, 0.42f);
            startRT.anchorMax = new Vector2(0.7f, 0.42f);
            startRT.pivot = new Vector2(0.5f, 0.5f);
            startRT.anchoredPosition = Vector2.zero;
            startRT.sizeDelta = new Vector2(0f, _itemHeight);

            _syncTapButton = RuntimeUI.CreateButton(_syncPanel.transform, "TapButton", "TAP",
                _itemColor, _itemHoverColor, _itemHeight * 2f, () => _latencyCalibrator.RegisterTap());
            RectTransform tapRT = _syncTapButton.GetComponent<RectTransform>();
            tapRT.anchorMin = new Vector2(0.3f, 0.18f);
            tapRT.anchorMax = new Vector2(0.7f, 0.18f);
            tapRT.pivot = new Vector2(0.5f, 0.5f);
            tapRT.anchoredPosition = Vector2.zero;
            tapRT.sizeDelta = new Vector2(0f, _itemHeight * 2f);
            _syncTapButton.SetActive(false); // only shown once a test is actually running

            GameObject nudgeMinus = RuntimeUI.CreateButton(_syncPanel.transform, "NudgeMinus", "-10ms",
                _itemColor, _itemHoverColor, _itemHeight * 0.7f, () => OnSyncNudge(-0.01f));
            RectTransform nudgeMinusRT = nudgeMinus.GetComponent<RectTransform>();
            nudgeMinusRT.anchorMin = new Vector2(0.1f, 0.5f);
            nudgeMinusRT.anchorMax = new Vector2(0.1f, 0.5f);
            nudgeMinusRT.pivot = new Vector2(0.5f, 0.5f);
            nudgeMinusRT.anchoredPosition = new Vector2(0f, -50f);
            nudgeMinusRT.sizeDelta = new Vector2(90f, _itemHeight * 0.7f);

            GameObject nudgePlus = RuntimeUI.CreateButton(_syncPanel.transform, "NudgePlus", "+10ms",
                _itemColor, _itemHoverColor, _itemHeight * 0.7f, () => OnSyncNudge(0.01f));
            RectTransform nudgePlusRT = nudgePlus.GetComponent<RectTransform>();
            nudgePlusRT.anchorMin = new Vector2(0.9f, 0.5f);
            nudgePlusRT.anchorMax = new Vector2(0.9f, 0.5f);
            nudgePlusRT.pivot = new Vector2(0.5f, 0.5f);
            nudgePlusRT.anchoredPosition = new Vector2(0f, -50f);
            nudgePlusRT.sizeDelta = new Vector2(90f, _itemHeight * 0.7f);

            _latencyCalibrator.OnBeatScheduled += OnSyncBeatScheduled;
            _latencyCalibrator.OnTapRegistered += OnSyncTapRegistered;
            _latencyCalibrator.OnCalibrationComplete += OnSyncCalibrationComplete;
            _latencyCalibrator.OnCalibrationCancelled += OnSyncCalibrationCancelled;

            _syncPanel.SetActive(false);
        }

        private void OnSyncStartClicked()
        {
            _syncStartButton.SetActive(false);
            _syncTapButton.SetActive(true);
            _syncStatusText.text = "Get ready...";
            _latencyCalibrator.BeginCalibration();
        }

        private void OnSyncBeatScheduled(int beatIndex, int totalBeats)
        {
            _syncStatusText.text = "Listen... then tap along.";
        }

        private void OnSyncTapRegistered(int tapsRecorded, int scoredBeats)
        {
            _syncStatusText.text = $"Tap {tapsRecorded} / {scoredBeats}";
        }

        private void OnSyncCalibrationComplete(float newOffsetSeconds, int samplesUsed)
        {
            _syncTapButton.SetActive(false);
            _syncStartButton.SetActive(true);
            _syncStatusText.text = samplesUsed > 0
                ? $"Done — measured from {samplesUsed} taps."
                : "Not enough taps registered — try again.";
            RefreshSyncOffsetLabel();
        }

        private void OnSyncCalibrationCancelled()
        {
            _syncTapButton.SetActive(false);
            _syncStartButton.SetActive(true);
        }

        private void OnSyncNudge(float deltaSeconds)
        {
            LatencyCalibrator.ApplyManualAdjustment(deltaSeconds);
            RefreshSyncOffsetLabel();
        }

        private void RefreshSyncOffsetLabel()
        {
            _syncOffsetText.text = $"Current offset: {LatencyCalibrator.CurrentOffset * 1000f:F0} ms";
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
