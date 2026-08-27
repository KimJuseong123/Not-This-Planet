using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.UI;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 빈 씬에 이 컴포넌트 하나만 올려두고 Play 하면 게임이 돈다.
    /// 세계·우주비행사·HUD 는 전부 런타임 코드가 세운다 (프로토타입 단계에서 프리팹 관리 비용 0).
    /// </summary>
    public class SpaceBootstrap : MonoBehaviour
    {
        [Header("세계")]
        public bool buildLevel = true;
        [Tooltip("기본값은 지구의 약 1/3.5. 여기만 바꿔도 게임 성격이 통째로 달라진다")]
        public Vector3 gravity = new Vector3(0f, Tuning.Gravity, 0f);
        public bool lockCursor = true;
        public bool starfield = true;

        [Header("우주비행사")]
        [Tooltip("끄면 블렌더 메시 대신 프리미티브 룩으로 돈다 (물리는 완전히 같다)")]
        public bool useModel = true;
        public string nickname = "비행사";
        public Color suit = new Color(0.93f, 0.94f, 0.96f);
        public Color accent = new Color(0.96f, 0.50f, 0.16f);

        public GameSession Session { get; private set; }
        public LevelBuilder Level { get; private set; }
        public PlayerRig Player { get; private set; }
        public Vacuum Sky { get; private set; }
        public Starfield Space { get; private set; }
        public GameHud Hud { get; private set; }

        void Awake()
        {
            Physics.gravity = gravity;
            Time.fixedDeltaTime = Tuning.FixedStep;      // 조인트가 많다 — 촘촘히 밟아야 안 떤다
            Application.targetFrameRate = 120;

            var args = System.Environment.GetCommandLineArgs();
            bool selfTest = System.Array.IndexOf(args, "-space-selftest") >= 0;
            bool shot = System.Array.IndexOf(args, "-space-screenshot") >= 0;

            // 헤드리스 점검에서는 먼지를 만들지 않는다 (알갱이 수백 개는 검사할 것이 없다)
            Puff.Enabled = !Application.isBatchMode;

            var sys = new GameObject("~시스템");
            sys.transform.SetParent(transform, false);

            Sky = sys.AddComponent<Vacuum>();
            Session = sys.AddComponent<GameSession>();
            Level = sys.AddComponent<LevelBuilder>();
            Hud = sys.AddComponent<GameHud>();

            if (buildLevel) Level.Build();
            if (starfield && !Application.isBatchMode) Space = Starfield.Create(transform, Sky.SunDirection);

            Vector3 spawn = Level.SpawnFoot + Vector3.up * 0.10f;
            Quaternion facing = Quaternion.LookRotation(Vector3.right, Vector3.up);

            Player = PlayerRig.Create(nickname, spawn, facing, suit, accent, useModel);
            Player.transform.SetParent(transform, true);

            Player.Cam = OrbitCamera.Create(Player.Hips.transform, Sky.SkyColor);
            Player.Cam.Yaw = 90f;                        // +X 를 바라보고 시작한다
            Player.Cam.transform.SetParent(transform, true);
            Player.Cam.SnapToTarget();

            Hud.Local = Player;
            Session.Begin(Player, Level, Level.SpawnFoot);
            if (!lockCursor) Session.SetCursor(false);

            Sky.ApplyToCameras();

            if (selfTest) gameObject.AddComponent<SpaceSelfTest>();
            if (shot) gameObject.AddComponent<SpaceScreenshot>();
        }
    }
}
