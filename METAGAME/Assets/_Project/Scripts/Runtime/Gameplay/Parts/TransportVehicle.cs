using System;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// Stage1의 TractorCraft 탑승체.
    ///
    /// TractorBeam 루트는 기존 에셋의 아래 방향 축을 그대로 유지해야 하므로, 빔 기준점의
    /// 회전은 건드리지 않는다. 대신 바닥 충돌판과 좌석은 월드의 위쪽을 바라보는 자식으로
    /// 만들고, 루트 Rigidbody를 함께 움직인다. 이 분리가 있어 에셋을 회전시켰을 때
    /// “위로 떠야 할 운반 장치가 옆으로 눕는” 문제가 생기지 않는다.
    ///
    /// <b>하차 판정은 절대 높이가 아니라 실제 접촉으로 한다.</b>
    /// 예전에는 루트가 <see cref="GroundY"/>(0.06m)에 도달했는지로 판정했다. 그래서
    /// 절벽 벽이나 파츠 위에 내려앉으면 콜라이더가 받쳐 그 높이에 영원히 닿지 못하고
    /// 하차가 완료되지 않았다. 또 낙하를 중력에 맡기지 않고 목표 높이까지 속도를 직접
    /// 대입했기 때문에, 접촉을 무시하고 지면을 파고들어 PhysX 가 튕겨내면서 장치가
    /// 땅밑으로 빠지고 탑승자가 날아갔다.
    ///
    /// 지금은 플랫폼 아랫면을 아래로 훑어(<see cref="ProbeGround"/>) 무엇이든 닿으면
    /// 하차가 완료된다. 낙하는 중력이 하고 이 코드는 하강 속도 상한과 수평 감속만 준다.
    /// 어떤 이유로든 닿지 못하면 <see cref="DismountTimeout"/> 뒤에 강제로 내린다 —
    /// 갇혀서 T(탈출)를 눌러야 하는 상태를 만들지 않는다.
    ///
    /// <b>부양 고도는 조종하는 사람이 정한다.</b>
    /// 예전에는 목표가 <see cref="HoverRootY"/>(5m) 하나로 고정이라 스프링이 항상 그
    /// 높이로 되돌렸다. 지금은 <see cref="TargetAltitude"/> 를 입력으로 올리고 내린다.
    /// 아래로는 <see cref="ProbeGround"/> 가 닿는 곳에서 멈추고(파고들지 않는다),
    /// 위로는 <see cref="MaxAltitude"/> 까지만 간다 — 경계 벽 콜라이더(9m)를 넘어
    /// 아레나를 빠져나가지 못하게 하는 값이다.
    ///
    /// <b>탑승자는 기체 안에 앉고 화면에서 감춰진다.</b>
    /// 좌석 높이를 상수로 두면 모델이 바뀔 때마다 어긋나므로, Visual 의 실제 렌더러 경계를
    /// 재서 눈높이가 기체 중심에 오도록 맞춘다(<see cref="ResolveSeatHeight"/>).
    /// 탑승 중임을 화면으로 알리는 조종석은 <see cref="EnsureCockpit"/> 가 임시로 세운다 —
    /// 실제 에셋이 들어오면 그 메서드만 갈아끼우면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TransportVehicle : MonoBehaviour
    {
        public static event Action<TransportVehicle> AnyActivated;
        public static event Action<TransportVehicle, PlayerRig> AnyBoarded;
        public static event Action<TransportVehicle, PlayerRig> AnyDismounted;

        public const float GroundY = 0.06f;
        public const float HoverRootY = 5.0f;
        public const float BeamHoldDistance = 3.1f;
        public const float BoardRadius = 4.2f;
        public const float InteractionFocusRange = 34f;
        public const float InteractionFocusHalfAngle = 38f;
        public const float InteractionFocusHeight = 1.2f;

        const float PlatformHeight = 0.22f;
        const float VisualSurfaceGap = 0.02f;
        const float SeatHeight = 0.43f;
        const float Acceleration = 16f;
        const float Braking = 12f;
        const float MaxSpeed = 8f;
        const float HoverSpring = 6f;
        /// <summary>기획 §14.1 "상승·하강 5m/s".</summary>
        const float MaxVerticalSpeed = 5f;
        const float ArenaLimit = 21f;

        // --- 고도 조작 ---
        /// <summary>입력을 쥐고 있을 때 목표 고도가 움직이는 속도(m/s).</summary>
        const float AltitudeRate = 3.5f;

        /// <summary>
        /// 올라갈 수 있는 최고 고도.
        /// 아레나 경계 벽에 붙여 둔 보이지 않는 콜라이더가 9m 다(docs/asset-catalog.md §10).
        /// 그보다 낮게 잡아 벽을 넘어 밖으로 나가지 못하게 한다.
        /// </summary>
        const float MaxAltitude = 8f;

        /// <summary>목표 고도가 이 안에 들어오면 부양이 안정된 것으로 본다.</summary>
        const float HoverSettleBand = 0.4f;

        /// <summary>상승 Cast가 장애물까지 남겨 두는 최소 여유.</summary>
        const float VerticalClearance = 0.06f;

        // --- 임시 조종석 ---
        // 실제 조종석 에셋이 들어오기 전까지 쓰는 대체물이다. Prim 으로 세우므로 새 에셋이
        // 필요 없고, 교체 지점은 EnsureCockpit 한 곳이다.
        //
        // 유리창과 창틀은 두지 않는다. 반투명 판을 눈앞에 두면 시야만 흐려지고, "타고 있다" 는
        // 느낌은 조종대만으로 충분하다. 실제 조종석 에셋이 들어오면 유리까지 그 모델이
        // 갖고 있을 것이므로 임시 대체물에서 흉내낼 이유가 없다.
        //
        // 배치는 카메라 화각을 기준으로 잡았다. OrbitCamera 의 수직 화각이 64도(반각 32도)라
        // 눈에서 d 만큼 앞의 물체는 d*tan(32도) 안쪽에 있어야 화면에 들어온다. 이 계산을
        // 빼먹어 처음에는 조종대가 50도 아래에 놓여 아예 보이지 않았다.

        /// <summary>
        /// 조종대. 눈 아래 앞쪽에 작게 둔다.
        /// 0.78m 앞에서 폭 0.62m 면 화면 폭의 약 45%, 0.40m 아래면 약 27도 — 화면 아래 띠에 앉는다.
        /// </summary>
        static readonly Vector3 ConsoleSize = new(0.62f, 0.06f, 0.22f);
        const float ConsoleForward = 0.78f;
        const float ConsoleDrop = 0.40f;

        /// <summary>조종대 위의 계기 화면.</summary>
        static readonly Vector3 ScreenSize = new(0.26f, 0.02f, 0.16f);
        const float ScreenForward = 0.76f;
        const float ScreenDrop = 0.37f;
        const float ScreenTiltDegrees = 28f;

        /// <summary>기체 높이에서 눈이 오는 비율. 0.5 는 중심, 1 은 천장이다.</summary>
        const float EyeHeightRatio = 0.62f;

        static readonly Color ConsoleColor = new(0.58f, 0.62f, 0.68f);
        static readonly Color ScreenColor = new(0.30f, 0.72f, 0.85f);

        // --- 하차 접지 판정 ---
        /// <summary>플랫폼 아랫면을 이만큼 띄운 곳에서 아래로 훑는다. 처음부터 겹쳐 있으면 BoxCast 가 놓친다.</summary>
        const float ProbeLift = 0.18f;

        /// <summary>아랫면에서 이 거리 안에 표면이 있으면 닿은 것으로 본다.</summary>
        const float ProbeReach = 0.10f;

        /// <summary>훑는 박스를 옆으로 조금 줄인다. 벽에 붙어 있을 때 옆면이 바닥으로 잡히는 것을 막는다.</summary>
        const float ProbeShrink = 0.05f;

        /// <summary>이보다 완만한 면만 바닥으로 인정한다(약 60도). 벽에 스치는 것을 착지로 보지 않는다.</summary>
        const float MinGroundNormalY = 0.5f;

        /// <summary>수직 속도가 이 아래면 멈춰 앉은 것으로 본다.</summary>
        const float LandedVerticalSpeed = 0.4f;

        /// <summary>하강 속도 상한. 이보다 빠르게 박으면 콜라이더 밀림이 커진다.</summary>
        const float MaxDescentSpeed = 6f;

        /// <summary>
        /// 이 시간을 넘기면 닿지 않았어도 내려 준다.
        /// 구덩이 위나 좁은 틈에 걸려 영구히 하차 대기 상태가 되는 것을 막는 안전장치다.
        /// </summary>
        const float DismountTimeout = 6f;

        // --- 하차 지점 ---
        /// <summary>플랫폼 중심에서 정면으로 이만큼 나간 곳에 내려놓는다. 플랫폼 절반 깊이(1.1m) + 사람 여유.</summary>
        const float ExitDistance = 1.8f;

        /// <summary>하차 지점을 찾을 때 위아래로 훑는 범위.</summary>
        const float ExitProbeUp = 1.6f;
        const float ExitProbeDown = 4.0f;

        /// <summary>하차 지점 탐색 방향 수. 정면부터 좌우로 45도씩 벌려 여덟 방향을 본다.</summary>
        const int ExitDirectionCount = 8;
        const float ExitAngleStep = 45f;

        /// <summary>한 방향에서 막히면 이만큼 더 멀리도 본다.</summary>
        const float ExitDistanceStep = 0.8f;

        /// <summary>
        /// 플랫폼 윗면보다 이만큼 높은 표면에는 세우지 않는다. 없으면 옆에 벽이 있을 때
        /// 벽 <b>위</b>를 하차 지점으로 골라 탑승자를 9m 경계벽 꼭대기로 올려 보낸다.
        /// </summary>
        const float MaxExitRise = 1.0f;

        /// <summary>마지막 수단에서 껍데기 윗면 위로 띄우는 여유.</summary>
        const float LastResortLift = 0.05f;

        /// <summary>탑승자가 설 자리가 있는지 볼 때 쓰는 몸 치수. 헬멧 지름 0.636m 와 키 1.147m 기준.</summary>
        const float RiderRadius = 0.34f;
        const float RiderHeight = 1.15f;

        Rigidbody body;
        BoxCollider platform;
        Transform platformAnchor;
        Transform seat;
        Transform visual;
        Transform cockpit;
        TractorBeam beam;
        LowGravityField field;
        bool fieldPhysicsAuthority = true;
        PlayerRig rider;
        Vector3 driveVector;
        Vector3 facingDirection = Vector3.forward;
        float verticalInput;
        float targetAltitude = HoverRootY;
        bool returning;
        bool grounded = true;
        float dismountElapsed;

        /// <summary>접지·하차 지점 판정용. BoxCast 결과를 담아 GC 를 만들지 않는다.</summary>
        static readonly RaycastHit[] ProbeHits = new RaycastHit[8];
        static readonly Collider[] OverlapHits = new Collider[16];

        /// <summary>Unity 기본 레이어. "쿼리에 걸리지 않겠다"는 뜻이라 바닥으로 보지 않는다.</summary>
        const int IgnoreRaycastLayer = 2;

        /// <summary>
        /// 바닥으로 인정할 레이어. 사람과 트리거, 운석, Ignore Raycast 는 뺀다.
        /// 지면과 절벽 벽은 Default, 우주선 파츠는 Part 레이어다. 자기 자신은 Prop 이라
        /// 레이어로 걸러지지 않으므로 <see cref="ProbeGround"/> 에서 계층으로 걸러낸다.
        ///
        /// 화이트리스트가 아니라 제외 목록인 이유는 "무엇에든 닿으면 내린다"가 이 수정의
        /// 핵심이기 때문이다. 앞으로 추가되는 지형 레이어가 자동으로 바닥이 되는 편이
        /// 빠뜨려서 다시 못 내리는 것보다 안전하다. 다만 Ignore Raycast 처럼 의미가
        /// 분명한 레이어는 명시적으로 뺀다.
        /// </summary>
        const int SurfaceMask = ~((1 << GameLayers.Player) | (1 << GameLayers.PlayerDowned)
                                  | (1 << GameLayers.Interactable) | (1 << GameLayers.Meteor)
                                  | (1 << GameLayers.BeamQuery) | (1 << IgnoreRaycastLayer));

        public Rigidbody Body => body;
        public TractorBeam Beam => beam;

        /// <summary>
        /// 기체 아래에 펼쳐지는 광역 저중력장(기획 §14.3). 빔과 별개다 —
        /// 빔은 파츠 하나를 정밀하게 놓고, 이쪽은 영역 안의 것들을 띄운다.
        /// </summary>
        public LowGravityField Field
        {
            get
            {
                if (field == null)
                {
                    field = GetComponent<LowGravityField>();
                    if (field == null) field = gameObject.AddComponent<LowGravityField>();
                }

                field.Configure(beam != null ? beam.BeamOrigin : transform, body);
                field.SetPhysicsAuthority(fieldPhysicsAuthority);

                return field;
            }
        }
        public PlayerRig Rider => rider;
        public bool IsOccupied => rider != null;
        public bool IsReturning => returning;
        public bool IsGrounded => grounded;
        public bool VerticalBlocked { get; private set; }

        /// <summary>
        /// Sets whether this vehicle's low-gravity field may apply authoritative forces.
        /// The value is retained before the field is lazily created so network clients
        /// cannot briefly simulate a server-owned field during initialization.
        /// </summary>
        public void SetFieldPhysicsAuthority(bool authority)
        {
            fieldPhysicsAuthority = authority;
            if (field != null) field.SetPhysicsAuthority(authority);
        }

        /// <summary>
        /// 플랫폼 아랫면이 지금 무엇에든 닿아 있는지. 하차 완료 판정과 같은 검사다.
        /// 절대 높이를 보지 않으므로 절벽 위든 파츠 위든 같게 동작한다.
        /// </summary>
        public bool IsTouchingSurface => ProbeGround(out _);
        /// <summary>
        /// 목표 고도에 올라와 안정됐는지. 견인을 걸 수 있는 상태인지 판정하는 데 쓴다.
        /// 고정 높이(5m)가 아니라 지금 목표로 하는 고도를 본다 — 그래야 낮은 곳이나
        /// 높은 곳에서도 견인이 된다.
        /// </summary>
        /// <summary>
        /// 조종사 없이 호버를 유지하는 중인가(기획 §14.2).
        ///
        /// 저중력장을 켠 채 하차하면 여기로 들어온다. 조종 입력은 받지 않고 고도만
        /// 지킨다 — 그래야 아래에서 용접하는 동안 장이 유지된다(§14.5).
        /// </summary>
        public bool Unmanned { get; private set; }

        public bool IsHovering => (rider != null || Unmanned) && !returning && body != null
                                  && Mathf.Abs(body.position.y - targetAltitude) < HoverSettleBand;

        /// <summary>지금 유지하려는 고도. 탑승하면 <see cref="HoverRootY"/> 에서 시작한다.</summary>
        public float TargetAltitude => targetAltitude;
        public Vector3 SeatFootPosition => seat != null ? seat.position : transform.position + Vector3.up * SeatHeight;
        /// <summary>
        /// 하차한 탑승자를 세울 발 위치.
        ///
        /// 예전에는 운반체 중심의 x·z 에 절대 높이를 돌려줬다. 그러면 플랫폼 콜라이더
        /// (2.4 x 0.34 x 2.2) 안에 사람을 꽂는 셈이라 PhysX 가 밀어내면서 날아갔다.
        /// 지금은 <see cref="TryFindExitFoot"/> 로 정면 바깥의 설 자리를 찾는다.
        /// </summary>
        public Vector3 GroundExitFootPosition
        {
            get { return TryFindExitFoot(out var foot) ? foot : LastResortFoot(); }
        }

        /// <summary>운반체가 바라보는 수평 방향. 요는 Visual 자식이 들고 있다(루트는 빔 축이라 아래를 본다).</summary>
        public Vector3 ExitForward
        {
            get
            {
                var forward = visual != null ? visual.forward : facingDirection;
                forward.y = 0f;
                return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
            }
        }
        public Vector3 HorizontalVelocity
        {
            get
            {
                if (body == null) return Vector3.zero;
                return new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            }
        }
        public float HorizontalSpeed => HorizontalVelocity.magnitude;

        void Awake()
        {
            EnsurePhysics();
        }

        public void Initialize(TractorBeam source, float holdDistance = BeamHoldDistance)
        {
            beam = source != null ? source : GetComponent<TractorBeam>();
            EnsurePhysics();

            if (beam != null)
                beam.Configure(transform, beam.Range, holdDistance);

            if (visual == null)
            {
                var children = GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < children.Length; i++)
                {
                    if (children[i] != transform && children[i].name == "Visual")
                    {
                        visual = children[i];
                        break;
                    }
                }
            }

            if (visual != null) MeshVisual.EnsureRenderers(visual.gameObject);
            RefreshSeat();
        }

        /// <summary>새로 활성화된 운반체를 각 플레이어 컨트롤러에 알린다.</summary>
        internal void NotifyActivated() => AnyActivated?.Invoke(this);

        void EnsurePhysics()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            body.mass = 80f;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            gameObject.layer = GameLayers.Prop;

            if (platformAnchor == null)
            {
                var existing = transform.Find("VehiclePlatform");
                if (existing != null) platformAnchor = existing;
                else
                {
                    var go = new GameObject("VehiclePlatform");
                    platformAnchor = go.transform;
                    platformAnchor.SetParent(transform, false);
                }
            }

            // The parent root faces down for the beam. Set world rotation explicitly so this
            // compound collider remains a horizontal floor for the rider.
            platformAnchor.position = transform.position + Vector3.up * PlatformHeight;
            platformAnchor.rotation = Quaternion.identity;
            platformAnchor.gameObject.layer = GameLayers.Prop;

            if (platform == null) platform = platformAnchor.GetComponent<BoxCollider>();
            if (platform == null) platform = platformAnchor.gameObject.AddComponent<BoxCollider>();
            platform.center = Vector3.zero;
            platform.size = new Vector3(2.4f, 0.34f, 2.2f);
            platform.isTrigger = false;

            if (seat == null)
            {
                var existing = transform.Find("VehicleSeat");
                if (existing != null) seat = existing;
                else
                {
                    var go = new GameObject("VehicleSeat");
                    seat = go.transform;
                    seat.SetParent(transform, false);
                }
            }

            // 좌석 높이는 기체 경계를 보고 정한다. 여기서 상수를 그대로 쓰면 PlaceOnGround 가
            // EnsurePhysics 를 다시 부를 때마다 RefreshSeat 의 결과가 지워진다.
            seat.position = transform.position + Vector3.up * ResolveSeatHeight();
            seat.rotation = Quaternion.identity;
            seat.gameObject.layer = GameLayers.Prop;
        }

        /// <summary>탑승하지 않은 장치를 맵의 바닥으로 되돌린다.</summary>
        public void PlaceOnGround(Vector3 position, float visualYaw)
        {
            var rootRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var groundPosition = new Vector3(position.x, GroundY, position.z);
            if (body == null) body = GetComponent<Rigidbody>();
            body.position = groundPosition;
            body.rotation = rootRotation;
            transform.SetPositionAndRotation(groundPosition, rootRotation);

            // 플랫폼과 좌석은 루트가 아래를 보는 운반체 축을 기준으로 만든다. Part_1처럼
            // 변환 전 루트가 일반 파츠 축을 가지고 있는 경우, 루트 자세를 먼저 바꾸지
            // 않으면 자식의 월드 회전이 함께 틀어진다.
            EnsurePhysics();

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = true;
            driveVector = Vector3.zero;
            returning = false;
            grounded = true;
            dismountElapsed = 0f;
            verticalInput = 0f;
            targetAltitude = HoverRootY;

            facingDirection = Quaternion.Euler(0f, visualYaw, 0f) * Vector3.forward;
            if (visual != null)
            {
                visual.rotation = Quaternion.Euler(0f, visualYaw, 0f);
                AlignVisualToSurface(GroundY);
            }
            RefreshSeat();
        }

        /// <summary>
        /// 실제 모델의 최저 렌더러 경계를 기준면에 맞춘다.
        /// 운반 장치 루트는 빔 축을 위한 기준점이라 모델 중심을 그대로 맞추면
        /// 큰 비행체의 절반이 바닥 아래로 내려갈 수 있다.
        /// </summary>
        public void AlignVisualToSurface(float surfaceY)
        {
            if (visual == null)
            {
                var children = GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < children.Length; i++)
                {
                    if (children[i] != transform && children[i].name == "Visual")
                    {
                        visual = children[i];
                        break;
                    }
                }
            }

            if (visual == null) return;
            MeshVisual.EnsureRenderers(visual.gameObject);
            if (!TryVisualAndColliderBounds(out var bounds)) return;

            var delta = surfaceY + VisualSurfaceGap - bounds.min.y;
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return;

            visual.position += Vector3.up * delta;
            Physics.SyncTransforms();
            RefreshSeat();
        }

        /// <summary>외형 계층의 Renderer와 Collider를 함께 재서 실제 최저면을 구한다.</summary>
        bool TryVisualAndColliderBounds(out Bounds bounds)
        {
            bounds = default;
            var first = true;

            MeshVisual.EnsureRenderers(visual.gameObject);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer
                    || (cockpit != null && renderer.transform.IsChildOf(cockpit))) continue;

                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            var colliders = visual.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled
                    || (cockpit != null && collider.transform.IsChildOf(cockpit))) continue;

                if (first) { bounds = collider.bounds; first = false; }
                else bounds.Encapsulate(collider.bounds);
            }

            return !first;
        }

        public bool TryBoard(PlayerRig player)
        {
            if (player == null || rider != null || returning) return false;

            if (!CanBoardFrom(player)) return false;

            body.useGravity = false;
            grounded = false;
            verticalInput = 0f;

            // 기획 §14.2 — "다른 플레이어가 … 탑승해 조종을 이어받을 수 있다".
            // 이어받는 것이므로 지금 높이가 기준이다. HoverRootY 로 되돌리면 새로 탄
            // 사람이 기체를 5m 로 끌어올리게 된다.
            if (Unmanned)
            {
                Unmanned = false;
            }
            else
            {
                body.linearVelocity = Vector3.zero;
                targetAltitude = HoverRootY;
            }

            // 좌석은 기체 경계를 보고 정하므로 태우기 전에 갱신한다 —
            // PlayerRig 가 SeatFootPosition 을 읽어 인형을 옮긴다.
            RefreshSeat();

            if (!player.TryEnterTransportVehicle(this))
            {
                body.useGravity = true;
                grounded = true;
                return false;
            }

            rider = player;
            SetCockpitVisible(true);
            AnyBoarded?.Invoke(this, player);
            return true;
        }

        /// <summary>운반 장치에 탑승할 수 있는 수평 거리 안에 있는지 확인한다.</summary>
        public bool CanBoardFrom(PlayerRig player)
        {
            if (player == null) return false;

            var offset = player.Position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= BoardRadius * BoardRadius;
        }

        public void BeginDismount()
        {
            if (rider == null || returning) return;
            driveVector = Vector3.zero;
            verticalInput = 0f;
            dismountElapsed = 0f;

            // 기획 §14.2 — "저중력장을 켠 채 하차해도 기체가 마지막 위치와 높이에서
            // 자동 호버한다". 그래야 §14.5 의 "조종사가 저중력장을 유지해 아래의 용접
            // 담당자를 띄운다" 가 성립한다. 예전에는 하차하면 무조건 중력을 켜서
            // 기체가 떨어졌고, 내리는 순간 저중력장이 사라졌다.
            if (Field != null && Field.IsActive)
            {
                Unmanned = true;
                returning = false;
                grounded = false;

                // 마지막 높이를 그대로 지킨다. HoverRootY 로 되돌리면 "마지막 높이" 가 아니다.
                targetAltitude = Mathf.Clamp(body.position.y, GroundY, MaxAltitude);

                CompleteDismount();
                return;
            }

            returning = true;
            grounded = false;

            // 낙하는 중력에 맡긴다. 속도를 직접 대입해 목표 높이로 밀어붙이면 콜라이더
            // 접촉이 무시되고 지면을 파고든다.
            body.useGravity = true;
        }

        /// <summary>
        /// 가속(Shift)을 누르고 있는가. 기획 §14.1 "가속 12m/s".
        /// </summary>
        public bool Boosting { get; private set; }

        public void SetBoost(bool boosting) => Boosting = boosting;

        /// <summary>
        /// 지금 쓸 수 있는 최고 수평 속도(m/s).
        ///
        /// 기획 §14.1 — 일반 8, 가속 12, "파츠 견인 중 최고 속도는 약 20% 감소".
        /// 견인 여부는 빔이 무언가를 잡고 있는지로 본다. 저중력장은 파츠를 끌지만
        /// 기체가 끌고 다니는 것이 아니므로 여기서 세지 않는다.
        /// </summary>
        public float CurrentMaxSpeed
        {
            get
            {
                var top = Boosting ? PhysicsScale.CraftBoostSpeed : MaxSpeed;
                var towing = (beam != null && beam.IsHolding)
                             || (field != null && field.IsActive && field.AffectedCount > 0);
                return towing ? top * PhysicsScale.CraftTowSpeedFactor : top;
            }
        }

        public void SetDriveVector(Vector3 worldDirection)
        {
            if (rider == null || returning) return;
            worldDirection.y = 0f;
            driveVector = worldDirection.sqrMagnitude > 1f
                ? worldDirection.normalized
                : worldDirection;
        }

        /// <summary>
        /// 탑승자의 카메라가 향하는 수평 방향으로 기체 Visual을 정렬한다.
        /// 이동 벡터와 분리된 명령이므로 A/D는 선체 위치만 옮기고 조종석 방향을 바꾸지 않는다.
        /// </summary>
        public void SetFacingDirection(Vector3 worldDirection)
        {
            if (rider == null || returning) return;

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f) return;

            facingDirection = worldDirection.normalized;
            if (visual != null)
                visual.rotation = Quaternion.LookRotation(facingDirection, Vector3.up);
        }

        /// <summary>
        /// 고도 입력. +1 이 상승, -1 이 하강, 0 이면 지금 고도를 유지한다.
        /// 탑승 중에는 <see cref="PlayerRig"/> 가 팔·점프를 돌리지 않으므로
        /// Ctrl·Space 를 여기에 그대로 쓸 수 있다.
        /// </summary>
        public void SetVerticalInput(float value)
        {
            if (rider == null || returning) { verticalInput = 0f; return; }
            verticalInput = Mathf.Clamp(value, -1f, 1f);
        }

        /// <summary>부활 때 탑승체와 플레이어를 함께 안전한 바닥 상태로 돌린다.</summary>
        public void ForceReset()
        {
            var currentPosition = transform.position;
            PlaceOnGround(currentPosition, 0f);

            SetCockpitVisible(false);

            var currentRider = rider;
            rider = null;
            returning = false;
            if (currentRider != null)
                currentRider.ForceExitTransportVehicle(this);
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>
        /// 물리 스텝 한 번. 평소에는 FixedUpdate 가 부른다.
        ///
        /// <see cref="Hazards.MeteorSpawner.Tick"/> 와 같은 이유로 공개해 둔다 — Time 을 직접
        /// 읽지 않으므로 EditMode 테스트가 <c>Physics.Simulate</c> 와 함께 하강 전체를
        /// 돌려 볼 수 있다. 하차가 실제로 완료되는지는 이렇게만 검증할 수 있다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (rider == null && !Unmanned) return;

            if (returning) DriveToGround(deltaTime);
            else DriveHover(deltaTime);
        }

        void DriveHover(float deltaTime)
        {
            var velocity = body.linearVelocity;
            var planar = new Vector3(velocity.x, 0f, velocity.z);
            var target = driveVector * CurrentMaxSpeed;
            var rate = driveVector.sqrMagnitude > 0.001f ? Acceleration : Braking;
            planar = Vector3.MoveTowards(planar, target, rate * deltaTime);

            var position = body.position;
            if ((position.x <= -ArenaLimit && planar.x < 0f) || (position.x >= ArenaLimit && planar.x > 0f))
                planar.x = 0f;
            if ((position.z <= -ArenaLimit && planar.z < 0f) || (position.z >= ArenaLimit && planar.z > 0f))
                planar.z = 0f;
            position.x = Mathf.Clamp(position.x, -ArenaLimit, ArenaLimit);
            position.z = Mathf.Clamp(position.z, -ArenaLimit, ArenaLimit);
            body.position = position;

            AdvanceTargetAltitude(deltaTime);

            // 기획 §14.2 — "호버 위치를 자동으로 되찾지는 않는다".
            // 밀려나 높이가 크게 어긋났으면 되돌리는 대신 그 높이를 새 목표로 삼는다.
            // 그러지 않으면 운석에 맞아 눌린 기체가 스스로 원래 높이로 올라간다.
            //
            // <b>무인 호버에만 적용한다.</b> 조종 중에는 스프링이 목표 고도를 따라가는
            // 지연 자체가 이 조건에 걸린다 — 상승 명령을 준 직후에는 실제 높이가 목표보다
            // 한참 아래이므로, 여기서 목표를 지금 높이로 끌어내리면 상승이 취소된다.
            // (Ascend_RaisesTargetAltitude_AndHoldsAfterRelease 가 그것을 잡아 주었다.)
            // §14.2 는 호버 유지 절이고, 조종 중 위치 복구는 조종사가 한다.
            if (Unmanned
                && Mathf.Abs(body.position.y - targetAltitude) > PhysicsScale.CraftHoverDriftBand)
                targetAltitude = Mathf.Clamp(body.position.y, GroundY, MaxAltitude);

            var vertical = Mathf.Clamp((targetAltitude - body.position.y) * HoverSpring,
                                       -MaxVerticalSpeed, MaxVerticalSpeed);
            body.linearVelocity = new Vector3(planar.x, vertical, planar.z);

            // 기획 §14.1 "충돌 후 기체는 자동으로 수평을 회복한다" 는 이미 성립한다 —
            // EnsurePhysics 가 RigidbodyConstraints.FreezeRotation 을 걸어 기체가 애초에
            // 기울지 않는다. 자세 복구 코드를 넣어 봤지만 회전이 잠겨 있어 아무 일도
            // 하지 않았다(S15P21B202-216 에서 확인).
            //
            // 실제로 기울었다 돌아오게 하려면 회전 잠금을 풀어야 하는데, 그러면 탑승자가
            // 서 있는 플랫폼이 함께 기울고 조인트로 붙은 인형이 흔들린다. 별 결정이다.
            body.angularVelocity = Vector3.zero;

        }


        /// <summary>
        /// 하차 하강. 수평만 세우고 낙하는 중력이 한다. 아랫면이 무엇에든 닿으면 완료다.
        /// </summary>
        void DriveToGround(float deltaTime)
        {
            dismountElapsed += deltaTime;

            var velocity = body.linearVelocity;
            var planar = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z),
                                              Vector3.zero, Braking * deltaTime);
            body.linearVelocity = new Vector3(planar.x, Mathf.Max(velocity.y, -MaxDescentSpeed), planar.z);
            body.angularVelocity = Vector3.zero;

            grounded = ProbeGround(out _);

            // 닿은 뒤에도 아래로 밀면 콜라이더를 파고들었다 튕겨 나온다. 탑승자가 조인트로
            // 붙어 있는 동안 그 진동이 그대로 전달되므로, 닿는 순간 하강을 끊는다.
            if (grounded && body.linearVelocity.y < 0f)
            {
                var settling = body.linearVelocity;
                body.linearVelocity = new Vector3(settling.x, 0f, settling.z);
            }

            var settled = grounded && Mathf.Abs(body.linearVelocity.y) < LandedVerticalSpeed;
            var timedOut = dismountElapsed >= DismountTimeout;
            if (!settled && !timedOut) return;

            if (!settled)
                Debug.LogWarning($"[TransportVehicle] 하차가 {DismountTimeout}초 안에 접지하지 못해 그대로 내린다. "
                                 + $"위치 {body.position}");

            CompleteDismount();
        }

        void CompleteDismount()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = true;
            returning = false;
            dismountElapsed = 0f;

            // 타임아웃으로 들어오면 닿지 않은 채로 끝난다. 그때도 IsGrounded 가 실제 상태를
            // 말해야 한다 — 무조건 true 로 두면 공중에 뜬 장치를 접지로 보고하게 된다.
            grounded = ProbeGround(out _);
            SetCockpitVisible(false);

            var currentRider = rider;
            rider = null;
            if (currentRider != null)
            {
                currentRider.CompleteTransportVehicleExit(this);
                AnyDismounted?.Invoke(this, currentRider);
            }
        }

        // ---------------------------------------------------------------- 좌석과 조종석

        /// <summary>좌석을 기체 경계에 맞춰 다시 놓는다.</summary>
        void RefreshSeat()
        {
            if (seat == null) return;

            seat.position = transform.position + Vector3.up * ResolveSeatHeight();
            seat.rotation = Quaternion.identity;
        }

        /// <summary>
        /// 좌석 발 높이(루트 기준).
        ///
        /// 예전에는 <see cref="SeatHeight"/>(0.43m) 고정이었다. 탑승자 키가 1.147m 라
        /// 머리가 기체 위로 튀어나와 꼭대기에 걸터앉은 것처럼 보였다. 지금은 기체 경계를
        /// 재서 눈높이를 <see cref="EyeHeightRatio"/> 위치에 맞춘다 — 모델이 바뀌면 따라간다.
        /// Visual 이 없으면(테스트·스킨 미적용) 예전 값을 그대로 쓴다.
        ///
        /// 중심이 아니라 위쪽으로 잡는 이유가 있다. 스킨(S15P21B202-84)이 기체 모델의 경계
        /// 중심을 운반체 루트에 맞춰 붙이는데, 착륙하면 루트가 지면 0.06m 라 기체 아래
        /// 절반이 땅에 묻힌다. 중심에 눈을 두면 눈높이가 지면 6cm 가 되어 땅에 붙어 본다.
        /// </summary>
        float ResolveSeatHeight()
        {
            if (!TryVisualBounds(out var bounds)) return SeatHeight;

            var eyeWorldY = bounds.min.y + bounds.size.y * EyeHeightRatio;
            var wanted = eyeWorldY - RagdollBuilder.HeadY - transform.position.y;

            // 발이 기체 아래로 빠지지 않게, 머리가 위로 튀어나오지 않게 묶는다.
            var floor = bounds.min.y - transform.position.y;
            var ceiling = bounds.max.y - RagdollBuilder.TotalHeight - transform.position.y;
            if (ceiling < floor) ceiling = floor;

            return Mathf.Clamp(wanted, floor, ceiling);
        }

        bool TryVisualBounds(out Bounds bounds)
        {
            bounds = default;
            if (visual == null) return false;

            MeshVisual.EnsureRenderers(visual.gameObject);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            var first = true;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer) continue;
                if (cockpit != null && renderers[i].transform.IsChildOf(cockpit)) continue;
                if (first) { bounds = renderers[i].bounds; first = false; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            return !first;
        }

        /// <summary>
        /// 탑승 중에만 조종석을 보여 주고, 그때 기체 껍데기는 감춘다.
        ///
        /// 카메라가 기체 메시 안에 있으면 대부분 백페이스 컬링으로 보이지 않지만, 접시형
        /// 기체의 테두리는 바깥면이 카메라를 향해서 화면 위·아래에 얇은 곡선 조각으로 남는다.
        /// 실제 조종석이라면 내부 벽이 그 자리를 채우지만 임시 대체물에는 없으므로 껍데기를
        /// 끄는 것이 깔끔하다. 하차하면 다시 켠다.
        /// </summary>
        void SetCockpitVisible(bool visible)
        {
            if (visible) EnsureCockpit();
            if (cockpit != null) cockpit.gameObject.SetActive(visible);

            SetHullVisible(!visible);
        }

        /// <summary>기체 껍데기 렌더러만 토글한다. 조종석은 Visual 의 자식이라 빼야 한다.</summary>
        void SetHullVisible(bool visible)
        {
            if (visual == null) return;

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (cockpit != null && renderers[i].transform.IsChildOf(cockpit)) continue;
                renderers[i].enabled = visible;
            }
        }

        /// <summary>
        /// 임시 조종석. 눈 아래 앞쪽의 조종대와 그 위의 계기 화면, 둘뿐이다.
        ///
        /// 새 에셋을 만들지 않고 <see cref="Prim"/> 으로 세운다. 콜라이더는 붙지 않는다
        /// (<see cref="Prim.Decor"/>) — 조종석이 물리에 끼어들면 안 된다.
        ///
        /// <b>실제 조종석 에셋이 들어오면 이 메서드만 갈아끼운다.</b>
        /// 위치 기준은 탑승자의 눈이고, 요는 Visual 을 따라간다.
        /// </summary>
        void EnsureCockpit()
        {
            if (cockpit != null) return;

            var parent = visual != null ? visual : transform;
            var holder = new GameObject("VehicleCockpit");
            cockpit = holder.transform;
            cockpit.SetParent(parent, false);

            var eye = transform.position + Vector3.up * (ResolveSeatHeight() + RagdollBuilder.HeadY);
            var forward = ExitForward;
            var look = Quaternion.LookRotation(forward, Vector3.up);
            cockpit.SetPositionAndRotation(eye, look);

            // 조종대 — 눈 아래 앞쪽에 작게. 계기 화면을 비스듬히 올려 둔다.
            var console = Prim.Decor(Prim.Box("조종대",
                eye + forward * ConsoleForward - Vector3.up * ConsoleDrop,
                ConsoleSize, ConsoleColor, 0.25f, cockpit));
            console.transform.rotation = look;

            var screen = Prim.Decor(Prim.Box("계기화면",
                eye + forward * ScreenForward - Vector3.up * ScreenDrop,
                ScreenSize, ScreenColor, 0.1f, cockpit));
            screen.transform.rotation = look * Quaternion.Euler(-ScreenTiltDegrees, 0f, 0f);

            cockpit.gameObject.SetActive(false);
        }

        /// <summary>
        /// 입력만큼 목표 고도를 옮긴다.
        ///
        /// 아래로는 <see cref="ProbeGround"/> 가 닿는 순간 지금 높이에 묶는다. 목표를 계속
        /// 내리면 스프링이 표면을 파고들도록 밀어붙이고, 그러면 하차 버그와 같은 방식으로
        /// PhysX 가 튕겨낸다. 위로는 <see cref="MaxAltitude"/> 까지만 간다.
        /// </summary>
        void AdvanceTargetAltitude(float deltaTime)
        {
            grounded = ProbeGround(out _);

            if (!Mathf.Approximately(verticalInput, 0f))
                targetAltitude += verticalInput * AltitudeRate * deltaTime;

            // 닿아 있는데도 더 내리려 하면 지금 높이가 하한이다.
            if (grounded && verticalInput < 0f)
                targetAltitude = Mathf.Max(targetAltitude, body.position.y);

            targetAltitude = Mathf.Clamp(targetAltitude, GroundY, MaxAltitude);
            VerticalBlocked = false;

            // Only the vertical command is blocked. Steering, field toggles, and dismount
            // remain available while an overhead collider occupies the rise path.
            if (targetAltitude > body.position.y + VerticalClearance
                && TryGetVerticalClearance(targetAltitude, out var allowedAltitude)
                && allowedAltitude < targetAltitude)
            {
                targetAltitude = Mathf.Clamp(allowedAltitude, body.position.y, MaxAltitude);
                VerticalBlocked = true;
            }
        }

        bool TryGetVerticalClearance(float desiredAltitude, out float allowedAltitude)
        {
            allowedAltitude = desiredAltitude;
            if (body == null || platform == null || platformAnchor == null) return false;

            var rise = desiredAltitude - body.position.y;
            if (rise <= 0f) return false;

            var scale = platform.transform.lossyScale;
            var halfExtents = new Vector3(Mathf.Abs(platform.size.x * scale.x) * 0.5f,
                                          Mathf.Abs(platform.size.y * scale.y) * 0.5f,
                                          Mathf.Abs(platform.size.z * scale.z) * 0.5f);
            halfExtents.x = Mathf.Max(0.05f, halfExtents.x - ProbeShrink);
            halfExtents.z = Mathf.Max(0.05f, halfExtents.z - ProbeShrink);
            halfExtents.y = Mathf.Max(0.02f, halfExtents.y);

            var count = Physics.BoxCastNonAlloc(platformAnchor.position, halfExtents, Vector3.up,
                                                ProbeHits, Quaternion.identity, rise + VerticalClearance,
                                                SurfaceMask, QueryTriggerInteraction.Ignore);
            var nearest = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = ProbeHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }

            if (float.IsPositiveInfinity(nearest)) return false;

            allowedAltitude = body.position.y + Mathf.Max(0f, nearest - VerticalClearance);
            return true;
        }

        /// <summary>
        /// 플랫폼 아랫면 아래로 표면이 있는지 본다. 지면이든 절벽이든 파츠든 가리지 않는다.
        /// 시작 박스가 이미 겹쳐 있으면 BoxCast 가 놓치므로 <see cref="ProbeLift"/> 만큼 띄워서 쏜다.
        /// </summary>
        bool ProbeGround(out RaycastHit ground)
        {
            ground = default;
            if (platform == null || platformAnchor == null) return false;

            var bottom = platformAnchor.position.y - platform.size.y * 0.5f;
            var origin = new Vector3(platformAnchor.position.x, bottom + ProbeLift, platformAnchor.position.z);
            var half = new Vector3(Mathf.Max(0.05f, platform.size.x * 0.5f - ProbeShrink),
                                   0.02f,
                                   Mathf.Max(0.05f, platform.size.z * 0.5f - ProbeShrink));

            var count = Physics.BoxCastNonAlloc(origin, half, Vector3.down, ProbeHits, Quaternion.identity,
                                                ProbeLift + ProbeReach, SurfaceMask,
                                                QueryTriggerInteraction.Ignore);

            var found = false;
            for (var i = 0; i < count; i++)
            {
                var hit = ProbeHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;   // 자기 플랫폼
                if (hit.normal.y < MinGroundNormalY) continue;               // 벽은 바닥이 아니다

                if (!found || hit.distance < ground.distance)
                {
                    ground = hit;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 정면 → 우 → 좌 → 후 순서로 설 자리를 찾는다.
        /// 정면이 벽에 막혀 있어도 내릴 수 있어야 하므로 대안 방향을 둔다.
        /// </summary>
        public bool TryFindExitFoot(out Vector3 foot)
        {
            // 1차는 사람이 들어갈 공간까지 확인한다. 전부 막히면 2차에서 그 조건만 뺀다 —
            // 살짝 겹쳐 밀리는 것이 껍데기 안에 갇히는 것보다 낫다.
            return TrySweepForExitFoot(true, out foot) || TrySweepForExitFoot(false, out foot);
        }

        /// <summary>
        /// 정면부터 좌우로 벌려 가며 설 자리를 찾는다.
        ///
        /// 예전에는 네 방향(정면·좌·우·후면) × 한 거리만 봤고, 전부 막히면 그대로 실패했다.
        /// 실패하면 호출부가 기체 중심에 세웠으므로(<see cref="LastResortFoot"/> 주석 참고)
        /// 탐색이 실패하는 것 자체가 치명적이었다. 여덟 방향 × 두 거리로 넓혔다.
        /// </summary>
        /// <param name="requireClearance">사람 하나가 들어갈 공간까지 요구할지.</param>
        bool TrySweepForExitFoot(bool requireClearance, out Vector3 foot)
        {
            foot = Vector3.zero;
            if (platform == null || platformAnchor == null) return false;

            var forward = ExitForward;
            var top = platformAnchor.position.y + platform.size.y * 0.5f;
            var reach = ExitProbeUp + ExitProbeDown + (top - transform.position.y);

            // 껍데기 밖으로 확실히 나가는 거리. 모델이 커지면 같이 커진다 —
            // 상수로 두면 큰 기체에서 하차 지점이 껍데기 안에 잡힌다.
            var near = ExitDistance;
            if (TryVisualBounds(out var bounds))
                near = Mathf.Max(near, Mathf.Max(bounds.extents.x, bounds.extents.z) + RiderRadius + 0.15f);

            for (var d = 0; d < ExitDirectionCount; d++)
            {
                var direction = ExitDirection(forward, d);

                for (var r = 0; r < 2; r++)
                {
                    var spot = transform.position + direction * (near + r * ExitDistanceStep);
                    var origin = new Vector3(spot.x, top + ExitProbeUp, spot.z);

                    if (!Physics.Raycast(origin, Vector3.down, out var hit, reach,
                                         SurfaceMask, QueryTriggerInteraction.Ignore))
                        continue;

                    if (hit.normal.y < MinGroundNormalY) continue;               // 경사·벽 위에는 세우지 않는다
                    if (hit.collider.transform.IsChildOf(transform)) continue;   // 자기 플랫폼 위는 정면이 아니다
                    if (hit.point.y > top + MaxExitRise) continue;               // 벽 위로 올려 보내지 않는다

                    if (requireClearance)
                    {
                        // 사람 하나가 들어갈 공간이 있는지. 발밑 표면에 캡슐이 걸리지 않게 조금 띄운다.
                        var lower = hit.point + Vector3.up * (RiderRadius + 0.06f);
                        var upper = hit.point + Vector3.up * (RiderHeight - RiderRadius);
                        if (upper.y < lower.y) upper = lower;
                        if (IsBlockedForRider(lower, upper)) continue;
                    }

                    foot = hit.point;
                    return true;
                }
            }

            return false;
        }

        /// <summary>정면(0) → -45 → +45 → -90 → +90 → -135 → +135 → 180 순서.</summary>
        static Vector3 ExitDirection(Vector3 forward, int index)
        {
            var step = (index + 1) / 2;
            var sign = index % 2 == 0 ? 1f : -1f;
            return Quaternion.AngleAxis(sign * step * ExitAngleStep, Vector3.up) * forward;
        }

        /// <summary>
        /// 사람 하나가 설 자리가 막혀 있는지.
        ///
        /// <c>Physics.CheckCapsule</c> 은 제외 목록을 받지 않는다. 그래서 예전 구현은
        /// 기체 자신의 플랫폼 콜라이더가 캡슐에 걸리면 설 자리가 있는데도 막혔다고 봤다.
        /// </summary>
        bool IsBlockedForRider(Vector3 lower, Vector3 upper)
        {
            var n = Physics.OverlapCapsuleNonAlloc(lower, upper, RiderRadius, OverlapHits,
                                                   SurfaceMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < n; i++)
            {
                var col = OverlapHits[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 모든 방향이 막혔을 때의 마지막 수단 — 껍데기 <b>바깥·윗면</b>에 세운다.
        ///
        /// 예전에는 플랫폼 콜라이더 위, 즉 <b>기체 중심</b>에 세웠다. 콜라이더와 겹치지 않으니
        /// 튕겨 나가지 않는다는 판단이었고, 기체에 껍데기가 없던 시절에는 맞았다.
        /// S15P21B202-84 스킨이 실제 기체 모델을 Visual 로 붙인 뒤로 그 자리는 껍데기 <b>안</b>이
        /// 되었다. 카메라는 탑승자를 따라가므로 함께 껍데기 안에 갇히고, 화면에는 접시 테두리가
        /// 얇은 곡선으로 남고 나머지는 눈앞의 제 몸으로 가득 찬다 — 하차했는데 흰 화면만
        /// 보이는 증상의 원인이다. 계측: 골반이 기체 중심에서 수평 0.000m 지점에 놓였다.
        ///
        /// 지금은 껍데기 경계 밖으로 밀고 윗면 높이에 둔다. 갇히지 않고, 떨어져도 기체
        /// 바깥으로 떨어진다. 여기까지 오는 것은 여덟 방향 × 두 거리 × 두 단계가 모두
        /// 실패한 경우이므로, 다음에 또 생기면 추적할 수 있게 경고를 남긴다.
        /// </summary>
        Vector3 LastResortFoot()
        {
            var center = transform.position;
            var top = platform != null && platformAnchor != null
                ? platformAnchor.position.y + platform.size.y * 0.5f + 0.02f
                : center.y;

            var clear = ExitDistance;
            if (TryVisualBounds(out var bounds))
            {
                top = Mathf.Max(top, bounds.max.y + LastResortLift);
                clear = Mathf.Max(clear, Mathf.Max(bounds.extents.x, bounds.extents.z) + RiderRadius + 0.15f);
            }

            var spot = center + ExitForward * clear;
            Debug.LogWarning($"[TransportVehicle] 하차 지점을 찾지 못했다 — 기체 {center}, " +
                             $"껍데기 밖 ({spot.x:0.00}, {top:0.00}, {spot.z:0.00}) 에 내려놓는다.");

            return new Vector3(spot.x, top, spot.z);
        }

    }
}
