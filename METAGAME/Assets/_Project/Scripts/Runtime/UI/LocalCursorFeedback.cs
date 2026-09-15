using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>클라이언트 전용 커서, 화면 불꽃, 2D 클릭음. 네트워크 소리 경로를 사용하지 않는다.</summary>
    public sealed class LocalCursorFeedback : MonoBehaviour
    {
        /// <summary>
        /// 커서 텍스처 한 변(픽셀). <b>40 에서 48 로 키웠다</b> (S15P21B202-535).
        ///
        /// <para>핀을 눕히면 대각선으로 서므로 한 변에 약 41px 이 필요해 40 에는 안
        /// 들어간다. <b>보이는 크기는 그대로고 늘어나는 것은 투명한 여백뿐</b>이다 —
        /// 핀 자체의 길이(<c>PinLength</c>)는 건드리지 않았다.</para>
        /// </summary>
        public const int CursorSize = 48;

        /// <summary>
        /// 핀을 눕히는 각도. <b>0 이면 예전처럼 똑바로 선다.</b>
        ///
        /// <para>135° 는 보통 마우스 커서와 같은 배치다 — 뾰족한 끝이 왼쪽 위, 몸이
        /// 오른쪽 아래. 지도 핀은 끝이 아래라 <b>그림이 클릭점 위에 서서 무엇을 누르는지
        /// 가렸다</b> (S15P21B202-535).</para>
        /// </summary>
        public const float PinAngleDegrees = 135f;

        /// <summary>그림이 잘리지 않게 두는 가장자리 여백(픽셀). 테두리 그리기가 이웃을 읽는다.</summary>
        const float PinMargin = 1.5f;
        const int SparkCount = 96;
        static LocalCursorFeedback _instance;
        readonly Spark[] _sparks = new Spark[SparkCount];
        Texture2D _marker, _normal, _pressed;
        Vector2 _normalHotspot, _pressedHotspot;
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
                _normal = CreateCursorTexture(_marker, color, false, out _normalHotspot);
                _pressed = CreateCursorTexture(_marker, color, true, out _pressedHotspot);
            }
            if (_normal != null && (changed || pressed != _wasPressed))
                // hotspot 을 그리는 쪽에서 받아 온다 — 각도를 바꾸면 끝이 가는 자리도
                // 달라지므로, 여기에 좌표를 따로 적어 두면 둘이 어긋난다 (S15P21B202-535).
                Cursor.SetCursor(pressed ? _pressed : _normal,
                    pressed ? _pressedHotspot : _normalHotspot, CursorMode.Auto);
            _wasPressed = pressed;
        }

        /// <summary>
        /// 눕힌 핀이 향하는 방향. <b>텍스처 좌표</b>(x 오른쪽, y 위쪽)다 —
        /// 화면은 y 가 아래라 부호가 뒤집힌다.
        ///
        /// <para>0° 면 <c>(0, 1)</c> 즉 끝에서 위로 — 예전과 같다.</para>
        /// </summary>
        public static Vector2 PinAxis(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        /// <summary>
        /// 핀 축에 수직인 방향. 폭이 이 축을 따라 퍼진다.
        ///
        /// <para><b>부호가 중요하다.</b> <c>(-axis.y, axis.x)</c> 로 두면 (cross, axis)
        /// 기저의 행렬식이 <c>-1</c> 이라 <b>회전이 아니라 좌우 반전</b>이 된다 —
        /// 0° 에서 <c>u</c> 가 예전 식과 부호가 반대가 되고, 좌우 비대칭인 그림이라면
        /// 커서가 거울상으로 뒤집힌다 (!449 AI 리뷰).</para>
        ///
        /// <para>지금 <c>PingMarker</c> 는 좌우가 완벽 대칭이라(실측: 비대칭 픽셀 0/88763)
        /// 보이는 결과는 같았다. <b>그래서 눈으로는 못 잡는 종류다.</b></para>
        /// </summary>
        public static Vector2 PinCross(Vector2 axis) => new Vector2(axis.y, -axis.x);

        /// <summary>
        /// 뾰족한 끝이 놓일 자리(텍스처 좌표). <b>그림이 칸 안에 딱 들어가게 민다.</b>
        ///
        /// <para>네 귀퉁이의 극값을 구해 여백만큼만 띄운다 — 각도가 바뀌어도 잘리지 않는다.</para>
        /// </summary>
        public static Vector2 PinTip(float degrees, float length, float width, int size)
        {
            var axis = PinAxis(degrees);
            var cross = PinCross(axis);
            float halfWidth = width * 0.5f;

            float left = Mathf.Min(0f, axis.x * length) - Mathf.Abs(cross.x) * halfWidth;
            float right = Mathf.Max(0f, axis.x * length) + Mathf.Abs(cross.x) * halfWidth;
            float bottom = Mathf.Min(0f, axis.y * length) - Mathf.Abs(cross.y) * halfWidth;
            float top = Mathf.Max(0f, axis.y * length) + Mathf.Abs(cross.y) * halfWidth;

            // 남는 자리는 반씩 나눠 가운데로 민다. 모자라면 왼쪽·아래에 붙는다.
            float slackX = Mathf.Max(0f, size - 1f - 2f * PinMargin - (right - left));
            float slackY = Mathf.Max(0f, size - 1f - 2f * PinMargin - (top - bottom));
            return new Vector2(PinMargin - left + slackX * 0.5f,
                               PinMargin - bottom + slackY * 0.5f);
        }

        /// <summary>
        /// <see cref="Cursor.SetCursor"/> 에 넘길 hotspot. <b>왼쪽 위 기준</b>이라
        /// 텍스처의 y 를 뒤집는다 — 이 한 줄을 빠뜨리면 클릭점이 위아래로 뒤집힌다.
        /// </summary>
        public static Vector2 Hotspot(Vector2 tipInTexture, int size) =>
            new Vector2(tipInTexture.x, size - 1f - tipInTexture.y);

        // The pin tip stays on the same hotspot while the pressed image contracts.
        public static Texture2D CreateCursorTexture(Texture2D source, Color tint, bool pressed)
            => CreateCursorTexture(source, tint, pressed, out _);

        public static Texture2D CreateCursorTexture(Texture2D source, Color tint, bool pressed,
                                                    out Vector2 hotspot)
            => CreateCursorTexture(source, tint, pressed, PinAngleDegrees, CursorSize, out hotspot);

        /// <summary>
        /// 각도와 칸 크기를 받는 형태. <b>검증이 0° 를 구워 예전 식과 맞대어 보려고</b>
        /// 열어 두었다 — 상수로만 두면 "각도 0 이면 예전과 같다" 를 잴 수가 없다.
        /// </summary>
        public static Texture2D CreateCursorTexture(Texture2D source, Color tint, bool pressed,
                                                    float degrees, int size, out Vector2 hotspot)
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
            var pixels = new Color[size * size];
            float height = pressed ? 28f : 35f;
            float width = height * (maxX - minX + 1f) / Mathf.Max(1f, maxY - minY + 1f);

            // 끝을 기준으로 축·수직축에 투영한다. 각도 0 이면 예전 식과 **실루엣이 정확히
            // 같다** — 다만 예전은 중심을 `size * 0.5` 로 잡아 반 픽셀 어긋나 있었고,
            // 여기서는 픽셀 중심에 맞춘다(실측: 보정 전 73픽셀 차이, 보정 후 0픽셀).
            // 말로 두지 않고 ZeroDegreesMatchesTheOldMappingPixelForPixel 이 잰다.
            var axis = PinAxis(degrees);
            var cross = PinCross(axis);
            var tip = PinTip(degrees, height, width, size);
            hotspot = Hotspot(tip, size);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - tip.x, dy = y - tip.y;
                float v = (dx * axis.x + dy * axis.y) / height;
                float u = (dx * cross.x + dy * cross.y) / width + 0.5f;
                if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                float alpha = source.GetPixelBilinear((minX + u * (maxX - minX)) / source.width,
                    (minY + v * (maxY - minY)) / source.height).a;
                Color color = pressed ? Color.Lerp(tint, Color.white, 0.45f) : tint;
                color.a = alpha;
                pixels[y * size + x] = color;
            }
            var outlined = (Color[])pixels.Clone();
            for (int y = 1; y < size - 1; y++)
            for (int x = 1; x < size - 1; x++)
            {
                int i = y * size + x;
                float edge = Mathf.Max(pixels[i - 1].a, pixels[i + 1].a,
                    pixels[i - size].a, pixels[i + size].a);
                if (pixels[i].a < 0.1f && edge > 0.3f)
                    outlined[i] = new Color(0.025f, 0.035f, 0.06f, edge * 0.85f);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
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
