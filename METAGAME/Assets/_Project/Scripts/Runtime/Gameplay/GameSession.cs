using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 한 판의 진행 상태. 입력을 읽어 인형에게 넘기고, 떨어지면 되돌리고,
    /// 골에 닿으면 판을 끝낸다.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance;

        public PlayerRig LocalPlayer;

        /// <summary>전원이 기절한 채 흐른 시간. 출구 판정에만 쓴다.</summary>
        float allDownedSeconds;
        public LevelBuilder Level;
        public Stage1Exploration Exploration;
        public TractorBeamController Transport;

        /// <summary>
        /// 끼임 탈출(기획 §21). <see cref="Begin"/> 이 만든다.
        ///
        /// 화면 표시가 <see cref="StuckEscape.Progress01"/> 을 읽을 수 있게 공개한다.
        /// </summary>
        public StuckEscape Escape { get; private set; }

        public int CheckpointIndex { get; private set; }
        public Vector3 SpawnFoot { get; private set; }
        public string StageHint { get; private set; }
        public bool Cleared { get; private set; }
        public float ClearTime { get; private set; }
        public float Elapsed { get; private set; }
        public int Falls { get; private set; }

        /// <summary>
        /// 진행도를 호스트가 정하는가 (S15P21B202-54).
        ///
        /// <para>멀티플레이에서 판의 진행도(경과 시간·클리어·체크포인트)는 <b>호스트 하나만</b>
        /// 정한다. 참가자가 각자 세면 같은 판인데 기록이 사람마다 달라진다.</para>
        ///
        /// <para>혼자 할 때는 <c>false</c> 다 — 네트워크가 없으면 이 값을 켜는 것이 없으므로
        /// 예전과 똑같이 로컬이 판을 진행한다.</para>
        /// </summary>
        public bool ProgressFromHost { get; private set; }

        public bool CursorLocked { get; private set; }

        readonly PingLifetimeState localPing = new PingLifetimeState();

        /// <summary>멀티플레이 오브젝트가 없을 때 HUD가 사용하는 로컬 핑.</summary>
        public bool LocalPingActive { get { return localPing.IsActive; } }
        public Vector3 LocalPingPosition { get { return localPing.Position; } }
        public double LocalPingExpiresAt { get { return localPing.ExpiresAt; } }

        /// <summary>true 면 사람 입력을 읽지 않는다 (셀프테스트가 직접 넣는다).</summary>
        public bool ScriptedInput;

        /// <summary>
        /// 로컬 메뉴가 열렸을 때만 사람 입력을 잠시 막는다.
        /// 세계 시간과 물리·운석 진행은 계속 흘러야 하므로 Time.timeScale을 바꾸지 않는다.
        /// </summary>
        public bool InputSuppressed { get; private set; }

        /// <summary>체크포인트 N 에 서 있을 때 화면에 뜨는 "지금 할 일".</summary>
        static readonly string[] Hints =
        {
            "화물을 들고 던져 보자. 저중력이라 아주 멀리 난다",
            "크레이터 두 개를 뛰어 건넌다. 달리기(Shift)로 도움닫기",
            "절벽은 점프로 못 넘는다. 뛰면서 핸드레일을 잡고 W 로 기어오른다",
            "균열 위 계류 케이블을 잡아라. 놓쳐도 천천히 떨어진다",
            "널빤지는 밟는 쪽으로 기운다. 가운데서 멈추지 말 것",
            "화물판이 올 때까지 기다렸다 올라탄다",
            "배터리 셀을 들고 와 소켓에 꽂으면 에어록이 열린다",
            "구조 로켓까지 남았다. 회전 안테나를 조심해라",
        };

        void Awake()
        {
            Instance = this;
            SpawnFoot = Vector3.zero;
            StageHint = Hints[0];
        }

        public void Begin(PlayerRig rig, LevelBuilder level, Vector3 spawnFoot,
                          Stage1Exploration exploration = null)
        {
            LocalPlayer = rig;
            Escape = new StuckEscape(rig);
            Level = level;
            Exploration = exploration;
            SpawnFoot = spawnFoot;
            CheckpointIndex = 0;
            Elapsed = 0f;
            Falls = 0;
            Cleared = false;
            ProgressFromHost = false;   // 새 판은 로컬로 시작한다. 호스트 값이 오면 켜진다 (S15P21B202-54)
            InputSuppressed = false;
            localPing.Clear();
            if (Exploration != null) Exploration.ResetRun();
            SetCursor(true);
        }

        void Update()
        {
            if (LocalPlayer == null) return;

            localPing.Tick(Time.timeAsDouble);

            // 경과 시간은 호스트가 정할 때는 세지 않는다 (S15P21B202-54) —
            // 각자 세면 사람마다 다른 기록이 남는다.
            if (!Cleared && !ProgressFromHost) Elapsed += Time.deltaTime;

            if (Exploration != null) Exploration.Tick();

            if (InputSuppressed)
                return;

            if (!ScriptedInput)
            {
                var f = Controls.Read();

                if (f.TabletToggle && Exploration != null)
                {
                    Exploration.ToggleTablet();
                    SetCursor(!Exploration.TabletOpen);
                }

                if (Exploration != null && Exploration.TabletOpen)
                {
                    if (f.TabletPreviousPage) Exploration.PreviousTabletPage();
                    if (f.TabletNextPage) Exploration.NextTabletPage();
                }

                if (Input.GetKeyDown(Controls.FreeCursor)) SetCursor(false);
                if (Input.GetMouseButtonDown(0) && !CursorLocked
                    && (Exploration == null || !Exploration.TabletOpen))
                    SetCursor(true);
                if (!CursorLocked)
                {
                    f.Look = Vector2.zero;
                    f.GrabLeft = false;
                    f.GrabRight = false;
                    f.ToolAction = false;
                    f.ToolPrimary = false;
                    f.ToolSecondary = false;
                    f.ToolUtility = false;
                    f.TransportAction = false;
                    f.FreeLook = false;
                    f.PingPressed = false;
                }

                if (Exploration != null && Exploration.TabletOpen)
                {
                    f.Move = Vector2.zero;
                    f.Look = Vector2.zero;
                    f.Jump = false;
                    f.Run = false;
                    f.GrabLeft = false;
                    f.GrabRight = false;
                    f.ToolAction = false;
                    f.ToolPrimary = false;
                    f.ToolSecondary = false;
                    f.ToolUtility = false;
                    f.TransportAction = false;
                    f.ArmsUp = false;
                    f.FreeLook = false;
                    f.PingPressed = false;
                }

                // 끼임 탈출은 3초 유지다(기획 §21). 예전에는 GetKeyDown 으로 즉시
                // 스폰 지점으로 보내면서 Revive 까지 불러 §20 의 기절·구조를 우회했다
                // (S15P21B202-248).
                Escape?.Tick(Time.deltaTime, f.StuckEscapeHeld);

                if (f.Restart) Restart();

                if (LocalPlayer.Cam != null)
                {
                    // 기절 시점의 시간을 센다(기획 §10.3). Time 을 카메라가 직접 읽지
                    // 않는 이유는 검증이 5초를 실제로 기다릴 수 없기 때문이다.
                    LocalPlayer.Cam.TickDownedView(Time.deltaTime);

                    LocalPlayer.Cam.SetFreeLook(f.FreeLook);     // Steer 보다 먼저 — Alt 시점의 pitch 범위를 선택한다
                    bool clampButtons = f.GrabLeft || f.GrabRight;
                    LocalPlayer.Cam.SetClampLiftResistance(
                        LocalPlayer.ClampGrabbed && clampButtons && !f.FreeLook);
                    LocalPlayer.Cam.SetClampLiftPitchLock(
                        LocalPlayer.ClampGrabbed && LocalPlayer.ClampLiftReady
                        && clampButtons && !f.FreeLook);
                    LocalPlayer.Cam.Steer(f.Look);
                }

                // 카메라가 갱신된 뒤 같은 프레임에 하나의 상호작용 대상을 고른다.
                // 운반 장치·고정쇠·손잡기 입력과 HUD가 모두 이 결과를 공유한다.
                LocalPlayer.RefreshInteractionFocus();

                if (f.PingPressed && !LocalPlayer.Downed)
                {
                    if (PingPlacement.TryResolve(LocalPlayer.AimOrigin, LocalPlayer.AimForward,
                                                  LocalPlayer, out var point, out _))
                        localPing.Place(point, Time.timeAsDouble, Tuning.PingLifetimeSeconds);
                }

                if (Transport != null)
                {
                    Transport.SetInput(f);
                    if (Transport.IsControlling)
                    {
                        // 탑승 중에는 플레이어 물리가 운반체와 싸우지 않게 한다. 이동은
                        // 운반체가 처리하고, 카메라 회전과 원본 클릭 입력은 컨트롤러가 쓴다.
                        f.Move = Vector2.zero;
                        f.Jump = false;
                        f.Run = false;
                        f.GrabLeft = false;
                        f.GrabRight = false;
                        f.ToolAction = false;
                        f.ToolPrimary = false;
                        f.ToolSecondary = false;
                        f.ToolUtility = false;
                        f.ArmsUp = false;
                    }
                }
                LocalPlayer.SetInput(f);
            }

            // 낙사와 전원 기절은 "누구를 어디로 되돌릴지" 를 정하는 판단이다.
            // 위치를 서버가 정할 때는(-147) 서버 몫이므로 여기서 물러난다. 그러지
            // 않으면 화면 속 몸만 스폰 지점으로 옮겨지고, 서버가 보낸 다음 위치에
            // 곧바로 도로 끌려간다.
            if (LocalPlayer.NetworkDriven) return;

            // 떨어졌는가 — 여기는 <b>낙사만</b> 본다.
            //
            // 예전에는 LocalPlayer.IsDead 도 함께 보고 곧바로 Respawn() 을 불렀다.
            // 그래서 운석에 쓰러지는 순간 혼자 스폰 지점에 되살아났고, 팀원이 도우러
            // 갈 틈도 이유도 없었다(기획 §26-13 이 묻는 긴장이 아예 없었다).
            // 체력 0 은 이제 PlayerRig 가 기절로 처리한다.
            if (HasFallen(LocalPlayer))
            {
                Falls++;
                Respawn();
                return;
            }

            TickAllDowned(Time.deltaTime);
        }

        /// <summary>
        /// 전원이 기절했을 때의 출구(기획 §26-13).
        ///
        /// 아무도 일으킬 수 없으므로 시간이 지나면 스폰 지점에서 되살아난다.
        /// 없으면 게임이 멈춘다 — 예전에는 즉시 부활이 우연히 그 역할을 했다.
        ///
        /// <b>파츠·용접·운석 흔적은 건드리지 않는다.</b> 스테이지 초기화가 아니다
        /// (기획 §22.4 의 발사 실패와 같은 원칙).
        ///
        /// 지금은 로컬 한 명이 전부이므로 "전원" 이 곧 로컬이다. 멀티가 붙으면
        /// 여기서 접속 인원을 세면 된다 — 판정을 이 한 곳에 모아 둔다.
        /// </summary>
        void TickAllDowned(float dt)
        {
            if (!LocalPlayer.Downed)
            {
                allDownedSeconds = 0f;
                return;
            }

            allDownedSeconds += dt;
            if (allDownedSeconds < PhysicsScale.AllDownedRespawnSeconds) return;

            allDownedSeconds = 0f;
            Falls++;
            Respawn();
        }

        public void SetCursor(bool locked)
        {
            CursorLocked = locked;
            if (Application.isBatchMode) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>로컬 메뉴가 열릴 때 플레이어의 다음 입력을 소비하지 않게 한다.</summary>
        public void SetInputSuppressed(bool suppressed)
        {
            InputSuppressed = suppressed;
            if (suppressed && LocalPlayer != null)
                LocalPlayer.SetInput(new InputFrame());
        }

        public void ReachCheckpoint(Checkpoint cp)
        {
            // 체크포인트도 호스트가 정한다 (S15P21B202-54). Checkpoint 의 트리거는
            // 참가자 화면에서도 돌기 때문이다.
            if (ProgressFromHost) return;

            if (cp == null || cp.Index < CheckpointIndex) return;
            CheckpointIndex = cp.Index;
            SpawnFoot = cp.SpawnFoot;
            StageHint = Hints[Mathf.Clamp(cp.Index, 0, Hints.Length - 1)];
            cp.MarkReached();
        }

        /// <summary>
        /// 스폰 지점으로 되돌린다. <b>이제 재시작 전용이다</b>(<see cref="Restart"/>).
        ///
        /// 예전에는 <c>T</c> 가 이것을 직접 불렀다. §21 은 "가까운 안전 지면" 을 요구하고
        /// "파츠, 비행체와 장비를 복구하는 기능은 아니다" 라고 정하는데, 이 함수는
        /// 스폰으로 보내고 <see cref="Transport"/> 를 되돌리고 <see cref="PlayerRig.Revive"/>
        /// 까지 부른다 — 재시작에는 맞고 끼임 탈출에는 전부 어긋난다.
        /// 끼임 탈출은 <see cref="Escape"/> 가 맡는다(S15P21B202-248).
        /// </summary>
        public void Respawn()
        {
            RespawnPlayer(LocalPlayer, Transport);
        }

        /// <summary>
        /// 한 사람을 체크포인트로 되돌린다.
        ///
        /// 혼자 실행할 때는 <see cref="Respawn"/> 이 로컬 플레이어로 부르고, 서버에서는
        /// 접속자마다 부른다(S15P21B202-55). 되돌리는 방법을 두 벌로 두면 한쪽만
        /// 고쳐지므로 여기 한 곳에 모아 둔다.
        /// </summary>
        /// <param name="transport">그 사람이 타고 있던 운반 장치 조작. 내려놓고 초기화한다.</param>
        public void RespawnPlayer(PlayerRig rig, Parts.TractorBeamController transport = null)
        {
            if (rig == null) return;
            if (transport != null) transport.Reset();

            // 기절한 채로 순간이동하면 스폰 지점에 쓰러져 있게 된다.
            rig.Revive(rig.MaxHealth);
            rig.ResetHealth();
            rig.Teleport(SpawnFoot + Vector3.up * 0.15f, Quaternion.LookRotation(Vector3.right, Vector3.up));
        }

        /// <summary>낙사인가. 서버가 접속자마다 같은 기준으로 본다(-55).</summary>
        public static bool HasFallen(PlayerRig rig)
        {
            if (rig == null) return false;
            Vector3 p = rig.Position;
            return p.y < Tuning.DeathHeight || float.IsNaN(p.x);
        }

        public void Clear()
        {
            // 진행도를 호스트가 정할 때는 여기서 물러난다 (S15P21B202-54).
            // Goal 의 트리거는 참가자 화면에서도 돌기 때문에, 막지 않으면 참가자가
            // 자기 시계로 ClearTime 을 정해 버린다.
            if (ProgressFromHost) return;

            if (Cleared) return;
            Cleared = true;
            ClearTime = Elapsed;
            StageHint = "구조 로켓 탑승! R 을 누르면 처음부터 다시";
            Debug.Log("[SpaceFlop] 도착 — " + ClearTime.ToString("0.0") + "초, 추락 " + Falls + "회");
        }

        /// <summary>
        /// 호스트가 정한 진행도를 받아 넣는다 (S15P21B202-54).
        ///
        /// <para>이 함수를 한 번이라도 부르면 <see cref="ProgressFromHost"/> 가 켜지고,
        /// 그 뒤로는 이 클래스가 진행도를 <b>스스로 바꾸지 않는다</b> — 경과 시간을 세지
        /// 않고, <see cref="Clear"/> 와 <see cref="ReachCheckpoint"/> 도 물러난다.</para>
        ///
        /// <para><b>Mirror 를 여기서 참조하지 않는다.</b> 값만 받는다. <c>Metagame.Gameplay</c>
        /// 는 네트워크 라이브러리를 참조하지 않으며(그래서 -145 에서 물리 코드가 한 줄도
        /// 바뀌지 않았다), 그 경계를 이 이슈에서도 지킨다. 부르는 쪽은
        /// <c>Metagame.Network</c> 의 <c>ProgressSync</c> 다.</para>
        ///
        /// <para><see cref="StageHint"/> 도 체크포인트에 맞춰 함께 고친다 — 참가자 화면의
        /// 안내문이 호스트와 달라지지 않게.</para>
        /// </summary>
        public void ApplyHostProgress(int checkpointIndex, bool cleared, float clearTime,
                                      float elapsed, int falls)
        {
            ProgressFromHost = true;

            CheckpointIndex = checkpointIndex;
            Cleared = cleared;
            ClearTime = clearTime;
            Elapsed = elapsed;
            Falls = falls;

            StageHint = cleared
                ? "구조 로켓 탑승! R 을 누르면 처음부터 다시"
                : Hints[Mathf.Clamp(checkpointIndex, 0, Hints.Length - 1)];
        }

        /// <summary>
        /// 다시 로컬이 진행도를 갖게 되돌린다 (S15P21B202-54).
        ///
        /// <para>매치가 끝나거나 연결이 끊겨 호스트에게서 값이 더 오지 않을 때 부른다.
        /// 이것이 없으면 마지막으로 받은 값에 시계가 멈춘 채로 남는다 — 혼자 하기로
        /// 넘어가도 경과 시간이 흐르지 않는다.</para>
        /// </summary>
        public void ClearHostProgress()
        {
            ProgressFromHost = false;
        }

        public void Restart()
        {
            // 재시작도 진행도를 바꾸는 일이다. 호스트가 정할 때는 참가자가 누른 R 로
            // 판이 되돌아가지 않는다 (S15P21B202-54). 막지 않으면 로컬 값이 0으로
            // 되돌아갔다가 다음 수신에 덮여, 눌린 것도 아니고 안 눌린 것도 아닌 상태가 된다.
            if (ProgressFromHost) return;

            CheckpointIndex = 0;
            SpawnFoot = Vector3.zero;
            StageHint = Hints[0];
            Elapsed = 0f;
            Falls = 0;
            Cleared = false;
            InputSuppressed = false;
            if (Exploration != null) Exploration.ResetRun();
            SetCursor(true);
            Respawn();
        }
    }
}
