using Mirror;
using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.Network
{
    /// <summary>
    /// 네트워크로 오가는 플레이어의 뿌리. 위치와 신원만 들고 있고, 몸은 각자 만든다.
    ///
    /// PlayerRig 는 프리팹이 아니라 런타임에 조인트로 조립하는 래그돌이라(RagdollBuilder)
    /// Mirror 의 스폰 목록에 등록할 수 없다. 그래서 NetworkIdentity 만 가진 얇은 프리팹을
    /// 스폰하고, 몸은 서버와 클라이언트가 같은 코드로 그 아래에 만든다.
    ///
    /// 네트워크로 오가는 것은 이 오브젝트의 위치·회전과 닉네임뿐이다. 팔다리가 흔들리는
    /// 모습은 각 PC 가 알아서 그린다 — 조금 달라 보여도 게임에 지장이 없고, 전부 보내면
    /// 데이터가 몇 배로 늘어난다.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetPlayer : NetworkBehaviour
    {
        /// 서버가 정하고 모두에게 전달된다. 접속을 확인할 때 Spring 이 알려 준 값이다.
        [SyncVar(hook = nameof(OnIdentityChanged))]
        public string nickname = string.Empty;

        [SyncVar]
        public long userId;

        private PlayerRig body;

        public override void OnStartClient() => BuildBody();

        public override void OnStartServer()
        {
            // 접속 확인을 통과한 연결에는 신원이 붙어 있다(MatchAuthenticator).
            // 여기서 채우면 SyncVar 를 타고 모든 클라이언트에 전달된다.
            // 인증자 없이 띄운 검증용 실행에서는 없을 수 있어 그때는 연결 번호로 대신한다.
            var identity = connectionToClient != null
                ? connectionToClient.authenticationData as MatchIdentity
                : null;

            if (identity != null)
            {
                userId = identity.UserId;
                nickname = identity.Nickname;
            }
            else if (string.IsNullOrEmpty(nickname))
            {
                userId = connectionToClient != null ? connectionToClient.connectionId : 0;
                nickname = "비행사 " + userId;
            }

            name = "플레이어 " + nickname;
            Debug.Log($"[NetPlayer] 서버 스폰 · {nickname}(userId={userId}) · 접속 인원 {NetworkServer.connections.Count}명");
            BuildBody();
        }

        /// <summary>
        /// 몸을 만든다. 서버·클라이언트 양쪽에서 같은 코드로 만들어 모양을 맞춘다.
        ///
        /// 내 캐릭터는 게임 씬(SpaceBootstrap)이 따로 세우므로 여기서 만들지 않는다.
        /// 두 벌이 겹치면 카메라가 붙을 대상이 모호해지고 입력도 둘로 갈린다.
        /// </summary>
        private void BuildBody()
        {
            if (isLocalPlayer || body != null) return;

            body = PlayerRig.Create(
                string.IsNullOrEmpty(nickname) ? "비행사" : nickname,
                transform.position,
                transform.rotation,
                RemoteSuitColor(userId),
                RemoteAccentColor(userId));

            // 보여 주기 전용으로 바꾼다. 골반만 이 오브젝트를 따라가고 팔다리는
            // 조인트에 맡긴다. 자식으로 붙이지 않는 이유는 RemoteAvatar 가 골반을
            // MovePosition 으로 직접 옮기기 때문이다 — 부모를 따라 끌려가면
            // 조인트가 그 이동을 순간이동으로 받아들여 몸이 튄다.
            RemoteAvatar.Attach(body, transform);

            if (isClient)
                Debug.Log($"[NetPlayer] 다른 플레이어가 보인다 · {nickname}(userId={userId})");
        }

        private void OnIdentityChanged(string _, string newNickname)
        {
            if (body == null) return;
            body.Nickname = string.IsNullOrEmpty(newNickname) ? "비행사" : newNickname;
            body.gameObject.name = "원격 " + body.Nickname;
        }

        public override void OnStopClient() => RemoveBody();
        public override void OnStopServer() => RemoveBody();

        private void RemoveBody()
        {
            if (body == null) return;
            Destroy(body.gameObject);
            body = null;
        }

        /// 같은 사람은 누구 화면에서나 같은 색으로 보이게 userId 로 정한다.
        private static Color RemoteSuitColor(long id)
        {
            float hue = Mathf.Repeat(id * 0.6180339f, 1f);   // 황금비로 흩뿌린다
            return Color.HSVToRGB(hue, 0.35f, 0.95f);
        }

        private static Color RemoteAccentColor(long id)
        {
            float hue = Mathf.Repeat(id * 0.6180339f + 0.5f, 1f);
            return Color.HSVToRGB(hue, 0.7f, 0.9f);
        }
    }
}
