using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Procedurally builds the lane visuals, hit bars, and hit zone Transforms at runtime.
    /// Attach to any GameObject in the scene. Runs before GameManager.Start via script execution
    /// order. Deliberately doesn't touch GameManager's own _notePrefab — notes are their own
    /// user-authored prefab, independent of lane layout (see SetupFromLaneBuilder).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class LaneSetup : MonoBehaviour
    {
        [Header("Layout")]
        // Fallback per-lane colors when _laneAreaPrefab is empty (plain code-generated quads).
        // Ignored once a prefab is assigned — its own sprite/color is what's used instead.
        [SerializeField] private Color[] _laneColors = {
            new Color(0.15f, 0.15f, 0.20f),
            new Color(0.12f, 0.12f, 0.17f),
            new Color(0.15f, 0.15f, 0.20f),
            new Color(0.12f, 0.12f, 0.17f),
            new Color(0.15f, 0.15f, 0.20f),
        };

        [Header("Lane Area")]
        [Tooltip("Template for one lane's hit bar segment — this is the hit bar area itself, " +
                 "not the full-height lane background. Its transform.localScale (x = width, " +
                 "y = height) and transform.position (x = center, y = height on screen) define " +
                 "the WHOLE hit bar; that's divided by the chosen lane count and each lane is an " +
                 "instance of this prefab rescaled to its width share, keeping the prefab's own " +
                 "height/sprite/color. Left empty: falls back to a plain colored bar sized from " +
                 "_hitBarColor/_hitBarHeight spanning the full camera width, same as before.")]
        [SerializeField] private GameObject _laneAreaPrefab;

        // 4-6, read from LaneSettings (user-configurable via MainMenu > Options > Lane Count).
        private int _laneCount;

        [Header("Hit Bar")]
        [SerializeField] private Color _hitBarColor = new Color(0.9f, 0.9f, 0.9f, 0.8f);
        [SerializeField] private float _hitBarHeight = 0.15f; // fraction of screen height

        [Header("Note")]
        // Only used to estimate a spawn margin above the screen (see spawnY below) — the note's
        // actual look/size comes from GameManager's own _notePrefab, not from LaneSetup, so this
        // just needs to be roughly note-sized, not an exact match.
        [SerializeField] private float _noteHeightFraction = 0.04f; // fraction of screen height

        [Header("References — auto-filled if left empty")]
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private InputHandler _inputHandler;

        private Camera _cam;

        private void Awake()
        {
            _cam = Camera.main;
            if (_gameManager == null) _gameManager = FindFirstObjectByType<GameManager>();
            if (_inputHandler == null) _inputHandler = FindFirstObjectByType<InputHandler>();

            Build();
        }

        private void Build()
        {
            _laneCount = LaneSettings.LaneCount;

            // Use viewport conversion so positions are always correct regardless of camera setup.
            // Viewport: (0,0) = bottom-left, (1,1) = top-right, z = distance from camera.
            float z = Mathf.Abs(_cam.transform.position.z);

            Vector3 bottomLeft  = _cam.ViewportToWorldPoint(new Vector3(0f, 0f, z));
            Vector3 topRight    = _cam.ViewportToWorldPoint(new Vector3(1f, 1f, z));

            float camWidth  = topRight.x - bottomLeft.x;
            float camHeight = topRight.y - bottomLeft.y;

            // Hit bar geometry — camera-fraction-based defaults, overridden entirely by
            // _laneAreaPrefab's own transform when one's assigned (see its tooltip).
            float hitBarWorldHeight = camHeight * _hitBarHeight;
            float hitBarY = bottomLeft.y + hitBarWorldHeight * 0.5f;  // near bottom
            float totalWidth  = camWidth;
            float areaCenterX = (bottomLeft.x + topRight.x) * 0.5f;
            if (_laneAreaPrefab != null)
            {
                totalWidth        = _laneAreaPrefab.transform.localScale.x;
                hitBarWorldHeight = _laneAreaPrefab.transform.localScale.y;
                areaCenterX       = _laneAreaPrefab.transform.position.x;
                hitBarY           = _laneAreaPrefab.transform.position.y;
            }
            float laneWidth = totalWidth / _laneCount;
            float areaLeftX = areaCenterX - totalWidth * 0.5f;

            float noteWorldHeight = camHeight * _noteHeightFraction;
            float spawnY = topRight.y + noteWorldHeight;  // just above top of screen

            var hitZones    = new Transform[_laneCount];
            var feedbacks   = new LaneTapFeedback[_laneCount];

            float camCenterY = (bottomLeft.y + topRight.y) * 0.5f;

            for (int i = 0; i < _laneCount; i++)
            {
                float laneX = areaLeftX + laneWidth * (i + 0.5f);

                // Lane background (full-height track column) — always a plain colored quad,
                // independent of the hit-bar prefab.
                CreateQuad(
                    $"Lane_{i}",
                    new Vector3(laneX, camCenterY, 1f),
                    new Vector3(laneWidth - 0.02f, camHeight, 1f),
                    LaneColor(i)
                );

                // Divider line
                if (i > 0)
                {
                    float divX = laneX - laneWidth * 0.5f;
                    CreateQuad(
                        $"Divider_{i}",
                        new Vector3(divX, camCenterY, 0.9f),
                        new Vector3(0.02f, camHeight, 1f),
                        new Color(0.3f, 0.3f, 0.3f)
                    );
                }

                // Hit bar — this IS the "lane area": an instance of _laneAreaPrefab resized to
                // this lane's share of its width if one's assigned, else the previous
                // flat-colored bar.
                GameObject hitBar = CreateLaneBlock($"HitBar_{i}", laneX, hitBarY, 0.5f, laneWidth - 0.02f, hitBarWorldHeight, _hitBarColor);

                // Tap flash overlay (starts invisible)
                GameObject flashObj = CreateQuad(
                    $"Flash_{i}",
                    new Vector3(laneX, hitBarY, 0.4f),
                    new Vector3(laneWidth - 0.02f, hitBarWorldHeight, 1f),
                    Color.clear
                );
                var feedback = flashObj.AddComponent<LaneTapFeedback>();
                feedback.SetHighlight(flashObj.GetComponent<SpriteRenderer>());
                feedbacks[i] = feedback;

                // Hit zone Transform (empty marker at center of hit bar)
                var hitZoneGO = new GameObject($"HitZone_{i}");
                hitZoneGO.transform.position = new Vector3(laneX, hitBarY, 0f);
                hitZones[i] = hitZoneGO.transform;
            }

            // Wire everything into GameManager via the internal setup method — deliberately not
            // touching _notePrefab (see SetupFromLaneBuilder's doc comment).
            _gameManager.SetupFromLaneBuilder(hitZones, feedbacks, spawnY);
            _inputHandler.SetLaneCount(_laneCount);
        }

        private Color LaneColor(int i)
        {
            if (_laneColors != null && i < _laneColors.Length)
                return _laneColors[i];
            return i % 2 == 0 ? new Color(0.15f, 0.15f, 0.20f) : new Color(0.12f, 0.12f, 0.17f);
        }

        // Splits _laneAreaPrefab into one lane's share of its total width, preserving whatever
        // sprite/color/material it was authored with — only localScale.x is overridden (height
        // was already taken from the prefab up in Build(), for the fallback-quad case only).
        // Falls back to a plain colored quad when no prefab is assigned.
        private GameObject CreateLaneBlock(string name, float centerX, float centerY, float z, float width, float height, Color fallbackColor)
        {
            if (_laneAreaPrefab == null)
                return CreateQuad(name, new Vector3(centerX, centerY, z), new Vector3(width, height, 1f), fallbackColor);

            GameObject block = Instantiate(_laneAreaPrefab, new Vector3(centerX, centerY, z), Quaternion.identity);
            block.name = name;
            block.SetActive(true);
            Vector3 scale = block.transform.localScale;
            scale.x = width;
            block.transform.localScale = scale;
            return block;
        }

        private GameObject CreateQuad(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.localScale = scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhitePixel;
            sr.material = SpriteMaterial;
            sr.color = color;
            return go;
        }

        private static Sprite _whitePixel;
        private static Sprite WhitePixel
        {
            get
            {
                if (_whitePixel == null)
                {
                    var tex = new Texture2D(1, 1);
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    _whitePixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                }
                return _whitePixel;
            }
        }

        private static Material _spriteMat;
        private static Material SpriteMaterial
        {
            get
            {
                if (_spriteMat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (shader == null) shader = Shader.Find("Sprites/Default"); // fallback
                    _spriteMat = new Material(shader);
                }
                return _spriteMat;
            }
        }
    }
}
