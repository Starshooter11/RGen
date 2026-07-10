using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Visual feedback for a lane press: a quick particle burst for a tap, or continuous
    /// emission for as long as a hold note is being held (see StartHold/StopHold, called by
    /// GameManager alongside NoteController's own StartHold/ReleaseHold). Attach one per lane
    /// hit-zone GameObject — LaneSetup creates and wires these, at the same position as the
    /// (now unused for rendering) highlight quad, so the particles burst from the hit bar.
    /// </summary>
    public class LaneTapFeedback : MonoBehaviour
    {
        // Kept only so LaneSetup's existing SetHighlight call still has somewhere to go — the
        // quad itself is still useful as this component's positioned anchor, but its
        // SpriteRenderer is no longer what renders the feedback (the particle system is).
        [SerializeField] private SpriteRenderer _highlight;
        public void SetHighlight(SpriteRenderer sr) => _highlight = sr;

        [Header("Particle")]
        [Tooltip("Optional custom particle effect — if assigned, its own ParticleSystem is used " +
                 "instead of the procedurally-built one below (which acts as the fallback if " +
                 "this is left empty, or if the assigned prefab has no ParticleSystem on it). " +
                 "Burst count and hold emission rate still apply either way; the fields below " +
                 "(color/lifetime/size/speed/shape) are ignored once a prefab is assigned — the " +
                 "prefab's own Main/Shape/Renderer modules take over instead.")]
        [SerializeField] private GameObject _particlePrefab;

        [SerializeField] private Color _particleColor = new Color(1f, 0.85f, 0.3f, 1f);
        [SerializeField] private float _particleLifetime = 0.35f;
        [SerializeField] private float _particleSize = 0.25f;
        [SerializeField] private float _particleSpeed = 1.5f;
        [SerializeField] private float _shapeRadius = 0.15f;
        [SerializeField] private int _tapBurstCount = 18;
        [SerializeField] private float _holdEmissionRate = 40f;

        private ParticleSystem _particles;

        private void Awake()
        {
            _particles = _particlePrefab != null ? BuildFromPrefab() : BuildParticleSystem();
        }

        // Quick burst for any lane press — fires regardless of whether it landed on a note,
        // same as the original Flash()'s unconditional call site in GameManager.OnLanePressed.
        public void Flash()
        {
            _particles.Emit(_tapBurstCount);
        }

        // Continuous emission for as long as a hold note is being held. Call StopHold() on
        // release, early release, or hold-complete — whichever GameManager path fires first —
        // to stop spawning new particles; already-emitted ones still fade out over
        // _particleLifetime instead of vanishing abruptly.
        public void StartHold()
        {
            var emission = _particles.emission;
            emission.rateOverTime = _holdEmissionRate;
        }

        public void StopHold()
        {
            var emission = _particles.emission;
            emission.rateOverTime = 0f;
        }

        // Instantiates _particlePrefab as a child of this lane's anchor, positioned to match.
        // Only loop/playOnAwake/starting emission rate are forced, regardless of how the
        // prefab's Main module was authored — Flash()/StartHold()/StopHold() drive playback
        // entirely via Emit()/rateOverTime rather than the system's own duration/loop cycle, so
        // it has to stay "playing" indefinitely with nothing auto-emitting until told to.
        // Everything else (color, shape, size, sortingOrder, speed, lifetime...) stays exactly
        // as authored on the prefab.
        private ParticleSystem BuildFromPrefab()
        {
            GameObject instance = Instantiate(_particlePrefab, transform.position, transform.rotation, transform);
            var ps = instance.GetComponentInChildren<ParticleSystem>();
            if (ps == null)
            {
                Debug.LogWarning($"[LaneTapFeedback] _particlePrefab on '{name}' has no ParticleSystem — falling back to the built-in particle effect.", this);
                Destroy(instance);
                return BuildParticleSystem();
            }

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;

            ps.Play();
            return ps;
        }

        private ParticleSystem BuildParticleSystem()
        {
            var ps = gameObject.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true; // stays "playing" forever so Emit()/rateOverTime always simulate; loop just means it never auto-stops, not that it auto-emits (rateOverTime starts at 0 below)
            main.playOnAwake = false;
            main.startLifetime = _particleLifetime;
            main.startSpeed = _particleSpeed;
            main.startSize = _particleSize;
            main.startColor = _particleColor;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // driven manually via Emit() and StartHold/StopHold

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; // radial burst in the XY plane, not along Z
            shape.radius = _shapeRadius;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var alphaGradient = new Gradient();
            alphaGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = alphaGradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = Sprites.ParticleMaterial;
            renderer.sortingOrder = 3;

            ps.Play();
            return ps;
        }
    }
}
