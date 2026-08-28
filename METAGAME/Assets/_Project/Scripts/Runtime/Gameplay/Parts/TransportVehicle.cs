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
        const float GroundSpring = 7f;
        const float GroundTolerance = 0.035f;
        const float VisualTurnSpeed = 420f;
        const float ArenaLimit = 21f;

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

        public Rigidbody Body => body;
        public TractorBeam Beam => beam;
        public PlayerRig Rider => rider;
        public bool IsOccupied => rider != null;
        public bool IsReturning => returning;
        public bool IsGrounded => grounded;
        public bool IsHovering => rider != null && !returning && body != null
                                  && Mathf.Abs(body.position.y - HoverRootY) < 0.4f;
        public Vector3 SeatFootPosition => seat != null ? seat.position : transform.position + Vector3.up * SeatHeight;
        public Vector3 GroundExitFootPosition => new Vector3(transform.position.x, GroundY + 0.04f, transform.position.z);
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
            body.useGravity = false;
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

        void FixedUpdate()
        {
            if (rider != null)
            {
                if (returning) DriveToGround(Time.fixedDeltaTime);
                else DriveHover(Time.fixedDeltaTime);
            }
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

        void DriveToGround(float deltaTime)
        {
            var velocity = body.linearVelocity;
            var planar = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z),
                                              Vector3.zero, Braking * deltaTime);
            var heightError = GroundY - body.position.y;
            var vertical = Mathf.Clamp(heightError * GroundSpring, -MaxVerticalSpeed, MaxVerticalSpeed);
            body.linearVelocity = new Vector3(planar.x, vertical, planar.z);
            body.angularVelocity = Vector3.zero;

            if (Mathf.Abs(heightError) <= GroundTolerance && Mathf.Abs(body.linearVelocity.y) < 0.18f)
            {
                var pos = body.position;
                pos.y = GroundY;
                body.position = pos;
                transform.position = pos;
                body.linearVelocity = Vector3.zero;
                body.useGravity = true;
                grounded = true;
                returning = false;

                var currentRider = rider;
                rider = null;
                if (currentRider != null)
                    currentRider.CompleteTransportVehicleExit(this);
            }
        }

        void TurnVisualToward(Vector3 direction, float deltaTime)
        {
            if (visual == null) return;
            var target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            visual.rotation = Quaternion.RotateTowards(visual.rotation, target, VisualTurnSpeed * deltaTime);
        }
    }
}
