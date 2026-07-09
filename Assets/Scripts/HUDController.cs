using TMPro;
using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Wires ScoreManager events to UI labels.
    /// Requires TextMeshPro. Assign references in Inspector.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private ScoreManager _scoreManager;
        [SerializeField] private TMP_Text _scoreLabel;
        [SerializeField] private TMP_Text _comboLabel;
        [SerializeField] private TMP_Text _judgementLabel;
        [SerializeField] private float _judgementHoldTime = 0.3f;  // fully visible duration
        [SerializeField] private float _judgementFadeTime = 0.4f;  // fade out duration

        private float _judgementTimer;

        private void OnEnable()
        {
            if (_scoreManager == null) return;
            _scoreManager.onScoreChanged.AddListener(OnScoreChanged);
            _scoreManager.onJudgement.AddListener(OnJudgement);
        }

        private void OnDisable()
        {
            if (_scoreManager == null) return;
            _scoreManager.onScoreChanged.RemoveListener(OnScoreChanged);
            _scoreManager.onJudgement.RemoveListener(OnJudgement);
        }

        private void Update()
        {
            if (_judgementLabel == null) return;
            _judgementTimer -= Time.deltaTime;

            // Hold fully visible, then fade
            float alpha = _judgementTimer > _judgementFadeTime
                ? 1f
                : Mathf.Clamp01(_judgementTimer / _judgementFadeTime);

            var c = _judgementLabel.color;
            _judgementLabel.color = new Color(c.r, c.g, c.b, alpha);
        }

        private void OnScoreChanged(int score)
        {
            if (_scoreLabel) _scoreLabel.text = score.ToString("N0");
        }

        private void OnJudgement(JudgementResult result, int combo)
        {
            if (_comboLabel) _comboLabel.text = combo > 1 ? $"{combo}x" : "";

            if (_judgementLabel)
            {
                _judgementLabel.text = result.ToString().ToUpper();
                _judgementLabel.color = result switch
                {
                    JudgementResult.Perfect => Color.yellow,
                    JudgementResult.Good    => Color.green,
                    JudgementResult.Bad     => Color.white,
                    _                       => Color.red,
                };
                _judgementTimer = _judgementHoldTime + _judgementFadeTime;
            }
        }
    }
}
