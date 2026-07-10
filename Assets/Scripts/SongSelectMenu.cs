using System.Collections;
using System.Collections.Generic;
using System.Xml;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RhythmGame
{
    /// <summary>
    /// Builds a scrollable, multi-step song-select overlay at runtime (same procedural
    /// approach as LaneSetup, via the shared RuntimeUI helpers): pick a song → pick an
    /// instrument part parsed from that song's sheet music → (if the part has two staves)
    /// pick which hand(s) to play → pick the max number of notes allowed on screen at once →
    /// pick note speed / playback speed. Shown from StartMenu's Standard button; on finish,
    /// stashes the choice in PendingSelection and loads the gameplay scene.
    /// </summary>
    public class SongSelectMenu : MonoBehaviour
    {
        [Header("References — auto-filled if left empty")]
        [SerializeField] private Canvas _canvas;

        [Header("Layout")]
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.85f);
        [SerializeField] private Color _itemColor = new Color(0.2f, 0.2f, 0.25f, 1f);
        [SerializeField] private Color _itemHoverColor = new Color(0.3f, 0.3f, 0.4f, 1f);
        [SerializeField] private float _itemHeight = 60f;
        [SerializeField] private float _itemSpacing = 8f;

        [Header("Gameplay scene")]
        [SerializeField] private string _gameplaySceneName = "SampleScene";

        private GameObject _songPanel;
        private RectTransform _songContent;
        private GameObject _partPanel;
        private RectTransform _partContent;
        private GameObject _clefPanel;
        private RectTransform _clefContent;
        private GameObject _maxNotesPanel;
        private RectTransform _maxNotesContent;
        private GameObject _speedPanel;
        private Slider _speedSlider;
        private TextMeshProUGUI _speedValueLabel;
        private float _selectedPlaybackSpeed = 1f;
        private readonly List<GameObject> _playbackSpeedButtons = new List<GameObject>();

        private readonly List<GameObject> _spawnedSongItems = new List<GameObject>();
        private readonly List<GameObject> _spawnedPartItems = new List<GameObject>();
        private readonly List<GameObject> _spawnedClefItems = new List<GameObject>();
        private readonly List<GameObject> _spawnedMaxNotesItems = new List<GameObject>();

        // Selection state carried between steps
        private SongLibrary.SongEntry _pendingSong;
        private XmlDocument _pendingDoc;
        private List<SheetMusicImporter.PartOption> _pendingParts;
        // The max-notes step's back button destination changes depending on whether the
        // clef step was shown (grand staff) or skipped (single-staff instrument).
        private System.Action _maxNotesBackAction;
        private System.Action _speedBackAction;
        private System.Action _onSpeedConfirmed;
        private Coroutine _songListLoadRoutine;

        // Speed defaults mirror GameManager's own defaults/ranges (that component isn't in
        // this scene, so we can't read them off a live instance — see PendingSelection).
        private const float DefaultSpeedLevel = 5f;
        private const float MinSpeedLevel = 1f;
        private const float MaxSpeedLevel = 20f;

        // Invoked by the root song-list step's back button — set per-Show() call since
        // StartMenu is the one who knows where "back" should go.
        private System.Action _onExit;

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
            Build();
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        // Resets to the first step (song list). onExit is called if the player backs all the
        // way out (e.g. to return to StartMenu's main panel); pass null for no back button.
        public void Show(System.Action onExit = null)
        {
            _onExit = onExit;
            HideAll();
            PopulateSongList();
            _songPanel.SetActive(true);
        }

        public void Hide() => HideAll();

        private void HideAll()
        {
            _songPanel.SetActive(false);
            _partPanel.SetActive(false);
            _clefPanel.SetActive(false);
            _maxNotesPanel.SetActive(false);
            _speedPanel.SetActive(false);
        }

        // -------------------------------------------------------------------------
        // Step 1: song list
        // -------------------------------------------------------------------------

        private void PopulateSongList()
        {
            RuntimeUI.ClearItems(_spawnedSongItems);
            _spawnedSongItems.Add(RuntimeUI.CreateEmptyLabel(_songContent, "Loading songs...", _itemHeight));

            if (_songListLoadRoutine != null) StopCoroutine(_songListLoadRoutine);
            _songListLoadRoutine = StartCoroutine(LoadSongListRoutine());
        }

        private IEnumerator LoadSongListRoutine()
        {
            List<SongLibrary.SongEntry> songs = null;
            yield return SongLibrary.LoadAsync(result => songs = result);

            RuntimeUI.ClearItems(_spawnedSongItems); // remove the "Loading..." placeholder

            if (songs == null || songs.Count == 0)
            {
                _spawnedSongItems.Add(RuntimeUI.CreateEmptyLabel(_songContent, "No songs found in StreamingAssets.", _itemHeight));
                yield break;
            }

            foreach (SongLibrary.SongEntry song in songs)
                _spawnedSongItems.Add(CreateItem(_songContent, $"Song_{song.displayName}", song.displayName, () => OnSongChosen(song)));
        }

        private void OnSongChosen(SongLibrary.SongEntry song)
        {
            _pendingSong = song;
            StartCoroutine(LoadPartsRoutine(song));
        }

        private IEnumerator LoadPartsRoutine(SongLibrary.SongEntry song)
        {
            XmlDocument doc = null;
            yield return SheetMusicImporter.LoadDocumentFromStreamingAssetsAsync(song.sheetMusicFileName, d => doc = d);

            if (doc == null)
            {
                Debug.LogError($"[SongSelectMenu] Could not read sheet music for '{song.displayName}'.");
                Show(_onExit);
                yield break;
            }

            _pendingDoc = doc;
            _pendingParts = SheetMusicImporter.ListParts(doc);
            ShowPartSelect();
        }

        // -------------------------------------------------------------------------
        // Step 2: instrument/part list
        // -------------------------------------------------------------------------

        private void ShowPartSelect()
        {
            HideAll();
            PopulatePartList();
            _partPanel.SetActive(true);
        }

        private void PopulatePartList()
        {
            RuntimeUI.ClearItems(_spawnedPartItems);

            if (_pendingParts == null || _pendingParts.Count == 0)
            {
                _spawnedPartItems.Add(RuntimeUI.CreateEmptyLabel(_partContent, "No instrument parts found in this file.", _itemHeight));
                return;
            }

            foreach (SheetMusicImporter.PartOption part in _pendingParts)
                _spawnedPartItems.Add(CreateItem(_partContent, $"Part_{part.displayName}", part.displayName, () => OnPartChosen(part)));
        }

        private void OnPartChosen(SheetMusicImporter.PartOption part)
        {
            // Only a grand-staff part (e.g. piano) has separate hands to choose between —
            // a single-staff instrument has nothing to split, so skip straight to the next step.
            if (part.staffCount >= 2)
                ShowClefSelect(part.globalPartIndex);
            else
                ShowMaxNotesSelect(part.globalPartIndex, ClefFilter.Both, ShowPartSelect);
        }

        // -------------------------------------------------------------------------
        // Step 3: hand/clef popup
        // -------------------------------------------------------------------------

        private void ShowClefSelect(int globalPartIndex)
        {
            HideAll();
            PopulateClefList(globalPartIndex);
            _clefPanel.SetActive(true);
        }

        private void PopulateClefList(int globalPartIndex)
        {
            RuntimeUI.ClearItems(_spawnedClefItems);
            _spawnedClefItems.Add(CreateItem(_clefContent, "Clef_Left", "Left Hand (Bass Clef)",
                () => ShowMaxNotesSelect(globalPartIndex, ClefFilter.BassOnly, () => ShowClefSelect(globalPartIndex))));
            _spawnedClefItems.Add(CreateItem(_clefContent, "Clef_Right", "Right Hand (Treble Clef)",
                () => ShowMaxNotesSelect(globalPartIndex, ClefFilter.TrebleOnly, () => ShowClefSelect(globalPartIndex))));
            _spawnedClefItems.Add(CreateItem(_clefContent, "Clef_Both", "Both Hands",
                () => ShowMaxNotesSelect(globalPartIndex, ClefFilter.Both, () => ShowClefSelect(globalPartIndex))));
        }

        // -------------------------------------------------------------------------
        // Step 4: max simultaneous notes
        // -------------------------------------------------------------------------

        private void ShowMaxNotesSelect(int globalPartIndex, ClefFilter clef, System.Action onBack)
        {
            _maxNotesBackAction = onBack;
            HideAll();
            PopulateMaxNotesList(globalPartIndex, clef);
            _maxNotesPanel.SetActive(true);
        }

        private void PopulateMaxNotesList(int globalPartIndex, ClefFilter clef)
        {
            RuntimeUI.ClearItems(_spawnedMaxNotesItems);

            // GameManager/LaneSetup live in the gameplay scene now, not this one, so this can't
            // read the real wired lane count directly — read the same persisted preference
            // LaneSetup itself reads instead, so this always matches whatever's actually built.
            int lanes = Mathf.Max(1, LaneSettings.LaneCount);
            for (int n = 1; n <= lanes; n++)
            {
                int captured = n; // capture by value, not by loop variable reference
                string label = n == 1 ? "1 note at a time" : $"Up to {n} notes at a time";
                _spawnedMaxNotesItems.Add(CreateItem(_maxNotesContent, $"MaxNotes_{n}", label,
                    () => ShowSpeedSelect(globalPartIndex, clef, captured,
                        () => ShowMaxNotesSelect(globalPartIndex, clef, _maxNotesBackAction))));
            }
        }

        // -------------------------------------------------------------------------
        // Step 5: note speed (shown before every play, like Rhythm Hive's speed popup —
        // a flat 1-20 scale independent of the song's detected BPM; see GameManager.SetSpeedLevel)
        // -------------------------------------------------------------------------

        private void ShowSpeedSelect(int globalPartIndex, ClefFilter clef, int maxNotesAtOnce, System.Action onBack)
        {
            _speedBackAction  = onBack;
            _onSpeedConfirmed = () => FinishSelection(globalPartIndex, clef, maxNotesAtOnce);

            HideAll();
            float initialSpeed = PendingSelection.HasPending ? PendingSelection.SpeedLevel : DefaultSpeedLevel;
            _speedSlider.SetValueWithoutNotify(initialSpeed);
            UpdateSpeedValueLabel(initialSpeed);
            _selectedPlaybackSpeed = PendingSelection.HasPending ? PendingSelection.PlaybackSpeed : 1f;
            RefreshPlaybackSpeedHighlight();
            _speedPanel.SetActive(true);
        }

        private void UpdateSpeedValueLabel(float value)
        {
            _speedValueLabel.text = $"Note Speed: {Mathf.RoundToInt(value)}";
        }

        private void SelectPlaybackSpeed(float speed)
        {
            _selectedPlaybackSpeed = speed;
            RefreshPlaybackSpeedHighlight();
        }

        private void RefreshPlaybackSpeedHighlight()
        {
            for (int i = 0; i < _playbackSpeedButtons.Count; i++)
            {
                bool selected = Mathf.Approximately(GameManager.AllowedPlaybackSpeeds[i], _selectedPlaybackSpeed);
                _playbackSpeedButtons[i].GetComponent<Image>().color = selected ? _itemHoverColor : _itemColor;
            }
        }

        private void FinishSelection(int globalPartIndex, ClefFilter clef, int maxNotesAtOnce)
        {
            HideAll();
            PendingSelection.Set(_pendingSong, _pendingDoc, globalPartIndex, clef, maxNotesAtOnce,
                _speedSlider.value, _selectedPlaybackSpeed);
            UnityEngine.SceneManagement.SceneManager.LoadScene(_gameplaySceneName);
        }

        // -------------------------------------------------------------------------
        // Build UI hierarchy
        // -------------------------------------------------------------------------

        private void Build()
        {
            (_songPanel, _songContent) = BuildListPanel("Select a Song", () => _onExit?.Invoke());
            (_partPanel, _partContent) = BuildListPanel("Select a Part", () => Show(_onExit));
            (_clefPanel, _clefContent) = BuildListPanel("Select Hand(s)", ShowPartSelect);
            (_maxNotesPanel, _maxNotesContent) = BuildListPanel("Max Notes at Once", () => _maxNotesBackAction?.Invoke());
            BuildSpeedPanel();
        }

        private (GameObject panel, RectTransform content) BuildListPanel(string title, System.Action onBack) =>
            RuntimeUI.BuildListPanel(_canvas.transform, title, onBack, _backgroundColor, _itemColor, _itemHoverColor, _itemHeight, _itemSpacing);

        private GameObject CreateItem(Transform parent, string name, string label, System.Action onClick) =>
            RuntimeUI.CreateButton(parent, name, label, _itemColor, _itemHoverColor, _itemHeight, onClick);

        private void BuildSpeedPanel()
        {
            _speedPanel = RuntimeUI.CreatePanel(_canvas.transform, "SpeedPanel", _backgroundColor);

            TextMeshProUGUI titleText = RuntimeUI.CreateText(_speedPanel.transform, "Title", "Speed Settings", 32, TextAlignmentOptions.Center);
            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -30f);
            titleRect.sizeDelta = new Vector2(0f, 60f);

            RuntimeUI.CreateBackButton(_speedPanel.transform, _itemColor, _itemHoverColor, () => _speedBackAction?.Invoke());

            _speedValueLabel = RuntimeUI.CreateText(_speedPanel.transform, "SpeedValueLabel", "Note Speed: 5", 26, TextAlignmentOptions.Center);
            RectTransform valueRT = _speedValueLabel.rectTransform;
            valueRT.anchorMin = new Vector2(0.1f, 0.5f);
            valueRT.anchorMax = new Vector2(0.9f, 0.5f);
            valueRT.pivot = new Vector2(0.5f, 0.5f);
            valueRT.anchoredPosition = new Vector2(0f, 150f);
            valueRT.sizeDelta = new Vector2(0f, 50f);

            _speedSlider = RuntimeUI.CreateSlider(_speedPanel.transform, "SpeedSlider",
                _itemColor, _itemHoverColor, Color.white, MinSpeedLevel, MaxSpeedLevel, wholeNumbers: true);
            RectTransform sliderRT = _speedSlider.GetComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0.15f, 0.5f);
            sliderRT.anchorMax = new Vector2(0.85f, 0.5f);
            sliderRT.pivot = new Vector2(0.5f, 0.5f);
            sliderRT.anchoredPosition = new Vector2(0f, 100f);
            sliderRT.sizeDelta = new Vector2(0f, 30f);
            _speedSlider.onValueChanged.AddListener(UpdateSpeedValueLabel);

            TextMeshProUGUI playbackLabel = RuntimeUI.CreateText(_speedPanel.transform, "PlaybackSpeedLabel",
                "Playback Speed (slows audio + notes; pitch drops below 1x)", 18, TextAlignmentOptions.Center);
            RectTransform playbackLabelRT = playbackLabel.rectTransform;
            playbackLabelRT.anchorMin = new Vector2(0.1f, 0.5f);
            playbackLabelRT.anchorMax = new Vector2(0.9f, 0.5f);
            playbackLabelRT.pivot = new Vector2(0.5f, 0.5f);
            playbackLabelRT.anchoredPosition = new Vector2(0f, 20f);
            playbackLabelRT.sizeDelta = new Vector2(0f, 30f);

            _playbackSpeedButtons.Clear();
            float[] speeds = GameManager.AllowedPlaybackSpeeds;
            float buttonWidth = 100f;
            float buttonSpacing = 16f;
            float rowWidth = speeds.Length * buttonWidth + (speeds.Length - 1) * buttonSpacing;
            float startX = -rowWidth / 2f + buttonWidth / 2f;

            for (int i = 0; i < speeds.Length; i++)
            {
                float speed = speeds[i];
                string label = speed >= 1f ? "1x (Normal)" : $"{speed:0.##}x";
                GameObject btn = CreateItem(_speedPanel.transform, $"PlaybackSpeed_{speed}", label, () => SelectPlaybackSpeed(speed));
                var btnRT = btn.GetComponent<RectTransform>();
                btnRT.anchorMin = new Vector2(0.5f, 0.5f);
                btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot = new Vector2(0.5f, 0.5f);
                btnRT.anchoredPosition = new Vector2(startX + i * (buttonWidth + buttonSpacing), -30f);
                btnRT.sizeDelta = new Vector2(buttonWidth, 40f);
                _playbackSpeedButtons.Add(btn);
            }

            GameObject startGO = CreateItem(_speedPanel.transform, "StartButton", "Start", () => _onSpeedConfirmed?.Invoke());
            var startRT = startGO.GetComponent<RectTransform>();
            startRT.anchorMin = new Vector2(0.5f, 0.5f);
            startRT.anchorMax = new Vector2(0.5f, 0.5f);
            startRT.pivot = new Vector2(0.5f, 0.5f);
            startRT.anchoredPosition = new Vector2(0f, -110f);
            startRT.sizeDelta = new Vector2(200f, 50f);

            _speedPanel.SetActive(false);
        }
    }
}
