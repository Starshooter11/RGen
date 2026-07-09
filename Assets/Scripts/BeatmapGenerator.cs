using System.Collections.Generic;
using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Converts raw onset times from AudioAnalyzer into a playable BeatmapData.
    /// Controls note density, minimum spacing, and lane assignment.
    /// </summary>
    public static class BeatmapGenerator
    {
        [System.Serializable]
        public struct Settings
        {
            [Tooltip("Number of playable lanes")]
            public int laneCount;

            [Tooltip("Minimum seconds between any two notes (prevents unplayable bursts)")]
            public float minNoteSeparation;

            [Tooltip("0-1: fraction of onsets kept as notes. Lower = sparser map.")]
            [Range(0.1f, 1f)]
            public float density;

            [Tooltip("Bias lane selection toward matching the onset's energy band (0 = random, 1 = fully energy-driven)")]
            [Range(0f, 1f)]
            public float energyLaneBias;

            [Tooltip("Relative weight given to repeating the previous lane vs. any other lane (0 = never repeat, 1 = no preference)")]
            [Range(0f, 1f)]
            public float repeatLaneWeight;

            public static Settings Default => new Settings
            {
                laneCount = 4,
                minNoteSeparation = 0.15f,
                density = 0.6f,
                energyLaneBias = 0.5f,
                repeatLaneWeight = 0.2f
            };
        }

        public static BeatmapData Generate(
            AudioAnalyzer.AnalysisResult analysis,
            string songName,
            Settings settings)
        {
            var beatmap = new BeatmapData(songName, analysis.bpm, analysis.duration);
            var onsets = analysis.onsetTimes;

            if (onsets == null || onsets.Count == 0)
            {
                Debug.LogWarning("[BeatmapGenerator] No onsets detected — beatmap will be empty.");
                return beatmap;
            }

            // Determine the strength threshold that keeps the top `density` fraction of onsets
            var strengths = analysis.onsetStrengths;
            float strengthThreshold = 0f;
            if (strengths != null && strengths.Count > 0 && settings.density < 1f)
            {
                var sorted = new List<float>(strengths);
                sorted.Sort();
                int cutoffIndex = Mathf.FloorToInt((1f - settings.density) * sorted.Count);
                cutoffIndex = Mathf.Clamp(cutoffIndex, 0, sorted.Count - 1);
                strengthThreshold = sorted[cutoffIndex];
            }

            float lastNoteTime = float.NegativeInfinity;
            int lastLane = -1;
            var rng = new System.Random(42);

            for (int i = 0; i < onsets.Count; i++)
            {
                float t = onsets[i];

                // Enforce minimum separation
                if (t - lastNoteTime < settings.minNoteSeparation)
                    continue;

                // Keep only the strongest onsets based on density setting
                if (strengths != null && i < strengths.Count && strengths[i] < strengthThreshold)
                    continue;

                int lane = PickLane(settings.laneCount, lastLane, settings.repeatLaneWeight, rng);
                beatmap.notes.Add(new NoteData(t, lane));
                lastNoteTime = t;
                lastLane = lane;
            }

            Debug.Log($"[BeatmapGenerator] Generated {beatmap.notes.Count} notes from {onsets.Count} onsets. BPM: {analysis.bpm:F1}");
            return beatmap;
        }

        // Weights the previous lane lower than the others so notes prefer switching lanes
        // without a hard rule against ever repeating (repeatLaneWeight == 0 recreates that rule).
        private static int PickLane(int laneCount, int lastLane, float repeatLaneWeight, System.Random rng)
        {
            if (laneCount == 1) return 0;

            float otherWeight = 1f;
            float totalWeight = otherWeight * (laneCount - 1) + repeatLaneWeight;
            float roll = (float)(rng.NextDouble() * totalWeight);

            float cumulative = 0f;
            for (int lane = 0; lane < laneCount; lane++)
            {
                cumulative += lane == lastLane ? repeatLaneWeight : otherWeight;
                if (roll < cumulative)
                    return lane;
            }
            return laneCount - 1;
        }
    }
}
