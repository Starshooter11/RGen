using System.Collections;
using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Flashes a SpriteRenderer when the lane is tapped.
    /// Attach one per lane hit-zone GameObject.
    /// </summary>
    public class LaneTapFeedback : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _highlight;

        public void SetHighlight(SpriteRenderer sr) => _highlight = sr;
        [SerializeField] private float _flashDuration = 0.08f;
        [SerializeField] private Color _flashColor = new Color(1f, 1f, 1f, 0.4f);

        public void Flash()
        {
            StopAllCoroutines();
            StartCoroutine(DoFlash());
        }

        private IEnumerator DoFlash()
        {
            _highlight.color = _flashColor;
            yield return new WaitForSeconds(_flashDuration);
            _highlight.color = Color.clear;
        }
    }
}
