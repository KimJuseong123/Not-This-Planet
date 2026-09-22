using System.Collections.Generic;
using Mirror;
using Metagame.Gameplay;
using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    public partial class GameHud
    {
        public const float TeamStatusHeight = 108f;
        public static readonly Color TeamAliveColor = new Color(.25f, .92f, .43f);
        public static readonly Color TeamDownedColor = new Color(1f, .24f, .28f);
        readonly TeamStatusRoster _teamRoster = new();
        readonly List<TeamView> _teamViews = new();
        Texture2D _teamCircle, _teamHeart;
        GUIStyle _teamNameStyle;

        sealed class TeamView
        {
            public TeamStatusRoster.Member Member;
            public readonly TeamPortrait Portrait = new();
            public string Name;
        }

        void UpdateTeamStatus()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            _teamRoster.Refresh(NetPlayer.AllClientPlayers, Local, NetworkClient.active || NetworkServer.active);
            for (int i = _teamViews.Count - 1; i >= 0; i--)
            {
                if (_teamRoster.Contains(_teamViews[i].Member)) continue;
                _teamViews[i].Portrait.Dispose();
                _teamViews.RemoveAt(i);
            }
            foreach (var member in _teamRoster.Members)
            {
                TeamView view = null;
                foreach (var candidate in _teamViews)
                    if (candidate.Member == member) { view = candidate; break; }
                if (view == null)
                {
                    view = new TeamView { Member = member };
                    _teamViews.Add(view);
                }
                if (view.Name != member.Name) { view.Name = member.Name; warmed = false; }
            }
        }

        void LateUpdate()
        {
            if (!ShowTeamStatus) return;
            foreach (var view in _teamViews)
                view.Portrait.Refresh(view.Member.Rig, view.Member.Appearance);
        }

        bool ShowTeamStatus
        {
            get
            {
                var session = GameSession.Instance;
                // 표시 조건 자체가 세션 수명을 검사하고 같은 참조로 소유권도 확인한다.
                if (session == null || Local == null || session.LocalPlayer != Local) return false;
                return !session.InputSuppressed && !session.ScriptedInput && !session.EndingActive
                    && (session.Exploration == null || !session.Exploration.TabletOpen);
            }
        }

        void DrawTeamStatus(float width)
        {
            if (!ShowTeamStatus || _teamRoster.Members.Count == 0) return;
            _teamCircle ??= CreateTeamIcon(false);
            _teamHeart ??= CreateTeamIcon(true);
            _teamNameStyle ??= new GUIStyle(small)
            {
                alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, richText = false
            };
            float cardWidth = Mathf.Min(128f, Mathf.Max(1f, (width - 36f) / _teamRoster.Members.Count));
            for (int i = 0; i < _teamRoster.Members.Count; i++)
            {
                var member = _teamRoster.Members[i];
                TeamView view = null;
                foreach (var candidate in _teamViews)
                    if (candidate.Member == member) { view = candidate; break; }
                if (view == null) continue;
                float x = 18f + i * cardWidth;
                float diameter = Mathf.Min(58f, cardWidth * .56f);
                var portrait = new Rect(x + (cardWidth - diameter - 26f) * .5f, 18f, diameter, diameter);
                var old = GUI.color;
                GUI.color = member.Accent;
                GUI.DrawTexture(portrait, _teamCircle);
                var inner = new Rect(portrait.x + 3f, portrait.y + 3f, diameter - 6f, diameter - 6f);
                GUI.color = new Color(.07f, .10f, .16f, .96f);
                GUI.DrawTexture(inner, _teamCircle);
                GUI.color = Color.white;
                if (view.Portrait.Texture != null) GUI.DrawTexture(inner, view.Portrait.Texture);
                else L(inner, "?", _teamNameStyle, member.Accent);
                GUI.color = member.Downed ? TeamDownedColor : TeamAliveColor;
                GUI.DrawTexture(new Rect(portrait.xMax + 6f, portrait.y + diameter * .5f - 11f, 23f, 23f), _teamHeart);
                GUI.color = old;
                // 긴 이름/리치 텍스트를 옆 플레이어 영역으로 넘기지 않는다.
                L(new Rect(x, 79f, cardWidth - 4f, 24f), member.Name, _teamNameStyle, member.Accent);
            }
        }

        static Texture2D CreateTeamIcon(bool heart)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = heart ? "Team heart" : "Team circle", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int covered = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float px = ((x + (sx + .5f) / 4f) / size * 2f - 1f) * 1.2f;
                            float py = ((y + (sy + .5f) / 4f) / size * 2f - 1f) * 1.2f;
                            float a = px * px + py * py - .78f;
                            if (heart ? a * a * a - px * px * py * py * py <= 0f : px * px + py * py <= 1.4f)
                                covered++;
                        }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(covered * 255 / 16));
                }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        void DisposeTeamStatus()
        {
            foreach (var view in _teamViews) view.Portrait.Dispose();
            _teamViews.Clear();
            if (_teamCircle != null) { if (Application.isPlaying) Destroy(_teamCircle); else DestroyImmediate(_teamCircle); }
            if (_teamHeart != null) { if (Application.isPlaying) Destroy(_teamHeart); else DestroyImmediate(_teamHeart); }
        }
    }

}
