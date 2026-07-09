using UnityEngine;

namespace RhythmGame
{
    /// <summary>
    /// Procedurally builds the lane visuals, hit bars, note prefab, and hit zone Transforms at runtime.
    /// Attach to any GameObject in the scene. Runs before GameManager.Start via script execution order.
    /// No manual scene wiring needed except assigning a song clip to GameManager.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class LaneSetup : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private int _laneCount = 5;
        [SerializeField] private Color[] _laneColors = {
            new Color(0.15f, 0.15f, 0.20f),
            new Color(0.12f, 0.12f, 0.17f),
            new Color(0.15f, 0.15f, 0.20f),
            new Color(0.12f, 0.12f, 0.17f),
            new Color(0.15f, 0.15f, 0.20f),
        };

        [Header("Hit Bar")]
        [SerializeField] private Color _hitBarColor = new Color(0.9f, 0.9f, 0.9f, 0.8f);
        [SerializeField] private float _hitBarHeight = 0.15f; // fraction of screen height

        [Header("Note")]
        [SerializeField] private Color _noteColor = Color.white;
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
            // Use viewport conversion so positions are always correct regardless of camera setup.
            // Viewport: (0,0) = bottom-left, (1,1) = top-right, z = distance from camera.
            float z = Mathf.Abs(_cam.transform.position.z);

            Vector3 bottomLeft  = _cam.ViewportToWorldPoint(new Vector3(0f, 0f, z));
            Vector3 topRight    = _cam.ViewportToWorldPoint(new Vector3(1f, 1f, z));

            float camWidth  = topRight.x - bottomLeft.x;
            float camHeight = topRight.y - bottomLeft.y;
            float laneWidth = camWidth / _laneCount;

            float hitBarWorldHeight = camHeight * _hitBarHeight;
            float hitBarY = bottomLeft.y + hitBarWorldHeight * 0.5f;  // near bottom

            float noteWorldHeight = camHeight * _noteHeightFraction;
            float spawnY = topRight.y + noteWorldHeight;  // just above top of screen

            var hitZones    = new Transform[_laneCount];
            var feedbacks   = new LaneTapFeedback[_laneCount];

            GameObject notePrefab = CreateNotePrefab(laneWidth * 0.85f, noteWorldHeight);

            float camCenterX = (bottomLeft.x + topRight.x) * 0.5f;
            float camCenterY = (bottomLeft.y + topRight.y) * 0.5f;

            for (int i = 0; i < _laneCount; i++)
            {
                float laneX = bottomLeft.x + laneWidth * (i + 0.5f);

                // Lane background
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

                // Hit bar
                GameObject hitBar = CreateQuad(
                    $"HitBar_{i}",
                    new Vector3(laneX, hitBarY, 0.5f),
                    new Vector3(laneWidth - 0.02f, hitBarWorldHeight, 1f),
                    _hitBarColor
                );

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

            // Wire everything into GameManager via the internal setup method
            _gameManager.SetupFromLaneBuilder(hitZones, feedbacks, notePrefab, spawnY);
            _inputHandler.SetLaneCount(_laneCount);
        }

        private Color LaneColor(int i)
        {
            if (_laneColors != null && i < _laneColors.Length)
                return _laneColors[i];
            return i % 2 == 0 ? new Color(0.15f, 0.15f, 0.20f) : new Color(0.12f, 0.12f, 0.17f);
        }

        private GameObject CreateNotePrefab(float width, float height)
        {
            var go = new GameObject("NotePrefab");
            go.SetActive(false); // prefab-like: inactive until instantiated

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateRoundedRectSprite(64, 24, _noteColor);
            sr.material = SpriteMaterial;
            sr.sortingOrder = 2;

            go.transform.localScale = new Vector3(width, height, 1f);
            go.AddComponent<NoteController>();
            DontDestroyOnLoad(go);
            return go;
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

        // Simple rounded rectangle texture for notes
        private static Sprite CreateRoundedRectSprite(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float rx = w * 0.3f, ry = h * 0.3f; // corner radius fractions
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    tex.SetPixel(x, y, IsInsideRoundedRect(x, y, w, h, rx, ry) ? color : Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Mathf.Max(w, h));
        }

        private static bool IsInsideRoundedRect(int x, int y, int w, int h, float rx, float ry)
        {
            float cx = Mathf.Clamp(x, rx, w - 1 - rx);
            float cy = Mathf.Clamp(y, ry, h - 1 - ry);
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy <= 1f;
        }
    }
}
