using Metagame.Gameplay;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 판의 진행도(경과 시간·클리어·체크포인트·추락)를 호스트에서 모두에게 내려보낸다
    /// (S15P21B202-54).
    ///
    /// <b>왜 필요한가.</b> 이 값들은 <see cref="GameSession"/> 이 각 기계에서 <b>따로</b>
    /// 세고 있었다. 그래서 같은 판인데 사람마다 경과 시간이 다르고, "도착했다" 는 순간이
    /// 공유되지 않았다 — 협동 게임에서 클리어를 같이 보지 못한다는 뜻이다.
    ///
    /// <b>새 판정을 만들지 않는다.</b> 호스트의 <see cref="GameSession"/> 이 이미 다
    /// 계산하고 있다(호스트는 <c>LocalPlayer</c> 가 있는 클라이언트이므로
    /// <c>GameSession.Update</c> 가 그대로 돈다). 여기서는 그 값을 실어 나르고, 참가자
    /// 쪽에서는 자기 것을 세지 않게 하는 일만 한다.
    ///
    /// <b>왜 별 오브젝트를 만들지 않았나.</b> <see cref="WorldSync"/> 와 같은
    /// <c>NetworkIdentity</c> 에 얹는다. 그쪽은 프리팹 없이 스폰 핸들러로 뜨도록 이미
    /// 정리돼 있고(프리팹을 쓰면 리눅스 헤드리스가 SIGSEGV 로 죽는다 — -162),
    /// 접속마다 핸들러를 다시 등록하는 문제(-315)와 <c>NetworkIdentity</c> 의 Awake 순서
    /// 함정도 거기서 이미 해결했다. 같은 함정을 두 번 밟을 이유가 없다.
    /// </summary>
    public sealed class ProgressSync : NetworkBehaviour
    {
        /// <summary>
        /// 내려보내는 주기. HUD 가 경과 시간을 <c>0.0</c> 초까지만 그리므로 10Hz 로 충분하다 —
        /// 표시 단위와 주기가 같아서 더 자주 보내도 화면에 차이가 없다.
        /// </summary>
        const float SyncInterval = 0.1f;

        [SyncVar] int checkpointIndex;
        [SyncVar] bool cleared;
        [SyncVar] float clearTime;
        [SyncVar] float elapsed;
        [SyncVar] int falls;

        /// <summary>백엔드에 <c>GAME_END</c> 를 이미 알렸는가. 한 판에 한 번만 보낸다.</summary>
        bool toldBackend;

        void Awake()
        {
            syncInterval = SyncInterval;
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;

            // 스폰이 끝나기 전에는 아무것도 하지 않는다. 그 사이에 참가자 분기가 돌면
            // 아직 0인 SyncVar 를 GameSession 에 밀어넣어 ProgressFromHost 가 켜지고,
            // 호스트 자신의 진행도가 0에 얼어붙는다.
            if (!isServer && !isClient) return;

            // 호스트 모드에서는 isServer 와 isClient 가 둘 다 참이다. 호스트가 정답을
            // 갖고 있으므로 서버 쪽을 먼저 본다 — 순서를 바꾸면 호스트가 자기 값을
            // 자기에게 다시 덮어쓴다.
            if (isServer) PushFromHost(session);
            else ApplyOnParticipant(session);
        }

        /// <summary>호스트: <see cref="GameSession"/> 이 센 값을 SyncVar 에 올린다.</summary>
        void PushFromHost(GameSession session)
        {
            checkpointIndex = session.CheckpointIndex;
            cleared = session.Cleared;
            clearTime = session.ClearTime;
            elapsed = session.Elapsed;
            falls = session.Falls;

            // 판이 다시 시작되면(GameSession.Restart) 다음 클리어도 알려야 한다.
            if (!cleared) toldBackend = false;
            else if (!toldBackend) { toldBackend = true; TellBackendGameEnded(); }
        }

        /// <summary>참가자: 받은 값을 <see cref="GameSession"/> 에 넣는다. 자기가 세지 않는다.</summary>
        void ApplyOnParticipant(GameSession session)
        {
            session.ApplyHostProgress(checkpointIndex, cleared, clearTime, elapsed, falls);
        }

        /// <summary>
        /// 클리어를 백엔드에 알린다 — 방이 <c>WAITING</c> 으로 돌아온다 (S15P21B202-316).
        ///
        /// <para>서버는 이미 <c>GAME_END</c> 를 받을 준비가 돼 있고 보내는 쪽이 없었다.
        /// 그래서 한 판을 끝내면 그 방이 영구히 <c>PLAYING</c> 에 남았다.</para>
        ///
        /// <para>보내는 것은 <b>방장 한 명</b>이다. 백엔드도 방장이 아니면 거절하지만,
        /// 여기서 걸러 두면 쓸데없는 <c>ERROR</c> 왕복이 생기지 않는다.</para>
        /// </summary>
        void TellBackendGameEnded()
        {
            NetworkSession lobby = NetworkSession.Instance;

            // 혼자 하기(로비를 안 거친 실행)와 전용 서버에는 로비 연결이 없다.
            if (lobby == null || !lobby.IsConnected)
            {
                Debug.Log("[ProgressSync] 클리어 — 로비 연결이 없어 GAME_END 를 보내지 않는다");
                return;
            }

            if (!lobby.IsRoomHost)
            {
                Debug.Log("[ProgressSync] 클리어 — 방장이 아니라 GAME_END 를 보내지 않는다");
                return;
            }

            lobby.SendGameEnd();
            Debug.Log("[ProgressSync] 클리어 — GAME_END 를 보냈다 · 기록 "
                      + clearTime.ToString("0.0") + "초 · 추락 " + falls + "회");
        }

        /// <summary>
        /// 이 오브젝트가 사라지면 참가자는 다시 로컬로 진행도를 센다.
        ///
        /// <para>없으면 마지막으로 받은 값에 시계가 멈춘 채로 남는다 — 매치가 끝나
        /// 혼자 하기로 넘어가도 경과 시간이 흐르지 않는다.</para>
        /// </summary>
        void OnDestroy()
        {
            if (isServer) return;

            GameSession session = GameSession.Instance;
            if (session != null) session.ClearHostProgress();
        }
    }
}
