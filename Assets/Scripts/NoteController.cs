using UnityEngine;

namespace RhythmGame
{
    // Ending: a hold note that was missed (never started) or released early — no longer
    // interactive, but stays visible/pinned until the sustain's natural end time so the
    // lane visually (and per GameManager's spawn gating) stays occupied for its full bar.
    public enum NoteState { Approaching, Held, Ending, Missed, Judged }

    public class NoteController : MonoBehaviour
    {
        public float HitTime        { get; private set; }
        public float HoldDuration   { get; private set; }
        public float ReleaseTime    => HitTime + HoldDuration;
        public int   Lane           { get; private set; }
        public bool  IsHold         => HoldDuration > 0f;
        public NoteState State      { get; private set; } = NoteState.Approaching;

        private Vector3 _startPos;
        private Vector3 _endPos;
        private float   _approachTime;
        private float   _spawnTime;
        private float   _totalDistance;

        private SpriteRenderer _headRenderer;

        // The sustain body is built from discrete segments, each sized exactly like the note
        // head, stacked edge-to-edge — rather than one continuously-scaled sprite. Easier to
        // verify visually (segment count is countable) and sidesteps scale-inheritance math
        // that should have worked out but wasn't matching the real held-note length in practice.
        private GameObject[] _holdSegments;
        private float        _segmentWorldHeight; // world-space height of one segment (== note head height)
        private float        _segmentDuration;    // seconds of hold represented by one segment

        // How many world units the note travels per second
        private float _unitsPerSecond;

        public void Init(float hitTime, int lane, float holdDuration, Vector3 spawnPos, Vector3 hitPos, float approachTime)
        {
            HitTime      = hitTime;
            Lane         = lane;
            HoldDuration = holdDuration;
            _startPos    = spawnPos;
            _endPos      = hitPos;
            _approachTime = approachTime;
            _spawnTime   = hitTime - approachTime;
            _totalDistance = Vector3.Distance(spawnPos, hitPos);
            _unitsPerSecond = _totalDistance / approachTime;

            _headRenderer = GetComponent<SpriteRenderer>();
            if (_headRenderer == null) _headRenderer = gameObject.AddComponent<SpriteRenderer>();
            _headRenderer.sprite   = Sprites.WhitePixel;
            _headRenderer.material = Sprites.URPMaterial;
            _headRenderer.color    = Color.white;
            _headRenderer.sortingOrder = 2;

            if (IsHold) BuildHoldSegments();

            transform.position = spawnPos;
        }

        private void BuildHoldSegments()
        {
            Vector3 headScale = transform.localScale;
            _segmentWorldHeight = headScale.y;
            _segmentDuration    = _segmentWorldHeight / _unitsPerSecond;

            // How far (in world units) the note would travel during the full hold at its
            // current fall speed, converted to a whole number of note-height segments —
            // rounded up so the bar never visually falls short of the true duration.
            float totalTailLength = HoldDuration * _unitsPerSecond;
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(totalTailLength / _segmentWorldHeight));

            _holdSegments = new GameObject[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                var seg = new GameObject($"HoldSegment_{i}");
                seg.transform.SetParent(transform, worldPositionStays: false);
                // Local scale of 1 == same world size as the head, since this is a child of a
                // transform already scaled to the note's size. Stacked edge-to-edge upward
                // (away from the hit zone, i.e. back the way the note came from).
                seg.transform.localScale    = Vector3.one;
                seg.transform.localPosition = new Vector3(0f, i + 0.5f, 0f);

                var sr = seg.AddComponent<SpriteRenderer>();
                sr.sprite   = Sprites.WhitePixel;
                sr.material = Sprites.URPMaterial;
                sr.color    = new Color(1f, 1f, 1f, 0.5f);
                sr.sortingOrder = 1;

                _holdSegments[i] = seg;
            }
        }

        private void Update()
        {
            if (State == NoteState.Judged) return;

            float songTime = GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;

            if (State == NoteState.Approaching)
            {
                // Lerp note head toward hit position
                float t = Mathf.InverseLerp(_spawnTime, HitTime, songTime);
                transform.position = Vector3.Lerp(_startPos, _endPos, t);

                // Auto-miss if past the bad window and not yet hit
                if (songTime > HitTime + GameManager.MissWindow)
                {
                    GameManager.Instance?.OnNoteMissed(this);

                    if (IsHold)
                    {
                        // Keep the sustain bar on screen through its full duration even though
                        // the player never started the hold, so the lane stays visually occupied.
                        transform.position = _endPos;
                        State = NoteState.Ending;
                    }
                    else
                    {
                        State = NoteState.Missed;
                        Destroy(gameObject);
                    }
                }
            }
            else if (State == NoteState.Held)
            {
                transform.position = _endPos;
                UpdateHoldSegments(songTime);

                // Auto-complete if held all the way through
                if (songTime >= ReleaseTime)
                    CompleteHold();
            }
            else if (State == NoteState.Ending)
            {
                // Coasting to the end of the sustain after an early release or an unstarted
                // hold — no further judging, just let the segments run out before despawning.
                transform.position = _endPos;
                UpdateHoldSegments(songTime);

                if (songTime >= ReleaseTime)
                {
                    State = NoteState.Judged;
                    Destroy(gameObject);
                }
            }
        }

        // Pin head at hit zone; remove segments from the far end (latest in time) first, so
        // the remaining visible bar always stays anchored at the pinned head/hit-zone end —
        // matching how the bar's near end never moves while its far end counts down.
        private void UpdateHoldSegments(float songTime)
        {
            if (_holdSegments == null) return;

            float remaining = Mathf.Max(0f, ReleaseTime - songTime);
            int visibleCount = Mathf.Clamp(Mathf.CeilToInt(remaining / _segmentDuration), 0, _holdSegments.Length);

            for (int i = _holdSegments.Length - 1; i >= visibleCount; i--)
            {
                if (_holdSegments[i] != null)
                {
                    Destroy(_holdSegments[i]);
                    _holdSegments[i] = null;
                }
            }
        }

        // Called by GameManager when player presses this lane within the hit window
        public void StartHold()
        {
            State = NoteState.Held;
        }

        // Called by GameManager when player releases the lane
        public void ReleaseHold(float songTime)
        {
            if (State != NoteState.Held) return;
            float heldFor  = songTime - HitTime;
            float fraction = Mathf.Clamp01(heldFor / HoldDuration);
            GameManager.Instance?.OnHoldReleased(this, fraction);
            // Stay visible/pinned until the sustain's natural end rather than despawning now.
            State = NoteState.Ending;
        }

        private void CompleteHold()
        {
            GameManager.Instance?.OnHoldCompleted(this);
            State = NoteState.Judged;
            Destroy(gameObject);
        }

        // Called for tap notes
        public void Judge()
        {
            State = NoteState.Judged;
            Destroy(gameObject);
        }
    }

    // Shared sprite/material helpers so NoteController doesn't depend on LaneSetup
    internal static class Sprites
    {
        private static Sprite _pixel;
        public static Sprite WhitePixel
        {
            get
            {
                if (_pixel != null) return _pixel;
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                return _pixel;
            }
        }

        private static Material _mat;
        public static Material URPMaterial
        {
            get
            {
                if (_mat != null) return _mat;
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                          ?? Shader.Find("Sprites/Default");
                _mat = new Material(shader);
                return _mat;
            }
        }
    }
}
