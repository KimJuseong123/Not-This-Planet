using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
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
        [SyncVar] DemoEndingSnapshot endingSnapshot;

        /// <summary>백엔드에 <c>GAME_END</c> 를 이미 알렸는가. 한 판에 한 번만 보낸다.</summary>
        bool toldBackend;

        /// <summary>서버 첫 틱에 toldBackend 를 맞췄는가.</summary>
        bool primed;

        // 참가자가 마지막으로 GameSession 에 넣은 값. 바뀐 것이 없으면 다시 넣지 않는다.
        bool applied;
        int appliedCheckpoint;
        bool appliedCleared;
        float appliedClearTime;
        float appliedElapsed;
        int appliedFalls;

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
            else
            {
                ApplyOnParticipant(session);
                // 전용 서버에는 로비 웹소켓이 없다. 그 경우 방장 클라이언트가 종료를 알린다.
                if (session.EndingActive && session.Ending.Finished && !toldBackend)
                {
                    toldBackend = true;
                    TellBackendGameEnded();
                }
            }
        }

        /// <summary>호스트: <see cref="GameSession"/> 이 센 값을 SyncVar 에 올린다.</summary>
        void PushFromHost(GameSession session)
        {
            // 이미 클리어된 판에 이 오브젝트가 새로 떠도(재스폰·재접속) 다시 알리지 않는다.
            // 그러지 않으면 첫 프레임에 GAME_END 가 한 번 더 나가고, 방은 이미 WAITING
            // 이므로 백엔드가 ERROR 로 되돌려준다.
            if (!primed)
            {
                primed = true;
                toldBackend = session.Cleared && (!session.EndingActive || session.Ending.Finished);
            }

            checkpointIndex = session.CheckpointIndex;
            cleared = session.Cleared;
            clearTime = session.ClearTime;
            elapsed = session.Elapsed;
            falls = session.Falls;
            if (session.EndingActive && endingSnapshot == null)
                endingSnapshot = session.Ending.Snapshot;

            // 판이 다시 시작되면(GameSession.Restart) 다음 클리어도 알려야 한다.
            if (!cleared) toldBackend = false;
            else if (!toldBackend && (!session.EndingActive || session.Ending.Finished))
            { toldBackend = true; TellBackendGameEnded(); }
        }

        /// <summary>참가자: 받은 값을 <see cref="GameSession"/> 에 넣는다. 자기가 세지 않는다.</summary>
        void ApplyOnParticipant(GameSession session)
        {
            if (session.Ending != null && !session.EndingActive && endingSnapshot != null)
                session.Ending.ApplySnapshot(endingSnapshot);
            // 바뀐 것이 없으면 넣지 않는다. 매 프레임 무조건 덮어쓰면 StageHint 까지 계속
            // 다시 세우고, 참가자가 R(재시작)을 눌러도 다음 프레임에 되돌려져 입력이
            // 씹힌 것처럼 보인다. 값은 10Hz 로만 오므로 대개 그대로다.
            if (applied
                && appliedCheckpoint == checkpointIndex
                && appliedCleared == cleared
                && appliedClearTime == clearTime
                && appliedElapsed == elapsed
                && appliedFalls == falls)
            {
                return;
            }

            applied = true;
            appliedCheckpoint = checkpointIndex;
            appliedCleared = cleared;
            appliedClearTime = clearTime;
            appliedElapsed = elapsed;
            appliedFalls = falls;

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
        /// <summary>
        /// 접속이 끊기면 참가자는 곧바로 로컬로 되돌아간다.
        ///
        /// <para><b><see cref="OnDestroy"/> 만으로는 부족하다.</b> 오브젝트가 살아 있는 채
        /// 값이 더 오지 않는 경우(호스트가 멈춤·연결 끊김)에는 파괴 콜백이 오지 않고,
        /// 그러면 <c>ProgressFromHost</c> 가 켜진 채로 남아 <b>경과 시간이 영구히 얼고</b>
        /// <c>Clear</c>·<c>ReachCheckpoint</c> 도 계속 막힌다. 게임을 껐다 켜야만 풀린다.</para>
        ///
        /// <para>Mirror 가 끊김을 알려 주는 자리가 여기다 — 파괴보다 먼저, 그리고 파괴가
        /// 오지 않는 경우에도 온다.</para>
        /// </summary>
        public override void OnStopClient()
        {
            RestoreLocalProgress();
        }

        /// <summary>
        /// 이 오브젝트가 사라지면 참가자는 다시 로컬로 진행도를 센다.
        ///
        /// <para>없으면 마지막으로 받은 값에 시계가 멈춘 채로 남는다 — 매치가 끝나
        /// 혼자 하기로 넘어가도 경과 시간이 흐르지 않는다.</para>
        /// </summary>
        void OnDestroy()
        {
            RestoreLocalProgress();
        }

        /// <summary>
        /// 참가자를 로컬 진행도로 되돌린다. 두 경로(<see cref="OnStopClient"/>·
        /// <see cref="OnDestroy"/>)에서 불리므로 여러 번 불려도 안전해야 한다.
        /// </summary>
        void RestoreLocalProgress()
        {
            // 호스트는 애초에 ProgressFromHost 를 켜지 않는다 — 끌 것도 없다.
            if (isServer) return;

            GameSession session = GameSession.Instance;
            if (session != null) session.ClearHostProgress();
        }
    }
}
