using UnityEngine;
using UnityEngine.Events;

namespace RhythmGame
{
    public enum JudgementResult { Perfect, Good, Bad, Miss }

    public class ScoreManager : MonoBehaviour
    {
        // Hit windows in seconds (one-sided)
        public const float PerfectWindow = 0.05f;  // ±50ms
        public const float GoodWindow    = 0.10f;  // ±100ms
        public const float BadWindow     = 0.15f;  // ±150ms

        private static readonly int[] BasePoints = { 300, 100, 50, 0 };

        public int Score        { get; private set; }
        public int Combo        { get; private set; }
        public int MaxCombo     { get; private set; }
        public int TotalNotes   { get; private set; }
        public int HitNotes     { get; private set; }

        public UnityEvent<JudgementResult, int> onJudgement;  // result, current combo
        public UnityEvent<int> onScoreChanged;

        public JudgementResult Evaluate(float timingError)
        {
            float abs = Mathf.Abs(timingError);
            if (abs <= PerfectWindow) return JudgementResult.Perfect;
            if (abs <= GoodWindow)    return JudgementResult.Good;
            if (abs <= BadWindow)     return JudgementResult.Bad;
            return JudgementResult.Miss;
        }

        public void RecordJudgement(JudgementResult result)
        {
            TotalNotes++;
            if (result != JudgementResult.Miss)
            {
                HitNotes++;
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
                int points = BasePoints[(int)result] * Mathf.Max(1, Combo / 10 + 1);
                Score += points;
                onScoreChanged?.Invoke(Score);
            }
            else
            {
                Combo = 0;
            }

            onJudgement?.Invoke(result, Combo);
        }

        // fraction: 0-1 of how much of the hold was completed before release
        public void RecordHoldRelease(float fraction)
        {
            if (fraction >= 0.9f)       RecordJudgement(JudgementResult.Perfect);
            else if (fraction >= 0.5f)  RecordJudgement(JudgementResult.Good);
            else                        RecordJudgement(JudgementResult.Miss);
        }

        public float Accuracy => TotalNotes > 0 ? (float)HitNotes / TotalNotes : 1f;
    }
}
