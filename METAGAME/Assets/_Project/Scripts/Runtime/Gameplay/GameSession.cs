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

        public int CheckpointIndex { get; private set; }
        public Vector3 SpawnFoot { get; private set; }
        public string StageHint { get; private set; }
        public bool Cleared { get; private set; }
        public float ClearTime { get; private set; }
        public float Elapsed { get; private set; }
        public int Falls { get; private set; }
        public bool CursorLocked { get; private set; }

        /// <summary>true 면 사람 입력을 읽지 않는다 (셀프테스트가 직접 넣는다).</summary>
        public bool ScriptedInput;

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
            Level = level;
            Exploration = exploration;
            SpawnFoot = spawnFoot;
            CheckpointIndex = 0;
            Elapsed = 0f;
            Falls = 0;
            Cleared = false;
            if (Exploration != null) Exploration.ResetRun();
            SetCursor(true);
        }

        void Update()
        {
            if (LocalPlayer == null) return;

            if (!Cleared) Elapsed += Time.deltaTime;

            if (Exploration != null) Exploration.Tick();

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
                }

                if (Input.GetKeyDown(Controls.Respawn)) Respawn();
                if (f.Restart) Restart();

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

                if (LocalPlayer.Cam != null)
                {
                    LocalPlayer.Cam.SetFreeLook(f.FreeLook);     // Steer 보다 먼저 — Alt 시점의 pitch 범위를 선택한다
                    bool clampButtons = f.GrabLeft || f.GrabRight;
                    LocalPlayer.Cam.SetClampLiftResistance(
                        LocalPlayer.ClampGrabbed && clampButtons && !f.FreeLook);
                    LocalPlayer.Cam.SetClampLiftPitchLock(
                        LocalPlayer.ClampGrabbed && LocalPlayer.ClampLiftReady
                        && clampButtons && !f.FreeLook);
                    LocalPlayer.Cam.Steer(f.Look);
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

        public void ReachCheckpoint(Checkpoint cp)
        {
            if (cp == null || cp.Index < CheckpointIndex) return;
            CheckpointIndex = cp.Index;
            SpawnFoot = cp.SpawnFoot;
            StageHint = Hints[Mathf.Clamp(cp.Index, 0, Hints.Length - 1)];
            cp.MarkReached();
        }

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
            if (Cleared) return;
            Cleared = true;
            ClearTime = Elapsed;
            StageHint = "구조 로켓 탑승! R 을 누르면 처음부터 다시";
            Debug.Log("[SpaceFlop] 도착 — " + ClearTime.ToString("0.0") + "초, 추락 " + Falls + "회");
        }

        public void Restart()
        {
            CheckpointIndex = 0;
            SpawnFoot = Vector3.zero;
            StageHint = Hints[0];
            Elapsed = 0f;
            Falls = 0;
            Cleared = false;
            if (Exploration != null) Exploration.ResetRun();
            SetCursor(true);
            Respawn();
        }
    }
}
