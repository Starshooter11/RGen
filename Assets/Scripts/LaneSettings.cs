using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Persisted lane-count preference (4-6), read by LaneSetup when building the gameplay
    /// scene and by SongSelectMenu when bounding its "max notes at once" step. A plain
    /// PlayerPrefs wrapper rather than a MonoBehaviour singleton — same reasoning as
    /// PendingSelection: small enough not to need scene lifetime management, and needs to be
    /// readable from both the MainMenu and gameplay scenes without a live cross-scene instance.
    /// </summary>
    public static class LaneSettings
    {
        private const string LaneCountPrefKey = "RGen_LaneCount";
        public const int MinLaneCount = 4;
        public const int MaxLaneCount = 6;
        public const int DefaultLaneCount = 5;

        public static int LaneCount =>
            Mathf.Clamp(PlayerPrefs.GetInt(LaneCountPrefKey, DefaultLaneCount), MinLaneCount, MaxLaneCount);

        public static void SetLaneCount(int count)
        {
            PlayerPrefs.SetInt(LaneCountPrefKey, Mathf.Clamp(count, MinLaneCount, MaxLaneCount));
            PlayerPrefs.Save();
        }
    }
}
