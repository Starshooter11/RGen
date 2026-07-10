using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Tap-to-the-beat audio latency calibration (the same idea as osu!'s offset wizard or
    /// Rayark's sync test): plays a metronome scheduled on the audio DSP clock, has the player
    /// tap along, and averages the error between each tap and its nearest beat into a single
    /// offset. Runs entirely from the MainMenu scene — the result is written to the PlayerPrefs
    /// key GameManager reads on Awake, so it takes effect the next time a song loads without
    /// this component or GameManager needing to reference each other directly.
    ///
    /// Uses AudioSettings.dspTime rather than Time.time/Update() for both scheduling clicks and
    /// timestamping taps: Time.time is frame-driven and can jitter or drift by more than a
    /// frame under load, which would swamp the latency being measured. dspTime tracks the audio
    /// hardware clock directly, the same clock GameManager.SongTime is effectively anchored to
    /// via AudioSource.time.
    /// </summary>
    public class LatencyCalibrator : MonoBehaviour
    {
        public const string OffsetPrefKey = "RGen_AudioLatencyOffset";

        [Header("Metronome")]
        [SerializeField] private float _bpm = 120f;
        [SerializeField] private int _leadInBeats = 4;  // played so the player can find the beat, not scored
        [SerializeField] private int _scoredBeats = 20; // taps recorded and averaged into the result
        [SerializeField] private float _clickFrequency = 1500f;
        [SerializeField] private float _clickDuration = 0.05f;

        // Taps further than this from their nearest beat are dropped rather than counted — a
        // stray early/late tap shouldn't drag the average toward garbage. Also doubles as the
        // beat-matching window in RegisterTap, so it must stay well under half a beat interval
        // (0.18s here vs. 0.25s at the default 120 BPM) or a tap could latch onto the wrong beat.
        [SerializeField] private float _maxUsableError = 0.18f;

        public event Action<int, int> OnBeatScheduled;         // (beatIndex, totalBeats)
        public event Action<int, int> OnTapRegistered;         // (tapsRecorded, scoredBeats)
        public event Action<float, int> OnCalibrationComplete; // (newOffsetSeconds, samplesUsed)
        public event Action OnCalibrationCancelled;

        public bool IsRunning { get; private set; }
        public static float CurrentOffset => PlayerPrefs.GetFloat(OffsetPrefKey, 0f);

        private AudioSource _audioSource;
        private AudioClip _clickClip;
        private double[] _beatDspTimes;
        private bool[] _beatClaimed;
        private readonly List<double> _errors = new List<double>();
        private Coroutine _scheduleRoutine;

        private void Awake()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _clickClip = GenerateClickClip();
        }

        public void BeginCalibration()
        {
            if (IsRunning) CancelCalibration();

            int totalBeats = _leadInBeats + _scoredBeats;
            _beatDspTimes = new double[totalBeats];
            _beatClaimed = new bool[totalBeats];
            _errors.Clear();

            double startDsp = AudioSettings.dspTime + 0.5; // buffer so the first PlayScheduled call isn't already late
            double beatInterval = 60.0 / _bpm;
            for (int i = 0; i < totalBeats; i++)
                _beatDspTimes[i] = startDsp + i * beatInterval;

            IsRunning = true;
            _scheduleRoutine = StartCoroutine(ScheduleRoutine());
        }

        public void CancelCalibration()
        {
            if (!IsRunning) return;
            IsRunning = false;
            if (_scheduleRoutine != null) StopCoroutine(_scheduleRoutine);
            _audioSource.Stop();
            OnCalibrationCancelled?.Invoke();
        }

        // Call from a UI button's onClick each time the player taps along with a click.
        // Matches against the nearest not-yet-claimed scored beat within _maxUsableError;
        // lead-in beats and stray taps outside that window are ignored rather than counted.
        public void RegisterTap()
        {
            if (!IsRunning) return;
            double tapTime = AudioSettings.dspTime;

            int bestBeat = -1;
            double bestAbsError = double.MaxValue;
            for (int i = _leadInBeats; i < _beatDspTimes.Length; i++)
            {
                if (_beatClaimed[i]) continue;
                double absError = Math.Abs(tapTime - _beatDspTimes[i]);
                if (absError < bestAbsError)
                {
                    bestAbsError = absError;
                    bestBeat = i;
                }
            }

            if (bestBeat == -1 || bestAbsError > _maxUsableError) return;

            _beatClaimed[bestBeat] = true;
            _errors.Add(tapTime - _beatDspTimes[bestBeat]);
            OnTapRegistered?.Invoke(_errors.Count, _scoredBeats);
        }

        private IEnumerator ScheduleRoutine()
        {
            for (int i = 0; i < _beatDspTimes.Length; i++)
            {
                _audioSource.clip = _clickClip;
                _audioSource.PlayScheduled(_beatDspTimes[i]);
                OnBeatScheduled?.Invoke(i, _beatDspTimes.Length);

                double wait = _beatDspTimes[i] - AudioSettings.dspTime;
                if (wait > 0) yield return new WaitForSecondsRealtime((float)wait);
            }

            // Give the player a moment after the last click to register a trailing tap.
            yield return new WaitForSecondsRealtime(0.5f);
            Finish();
        }

        private void Finish()
        {
            IsRunning = false;

            if (_errors.Count == 0)
            {
                // Nothing usable recorded — leave the previously saved offset untouched rather
                // than overwriting a good calibration with 0.
                OnCalibrationComplete?.Invoke(CurrentOffset, 0);
                return;
            }

            float offset = ComputeOffset();
            PlayerPrefs.SetFloat(OffsetPrefKey, offset);
            PlayerPrefs.Save();
            OnCalibrationComplete?.Invoke(offset, _errors.Count);
        }

        // Median first (robust to any outlier RegisterTap's own window let through), then the
        // mean of samples close to that median — two-pass, same idea as osu!'s offset wizard,
        // so one flukey tap can't skew a plain average.
        //
        // Sign: a positive error here means the player's tap consistently landed AFTER its
        // scheduled dsp beat — their audio output has that much extra latency beyond Unity's
        // internal clock, so what they physically hear (and react to) always lags dspTime by
        // that amount. GameManager.SongTime is (audioSource.time + offset); to make SongTime
        // reflect what the player actually hears rather than Unity's internal position, it needs
        // to run behind by that same amount, so the saved offset is the negation of the error.
        private float ComputeOffset()
        {
            var sorted = new List<double>(_errors);
            sorted.Sort();
            double median = sorted[sorted.Count / 2];

            double sum = 0;
            int count = 0;
            foreach (double e in sorted)
            {
                if (Math.Abs(e - median) > _maxUsableError) continue;
                sum += e;
                count++;
            }
            double mean = count > 0 ? sum / count : median;

            return -(float)mean;
        }

        // Directly adjust the saved offset without running the tap test — the ±10ms fine-tune
        // buttons in the Sync panel use this after an auto-calibration to nudge by feel.
        public static void ApplyManualAdjustment(float deltaSeconds)
        {
            PlayerPrefs.SetFloat(OffsetPrefKey, CurrentOffset + deltaSeconds);
            PlayerPrefs.Save();
        }

        // Same synthesis approach as SoundSettings.GenerateClickClip — a short synthesized blip
        // rather than a shipped asset, kept local since the frequency/duration are tunable here.
        private AudioClip GenerateClickClip()
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * _clickDuration);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = 1f - t / _clickDuration;
                samples[i] = Mathf.Sin(2f * Mathf.PI * _clickFrequency * t) * envelope * 0.5f;
            }

            var clip = AudioClip.Create("MetronomeClick", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
