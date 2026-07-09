using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Persistent (DontDestroyOnLoad) volume settings — BGM (song playback, applied by
    /// GameManager to its AudioSource) and UI (button click SFX, played directly here).
    /// Named SoundSettings rather than AudioSettings to avoid colliding with the built-in
    /// UnityEngine.AudioSettings static class.
    /// </summary>
    public class SoundSettings : MonoBehaviour
    {
        public static SoundSettings Instance { get; private set; }

        private const string BgmVolumeKey = "RGen_BgmVolume";
        private const string UiVolumeKey  = "RGen_UiVolume";
        private const float DefaultVolume = 0.8f;

        public float BgmVolume { get; private set; } = DefaultVolume;
        public float UiVolume  { get; private set; } = DefaultVolume;

        private AudioSource _sfxSource;
        private AudioClip _clickClip;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BgmVolume = PlayerPrefs.GetFloat(BgmVolumeKey, DefaultVolume);
            UiVolume  = PlayerPrefs.GetFloat(UiVolumeKey, DefaultVolume);

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _clickClip = GenerateClickClip();
        }

        public void SetBgmVolume(float volume)
        {
            BgmVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(BgmVolumeKey, BgmVolume);
        }

        public void SetUiVolume(float volume)
        {
            UiVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(UiVolumeKey, UiVolume);
        }

        public void PlayClick()
        {
            if (_clickClip == null) return;
            _sfxSource.PlayOneShot(_clickClip, UiVolume);
        }

        // Short synthesized blip — a placeholder click sound in the same spirit as the
        // project's other runtime-generated placeholder assets (NoteController's white-pixel
        // sprite, etc.) until a real SFX asset replaces it.
        private static AudioClip GenerateClickClip()
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.06f;
            const float frequency = 1200f;
            int sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = 1f - t / durationSeconds; // linear fade-out, avoids a click-of-the-click pop
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            }

            var clip = AudioClip.Create("UIClick", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
