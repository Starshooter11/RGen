using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Mobile-first input: divides the screen into equal vertical strips (one per lane)
    /// and fires OnLanePressed when a finger touches down in that strip.
    /// Falls back to keyboard (A S D F) in editor for quick testing.
    /// </summary>
    public class InputHandler : MonoBehaviour
    {
        [SerializeField] private int _laneCount = 4;

        public void SetLaneCount(int count) => _laneCount = count;

        private void Update()
        {
            if (GameManager.Instance == null) return;

#if UNITY_EDITOR
            HandleKeyboard();
#endif
            HandleTouch();
        }

        // Project uses the new Input System exclusively (Active Input Handling = "Input System
        // Package"), which disables the legacy UnityEngine.Input touch API entirely — must read
        // touches through Touchscreen.current instead.
        //
        // Per-slot previous phase, so OnLanePressed/OnLaneReleased fire only on an actual
        // Began/Ended transition. Without this, a held touch that reads as Began for more than
        // one Update() (raw phase polling doesn't guarantee a fresh event every frame) fired
        // OnLanePressed repeatedly for one physical tap — each extra fire judged the next
        // un-judged note in that lane too, so two notes close together in the same lane could
        // both get credited to a single tap.
        private UnityEngine.InputSystem.TouchPhase[] _previousTouchPhases;

        private void HandleTouch()
        {
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen == null) return;

            var touches = touchscreen.touches;
            if (_previousTouchPhases == null || _previousTouchPhases.Length != touches.Count)
                _previousTouchPhases = new UnityEngine.InputSystem.TouchPhase[touches.Count];

            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                var phase = touch.phase.ReadValue();
                var previousPhase = _previousTouchPhases[i];
                _previousTouchPhases[i] = phase;

                if (phase == previousPhase) continue; // no new event for this slot this frame
                if (phase == UnityEngine.InputSystem.TouchPhase.None) continue;

                int lane = TouchToLane(touch.position.ReadValue());
                if (phase == UnityEngine.InputSystem.TouchPhase.Began)
                    GameManager.Instance.OnLanePressed(lane);
                else if (phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                         phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    GameManager.Instance.OnLaneReleased(lane);
            }
        }

        private int TouchToLane(Vector2 screenPos)
        {
            float fraction = screenPos.x / Screen.width;
            return Mathf.Clamp(Mathf.FloorToInt(fraction * _laneCount), 0, _laneCount - 1);
        }

#if UNITY_EDITOR
        private static readonly UnityEngine.InputSystem.Key[] _editorKeys =
        {
            UnityEngine.InputSystem.Key.A,
            UnityEngine.InputSystem.Key.S,
            UnityEngine.InputSystem.Key.D,
            UnityEngine.InputSystem.Key.F,
            UnityEngine.InputSystem.Key.Space,
        };

        private void HandleKeyboard()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            for (int lane = 0; lane < Mathf.Min(_laneCount, _editorKeys.Length); lane++)
            {
                if (kb[_editorKeys[lane]].wasPressedThisFrame)
                    GameManager.Instance.OnLanePressed(lane);
                else if (kb[_editorKeys[lane]].wasReleasedThisFrame)
                    GameManager.Instance.OnLaneReleased(lane);
            }
        }
#endif
    }
}
