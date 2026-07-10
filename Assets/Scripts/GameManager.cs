using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace RhythmGame
{
    /// <summary>
    /// Main game controller. Attach to a GameObject in the scene.
    ///
    /// Scene wiring required:
    ///   - Assign notePrefab, lanePositions (one Transform per lane) in the Inspector.
    ///   - lanePositions[i].position = the HIT ZONE position for lane i.
    ///   - Set spawnYOffset so notes appear above the screen.
    ///   - Assign a ScoreManager component.
    ///
    /// Songs are no longer assigned directly — SongSelectMenu (in the MainMenu scene) scans
    /// StreamingAssets via SongLibrary, stashes the chosen song + settings in
    /// PendingSelection, and loads this scene; Start() then calls PlaySong() automatically.
    /// Audio-onset analysis (AudioAnalyzer/BeatmapGenerator.Generate) is currently unhooked:
    /// every playable song must ship with a matching sheet-music file, parsed via
    /// SheetMusicImporter.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        // Player-facing "speed" is a flat 1-20 scale (Rhythm Hive-style), decoupled from the
        // song's detected BPM — CMOD rather than XMOD, since BeatmapGenerator's BPM estimate
        // isn't reliable enough to safely scale note speed off of.
        //
        // This is the DDR/beatmania "Hi-Speed" model: it stretches/shrinks how far above the
        // hit zone notes spawn (SpawnDistanceMultiplier), NOT how long they take to arrive
        // (ApproachTime stays fixed). Changing approach time instead would confound two
        // different things — a shorter approach time is both a faster-LOOKING note and a
        // genuinely smaller reaction window, so its effect was hard to read as "speed" versus
        // "difficulty." Scaling distance at a fixed arrival time changes only how fast the note
        // visually crosses the screen; timing/difficulty is untouched.
        [Header("Note Speed")]
        [SerializeField] private float _approachTime = 2f; // fixed: seconds from spawn to hit, independent of speed
        [SerializeField] private float _minSpawnMultiplier = 0.5f; // SpeedLevel 1: notes spawn close, look slow
        [SerializeField] private float _maxSpawnMultiplier = 3f;   // SpeedLevel 20: notes spawn far above-screen, look fast
        private const float MinSpeedLevel = 1f;
        private const float MaxSpeedLevel = 20f;
        private const float DefaultSpeedLevel = 5f;
        private const string SpeedLevelPrefKey = "RGen_SpeedLevel";

        public float SpeedLevel { get; private set; } = DefaultSpeedLevel;
        public float ApproachTime => _approachTime;
        public float SpawnDistanceMultiplier { get; private set; }

        // Called by SongSelectMenu's speed step; persists across sessions.
        public void SetSpeedLevel(float level)
        {
            SpeedLevel = Mathf.Clamp(level, MinSpeedLevel, MaxSpeedLevel);
            SpawnDistanceMultiplier = Mathf.Lerp(_minSpawnMultiplier, _maxSpawnMultiplier,
                (SpeedLevel - MinSpeedLevel) / (MaxSpeedLevel - MinSpeedLevel));
            PlayerPrefs.SetFloat(SpeedLevelPrefKey, SpeedLevel);
        }

        // Slows down the whole song (audio + notes together) for practice, via AudioSource.pitch —
        // Unity has no built-in pitch-preserving time-stretch, so the audio drops in pitch as it
        // slows down. Restricted to a fixed set of steps rather than a free slider since the
        // pitch-shift artifact gets more noticeable/uglier the further it strays from 1x.
        public static readonly float[] AllowedPlaybackSpeeds = { 0.25f, 0.5f, 0.75f, 1f };
        private const string PlaybackSpeedPrefKey = "RGen_PlaybackSpeed";

        public float PlaybackSpeed { get; private set; } = 1f;

        public void SetPlaybackSpeed(float speed)
        {
            float closest = AllowedPlaybackSpeeds[0];
            foreach (float s in AllowedPlaybackSpeeds)
                if (Mathf.Abs(s - speed) < Mathf.Abs(closest - speed)) closest = s;

            PlaybackSpeed = closest;
            _audioSource.pitch = PlaybackSpeed;
            PlayerPrefs.SetFloat(PlaybackSpeedPrefKey, PlaybackSpeed);
        }

        // Miss window: how far past hit time we still count a miss event (vs. already despawned)
        public const float MissWindow = 0.2f;

        [Header("Song Playback")]
        [SerializeField] private string _mainMenuSceneName = "MainMenu";

        // Loaded fresh each time this scene starts, from whatever the player last calibrated
        // via LatencyCalibrator's Sync panel (MainMenu > Options > Audio Sync). Defaults to 0
        // (no correction) until the player runs that test at least once.
        private float _audioLatencyOffset;

        [Header("Beatmap Generation")]
        [SerializeField] private BeatmapGenerator.Settings _generatorSettings = BeatmapGenerator.Settings.Default;
        // Number of playable lanes, finalized once LaneSetup wires in the scene's hit zones.
        // SongSelectMenu reads this to bound its "max notes at once" preset list.
        public int LaneCount => _generatorSettings.laneCount;

        [Header("Scene References")]
        [SerializeField] private Transform[] _laneHitZones;      // one per lane, at hit-zone Y
        [SerializeField] private LaneTapFeedback[] _laneFeedback; // optional, same count as lanes
        [SerializeField] private float _spawnY = 6f;              // world-space Y where notes spawn
        [SerializeField] private GameObject _notePrefab;
        [SerializeField] private ScoreManager _scoreManager;

        // Raised after playback finishes and any remaining on-screen notes have cleared,
        // just before returning to the MainMenu scene — available for any same-scene listener
        // (e.g. a future results/score screen) that wants to react before the scene unloads.
        public UnityEvent onSongEnded;

        private AudioSource _audioSource;
        private BeatmapData _beatmap;
        private int _nextNoteIndex;
        private bool _playing;
        private bool _ready;

        // Called by LaneSetup before Start runs. Doesn't touch _notePrefab — that's assigned
        // directly here in GameManager's own Inspector, a real user-authored prefab, so notes
        // aren't dictated by lane layout (see NoteController.Init preserving that prefab's own
        // SpriteRenderer instead of overwriting it).
        public void SetupFromLaneBuilder(Transform[] hitZones, LaneTapFeedback[] feedbacks, float spawnY)
        {
            _laneHitZones = hitZones;
            _laneFeedback = feedbacks;
            _spawnY = spawnY;
            _generatorSettings.laneCount = hitZones.Length;
        }

        // Tracks live note GameObjects per lane for input judgment
        private List<NoteController>[] _activeNotes;
        // Tracks which note is currently being held per lane
        private NoteController[] _heldNote;
        // Per lane, the time (song time) up to which a previously spawned sustain note
        // still occupies that lane — no new note may spawn there before this passes.
        private float[] _laneHoldEndTime;

        public float SongTime => _playing
            ? (_audioSource.isPlaying ? _audioSource.time : (Time.time - _songActualStartTime) * PlaybackSpeed) + _audioLatencyOffset
            : 0f;

        private float _songActualStartTime;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.volume = SoundSettings.Instance != null ? SoundSettings.Instance.BgmVolume : 1f;

            // Defaults for launching straight into this scene (e.g. testing in the Editor)
            // without going through the menu — overridden below in Start() if a selection
            // came from SongSelectMenu (a different scene now).
            SetSpeedLevel(PlayerPrefs.GetFloat(SpeedLevelPrefKey, DefaultSpeedLevel));
            SetPlaybackSpeed(PlayerPrefs.GetFloat(PlaybackSpeedPrefKey, 1f));
            _audioLatencyOffset = PlayerPrefs.GetFloat(LatencyCalibrator.OffsetPrefKey, 0f);
        }

        private IEnumerator Start()
        {
            // Validate lane count against hit zones
            int lanes = Mathf.Min(_generatorSettings.laneCount, _laneHitZones?.Length ?? 0);
            if (lanes == 0)
            {
                Debug.LogError("[GameManager] No lane hit zones assigned.");
                yield break;
            }
            _generatorSettings.laneCount = lanes;

            // Initialize active note lists
            _activeNotes     = new List<NoteController>[lanes];
            _heldNote        = new NoteController[lanes];
            _laneHoldEndTime = new float[lanes];
            for (int i = 0; i < lanes; i++)
            {
                _activeNotes[i] = new List<NoteController>();
                _laneHoldEndTime[i] = float.NegativeInfinity;
            }

            // yield a frame so LaneSetup and other Awakes finish
            yield return null;

            _ready = true;

            // SongSelectMenu (MainMenu scene) stashed a choice before loading us — start it
            // automatically instead of waiting for a same-scene button click, since that
            // button now lives in a different scene.
            if (PendingSelection.HasPending)
            {
                SetSpeedLevel(PendingSelection.SpeedLevel);
                SetPlaybackSpeed(PendingSelection.PlaybackSpeed);
                PlaySong(PendingSelection.Song, PendingSelection.SheetMusicDoc,
                    PendingSelection.GlobalPartIndex, PendingSelection.Clef, PendingSelection.MaxNotesAtOnce);
                PendingSelection.Clear();
            }
        }

        private Coroutine _playRoutine;

        // Called by SongSelectMenu once the player has picked a song, an instrument part
        // within it, which hand(s) to play (if applicable), and the max number of notes
        // allowed on screen at once. sheetMusicDoc is the document SongSelectMenu already
        // loaded to list parts — passed back in so it isn't fetched twice.
        public void PlaySong(SongLibrary.SongEntry song, XmlDocument sheetMusicDoc, int globalPartIndex, ClefFilter clef, int maxNotesAtOnce)
        {
            if (_playRoutine != null) StopCoroutine(_playRoutine);
            _playRoutine = StartCoroutine(PlaySongRoutine(song, sheetMusicDoc, globalPartIndex, clef, maxNotesAtOnce));
        }

        private IEnumerator PlaySongRoutine(SongLibrary.SongEntry song, XmlDocument sheetMusicDoc, int globalPartIndex, ClefFilter clef, int maxNotesAtOnce)
        {
            while (!_ready) yield return null;

            _playing = false;
            _audioSource.Stop();
            ClearActiveNotes();
            _beatmap = null;
            _nextNoteIndex = 0;
            for (int i = 0; i < _laneHoldEndTime.Length; i++)
                _laneHoldEndTime[i] = float.NegativeInfinity;

            AudioClip clip = null;
            yield return LoadAudioClip(song.audioFileName, c => clip = c);
            if (clip == null) yield break;
            _audioSource.clip = clip;

            _beatmap = SheetMusicImporter.ParseMscx(sheetMusicDoc, song.displayName, _generatorSettings.laneCount, globalPartIndex, clef, maxNotesAtOnce);
            if (_beatmap == null) yield break;

            // Wait exactly one ApproachTime so the first notes are already travelling
            // when the music starts — they'll arrive at the hit bar right on cue
            yield return new WaitForSeconds(ApproachTime);

            _playing = true;
            _nextNoteIndex = 0;
            _songActualStartTime = Time.time;
            _audioSource.Play();

            Debug.Log($"[GameManager] Started '{song.displayName}'. {_beatmap.notes.Count} notes, BPM {_beatmap.bpm:F1}");
        }

        private IEnumerator LoadAudioClip(string relativePath, Action<AudioClip> onLoaded)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativePath);
            AudioType type = GetAudioType(Path.GetExtension(relativePath));

            using var request = UnityWebRequestMultimedia.GetAudioClip(path, type);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[GameManager] Failed to load audio '{path}': {request.error}");
                onLoaded(null);
                yield break;
            }

            onLoaded(DownloadHandlerAudioClip.GetContent(request));
        }

        private static AudioType GetAudioType(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".wav":  return AudioType.WAV;
                case ".mp3":  return AudioType.MPEG;
                case ".ogg":  return AudioType.OGGVORBIS;
                case ".aiff":
                case ".aif":  return AudioType.AIFF;
                // Unity has no dedicated mp4 AudioType; ACC decodes the AAC audio track
                // muxed inside .m4a/.mp4. Platform decoder support varies.
                case ".m4a":
                case ".mp4":  return AudioType.ACC;
                default:      return AudioType.UNKNOWN;
            }
        }

        private void Update()
        {
            if (!_playing || _beatmap == null) return;

            // AudioSource.isPlaying alone isn't reliable for detecting a clip's natural end here
            // — clips loaded via UnityWebRequestMultimedia/DownloadHandlerAudioClip can keep
            // reporting "playing" past the clip's actual length (this is a known quirk with
            // streamed/buffered clips). Elapsed time vs. the clip's own length is deterministic
            // regardless of that, so check both — isPlaying still catches the source being
            // stopped/destroyed externally before the clip would naturally finish.
            bool reachedClipEnd = _audioSource.clip != null && _audioSource.time >= _audioSource.clip.length - 0.05f;
            if (!_audioSource.isPlaying || reachedClipEnd)
            {
                EndSong();
                return;
            }

            float t = SongTime;

            // Spawn notes that should appear on screen now
            while (_nextNoteIndex < _beatmap.notes.Count)
            {
                NoteData note = _beatmap.notes[_nextNoteIndex];
                if (note.time - t > ApproachTime) break;

                // Drop notes that would land on a lane still occupied by an earlier sustain note
                if (note.time < _laneHoldEndTime[note.lane])
                {
                    _nextNoteIndex++;
                    continue;
                }

                SpawnNote(note);
                if (note.IsHold)
                    _laneHoldEndTime[note.lane] = note.time + note.holdDuration;
                _nextNoteIndex++;
            }

            // Cleanup judged/null refs
            foreach (var laneList in _activeNotes)
                laneList.RemoveAll(n => n == null);
        }

        // Called when the AudioSource finishes playing the current song.
        private void EndSong()
        {
            _playing = false;
            ClearActiveNotes();
            Debug.Log("[GameManager] Song ended.");
            onSongEnded?.Invoke();
            // SongSelectMenu lives in the MainMenu scene now — go back there instead of
            // re-showing a same-scene panel.
            SceneManager.LoadScene(_mainMenuSceneName);
        }

        private void ClearActiveNotes()
        {
            if (_activeNotes == null) return;
            foreach (var laneList in _activeNotes)
            {
                foreach (NoteController nc in laneList)
                    if (nc != null) Destroy(nc.gameObject);
                laneList.Clear();
            }
            if (_heldNote != null)
                for (int i = 0; i < _heldNote.Length; i++)
                    _heldNote[i] = null;
        }

        private void SpawnNote(NoteData noteData)
        {
            if (_notePrefab == null) return;
            if (noteData.lane >= _laneHitZones.Length) return;

            Transform hitZone = _laneHitZones[noteData.lane];
            // Scale the spawn point's distance above the hit zone by the speed setting
            // (see SpawnDistanceMultiplier doc above) rather than moving the hit zone itself.
            float hitY = hitZone.position.y;
            float spawnY = hitY + (_spawnY - hitY) * SpawnDistanceMultiplier;
            Vector3 spawnPos = new Vector3(hitZone.position.x, spawnY, 0f);

            GameObject go = Instantiate(_notePrefab, spawnPos, Quaternion.identity, transform);
            NoteController nc = go.GetComponent<NoteController>();
            if (nc == null) nc = go.AddComponent<NoteController>();

            nc.Init(noteData.time, noteData.lane, noteData.holdDuration, spawnPos, hitZone.position, ApproachTime);
            _activeNotes[noteData.lane].Add(nc);
        }

        public void OnLanePressed(int lane)
        {
            if (!_playing || lane < 0 || lane >= _activeNotes.Length) return;

            float t = SongTime;

            if (_laneFeedback != null && lane < _laneFeedback.Length && _laneFeedback[lane] != null)
                _laneFeedback[lane].Flash();

            NoteController closest = null;
            float bestError = float.MaxValue;

            foreach (NoteController nc in _activeNotes[lane])
            {
                if (nc == null || nc.State != NoteState.Approaching) continue;
                float absErr = Mathf.Abs(t - nc.HitTime);
                if (absErr < ScoreManager.BadWindow && absErr < bestError)
                {
                    bestError = absErr;
                    closest = nc;
                }
            }

            if (closest == null) return;

            var result = _scoreManager.Evaluate(t - closest.HitTime);

            if (closest.IsHold)
            {
                // Score the initial press; hold completion scored on release
                _scoreManager.RecordJudgement(result);
                closest.StartHold();
                _heldNote[lane] = closest;
                // Don't remove from _activeNotes yet — still needs to be despawned later
                if (_laneFeedback != null && lane < _laneFeedback.Length && _laneFeedback[lane] != null)
                    _laneFeedback[lane].StartHold();
            }
            else
            {
                _scoreManager.RecordJudgement(result);
                _activeNotes[lane].Remove(closest);
                closest.Judge();
            }
        }

        public void OnLaneReleased(int lane)
        {
            if (!_playing || lane < 0 || lane >= (_heldNote?.Length ?? 0)) return;
            NoteController held = _heldNote[lane];
            if (held == null) return;
            _heldNote[lane] = null;
            _activeNotes[lane].Remove(held);
            held.ReleaseHold(SongTime);
            if (_laneFeedback != null && lane < _laneFeedback.Length && _laneFeedback[lane] != null)
                _laneFeedback[lane].StopHold();
        }

        public void OnHoldReleased(NoteController note, float fraction)
        {
            _scoreManager?.RecordHoldRelease(fraction);
        }

        public void OnHoldCompleted(NoteController note)
        {
            if (_heldNote != null && note.Lane < _heldNote.Length)
                _heldNote[note.Lane] = null;
            _activeNotes[note.Lane].Remove(note);
            _scoreManager?.RecordJudgement(JudgementResult.Perfect);
            // Reached ReleaseTime while still held (player never let go) — OnLaneReleased won't
            // fire until later, so stop the particle stream here rather than leaving it running
            // past the note's actual end.
            if (_laneFeedback != null && note.Lane < _laneFeedback.Length && _laneFeedback[note.Lane] != null)
                _laneFeedback[note.Lane].StopHold();
        }

        public void OnNoteMissed(NoteController note)
        {
            if (_scoreManager == null) return;
            _scoreManager.RecordJudgement(JudgementResult.Miss);
            if (_activeNotes != null && note.Lane < _activeNotes.Length)
                _activeNotes[note.Lane].Remove(note);
        }
    }
}
