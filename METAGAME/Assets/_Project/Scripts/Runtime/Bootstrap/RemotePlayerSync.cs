using System;
using System.Collections.Generic;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Network;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 인게임 위치 동기화의 조합 지점. 내 위치를 주기적으로 보내고,
    /// 다른 사람의 위치를 받아 아바타로 세운다.
    ///
    /// 네트워크(Metagame.Network)와 게임플레이(Metagame.Gameplay)를 잇는 역할이라
    /// 양쪽을 모두 참조하는 Bootstrap 에 둔다 — Gameplay 가 Network 를 알게 하지 않는다.
    /// </summary>
    public sealed class RemotePlayerSync : MonoBehaviour
    {
        /// 초당 전송 횟수. 올리면 부드러워지지만 트래픽이 는다.
        private const float SendsPerSecond = 12f;

        /// 가만히 서 있을 때는 보내지 않는다.
        private const float PositionEpsilon = 0.02f;
        private const float YawEpsilon = 1.5f;

        private NetworkSession network;
        private PlayerRig local;
        private Transform avatarRoot;

        private readonly Dictionary<long, RemotePlayerAvatar> avatars =
            new Dictionary<long, RemotePlayerAvatar>();
        private readonly List<long> stale = new List<long>();

        private float sendTimer;
        private Vector3 lastSentPosition;
        private float lastSentYaw;
        private bool sentOnce;

        private GUIStyle nameStyle;
        private Font koreanFont;

        public static RemotePlayerSync Create(NetworkSession session, PlayerRig localPlayer, Transform parent)
        {
            var go = new GameObject("~원격 플레이어");
            if (parent != null) go.transform.SetParent(parent, true);

            var sync = go.AddComponent<RemotePlayerSync>();
            sync.network = session;
            sync.local = localPlayer;
            sync.avatarRoot = go.transform;
            sync.Subscribe();
            return sync;
        }

        private void Subscribe()
        {
            if (network == null) return;
            network.PlayerMoved += OnPlayerMoved;
            network.RoomStateChanged += OnRoomStateChanged;
        }

        void OnDestroy()
        {
            if (network == null) return;
            network.PlayerMoved -= OnPlayerMoved;
            network.RoomStateChanged -= OnRoomStateChanged;
        }

        void Update()
        {
            if (network == null || local == null) return;

            sendTimer += Time.deltaTime;
            if (sendTimer < 1f / SendsPerSecond) return;
            sendTimer = 0f;

            Vector3 position = local.FootPosition;
            float yaw = local.Cam != null
                ? local.Cam.Yaw
                : local.transform.eulerAngles.y;

            if (sentOnce &&
                (position - lastSentPosition).sqrMagnitude < PositionEpsilon * PositionEpsilon &&
                Mathf.Abs(Mathf.DeltaAngle(yaw, lastSentYaw)) < YawEpsilon)
                return;

            network.SendMove(position.x, position.y, position.z, yaw);
            lastSentPosition = position;
            lastSentYaw = yaw;
            sentOnce = true;
        }

        private void OnPlayerMoved(PlayerMove move)
        {
            // 서버는 보낸 사람에게 되돌려 보내지 않지만, 방어적으로 한 번 더 막는다.
            if (network != null && move.UserId == network.UserId) return;

            RemotePlayerAvatar avatar;
            if (!avatars.TryGetValue(move.UserId, out avatar) || avatar == null)
            {
                avatar = RemotePlayerAvatar.Create(move.UserId, NicknameOf(move.UserId), avatarRoot);
                avatars[move.UserId] = avatar;
                avatar.MoveTo(new Vector3(move.X, move.Y, move.Z), move.Yaw);
                avatar.Snap();   // 처음 등장은 원점에서 날아오지 않게 바로 세운다
                return;
            }

            avatar.MoveTo(new Vector3(move.X, move.Y, move.Z), move.Yaw);
        }

        /// <summary>방을 나갔거나 유예를 넘겨 제외된 사람의 아바타를 치운다.</summary>
        private void OnRoomStateChanged(RoomSnapshot room)
        {
            stale.Clear();

            foreach (KeyValuePair<long, RemotePlayerAvatar> entry in avatars)
            {
                PlayerSnapshot player;
                if (room == null || !room.TryGetPlayer(entry.Key, out player))
                {
                    stale.Add(entry.Key);
                    continue;
                }

                if (entry.Value != null) entry.Value.Rename(player.Nickname);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                RemotePlayerAvatar avatar;
                if (avatars.TryGetValue(stale[i], out avatar) && avatar != null)
                    Destroy(avatar.gameObject);
                avatars.Remove(stale[i]);
            }
        }

        private string NicknameOf(long userId)
        {
            RoomSnapshot room = network != null ? network.CurrentRoom : null;
            PlayerSnapshot player;
            if (room != null && room.TryGetPlayer(userId, out player))
                return player.Nickname;
            return userId.ToString();
        }

        void OnGUI()
        {
            if (avatars.Count == 0) return;

            Camera view = local != null && local.Cam != null ? local.Cam.Cam : Camera.main;
            if (view == null) return;

            EnsureStyle();

            foreach (KeyValuePair<long, RemotePlayerAvatar> entry in avatars)
            {
                RemotePlayerAvatar avatar = entry.Value;
                if (avatar == null) continue;

                Vector3 screen = view.WorldToScreenPoint(avatar.LabelAnchor);
                if (screen.z <= 0f) continue;      // 카메라 뒤쪽

                var area = new Rect(screen.x - 100f, Screen.height - screen.y - 12f, 200f, 24f);
                Color previous = GUI.color;
                GUI.color = RemotePlayerAvatar.ColorFor(avatar.UserId);
                GUI.Label(area, avatar.Nickname, nameStyle);
                GUI.color = previous;
            }
        }

        private void EnsureStyle()
        {
            if (nameStyle != null) return;

            koreanFont = FindKoreanFont();
            nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                wordWrap = false
            };
            if (koreanFont != null) nameStyle.font = koreanFont;
        }

        private static Font FindKoreanFont()
        {
            string[] wanted = { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Nanum Gothic", "Gulim", "굴림" };
            string[] installed = Font.GetOSInstalledFontNames();
            if (installed == null) return null;

            for (int i = 0; i < wanted.Length; i++)
                for (int j = 0; j < installed.Length; j++)
                    if (string.Equals(wanted[i], installed[j], StringComparison.OrdinalIgnoreCase))
                        return Font.CreateDynamicFontFromOSFont(installed[j], 15);
            return null;
        }
    }
}
