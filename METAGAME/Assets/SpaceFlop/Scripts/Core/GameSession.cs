using UnityEngine;

namespace SpaceFlop
{
    /// <summary>
    /// 한 판의 진행 상태. 입력을 읽어 인형에게 넘기고, 떨어지면 되돌리고,
    /// 골에 닿으면 판을 끝낸다.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance;

        public PlayerRig LocalPlayer;
        public LevelBuilder Level;

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

        public void Begin(PlayerRig rig, LevelBuilder level, Vector3 spawnFoot)
        {
            LocalPlayer = rig;
            Level = level;
            SpawnFoot = spawnFoot;
            CheckpointIndex = 0;
            Elapsed = 0f;
            Falls = 0;
            Cleared = false;
            SetCursor(true);
        }

        void Update()
        {
            if (LocalPlayer == null) return;

            if (!Cleared) Elapsed += Time.deltaTime;

            if (!ScriptedInput)
            {
                var f = Controls.Read();

                if (Input.GetKeyDown(Controls.FreeCursor)) SetCursor(false);
                if (Input.GetMouseButtonDown(0) && !CursorLocked) SetCursor(true);
                if (!CursorLocked) { f.Look = Vector2.zero; f.GrabLeft = false; f.GrabRight = false; f.FreeLook = false; }

                if (Input.GetKeyDown(Controls.Respawn)) Respawn();
                if (f.Restart) Restart();

                if (LocalPlayer.Cam != null)
                {
                    LocalPlayer.Cam.SetFreeLook(f.FreeLook);     // Steer 보다 먼저 — 켜지는 순간 기준 각도를 얼린다
                    LocalPlayer.Cam.Steer(f.Look);
                }
                LocalPlayer.SetInput(f);
            }

            // 떨어졌는가
            Vector3 p = LocalPlayer.Position;
            if (p.y < Tuning.DeathHeight || float.IsNaN(p.x))
            {
                Falls++;
                Respawn();
            }
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
            if (LocalPlayer == null) return;
            LocalPlayer.Teleport(SpawnFoot + Vector3.up * 0.15f, Quaternion.LookRotation(Vector3.right, Vector3.up));
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
            Respawn();
        }
    }
}
