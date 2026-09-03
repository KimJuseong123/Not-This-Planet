using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Demo;
using Metagame.Gameplay.Parts;
using Metagame.Network;
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

        [Header("카메라")]
        public bool firstPerson = true;

        [Header("우주비행사")]
        [Tooltip("끄면 블렌더 메시 대신 프리미티브 룩으로 돈다 (물리는 완전히 같다)")]
        public bool useModel = true;
        public string nickname = "비행사";
        public Color suit = new Color(0.93f, 0.94f, 0.96f);
        public Color accent = new Color(0.96f, 0.50f, 0.16f);

        [Header("분위기")]
        [Tooltip("지정하면 이 Stage는 Vacuum·레거시 Starfield 대신 공용 URP 분위기 프레임워크를 사용한다.")]
        [SerializeField] AtmosphereProfile atmosphereProfile;

        public GameSession Session { get; private set; }
        public LevelBuilder Level { get; private set; }
        public PlayerRig Player { get; private set; }
        public Vacuum Sky { get; private set; }
        public Starfield Space { get; private set; }
        public AtmosphereController Atmosphere { get; private set; }
        public GameHud Hud { get; private set; }
        public Stage1Exploration Exploration { get; private set; }
        public Stage1ExplorationHud ExplorationHud { get; private set; }
        public TractorBeamController Transport { get; private set; }

        void Awake()
        {
            // SampleScene처럼 기본 Main Camera가 들어 있는 씬에서도 런타임 카메라만 사용한다.
            var sceneCameras = FindObjectsByType<Camera>();
            for (int i = 0; i < sceneCameras.Length; i++)
            {
                if (sceneCameras[i] == null) continue;
                sceneCameras[i].enabled = false;
                var listener = sceneCameras[i].GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }

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

            bool useAtmosphere = atmosphereProfile != null && !Application.isBatchMode;
            if (useAtmosphere)
            {
                atmosphereProfile.Validate();
                Atmosphere = sys.AddComponent<AtmosphereController>();
                sys.AddComponent<AtmosphereBackdrop>();
                sys.AddComponent<AtmosphereMeteorShower>();
                sys.AddComponent<AtmosphereDustGust>();
            }
            else
            {
                Sky = sys.AddComponent<Vacuum>();
            }

            Session = sys.AddComponent<GameSession>();
            Level = sys.AddComponent<LevelBuilder>();
            Hud = sys.AddComponent<GameHud>();
            Exploration = sys.AddComponent<Stage1Exploration>();
            ExplorationHud = sys.AddComponent<Stage1ExplorationHud>();
            Transport = sys.AddComponent<TractorBeamController>();
            ExplorationHud.Initialize(Exploration, Session);

            if (buildLevel) Level.Build();
            Exploration.Configure(FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None));
            DisableDemoClampReleasers();
            DisableDemoBeamDrivers();
            if (!useAtmosphere && starfield && !Application.isBatchMode)
                Space = Starfield.Create(transform, Sky.SunDirection);

            var beams = FindObjectsByType<TractorBeam>(FindObjectsSortMode.None);

            // 전용 서버에는 "내 캐릭터" 가 없다. 접속한 사람 수만큼 몸을 세우는 일은
            // NetPlayer 가 하고(-147), 카메라·HUD 는 그릴 화면이 없어 필요 없다.
            // 세계는 세워야 한다 — 그 위에서 물리가 돌아야 하기 때문이다.
            // NetworkClient.active 로 판별하면 안 된다 — 방장(StartHost)도 게임 씬이
            // 뜨는 동안에는 그 값이 거짓이라 전용 서버로 오인된다(MatchRole 주석 참고).
            if (Metagame.Network.MatchRole.DedicatedServer)
            {
                // 운반체 배치는 서버가 정한다(-152). 위치는 WorldSync 로 내려간다.
                // 조작은 사람마다 다르므로 이 컨트롤러는 배치만 하고 손을 뗀다 —
                // 접속자별 조작은 NetPlayer 가 자기 컨트롤러로 한다(-149).
                Transport.Configure(null, beams);
                Transport.Authoritative = false;
                Transport.PhysicsAuthority = true;
                Session.Transport = Transport;
                Session.Begin(null, Level, Level.SpawnFoot, Exploration);
                return;
            }

            // 매치에 붙은 클라이언트는 운반체를 스스로 섞지 않는다 — 섞으면 서버가
            // 보낸 위치와 한 번 어긋났다가 되돌아가는 것이 눈에 보인다.
            // 혼자 실행할 때는 지금까지처럼 매번 섞는다.
            // 여기도 NetworkClient.active 만 보면 안 된다 — 방장은 씬이 뜨는 동안
            // 그 값이 거짓이라 운반체를 혼자 섞어 버리고, 서버가 보낸 위치와 어긋난다.
            bool inMatch = Mirror.NetworkClient.active || Mirror.NetworkServer.active;

            Vector3 spawn = Level.SpawnFoot + Vector3.up * 0.10f;
            Quaternion facing = Quaternion.LookRotation(Vector3.right, Vector3.up);

            Player = PlayerRig.Create(nickname, spawn, facing, suit, accent, useModel);
            Player.transform.SetParent(transform, true);

            Color cameraSkyColor = useAtmosphere ? atmosphereProfile.sky.horizonColor : Sky.SkyColor;
            Player.Cam = OrbitCamera.Create(Player.Hips.transform, cameraSkyColor, firstPerson);
            Player.Cam.Yaw = 90f;                        // +X 를 바라보고 시작한다
            Player.Cam.transform.SetParent(transform, true);
            Player.Cam.SnapToTarget();
            ExplorationHud.AttachTablet(Player.Cam.Cam);
            Player.ConfigureClampInteraction(Player.Cam.transform);
            Transport.Configure(Player, beams, randomizeVehicles: !inMatch);

            // 매치 중에는 조준·안내만 하고 실행은 서버에 맡긴다(-149).
            Transport.Authoritative = !inMatch;
            Transport.PhysicsAuthority = !inMatch;

            Hud.Local = Player;
            Hud.Transport = Transport;
            Hud.Exploration = Exploration;
            Session.Transport = Transport;
            Exploration.Begin(Player);
            Session.Begin(Player, Level, Level.SpawnFoot, Exploration);
            if (!lockCursor) Session.SetCursor(false);

            // 다른 사람의 몸은 Mirror 가 세운다(NetPlayer). 예전에는 WebSocket 으로
            // 좌표를 받아 여기서 직접 세웠는데(-128), 그대로 두면 아바타가 두 벌 생긴다.

            if (useAtmosphere)
            {
                Atmosphere.BindCamera(Player.Cam.Cam);
                Atmosphere.Apply(atmosphereProfile);

                var atmosphereAlert = sys.AddComponent<Stage1AtmosphereAlert>();
                atmosphereAlert.Configure(Atmosphere, FindAnyObjectByType<Metagame.Gameplay.Hazards.MeteorSpawner>());
            }
            else
            {
                Sky.ApplyToCameras();
            }

            if (selfTest) gameObject.AddComponent<SpaceSelfTest>();
            if (shot) gameObject.AddComponent<SpaceScreenshot>();
        }

        /// <summary>
        /// 입력이 연결된 현재 Stage1에서는 예전 시연용 자동 해제를 사용하지 않는다.
        /// 씬에 남아 있는 시연 컴포넌트와 에디터 도구로 새로 만들어질 컴포넌트 모두
        /// 플레이어의 고정쇠 조작을 방해하지 않도록 런타임에서 차단한다.
        /// </summary>
        static void DisableDemoClampReleasers()
        {
            var demos = FindObjectsByType<DemoClampReleaser>(FindObjectsSortMode.None);
            for (var i = 0; i < demos.Length; i++)
                if (demos[i] != null)
                    demos[i].enabled = false;
        }

        /// <summary>실제 플레이 입력이 연결되면 예전 자동 운반 시연을 끈다.</summary>
        static void DisableDemoBeamDrivers()
        {
            var demos = FindObjectsByType<DemoBeamDriver>(FindObjectsSortMode.None);
            for (var i = 0; i < demos.Length; i++)
                if (demos[i] != null)
                    demos[i].enabled = false;
        }
    }
}
