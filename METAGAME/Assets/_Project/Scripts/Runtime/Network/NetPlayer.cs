using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.Network
{
    /// <summary>
    /// 네트워크로 오가는 플레이어의 뿌리. 위치와 신원만 들고 있고, 몸은 각자 만든다.
    ///
    /// PlayerRig 는 프리팹이 아니라 런타임에 조인트로 조립하는 래그돌이라(RagdollBuilder)
    /// Mirror 의 스폰 목록에 등록할 수 없다. 그래서 NetworkIdentity 만 가진 얇은 프리팹을
    /// 스폰하고, 몸은 서버와 클라이언트가 각자 만든다.
    ///
    /// <b>물리는 서버에서만 돈다</b>(S15P21B202-145·-147). 클라이언트는 누른 키를
    /// [Command] 로 보내고, 서버의 PlayerRig 가 그 입력으로 힘을 가한다. 결과인 골반
    /// 위치가 이 오브젝트에 실려 NetworkTransform 으로 모두에게 간다. 내 캐릭터도
    /// 예외가 아니다 — 각자 계산하면 두 부품을 함께 옮길 때 결과가 갈린다.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetPlayer : NetworkBehaviour
    {
        /// <summary>
        /// 입력을 보내는 최소 간격. 물리는 100Hz 로 돌지만 그 속도로 보낼 이유는 없다 —
        /// 사람이 키를 100분의 1초 단위로 바꾸지 않는다. 바뀌면 바로 보내고,
        /// 안 바뀌어도 이 간격마다 한 번은 보내 서버가 연결이 살아 있음을 안다.
        /// </summary>
        const float SendInterval = 1f / 20f;

        /// 서버가 정하고 모두에게 전달된다. 접속을 확인할 때 Spring 이 알려 준 값이다.
        [SyncVar(hook = nameof(OnIdentityChanged))]
        public string nickname = string.Empty;

        [SyncVar]
        public long userId;

        /// <summary>
        /// 지금 운반 장치를 타고 있는가. 서버가 정하고 모두에게 전달된다(-149).
        /// 클라이언트의 화면에는 조작 결과가 자세로만 오므로, "저 사람이 탔다" 는
        /// 사실 자체는 따로 알려 줘야 HUD 가 옳게 그린다.
        /// </summary>
        [SyncVar]
        public bool riding;

        // 이동 판정을 복제하지 않고, 표시용 걷기/달리기 상태만 함께 전달한다.
        [SyncVar]
        RabbitLocomotion.State _locomotionState;

        /// <summary>서버에서 물리를 도는 진짜 몸. 클라이언트에서는 그림용 몸이다.</summary>
        PlayerRig body;

        void LateUpdate()
        {
            if (body == null || body.Doll == null || body.Hips == null) return;
            if (isServer) _locomotionState = RabbitLocomotion.ReadState(body);
            if (body.Doll.Suit != null)
                body.Doll.Suit.NetworkLocomotionState = _locomotionState;
        }

        /// <summary>이 사람의 운반 장치 조작. 서버에만 있다(-149).</summary>
        TractorBeamController transport;

        /// <summary>게임 씬이 만든 몸을 넘겨받았는가. 그렇다면 여기서 없애지 않는다(-263).</summary>
        bool adoptedBody;

        /// <summary>몸을 못 세운 채 지나간 물리 스텝 수. 진단 로그를 한 번만 남기려고 센다.</summary>
        int waitedSteps;

        /// <summary>쓰러져 있은 시간. 전원 기절 구제를 재는 데 쓴다(-55).</summary>
        float downedSeconds;

        /// <summary>서버가 들고 있는 접속자 목록. "전원 기절" 의 '전원' 이 이것이다.</summary>
        static readonly List<NetPlayer> ServerPlayers = new List<NetPlayer>();

        // ── 서버 ────────────────────────────────────────────────
        InputFrame pending;          // 이 사람이 마지막으로 보낸 입력
        bool everReceived;           // 서버가 이 사람 입력을 한 번이라도 받았나(진단용)
        string lastAim;              // 마지막으로 남긴 조준 상태(바뀔 때만 찍는다)

        // ── 클라이언트(내 것) ───────────────────────────────────
        InputFrame lastSent;
        float sendTimer;
        bool everSent;

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
            ServerPlayers.Add(this);

            // 몸은 여기서 만들지 않는다. 호스트 모드에서는 게임 씬이 다 뜨기 전에
            // 스폰이 먼저 오기 때문이다(S15P21B202-263 실측) — 그때 만들면 로비 씬에
            // 래그돌이 생기고, 방장의 몸을 넘겨받을 기회도 놓친다.
            // 준비되면 FixedUpdate 가 만든다.
            Debug.Log($"[NetPlayer] 서버 스폰 · {nickname}(userId={userId}) · 접속 인원 {NetworkServer.connections.Count}명");
        }

        public override void OnStartClient()
        {
            // 서버 겸 클라이언트(호스트 모드)라면 몸은 이미 서버 쪽에서 만들었다.
            if (isServer) return;

            // 내 몸은 게임 씬이 이미 세워 뒀다. 아직이면 Update 가 계속 시도한다.
            if (isLocalPlayer) AdoptLocalBody();
            else BuildRemoteBody();
        }

        // ── 서버: 진짜 몸 ───────────────────────────────────────

        /// <summary>
        /// 물리가 도는 몸을 세운다. 이 몸이 부품을 밀고 지형을 밟는 유일한 실체다.
        /// </summary>
        /// <returns>몸을 세웠으면 참. 게임 씬이 아직 준비되지 않았으면 거짓.</returns>
        /// <summary>
        /// 이 플레이어가 방장 자신인가 (호스트 모드에서 서버 겸 클라이언트인 그 사람).
        ///
        /// <c>isLocalPlayer</c> 로 판단하면 안 된다 — 그 값은 <b>클라이언트 쪽</b> 스폰 처리가
        /// 끝나야 참이 되는데, 서버의 첫 <c>FixedUpdate</c> 가 그보다 먼저 도는 경우가 있다.
        /// 그러면 방장을 못 알아보고 몸을 하나 더 만들어 버린다(2026-09-02 실측).
        /// 연결이 서버 자신의 로컬 연결인지는 서버 쪽에서 바로 알 수 있어 이런 틈이 없다.
        /// </summary>
        bool IsHostOwnPlayer =>
            NetworkServer.active
            && NetworkServer.localConnection != null
            && connectionToClient == NetworkServer.localConnection;

        bool BuildServerBody()
        {
            var session = FindAnyObjectByType<GameSession>();

            // 게임 씬이 아직이다. 다음 물리 스텝에 다시 본다.
            if (session == null) return false;

            // 호스트 모드(방장의 PC 가 서버 겸 클라이언트)에서 방장 자신의 몸은
            // 게임 씬이 이미 세웠다 — 카메라·HUD·태블릿이 전부 그 몸에 붙어 있다.
            // 여기서 하나 더 만들면 방장만 몸이 두 벌이 되고, 카메라가 따라갈 대상이
            // 모호해진다(S15P21B202-263).
            if (IsHostOwnPlayer)
            {
                // 게임 씬이 아직 내 몸을 안 세웠으면 기다린다. 먼저 만들면 두 벌이 된다.
                if (session.LocalPlayer == null) return false;

                body = session.LocalPlayer;
                adoptedBody = true;

                // 진행 판정(낙사·전원 기절)은 서버가 한다. 호스트에서는 내가 곧 서버라
                // GameSession 이 로컬로 한 번 더 부활시키면 두 번 일어난다.
                body.NetworkDriven = true;

                AttachTransport();
                transform.SetPositionAndRotation(body.Hips.position, body.Hips.rotation);
                Debug.Log($"[NetPlayer] 방장의 몸을 넘겨받았다 · {nickname}(userId={userId})");
                return true;
            }

            Vector3 foot = session != null ? session.SpawnFoot : transform.position;
            Quaternion facing = Quaternion.LookRotation(Vector3.right, Vector3.up);

            // 헤드리스 서버에는 모델을 씌우지 않는다. 그릴 일이 없고 로딩만 든다.
            body = PlayerRig.Create(
                string.IsNullOrEmpty(nickname) ? "비행사" : nickname,
                foot + Vector3.up * 0.10f, facing,
                SuitColor(userId), AccentColor(userId),
                useModel: !Application.isBatchMode);

            AttachTransport();
            transform.SetPositionAndRotation(body.Hips.position, body.Hips.rotation);
            return true;
        }

        /// <summary>
        /// 운반 장치 조작을 이 사람 몫으로 하나 붙인다(S15P21B202-149).
        /// 컨트롤러가 1인용이라 접속자마다 하나씩 둔다 — 장치 자체는 씬에 하나뿐이고
        /// 여럿이 나눠 쓴다. 배치는 이미 끝나 있으므로 여기서 다시 섞지 않는다.
        /// </summary>
        void AttachTransport()
        {
            transport = body.gameObject.AddComponent<TractorBeamController>();
            transport.Configure(body, FindObjectsByType<TractorBeam>(FindObjectsSortMode.None),
                                randomizeVehicles: false);
        }

        /// <summary>
        /// 서버에서만 돈다. 마지막으로 받은 입력을 넣고, 물리가 낸 결과를 이 오브젝트에
        /// 옮겨 싣는다. NetworkTransform 이 그 값을 모두에게 보낸다.
        ///
        /// PlayerRig 자신의 FixedUpdate 가 물리를 돌리므로 여기서는 넣고 꺼내기만 한다 —
        /// 물리 코드는 건드리지 않는다.
        /// </summary>
        [ServerCallback]
        void FixedUpdate()
        {
            // 게임 씬이 준비될 때까지 기다렸다 몸을 세운다.
            if (body == null)
            {
                if (BuildServerBody()) { waitedSteps = 0; }
                else
                {
                    // 오래 기다리면 왜 못 세우는지 한 번 남긴다. 이 화면이 그대로 멈춘
                    // 것처럼 보이므로, 로그가 없으면 원인을 찾을 실마리가 없다.
                    if (++waitedSteps == 250)
                    {
                        var session = FindAnyObjectByType<GameSession>();
                        Debug.LogWarning(
                            $"[NetPlayer] 5초째 몸을 못 세우고 있다 · {nickname}(userId={userId}) · " +
                            $"게임씬={(session != null ? "있음" : "없음")} · " +
                            $"방장자신={IsHostOwnPlayer} · " +
                            $"씬이세운내몸={(session != null && session.LocalPlayer != null ? "있음" : "없음")}");
                    }
                    return;
                }
            }

            // 방장 자신의 몸은 게임 씬(GameSession)이 매 프레임 직접 입력을 넣는다.
            // 그 몸에 pending 을 덮어쓰면 안 된다 — Update 가 body.CurrentInput 을 읽어
            // 다시 보내므로 "내가 쓴 값을 내가 되읽는" 고리가 생기고, 입력이 처음 값에서
            // 영원히 갱신되지 않는다. 그래서 방장만 움직이지 못했다(2026-09-02 실측).
            // FixedUpdate 는 Update 보다 먼저 도니까 순서로도 막을 수 없다.
            InputFrame frame = adoptedBody ? body.CurrentInput : pending;

            if (transport != null)
            {
                transport.SetInput(frame);

                // 탑승이 실제로 서버에서 일어났는지, 그때 탈것이 움직이는지 남긴다.
                // 참가자가 탄 탈것이 방장 화면에서 어긋나는데, 서버가 태우지 않은 것인지
                // 태우고도 안 움직이는 것인지 구분할 방법이 없었다(2026-09-03).
                if (transport.IsControlling != riding)
                {
                    var vehicle = transport.FocusedVehicle;
                    Debug.Log($"[NetPlayer] {(transport.IsControlling ? "탑승" : "하차")} · "
                              + $"{nickname}(userId={userId}) · "
                              + $"탈것={(vehicle != null ? vehicle.name : "모름")}");
                }
                riding = transport.IsControlling;

                // 탑승 중에는 몸이 운반체와 싸우지 않게 한다. 이동은 운반체가 처리하고
                // 몸은 좌석에 앉아 있기만 한다 — 혼자 실행할 때 GameSession 이 하는 것과
                // 같은 정리다(그쪽은 클라이언트, 여기는 서버라 각자 한 번씩 필요하다).
                if (transport.IsControlling)
                {
                    frame.Move = Vector2.zero;
                    frame.Jump = false;
                    frame.Run = false;
                    frame.GrabLeft = false;
                    frame.GrabRight = false;
                    frame.ToolAction = false;
                    frame.ToolPrimary = false;
                    frame.ToolSecondary = false;
                    frame.ToolUtility = false;
                    frame.ArmsUp = false;
                }
            }

            // 넘겨받은 몸(방장 자신)에는 넣지 않는다 — 위에서 적은 고리 때문이다.
            if (!adoptedBody) body.SetInput(frame);

            ReportAim();
            JudgeProgress(Time.fixedDeltaTime);
            transform.SetPositionAndRotation(body.Hips.position, body.Hips.rotation);
        }

        /// <summary>
        /// 서버가 이 사람의 조준을 어떻게 보고 있는지 남긴다. 바뀔 때만 찍는다.
        ///
        /// 참가자만 상호작용이 되지 않는 일이 있었는데, 서버가 대상을 못 잡는 것인지
        /// 잡고도 실행이 막힌 것인지 구분할 방법이 없었다. 대상·탑승·기절을 함께 남기면
        /// 그 자리에서 갈린다 — RefreshInteractionFocus 는 탑승·기절이면 대상을 비운다.
        /// </summary>
        void ReportAim()
        {
            if (body == null) return;

            bool holding = (body.LeftHand != null && body.LeftHand.Held != null)
                        || (body.RightHand != null && body.RightHand.Held != null);

            // 무거운 부품은 손으로 쥐는 대신 "운반" 으로 간다. 둘은 다른 경로라
            // 쥐기만 보면 운반이 되는 중인지 알 수 없다.
            bool carrying = (body.LeftHand != null && body.LeftHand.Carrying)
                         || (body.RightHand != null && body.RightHand.Carrying);

            string now = $"{body.CurrentInteractionTarget.Kind}"
                       + $"·탑승{(body.IsRiding ? "O" : "X")}"
                       + $"·기절{(body.Downed ? "O" : "X")}"
                       + $"·잡음{(holding ? "O" : "X")}"
                       + $"·운반{(carrying ? "O" : "X")}";
            if (now == lastAim) return;

            lastAim = now;
            Debug.Log($"[NetPlayer] 조준 · {nickname}(userId={userId}) · {now}");
        }

        /// <summary>
        /// 낙사와 전원 기절을 서버가 판정한다(S15P21B202-55).
        ///
        /// 혼자 실행할 때는 GameSession 이 하지만, 전용 서버에는 "내 캐릭터" 가 없어
        /// 그 경로가 돌지 않는다(SpaceBootstrap 이 Session.Begin(null, ...) 로 시작한다).
        /// 그대로 두면 맵 밖으로 나간 사람이 영원히 떨어진다.
        /// </summary>
        [Server]
        void JudgeProgress(float deltaTime)
        {
            var session = GameSession.Instance;
            if (session == null) return;

            if (GameSession.HasFallen(body))
            {
                session.RespawnPlayer(body, transport);
                downedSeconds = 0f;
                return;
            }

            // 전원이 기절하면 아무도 일으킬 수 없다. 시간이 지나면 되살아난다
            // (기획 §26-13). 혼자일 때는 "전원" 이 곧 나 하나다.
            if (!body.Downed)
            {
                downedSeconds = 0f;
                return;
            }

            downedSeconds += deltaTime;
            if (downedSeconds < PhysicsScale.AllDownedRespawnSeconds) return;
            if (!EveryoneDowned()) return;

            downedSeconds = 0f;
            session.RespawnPlayer(body, transport);
        }

        /// <summary>접속한 사람이 모두 쓰러져 있는가.</summary>
        static bool EveryoneDowned()
        {
            foreach (var other in ServerPlayers)
                if (other != null && other.body != null && !other.body.Downed)
                    return false;
            return true;
        }

        /// <summary>
        /// 클라이언트가 누른 키. 서버는 보관만 하고 다음 물리 스텝에 적용한다 —
        /// 받은 즉시 물리를 돌리면 패킷이 몰려 올 때 그 사람만 여러 번 움직인다.
        /// </summary>
        [Command]
        void CmdSendInput(InputFrame frame)
        {
            if (!everReceived)
            {
                everReceived = true;
                Debug.Log($"[NetPlayer] 서버가 입력을 받기 시작 · {nickname}(userId={userId})");
            }
            pending = frame;
        }

        // ── 클라이언트: 내 몸 ───────────────────────────────────

        /// <summary>
        /// 게임 씬(SpaceBootstrap)이 이미 세워 둔 내 몸을 넘겨받아, 스스로 움직이는 대신
        /// 서버가 보낸 자리를 따라가게 한다.
        ///
        /// 새로 만들지 않는 이유는 카메라·HUD·태블릿이 모두 그 몸에 붙어 있어서다.
        /// 여기서 한 벌 더 만들면 카메라가 붙을 대상이 둘이 된다.
        /// </summary>
        /// <returns>넘겨받았으면 참. 게임 씬이 아직 준비되지 않았으면 거짓.</returns>
        bool AdoptLocalBody()
        {
            var session = FindAnyObjectByType<GameSession>();
            body = session != null ? session.LocalPlayer : null;
            if (body == null) return false;

            RemoteAvatar.Attach(body, transform, mine: true);
            Debug.Log($"[NetPlayer] 내 캐릭터를 서버에 맡겼다 · {nickname}(userId={userId})");
            return true;
        }

        /// <summary>
        /// 내가 누른 키를 서버로 보낸다. 카메라는 로컬에 남으므로 조준 방향은
        /// 입력(<see cref="InputFrame.Look"/>)에 실려 함께 간다.
        /// </summary>
        void Update()
        {
            if (!isLocalPlayer) return;

            // 스폰 메시지가 게임 씬이 몸을 세우기 전에 도착할 수 있다. 준비될 때까지 기다린다.
            if (body == null && !AdoptLocalBody()) return;

            sendTimer += Time.deltaTime;

            // 카메라는 내 PC 에만 있다. 서버가 "앞" 이 어느 쪽인지 알 수 있도록
            // 지금 보고 있는 각도를 입력에 실어 함께 보낸다.
            InputFrame now = body.CurrentInput;
            if (body.Cam != null)
            {
                now.AimYaw = body.Cam.Yaw;
                now.AimPitch = body.Cam.Pitch;

                // 조준을 어디서 시작하는지도 보낸다. 각도만 보내면 서버는 몸에서 재게 되어
                // 내 화면과 몇 미터 어긋난다 — 참가자가 눈앞의 부품을 못 잡던 이유다.
                now.AimFrom = body.Cam.transform.position;
            }

            bool changed = !everSent || !Same(now, lastSent);
            if (!changed && sendTimer < SendInterval) return;

            if (!everSent)
                Debug.Log($"[NetPlayer] 입력 전송 시작 · {nickname}(userId={userId}) · " +
                          $"isServer={isServer} isLocalPlayer={isLocalPlayer}");

            CmdSendInput(now);
            lastSent = now;
            everSent = true;
            sendTimer = 0f;
        }

        /// <summary>
        /// 두 입력이 사실상 같은가. 마우스와 시선은 늘 미세하게 흔들려 그대로
        /// 비교하면 매 프레임 "바뀜" 이 되고, 제한을 둔 뜻이 사라진다.
        /// 시선은 1도까지는 같은 것으로 본다 — 그 차이는 화면에서 보이지 않는다.
        /// </summary>
        static bool Same(InputFrame a, InputFrame b)
        {
            return (a.Move - b.Move).sqrMagnitude < 0.0001f
                && (a.Look - b.Look).sqrMagnitude < 0.0001f
                && Mathf.Abs(Mathf.DeltaAngle(a.AimYaw, b.AimYaw)) < 1f
                && Mathf.Abs(a.AimPitch - b.AimPitch) < 1f
                // 조준 시작점도 늘 조금씩 흔들린다. 10cm 까지는 같은 것으로 본다 —
                // 잡기 판정(보조 반경 1.1m)에 영향을 주지 않는 차이다.
                && (a.AimFrom - b.AimFrom).sqrMagnitude < 0.01f
                && a.Jump == b.Jump && a.Run == b.Run
                && a.GrabLeft == b.GrabLeft && a.GrabRight == b.GrabRight
                && a.ToolAction == b.ToolAction
                && a.ToolPrimary == b.ToolPrimary && a.ToolSecondary == b.ToolSecondary
                && a.ToolUtility == b.ToolUtility
                && a.TransportAction == b.TransportAction
                && a.ArmsUp == b.ArmsUp && a.FreeLook == b.FreeLook
                && a.TabletToggle == b.TabletToggle && a.FieldToggle == b.FieldToggle
                && a.TabletPreviousPage == b.TabletPreviousPage
                && a.TabletNextPage == b.TabletNextPage
                && a.Restart == b.Restart;
        }

        // ── 클라이언트: 남의 몸 ─────────────────────────────────

        /// <summary>다른 사람의 몸. 판정에 관여하지 않는 그림이다.</summary>
        void BuildRemoteBody()
        {
            if (body != null) return;

            body = PlayerRig.Create(
                string.IsNullOrEmpty(nickname) ? "비행사" : nickname,
                transform.position,
                transform.rotation,
                SuitColor(userId),
                AccentColor(userId));

            // 자식으로 붙이지 않는 이유는 RemoteAvatar 가 골반을 MovePosition 으로
            // 직접 옮기기 때문이다 — 부모를 따라 끌려가면 조인트가 그 이동을
            // 순간이동으로 받아들여 몸이 튄다.
            RemoteAvatar.Attach(body, transform);

            Debug.Log($"[NetPlayer] 다른 플레이어가 보인다 · {nickname}(userId={userId})");
        }

        void OnIdentityChanged(string _, string newNickname)
        {
            if (body == null) return;
            body.Nickname = string.IsNullOrEmpty(newNickname) ? "비행사" : newNickname;
        }

        public override void OnStopClient() => RemoveBody();
        public override void OnStopServer()
        {
            ServerPlayers.Remove(this);
            RemoveBody();
        }

        /// <summary>
        /// 몸을 치운다. 내 몸은 게임 씬이 만든 것이라 여기서 없애지 않는다 —
        /// 카메라와 HUD 가 아직 그것을 보고 있다.
        /// </summary>
        void RemoveBody()
        {
            if (body == null) return;

            // 넘겨받은 몸은 게임 씬 것이다 — 카메라와 HUD 가 아직 보고 있다.
            if (!adoptedBody && (!isLocalPlayer || isServer)) Destroy(body.gameObject);

            body = null;
            adoptedBody = false;
        }

        /// 같은 사람은 누구 화면에서나 같은 색으로 보이게 userId 로 정한다.
        static Color SuitColor(long id)
        {
            float hue = Mathf.Repeat(id * 0.6180339f, 1f);   // 황금비로 흩뿌린다
            return Color.HSVToRGB(hue, 0.35f, 0.95f);
        }

        static Color AccentColor(long id)
        {
            float hue = Mathf.Repeat(id * 0.6180339f + 0.5f, 1f);
            return Color.HSVToRGB(hue, 0.7f, 0.9f);
        }
    }
}
