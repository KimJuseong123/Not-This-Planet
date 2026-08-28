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
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TransportVehicle : MonoBehaviour
    {
        public const float GroundY = 0.06f;
        public const float HoverRootY = 5.0f;
        public const float BeamHoldDistance = 3.1f;
        public const float BoardRadius = 4.2f;

        const float PlatformHeight = 0.22f;
        const float SeatHeight = 0.43f;
        const float Acceleration = 16f;
        const float Braking = 12f;
        const float MaxSpeed = 8f;
        const float HoverSpring = 6f;
        const float MaxVerticalSpeed = 4.2f;
        const float VisualTurnSpeed = 420f;
        const float ArenaLimit = 21f;

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

        /// <summary>탑승자가 설 자리가 있는지 볼 때 쓰는 몸 치수. 헬멧 지름 0.636m 와 키 1.147m 기준.</summary>
        const float RiderRadius = 0.34f;
        const float RiderHeight = 1.15f;

        Rigidbody body;
        BoxCollider platform;
        Transform platformAnchor;
        Transform seat;
        Transform visual;
        TractorBeam beam;
        PlayerRig rider;
        Vector3 driveVector;
        bool returning;
        bool grounded = true;
        float dismountElapsed;

        /// <summary>접지·하차 지점 판정용. BoxCast 결과를 담아 GC 를 만들지 않는다.</summary>
        static readonly RaycastHit[] ProbeHits = new RaycastHit[8];

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
        public PlayerRig Rider => rider;
        public bool IsOccupied => rider != null;
        public bool IsReturning => returning;
        public bool IsGrounded => grounded;

        /// <summary>
        /// 플랫폼 아랫면이 지금 무엇에든 닿아 있는지. 하차 완료 판정과 같은 검사다.
        /// 절대 높이를 보지 않으므로 절벽 위든 파츠 위든 같게 동작한다.
        /// </summary>
        public bool IsTouchingSurface => ProbeGround(out _);
        public bool IsHovering => rider != null && !returning && body != null
                                  && Mathf.Abs(body.position.y - HoverRootY) < 0.4f;
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
            get { return TryFindExitFoot(out var foot) ? foot : PlatformTopFoot(); }
        }

        /// <summary>운반체가 바라보는 수평 방향. 요는 Visual 자식이 들고 있다(루트는 빔 축이라 아래를 본다).</summary>
        public Vector3 ExitForward
        {
            get
            {
                var forward = visual != null ? visual.forward : Vector3.forward;
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
        }

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

            seat.position = transform.position + Vector3.up * SeatHeight;
            seat.rotation = Quaternion.identity;
            seat.gameObject.layer = GameLayers.Prop;
        }

        /// <summary>탑승하지 않은 장치를 맵의 바닥으로 되돌린다.</summary>
        public void PlaceOnGround(Vector3 position, float visualYaw)
        {
            EnsurePhysics();

            var rootRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var groundPosition = new Vector3(position.x, GroundY, position.z);
            body.position = groundPosition;
            body.rotation = rootRotation;
            transform.SetPositionAndRotation(groundPosition, rootRotation);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = true;
            driveVector = Vector3.zero;
            returning = false;
            grounded = true;
            dismountElapsed = 0f;

            if (visual != null)
                visual.rotation = Quaternion.Euler(0f, visualYaw, 0f);
        }

        public bool TryBoard(PlayerRig player)
        {
            if (player == null || rider != null || returning) return false;

            var offset = player.Position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > BoardRadius * BoardRadius) return false;

            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            grounded = false;

            if (!player.TryEnterTransportVehicle(this))
            {
                body.useGravity = true;
                grounded = true;
                return false;
            }

            rider = player;
            return true;
        }

        public void BeginDismount()
        {
            if (rider == null || returning) return;
            driveVector = Vector3.zero;
            returning = true;
            grounded = false;
            dismountElapsed = 0f;

            // 낙하는 중력에 맡긴다. 속도를 직접 대입해 목표 높이로 밀어붙이면 콜라이더
            // 접촉이 무시되고 지면을 파고든다.
            body.useGravity = true;
        }

        public void SetDriveVector(Vector3 worldDirection)
        {
            if (rider == null || returning) return;
            worldDirection.y = 0f;
            driveVector = worldDirection.sqrMagnitude > 1f
                ? worldDirection.normalized
                : worldDirection;
        }

        /// <summary>부활 때 탑승체와 플레이어를 함께 안전한 바닥 상태로 돌린다.</summary>
        public void ForceReset()
        {
            var currentPosition = transform.position;
            PlaceOnGround(currentPosition, 0f);

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
            if (rider == null) return;

            if (returning) DriveToGround(deltaTime);
            else DriveHover(deltaTime);
        }

        void DriveHover(float deltaTime)
        {
            var velocity = body.linearVelocity;
            var planar = new Vector3(velocity.x, 0f, velocity.z);
            var target = driveVector * MaxSpeed;
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

            var vertical = Mathf.Clamp((HoverRootY - body.position.y) * HoverSpring,
                                       -MaxVerticalSpeed, MaxVerticalSpeed);
            body.linearVelocity = new Vector3(planar.x, vertical, planar.z);
            body.angularVelocity = Vector3.zero;
            grounded = false;

            if (planar.sqrMagnitude > 0.04f)
                TurnVisualToward(planar, deltaTime);
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

            var currentRider = rider;
            rider = null;
            if (currentRider != null)
                currentRider.CompleteTransportVehicleExit(this);
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
            foot = Vector3.zero;
            if (platform == null || platformAnchor == null) return false;

            var forward = ExitForward;
            var right = Vector3.Cross(Vector3.up, forward);
            var top = platformAnchor.position.y + platform.size.y * 0.5f;

            for (var i = 0; i < 4; i++)
            {
                var direction = i switch
                {
                    0 => forward,
                    1 => right,
                    2 => -right,
                    _ => -forward,
                };

                var spot = transform.position + direction * ExitDistance;
                var origin = new Vector3(spot.x, top + ExitProbeUp, spot.z);

                if (!Physics.Raycast(origin, Vector3.down, out var hit,
                                     ExitProbeUp + ExitProbeDown + (top - transform.position.y),
                                     SurfaceMask, QueryTriggerInteraction.Ignore))
                    continue;

                if (hit.normal.y < MinGroundNormalY) continue;               // 경사·벽 위에는 세우지 않는다
                if (hit.collider.transform.IsChildOf(transform)) continue;   // 자기 플랫폼 위는 정면이 아니다

                // 사람 하나가 들어갈 공간이 있는지. 발밑 표면에 캡슐이 걸리지 않게 조금 띄운다.
                var lower = hit.point + Vector3.up * (RiderRadius + 0.06f);
                var upper = hit.point + Vector3.up * (RiderHeight - RiderRadius);
                if (upper.y < lower.y) upper = lower;
                if (Physics.CheckCapsule(lower, upper, RiderRadius, SurfaceMask, QueryTriggerInteraction.Ignore))
                    continue;

                foot = hit.point;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 네 방향 모두 막혔을 때의 마지막 수단 — 플랫폼 위에 세운다.
        /// 겹치지 않으므로 튕겨 나가지 않고, 걸어서 내려올 수 있다.
        /// </summary>
        Vector3 PlatformTopFoot()
        {
            if (platform == null || platformAnchor == null) return transform.position;
            return platformAnchor.position + Vector3.up * (platform.size.y * 0.5f + 0.02f);
        }

        void TurnVisualToward(Vector3 direction, float deltaTime)
        {
            if (visual == null) return;
            var target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            visual.rotation = Quaternion.RotateTowards(visual.rotation, target, VisualTurnSpeed * deltaTime);
        }
    }
}
