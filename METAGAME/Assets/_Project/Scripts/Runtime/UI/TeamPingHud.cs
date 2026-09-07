using UnityEngine;
using Metagame.Gameplay;
using Metagame.Network;

namespace Metagame.UI
{
    /// <summary>
    /// 팀 핑을 관찰자 카메라 기준의 화면 좌표로 표시한다.
    /// 핑 자체의 위치와 수명은 서버 상태를 사용하고, 이 컴포넌트는 표현만 담당한다.
    /// </summary>
    public sealed class TeamPingHud : MonoBehaviour
    {
        const float EdgeMargin = 28f;
        const float IconSize = 30f;
        const float LabelWidth = 54f;
        const float LabelHeight = 20f;

        GameSession session;
        Texture2D icon;
        Texture2D generatedIcon;
        Texture2D white;
        GUIStyle distanceStyle;

        public void Initialize(GameSession gameSession)
        {
            session = gameSession;
        }

        void Awake()
        {
            var sourceIcon = Resources.Load<Texture2D>("PingMarker");
            icon = MakeTintableIcon(sourceIcon);

            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        void OnDestroy()
        {
            if (white != null) Destroy(white);
            if (generatedIcon != null) Destroy(generatedIcon);
        }

        /// <summary>
        /// 생성 원본의 알파를 보존하고 RGB를 흰색으로 통일한다. 이렇게 하면
        /// 검은 단색 원본이어도 GUI 색상 곱셈으로 플레이어 색상을 정확히 입힐 수 있다.
        /// </summary>
        Texture2D MakeTintableIcon(Texture2D source)
        {
            if (source == null || !source.isReadable) return source;

            var pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].r = 255;
                pixels[i].g = 255;
                pixels[i].b = 255;
            }

            generatedIcon = new Texture2D(source.width, source.height,
                                           TextureFormat.RGBA32, false);
            generatedIcon.name = "PingMarker_Tintable";
            generatedIcon.SetPixels32(pixels);
            generatedIcon.Apply(false, true);
            return generatedIcon;
        }

        void EnsureStyle()
        {
            if (distanceStyle != null) return;

            distanceStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyle();

            if (session == null) session = GameSession.Instance;
            if (session == null || session.LocalPlayer == null || session.LocalPlayer.Cam == null)
                return;

            Camera camera = session.LocalPlayer.Cam.Cam;
            if (camera == null || icon == null) return;

            var players = NetPlayer.AllClientPlayers;
            if (players != null && players.Count > 0)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == null || !player.HasActivePing) continue;
                    DrawPing(camera, player.PingPosition, player.PingColor);
                }
                return;
            }

            // 네트워크 오브젝트가 없는 솔로에서는 GameSession의 동일한 로컬 규칙을 사용한다.
            if (session.LocalPingActive)
                DrawPing(camera, session.LocalPingPosition, session.LocalPlayer.TeamColor);
        }

        void DrawPing(Camera camera, Vector3 worldPosition, Color color)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            bool onScreen = screen.z > 0f
                         && screen.x >= 0f && screen.x <= Screen.width
                         && screen.y >= 0f && screen.y <= Screen.height;

            Vector2 point;
            float rotation = 0f;
            if (onScreen)
            {
                point = new Vector2(screen.x, Screen.height - screen.y);
            }
            else
            {
                Vector3 local = camera.transform.InverseTransformPoint(worldPosition);
                bool behind = local.z < 0f;
                if (behind)
                {
                    local.x = -local.x;
                    local.y = -local.y;
                }

                // GUI의 y축은 아래 방향이므로 월드 위쪽을 화면 위쪽으로 반전한다.
                Vector2 direction = new Vector2(local.x, -local.y);
                if (direction.sqrMagnitude < 0.0001f)
                    direction = behind ? Vector2.down : Vector2.up;
                direction.Normalize();

                float halfWidth = Mathf.Max(1f, Screen.width * 0.5f - EdgeMargin - IconSize * 0.5f);
                float halfHeight = Mathf.Max(1f, Screen.height * 0.5f - EdgeMargin - IconSize * 0.5f);
                float scale = float.MaxValue;
                if (Mathf.Abs(direction.x) > 0.001f)
                    scale = Mathf.Min(scale, halfWidth / Mathf.Abs(direction.x));
                if (Mathf.Abs(direction.y) > 0.001f)
                    scale = Mathf.Min(scale, halfHeight / Mathf.Abs(direction.y));

                point = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                      + direction * scale;
                rotation = Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
            }

            DrawIcon(point, color, rotation);

            int distance = Mathf.RoundToInt(Vector3.Distance(camera.transform.position, worldPosition));
            DrawDistance(point, distance + "m", color);
        }

        void DrawIcon(Vector2 point, Color color, float rotation)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 0.96f);

            if (Mathf.Abs(rotation) > 0.01f)
                GUIUtility.RotateAroundPivot(rotation, point);

            GUI.DrawTexture(new Rect(point.x - IconSize * 0.5f,
                                     point.y - IconSize * 0.5f,
                                     IconSize, IconSize), icon,
                            ScaleMode.ScaleToFit, true);

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        void DrawDistance(Vector2 point, string text, Color color)
        {
            bool putLeft = point.x > Screen.width * 0.70f;
            float x = putLeft ? point.x - IconSize * 0.5f - LabelWidth - 4f
                              : point.x + IconSize * 0.5f + 4f;
            float y = Mathf.Clamp(point.y - LabelHeight * 0.5f, 4f,
                                  Mathf.Max(4f, Screen.height - LabelHeight - 4f));
            var rect = new Rect(x, y, LabelWidth, LabelHeight);

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.52f);
            GUI.DrawTexture(new Rect(rect.x - 3f, rect.y, rect.width + 6f, rect.height), white);

            distanceStyle.alignment = putLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.78f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, distanceStyle);
            GUI.color = color;
            GUI.Label(rect, text, distanceStyle);
            GUI.color = previousColor;
        }
    }
}
