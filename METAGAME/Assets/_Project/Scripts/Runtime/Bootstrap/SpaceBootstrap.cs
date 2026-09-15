using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Audio;
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
    [RequireComponent(typeof(DevManager))]
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

        [Header("1인칭 손")]
        [Tooltip("카메라 기준 손 위치·각도와 잡기 포즈를 조정한다")]
        public FirstPersonHandView.Settings firstPersonHandSettings = FirstPersonHandView.Settings.Default;

        [Header("우주비행사")]
        [Tooltip("끄면 블렌더 메시 대신 프리미티브 룩으로 돈다 (물리는 완전히 같다)")]
        public bool useModel = true;
        public string nickname = "비행사";
        public Color suit = new Color(0.93f, 0.94f, 0.96f);
        public Color accent = new Color(0.96f, 0.50f, 0.16f);

        [Header("분위기")]
        [Tooltip("지정하면 이 Stage는 Vacuum·레거시 Starfield 대신 공용 URP 분위기 프레임워크를 사용한다.")]
        [SerializeField] AtmosphereProfile atmosphereProfile;

        [Header("오디오")]
        [Tooltip("비워 두면 Resources/Audio/Stage1AudioManager를 사용한다.")]
        [SerializeField] AudioManager audioManager;

        public GameSession Session { get; private set; }
        public LevelBuilder Level { get; private set; }
        public PlayerRig Player { get; private set; }
        public Vacuum Sky { get; private set; }
        public Starfield Space { get; private set; }
        public AtmosphereController Atmosphere { get; private set; }

        /// <summary>수제 맵에서 별만 뿌릴 때 쓰는 배경 (S15P21B202-546).</summary>
        public AtmosphereBackdrop Backdrop { get; private set; }
        public GameHud Hud { get; private set; }
        public GamePauseMenu PauseMenu { get; private set; }
        public TeamPingHud PingHud { get; private set; }

        /// <summary>기절한 동료 일으키기 진행도 (S15P21B202-390).</summary>
        public DownedRescueHud RescueHud { get; private set; }
        public TeamNameplateHud NameplateHud { get; private set; }
        public Stage1Exploration Exploration { get; private set; }
        public Stage1ExplorationHud ExplorationHud { get; private set; }
        public TractorBeamController Transport { get; private set; }
        public SoundRuntime Sound { get; private set; }

        GameSettingsRuntime settingsRuntime;

        void Awake()
        {
            var authoredMap = FindAnyObjectByType<AuthoredMapSettings>();
            bool authoredLighting = authoredMap != null && authoredMap.preserveSceneLighting;
            settingsRuntime = GameSettingsRuntime.Ensure();
            settingsRuntime.Changed += OnSettingsChanged;

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

            // Existing Main scenes may predate the editor collider pass. Repair only static
            // environment mesh surfaces here; moving gameplay objects keep their simple
            // compound colliders, and this also makes headless physics match the client.
            MeshColliderUtility.EnsureStage1EnvironmentColliders();

            var args = System.Environment.GetCommandLineArgs();
            bool selfTest = System.Array.IndexOf(args, "-space-selftest") >= 0;
            bool shot = System.Array.IndexOf(args, "-space-screenshot") >= 0;

            // 헤드리스 점검에서는 먼지를 만들지 않는다 (알갱이 수백 개는 검사할 것이 없다)
            Puff.Enabled = !Application.isBatchMode;

            var sys = new GameObject("~시스템");
            sys.transform.SetParent(transform, false);

            bool useAtmosphere = !authoredLighting && atmosphereProfile != null && !Application.isBatchMode;

            // 수제 맵이라도 **별은 코드가 뿌린다** (S15P21B202-546). 조명·안개·스카이박스는
            // 아트 것을 그대로 둔다 — 갈라 놓지 않으면 문지기 하나가 넷을 한꺼번에 끈다.
            bool authoredStarsOnly = authoredLighting
                                     && authoredMap.useCodeStarfield
                                     && atmosphereProfile != null
                                     && !Application.isBatchMode;
            Stage1DustStormHazard dustStormHazard = null;
            AtmosphereDustStorm dustStormVisual = null;
            if (atmosphereProfile != null)
            {
                // 방장/서버만 매치 시드를 만들고, 참가자는 Mirror 동기화가 올 때까지 기다린다.
                // 그래서 같은 방의 폭풍 물리와 시각 효과가 같은 시퀀스를 사용한다.
                DustStormMatchSeed.PrepareForMatch();
                atmosphereProfile.Validate();
                dustStormHazard = sys.AddComponent<Stage1DustStormHazard>();
                // 수제 맵의 조명을 보존해도 플레이어에게 영향을 주는 폭풍은 보여야 한다.
                if (!Application.isBatchMode && !Metagame.Network.MatchRole.DedicatedServer)
                    dustStormVisual = sys.AddComponent<AtmosphereDustStorm>();
            }

            if (useAtmosphere)
            {
                Atmosphere = sys.AddComponent<AtmosphereController>();
                sys.AddComponent<AtmosphereBackdrop>();
                sys.AddComponent<AtmosphereMeteorShower>();
                sys.AddComponent<AtmosphereDustGust>();
            }
            else if (authoredStarsOnly)
            {
                // 카메라는 아직 없다. `AtmosphereBackdrop.LateUpdate` 가 `Camera.main` 을
                // 찾아 따라가므로 여기서는 넘기지 않는다.
                Backdrop = sys.AddComponent<AtmosphereBackdrop>();
                Backdrop.ApplyStarsOnly(atmosphereProfile);
            }
            else if (!authoredLighting)
            {
                Sky = sys.AddComponent<Vacuum>();
            }

            Session = sys.AddComponent<GameSession>();
            Level = sys.AddComponent<LevelBuilder>();
            Hud = sys.AddComponent<GameHud>();
            PauseMenu = sys.AddComponent<GamePauseMenu>();
            PauseMenu.Initialize(Session);
            PingHud = sys.AddComponent<TeamPingHud>();
            PingHud.Initialize(Session);
            // 팀원 머리 위 이름표 (S15P21B202-375). 핑과 같은 방식이라 옆에 둔다.
            NameplateHud = sys.AddComponent<TeamNameplateHud>();
            NameplateHud.Initialize(Session);
            // 동료 일으키기 진행도 (S15P21B202-390). 핑·이름표와 같은 방식이다 —
            // 판단은 PlayerRig.TickRescue 가 하고 이쪽은 그리기만 한다.
            RescueHud = sys.AddComponent<DownedRescueHud>();
            RescueHud.Initialize(Session);
            // 방장과의 연결이 조용해지면 알리고, 오래 조용하면 로비로 돌려보낸다
            // (S15P21B202-385). 감시와 표시를 나눠 둔다 — 감시는 네트워크, 표시는 UI.
            sys.AddComponent<Metagame.Network.HostLinkWatch>();
            sys.AddComponent<HostNoticeHud>();
            // 방장이 사라지면 남은 사람 중 하나가 이어받는다 (S15P21B202-389).
            // 게임 씬에만 둔다 — 그래서 여기서 받는 GAME_STARTED 는 언제나 이전이다.
            sys.AddComponent<Metagame.Network.HostMigration>();
            Exploration = sys.AddComponent<Stage1Exploration>();
            ExplorationHud = sys.AddComponent<Stage1ExplorationHud>();
            Transport = sys.AddComponent<TractorBeamController>();
            ExplorationHud.Initialize(Exploration, Session);

            if (buildLevel) Level.Build();

            Session.RecoveryFromServer = Mirror.NetworkClient.active || Mirror.NetworkServer.active;
            var recovery = sys.AddComponent<MapRecovery>();
            Session.Recovery = recovery;
            recovery.Configure(Session, DustStormBounds.FindOrFallback(),
                FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None),
                FindObjectsByType<ToolBase>(FindObjectsSortMode.None),
                FindObjectsByType<LaunchTest>(FindObjectsSortMode.None));
            sys.AddComponent<MapRecoveryBridge>().Configure(recovery);

            if (dustStormHazard != null)
            {
                var mapBounds = DustStormBounds.FindOrFallback();
                dustStormHazard.Configure(atmosphereProfile, mapBounds);
                if (dustStormVisual != null)
                {
                    dustStormVisual.BindMapBounds(mapBounds);
                    // 일반 맵은 아래 Atmosphere.Apply가 초기화한다. 수제 맵은 직접 연결한다.
                    if (!useAtmosphere)
                        dustStormVisual.Apply(atmosphereProfile);
                }

                var clockBridge = sys.AddComponent<DustStormClockBridge>();
                clockBridge.Configure(dustStormVisual, dustStormHazard);
            }

            // 운석 공세도 호스트 하나가 정한다 (S15P21B202-352). 위의 모래바람과 같은
            // 모양이다 — Gameplay 는 Mirror 를 모르고, 네트워크 판단은 어댑터가 소유한다.
            //
            // 전용 서버 조기 반환(아래)보다 **먼저** 설치해야 한다: 세계는 서버에서도
            // 돌아야 하고, 운석을 정하는 것이 바로 그 서버다.
            var meteorSpawnerForClock = FindAnyObjectByType<Metagame.Gameplay.Hazards.MeteorSpawner>();
            if (meteorSpawnerForClock != null)
            {
                var meteorBridge = sys.AddComponent<Metagame.Network.MeteorClockBridge>();
                meteorBridge.Configure(meteorSpawnerForClock);
            }

            // 로그 진단 (S15P21B202-352). `-netdiag` 또는 METAGAME_NETDIAG=1 일 때만.
            // GUI 를 눈으로 읽는 대신 양쪽 로그를 기계로 대조해 검증한다.
            if (Metagame.Network.NetDiagnostics.Requested)
                sys.AddComponent<Metagame.Network.NetDiagnostics>();

            Exploration.Configure(FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None));
            DisableDemoClampReleasers();
            DisableDemoBeamDrivers();
            if (!authoredLighting && !useAtmosphere && starfield && !Application.isBatchMode)
                Space = Starfield.Create(transform, Sky.SunDirection);

            var ending = sys.AddComponent<DemoEnding>();
            bool endingNetworked = Mirror.NetworkServer.active || Mirror.NetworkClient.active;
            ending.Configure(Session, FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None),
                () => !endingNetworked || Mirror.NetworkServer.active,
                () => endingNetworked ? Mirror.NetworkTime.time : Time.unscaledTimeAsDouble,
                CaptureEndingPlayers,
                () => NetworkSession.Instance != null && NetworkSession.Instance.CurrentRoom != null
                    ? NetworkSession.Instance.RoomPlayTimeSeconds : Session.Elapsed);
            foreach (var hatch in FindObjectsByType<ShipHatch>(FindObjectsSortMode.None)) hatch.enabled = false;
            foreach (var launch in FindObjectsByType<LaunchTest>(FindObjectsSortMode.None)) launch.enabled = false;
            EndingSceneFlow.Ensure().Watch(ending);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!MatchRole.DedicatedServer)
                GetComponent<DevManager>().Configure(Session, ending, PauseMenu, endingNetworked);
#endif

            var beams = FindObjectsByType<TractorBeam>(FindObjectsSortMode.None);

            // 전용 서버에는 "내 캐릭터" 가 없다. 접속한 사람 수만큼 몸을 세우는 일은
            // NetPlayer 가 하고(-147), 카메라·HUD 는 그릴 화면이 없어 필요 없다.
            // 세계는 세워야 한다 — 그 위에서 물리가 돌아야 하기 때문이다.
            // NetworkClient.active 로 판별하면 안 된다 — 방장(StartHost)도 게임 씬이
            // 뜨는 동안에는 그 값이 거짓이라 전용 서버로 오인된다(MatchRole 주석 참고).
            if (Metagame.Network.MatchRole.DedicatedServer)
            {
                Sound = CreateSoundRuntime(sys, true, true);

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

            Sound = CreateSoundRuntime(sys, !inMatch, inMatch);

            Vector3 spawn = Level.SpawnFoot + Vector3.up * 0.10f;
            Quaternion facing = Quaternion.LookRotation(Vector3.right, Vector3.up);

            Player = PlayerRig.Create(nickname, spawn, facing, suit, accent, useModel);
            PlayerAppearance.Apply(Player.Doll, CustomizationData.Load().Packed);
            Player.transform.SetParent(transform, true);

            Color cameraSkyColor = useAtmosphere ? atmosphereProfile.sky.horizonColor : Sky != null ? Sky.SkyColor : RenderSettings.ambientSkyColor;
            Player.Cam = OrbitCamera.Create(Player.Hips.transform, cameraSkyColor, firstPerson);
            if (settingsRuntime != null && settingsRuntime.Current != null)
                Player.Cam.ApplySettings(settingsRuntime.Current.controls);
            Player.Cam.Yaw = 90f;                        // +X 를 바라보고 시작한다
            Player.Cam.transform.SetParent(transform, true);
            Player.Cam.SnapToTarget();
            FirstPersonHandView.Create(Player, Player.Cam, firstPersonHandSettings);
            ExplorationHud.AttachTablet(Player.Cam.Cam);
            Player.ConfigureClampInteraction(Player.Cam.transform);
            Transport.Configure(Player, beams, randomizeVehicles: !inMatch && (authoredMap == null || authoredMap.randomizeVehicles));

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
            else if (Sky != null) Sky.ApplyToCameras();
            else
            {
                Player.Cam.Cam.clearFlags = CameraClearFlags.Skybox;
                Player.Cam.Cam.farClipPlane = 400f;
                Player.Cam.Cam.allowHDR = true;
                authoredMap.ConfigureCamera(Player.Cam.Cam);
                if (atmosphereProfile != null && !Application.isBatchMode)
                {
                    // 별도 오브젝트에 두어 안개 초기화가 기존 모래폭풍을 해제하지 않게 한다.
                    var fogRoot = new GameObject("Atmosphere Fog");
                    fogRoot.transform.SetParent(sys.transform, false);
                    Atmosphere = fogRoot.AddComponent<AtmosphereController>();
                    Atmosphere.BindCamera(Player.Cam.Cam);
                    Atmosphere.ApplyFogOnly(atmosphereProfile);
                }
            }

            // 낙하 예고 원과 분화구를 그리는 컴포넌트를 붙인다(S15P21B202-275).
            //
            // 이 두 줄이 없어서 S15P21B202-241 · 251 · 268 이 만든 표현이 인게임에서
            // 한 번도 실행되지 않았다. 방망이(§18)가 "낙하 지점 반경 2.5m 안" 을
            // 요구하는데 원이 없으면 어디가 2.5m 인지 알 수 없다.
            //
            // 대기 연출과 달리 useAtmosphere 와 무관하다 — 진공에서도 운석은 떨어진다.
            // 전용 서버는 위에서 이미 돌아나갔으므로 화면 없는 쪽에는 붙지 않는다.
            var meteorSpawner = FindAnyObjectByType<Metagame.Gameplay.Hazards.MeteorSpawner>();
            if (meteorSpawner != null) meteorSpawner.EnsureViews();

            if (selfTest) gameObject.AddComponent<SpaceSelfTest>();
            if (shot) gameObject.AddComponent<SpaceScreenshot>();
        }

        DemoEndingPlayer[] CaptureEndingPlayers()
        {
            var players = new System.Collections.Generic.List<DemoEndingPlayer>();
            if (Mirror.NetworkServer.active)
            {
                foreach (var connection in Mirror.NetworkServer.connections.Values)
                {
                    if (connection == null || connection.identity == null) continue;
                    var player = connection.identity.GetComponent<NetPlayer>();
                    if (player == null) continue;
                    players.Add(new DemoEndingPlayer
                    { Name = player.nickname, Suit = player.TeamSuitColor, Accent = player.TeamAccentColor,
                        Appearance = player.Appearance });
                }
            }
            else if (Session.LocalPlayer != null)
                players.Add(new DemoEndingPlayer { Name = nickname, Suit = suit, Accent = accent,
                    Appearance = CustomizationData.Load().Packed });
            return players.ToArray();
        }

        SoundRuntime CreateSoundRuntime(GameObject systemRoot, bool authority, bool networked)
        {
            var runtime = systemRoot.AddComponent<SoundRuntime>();
            var manager = audioManager != null
                ? audioManager
                : Resources.Load<AudioManager>("Audio/Stage1AudioManager");
            runtime.Configure(manager, authority, networked);
            if (!MatchRole.DedicatedServer && !Application.isBatchMode)
                systemRoot.AddComponent<GameMusicPlayer>().Configure(manager);

            if (networked)
            {
                var bridge = systemRoot.AddComponent<NetworkSoundBridge>();
                bridge.Configure(runtime);
            }

            return runtime;
        }

        void OnSettingsChanged(GameSettingsData settings)
        {
            if (Player != null && Player.Cam != null && settings != null)
                Player.Cam.ApplySettings(settings.controls);
        }

        void OnDestroy()
        {
            if (settingsRuntime != null)
                settingsRuntime.Changed -= OnSettingsChanged;
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
