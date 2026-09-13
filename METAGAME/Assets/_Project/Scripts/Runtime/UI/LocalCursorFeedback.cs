using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>클라이언트 전용 커서, 화면 불꽃, 2D 클릭음. 네트워크 소리 경로를 사용하지 않는다.</summary>
    public sealed class LocalCursorFeedback : MonoBehaviour
    {
        const int CursorSize = 40;
        const int SparkCount = 96;
        static LocalCursorFeedback _instance;
        readonly Spark[] _sparks = new Spark[SparkCount];
        Texture2D _marker, _normal, _pressed;
        AudioSource _audio;
        AudioClip _click;
        Color _color;
        bool _wasPressed;
        int _nextSpark;
        int _lastPressFrame = -1;
        float _pressedUntil;

        struct Spark
        {
            public Vector2 origin, velocity;
            public Color color;
            public float born, lifetime, size;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.isBatchMode || _instance != null) return;
            var host = new GameObject("~Local Cursor Feedback");
            DontDestroyOnLoad(host);
            host.AddComponent<LocalCursorFeedback>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _marker = Resources.Load<Texture2D>("PingMarker");
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;
            _click = CreateClickClip();
        }

        public static void NotifyPress()
        {
            if (_instance == null || !Application.isFocused) return;
            _instance.PlayFeedback();
        }

        Color PlayerColor
        {
            get
            {
                // The local rig can retain its bootstrap color after Mirror adopts it.
                // Read the owning network player's assigned ping color first.
                var players = NetPlayer.AllClientPlayers;
                for (int i = 0; i < players.Count; i++)
                    if (players[i] != null && players[i].isLocalPlayer) return players[i].PingColor;
                return GameSession.Instance != null && GameSession.Instance.LocalPlayer != null
                    ? GameSession.Instance.LocalPlayer.TeamColor : Color.white;
            }
        }

        void OnEnable() => _color = Color.clear;

        void LateUpdate()
        {
            _audio.volume = GameSettingsRuntime.Instance?.Current?.audio?.sfxVolume ?? 1f;
            Color color = PlayerColor;
            bool pressed = Application.isFocused && (Input.GetMouseButton(0)
                || Input.GetMouseButton(1) || Input.GetMouseButton(2)
                || Time.unscaledTime < _pressedUntil);
            bool changed = _normal == null || color != _color;
            if (changed && _marker != null && _marker.isReadable)
            {
                if (_normal != null) Destroy(_normal);
                if (_pressed != null) Destroy(_pressed);
                _color = color;
                _normal = CreateCursorTexture(_marker, color, false);
                _pressed = CreateCursorTexture(_marker, color, true);
            }
            if (_normal != null && (changed || pressed != _wasPressed))
                Cursor.SetCursor(pressed ? _pressed : _normal,
                    new Vector2(CursorSize / 2f, CursorSize - 2f), CursorMode.Auto);
            _wasPressed = pressed;
        }

        // The pin tip stays on the same hotspot while the pressed image contracts.
        public static Texture2D CreateCursorTexture(Texture2D source, Color tint, bool pressed)
        {
            int minX = source.width, minY = source.height, maxX = 0, maxY = 0;
            var sourcePixels = source.GetPixels32();
            for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
            {
                if (sourcePixels[y * source.width + x].a < 128) continue;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            var pixels = new Color[CursorSize * CursorSize];
            float height = pressed ? 28f : 35f;
            float width = height * (maxX - minX + 1f) / Mathf.Max(1f, maxY - minY + 1f);
            for (int y = 2; y < CursorSize; y++)
            for (int x = 0; x < CursorSize; x++)
            {
                float u = (x - CursorSize * 0.5f) / width + 0.5f;
                float v = (y - 2f) / height;
                if (u < 0f || u > 1f || v > 1f) continue;
                float alpha = source.GetPixelBilinear((minX + u * (maxX - minX)) / source.width,
                    (minY + v * (maxY - minY)) / source.height).a;
                Color color = pressed ? Color.Lerp(tint, Color.white, 0.45f) : tint;
                color.a = alpha;
                pixels[y * CursorSize + x] = color;
            }
            var outlined = (Color[])pixels.Clone();
            for (int y = 1; y < CursorSize - 1; y++)
            for (int x = 1; x < CursorSize - 1; x++)
            {
                int i = y * CursorSize + x;
                float edge = Mathf.Max(pixels[i - 1].a, pixels[i + 1].a,
                    pixels[i - CursorSize].a, pixels[i + CursorSize].a);
                if (pixels[i].a < 0.1f && edge > 0.3f)
                    outlined[i] = new Color(0.025f, 0.035f, 0.06f, edge * 0.85f);
            }
            var texture = new Texture2D(CursorSize, CursorSize, TextureFormat.RGBA32, false);
            texture.name = pressed ? "PingCursor_Pressed" : "PingCursor_Normal";
            texture.SetPixels(outlined);
            texture.Apply(false, false); // Hardware cursor textures must remain CPU readable.
            return texture;
        }

        public static AudioClip CreateClickClip()
        {
            const int rate = 44100;
            var samples = new float[4410];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Min(1f, t / 0.002f) * Mathf.Exp(-t * 55f)
                    * Mathf.Clamp01((0.1f - t) / 0.012f);
                float phase = 2f * Mathf.PI * (1400f * t - 3200f * t * t);
                samples[i] = envelope * (Mathf.Sin(phase) * 0.32f
                    + Mathf.Sin(phase * 1.5f) * 0.12f);
            }
            var clip = AudioClip.Create("UI_SoftPop", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void PlayFeedback()
        {
            // IMGUI can visit multiple controls/events during a single frame.
            if (_lastPressFrame == Time.frameCount) return;
            _lastPressFrame = Time.frameCount;
            _pressedUntil = Time.unscaledTime + 0.1f;
            _audio.volume = GameSettingsRuntime.Instance?.Current?.audio?.sfxVolume ?? 1f;
            _audio.Stop(); // Bound repeated clicks to one short local voice.
            _audio.clip = _click;
            _audio.Play();
            Vector2 point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            for (int i = 0; i < 12; i++)
            {
                float angle = (i + 0.25f) * Mathf.PI * 2f / 12f;
                _sparks[_nextSpark] = new Spark
                {
                    origin = point,
                    velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (65f + i % 3 * 22f),
                    color = PlayerColor,
                    born = Time.unscaledTime,
                    lifetime = 0.32f + i % 3 * 0.045f,
                    size = 2.5f + i % 2
                };
                _nextSpark = (_nextSpark + 1) % SparkCount;
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            int depth = GUI.depth;
            Color color = GUI.color;
            Matrix4x4 matrix = GUI.matrix;
            GUI.depth = -32000;
            GUI.matrix = Matrix4x4.identity;
            for (int i = 0; i < _sparks.Length; i++)
            {
                Spark spark = _sparks[i];
                float age = Time.unscaledTime - spark.born;
                if (spark.lifetime <= 0f || age < 0f || age >= spark.lifetime) continue;
                Vector2 point = spark.origin + spark.velocity * age + Vector2.up * (90f * age * age);
                Color tint = Color.Lerp(spark.color, Color.white, 0.25f);
                tint.a = 1f - age / spark.lifetime;
                GUI.color = tint;
                float size = spark.size * (1f - age / spark.lifetime * 0.65f);
                GUI.DrawTexture(new Rect(point.x - size / 2, point.y - size / 2, size, size), Texture2D.whiteTexture);
            }
            GUI.matrix = matrix;
            GUI.color = color;
            GUI.depth = depth;
        }

        void OnDisable()
        {
            if (_instance == this) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            if (_audio != null) _audio.Stop();
            System.Array.Clear(_sparks, 0, _sparks.Length);
        }

        void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;
            if (_normal != null) Destroy(_normal);
            if (_pressed != null) Destroy(_pressed);
            if (_click != null) Destroy(_click);
        }
    }
}
