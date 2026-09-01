using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 우주비행사의 이동과 물리 상호작용을 연결한다.
    ///
    ///  · 골반에 매달린 보이지 않는 스프링이 몸무게를 떠받쳐 서 있게 한다 (다리는 거들 뿐)
    ///  · 골반을 카메라 기준 정면으로 세우고, 이동 방향과 독립적으로 유지한다
    ///  · 다리는 걷기 주기에 맞춰 조인트 목표 자세를 흔든다 (애니메이션 클립 없음)
    ///  · 팔은 카메라가 보는 쪽으로 뻗되, 근처에 잡을 것이 있으면 그쪽으로 휘어 간다
    ///  · GrabbableRole.Tool 도구는 양손 논리 고정 그립으로 잡고 중력과 누적 속도를 제거해 전방에 고정된다
    ///  · 일정 질량 이상인 동적 물체는 들어 올리지 않고 고정물을 짚듯 잡는다
    ///
    /// 이동 자체는 목표 속도를 직접 따라가 안정적으로 조종하고, 잡기·등반·충돌 반응은 물리에 맡긴다.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        const float ClampInteractionRange = 3f;
        const float ClampInteractionHalfAngle = 60f;

        public Doll Doll;
        /// <summary>
        /// 이 리그를 보는 카메라. <b>원격 플레이어는 null 이다</b> — 그래서 1인칭 숨김이
        /// 제 캐릭터에만 적용되고 남의 캐릭터는 그대로 보인다.
        /// </summary>
        public OrbitCamera Cam
        {
            get => cam;
            set { cam = value; RefreshDollVisibility(); }
        }

        OrbitCamera cam;
        public bool IsLocal = true;
        public string Nickname = "비행사";

        // ── 상태 ────────────────────────────────────────────────
        public bool Grounded { get; private set; }
        public float GroundDistance { get; private set; }
        public Vector3 GroundVelocity { get; private set; }
        public float Speed { get; private set; }
        public float MaxHealth { get { return Tuning.MaxHealth; } }
        public float Health { get; private set; }
        public float Health01 { get { return MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / MaxHealth); } }
        public bool IsDead { get { return Health <= 0f; } }
        public TransportVehicle MountedVehicle { get; private set; }
        public bool IsMounted { get { return MountedVehicle != null; } }

        /// <summary>
        /// 몸이 조인트로 붙어 있는 강체. 운반 장치 탑승도, 우주선 탑승도 이것을 채운다.
        ///
        /// 예전에는 탑승 상태가 <see cref="MountedVehicle"/> 하나였고 타입이
        /// <see cref="TransportVehicle"/> 로 못박혀 있었다. 그래서 우주선에는 탈 수 없었고,
        /// S15P21B202-200 의 탑승은 인원을 세는 것뿐이었다 — 발사하면 발사대에 서서
        /// 우주선이 올라가는 것을 구경했다.
        ///
        /// 운반 장치 고유의 처리(조종 입력, 조종석 시야, 하차 발판 탐색)는
        /// <see cref="MountedVehicle"/> 이 그대로 갖는다. 여기 올라오는 것은 "몸이 붙어
        /// 있으니 보행 물리를 돌리지 않는다" 는 공통 사실뿐이다.
        /// </summary>
        public Rigidbody RidingBody { get; private set; }

        /// <summary>무엇에든 붙어 실려 가는 중인가.</summary>
        public bool IsRiding { get { return RidingBody != null; } }
        public bool Hanging { get { return LeftHand.Hanging || RightHand.Hanging; } }
        public bool Carrying { get { return LeftHand.Carrying || RightHand.Carrying; } }
        public bool Ragdolled { get; private set; }      // 크게 넘어져 조종이 잠깐 풀린 상태
        public float StepPhase { get; private set; }
        /// <summary>현재 캐릭터가 유지하는 수평 정면. 이동 방향과 독립적으로 카메라를 따른다.</summary>
        public Vector3 FacingDirection { get { return faceDir; } }
        /// <summary>현재 실제 수평 이동을 캐릭터 정면 기준 좌우·전후로 나타낸 값.</summary>
        public Vector2 MovementDirectionLocal { get { return movementDirectionLocal; } }
        /// <summary>공중에서 자세제어 분사기를 쓰고 있는가 (HUD·연기 표시용).</summary>
        public bool Thrusting { get; private set; }
        public float AirTime { get; private set; }

        /// <summary>
        /// 광역 저중력장 안에서 떠 있는가.
        ///
        /// 파츠는 저중력장이 힘으로 끌어당기지만 플레이어는 그러지 않는다. 기획
        /// (S15P21B202-107 §14.3)이 "플레이어는 중심으로 강제 견인되지 않고 직접 움직인다"
        /// 고 정했기 때문이다. 그래서 저중력장은 "여기가 영역이다" 만 알려 주고
        /// (<see cref="Parts.LowGravityField.FindContaining"/>) 실제 부유는 여기서 만든다.
        ///
        /// 인형에 힘을 주지 않고 속도를 직접 넣는 이유도 같다. 래그돌의 강체를 하나씩
        /// 띄우면 팔다리가 따로 떠서 자세가 무너진다. <see cref="DriveMovementVelocity"/> 가
        /// 이미 수평을 그렇게 다루고 있어 세로도 같은 방식으로 맞췄다.
        /// </summary>
        public bool Floating { get; private set; }
        public Grabbable HeldTool { get { return toolHeld; } }
        /// <summary>현재 장착된 도구 동작 컴포넌트. 도구가 없거나 구형 Tool이면 null이다.</summary>
        public ToolBase ActiveTool { get { return activeTool; } }
        public float ToolCharge { get { return toolCharge; } }
        public bool ChargingTool { get { return chargingTool; } }
        /// <summary>현재 조준 중인 고정쇠를 찾고 해제 홀드를 진행하는 상호작용 주체.</summary>
        public Interactor ClampInteractor { get; private set; }
        /// <summary>좌·우 중 하나 이상의 버튼으로 고정쇠를 잡은 상태.</summary>
        public bool ClampGrabbed { get { return clampGrabbed; } }

        /// <summary>
        /// 지금 잡고 있는 고정못. 없으면 null. 연출과 검증이 쓴다.
        /// </summary>
        public PartClamp ClampGrabTarget { get { return clampGrabTarget; } }

        /// <summary>이번 틱에 고정못을 실제로 뒤로 당겼는가. 낑낑대는 연출이 붙을 신호다.</summary>
        public bool ClampPulling { get; private set; }
        /// <summary>고정쇠를 위로 드는 거리 기준을 넘었는가.</summary>
        /// <summary>
        /// 예전 조작(마우스를 위로 들어 홀드를 시작)의 잔재. 뒤로 이동 방식으로 바뀐
        /// 뒤에는 "잡고 있는가" 와 같은 뜻이다. HUD 가 아직 이 이름을 쓴다.
        /// </summary>
        public bool ClampLiftReady { get { return clampGrabbed; } }
        /// <summary>고정쇠를 위로 드는 거리의 0~1 진행도.</summary>
        /// <summary>
        /// 잡고 있는 고정못이 얼마나 뽑혔는가 0~1.
        ///
        /// 예전에는 "마우스를 위로 얼마나 들었는가" 였다. 그 단계가 없어졌으므로
        /// 고정못의 진행분을 그대로 돌려준다.
        /// </summary>
        public float ClampLiftProgress
        {
            get { return clampGrabTarget != null ? clampGrabTarget.Progress : 0f; }
        }
        /// <summary>위로 든 뒤 실제 해제 유지 시간을 진행 중인가.</summary>
        public bool ClampReleaseHolding
        {
            get { return ClampInteractor != null && ClampInteractor.State == InteractorState.Holding; }
        }

        public Hand LeftHand { get { return Doll.Left.Hand; } }
        public Hand RightHand { get { return Doll.Right.Hand; } }
        public Rigidbody Hips { get { return Doll.Hips; } }
        public Vector3 Position { get { return Doll.Hips.position; } }
        public Vector3 FootPosition { get { return Doll.Hips.position - Vector3.up * RagdollBuilder.HipsY; } }

        InputFrame input;
        Vector3 faceDir = Vector3.forward;
        Vector2 movementDirectionLocal;
        float jumpLock;
        float regrabLock;
        float uprightBlend = 1f;
        float thrustPuff;
        float fallPeakFootHeight;

        /// <summary>부유하느라 인형의 중력을 꺼 둔 상태인가. 켜고 끄는 시점을 한 번씩만 만든다.</summary>
        bool floatingGravityOff;
        Grabbable toolHeld;
        float toolCharge;
        bool toolActionHeld;
        bool chargingTool;
        float toolThrowRegrabLock;
        Rigidbody toolBody;
        bool toolBodyHadGravity;
        ToolBase activeTool;
        bool previousToolPrimary;
        bool previousToolSecondary;
        bool previousToolUtility;
        bool suppressToolPrimary;
        bool suppressToolSecondary;
        bool suppressToolUtility;
        bool previousGrabLeft;
        bool previousGrabRight;
        bool clampGrabbed;
        PartClamp clampGrabTarget;
        Rigidbody pendingThrowBody;
        Vector3 pendingThrowVelocity;
        Rigidbody groundBody;
        FixedJoint mountedJoint;
        Rigidbody ignoredHeldBodyA;
        Rigidbody ignoredHeldBodyB;
        readonly Dictionary<Rigidbody, CarriedBodyState> carriedBodies = new Dictionary<Rigidbody, CarriedBodyState>(4);
        readonly List<CollisionPair> heldObjectCollisionPairs = new List<CollisionPair>(24);
        readonly RaycastHit[] rayHits = new RaycastHit[8];

        struct CarriedBodyState
        {
            public bool WasKinematic;
            public bool HadGravity;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public CollisionDetectionMode CollisionDetectionMode;
            public RigidbodyInterpolation Interpolation;
            public Vector3 FollowPosition;
            public bool FollowInitialized;
        }

        struct CollisionPair
        {
            public Collider Player;
            public Collider Held;
        }

        public static PlayerRig Create(string nickname, Vector3 footPos, Quaternion facing,
                                       Color suit, Color accent, bool useModel = true)
        {
            var go = new GameObject("플레이어 " + nickname);
            var rig = go.AddComponent<PlayerRig>();
            rig.Nickname = nickname;
            bool model = useModel && SpaceSuit.Available;
            rig.Doll = RagdollBuilder.Build(nickname, footPos, facing, suit, accent, !model);
            rig.Doll.Root.SetParent(go.transform, true);
            rig.ClampInteractor = go.AddComponent<Interactor>();
            rig.ConfigureClampInteraction(rig.Doll.Head.transform);
            rig.ResetHealth();
            rig.fallPeakFootHeight = footPos.y;
            rig.faceDir = facing * Vector3.forward;
            return rig;
        }

        void OnDestroy()
        {
            if (activeTool != null) activeTool.Unequip();
            RestoreAllCarriedBodies();
            ClearHeldObjectOwnerCollision();
        }

        public void SetInput(InputFrame frame)
        {
            input = frame;
        }

        /// <summary>카메라가 만들어진 뒤 고정쇠 조준 기준을 갱신한다.</summary>
        public void ConfigureClampInteraction(Transform aim)
        {
            if (ClampInteractor == null)
                ClampInteractor = gameObject.AddComponent<Interactor>();

            ClampInteractor.Configure(aim, ClampInteractionRange, ClampInteractionHalfAngle);
        }
        public InputFrame CurrentInput { get { return input; } }

        /// <summary>체크포인트 부활이나 새 라운드에서 체력을 초기화한다.</summary>
        public void ResetHealth()
        {
            Health = MaxHealth;
        }

        /// <summary>외부 피해 오브젝트와 낙하 판정이 공통으로 사용하는 피해 진입점.</summary>
        public void ApplyDamage(float amount, string source = "피해")
        {
            if (IsDead || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;

            Health = Mathf.Clamp(Health - amount, 0f, MaxHealth);
            if (Health <= 0f)
            {
                Ragdolled = true;
                Debug.Log("[SpaceFlop] " + Nickname + " 체력 소진 (" + source + ")");
            }
        }

        // ── 부활 / 순간이동 ─────────────────────────────────────
        public void Teleport(Vector3 footPos, Quaternion facing)
        {
            if (MountedVehicle != null) ForceExitTransportVehicle(MountedVehicle);
            else DisconnectTransportJoint();   // 우주선에 붙어 있었다면 여기서 떨어진다

            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);
            Doll.Teleport(footPos + Vector3.up * RagdollBuilder.HipsY, facing);
            faceDir = facing * Vector3.forward;
            movementDirectionLocal = Vector2.zero;
            uprightBlend = 1f;
            Ragdolled = false;
            StepPhase = 0f;
            AirTime = 0f;
            fallPeakFootHeight = footPos.y;
            ResetToolState();
            toolThrowRegrabLock = 0f;
            pendingThrowBody = null;
            previousGrabLeft = false;
            previousGrabRight = false;
            ClampInteractor?.Cancel();
            ResetClampGrab();
            if (Cam != null)
            {
                Cam.Yaw = facing.eulerAngles.y;
                Cam.SnapToTarget();
            }
        }

        /// <summary>
        /// 몸을 강체에 붙인다. 붙이는 코드가 여기 한 군데다.
        ///
        /// <b><see cref="FixedJoint.connectedBody"/> 를 반드시 채운다.</b> 비워 두면
        /// <see cref="FixedJoint.connectedAnchor"/> 가 월드 좌표로 해석되어 몸이 원점으로
        /// 빨려간다 — S15P21B202-132 에서 21.34m 끌려간 원인이 그것이었다.
        /// </summary>
        void AttachTo(Rigidbody body)
        {
            mountedJoint = Doll.Hips.gameObject.AddComponent<FixedJoint>();
            mountedJoint.connectedBody = body;
            mountedJoint.autoConfigureConnectedAnchor = false;
            mountedJoint.anchor = Vector3.zero;
            mountedJoint.connectedAnchor = body.transform.InverseTransformPoint(Doll.Hips.position);
            mountedJoint.enableCollision = false;
            mountedJoint.enablePreprocessing = false;
            mountedJoint.breakForce = float.PositiveInfinity;
            mountedJoint.breakTorque = float.PositiveInfinity;
            RidingBody = body;
        }

        /// <summary>탑승 직전에 손·도구·보행 상태를 정리한다. 두 탑승 경로가 함께 쓴다.</summary>
        void ClearForRide()
        {
            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);
            ClampInteractor?.Cancel();
            ResetClampGrab();
            ResetToolState();

            Grounded = true;
            GroundDistance = Tuning.RideHeight;
            GroundVelocity = Vector3.zero;
            Speed = 0f;
            movementDirectionLocal = Vector2.zero;
            AirTime = 0f;
            Thrusting = false;
            Ragdolled = false;
        }

        /// <summary>운반 장치에 올라탄다. 고정쇠·도구 상태와 무관하게 탑승을 허용한다.</summary>
        public bool TryEnterTransportVehicle(TransportVehicle vehicle)
        {
            if (vehicle == null || IsRiding || Doll == null || Doll.Hips == null)
                return false;

            ClearForRide();

            var foot = vehicle.SeatFootPosition;
            Doll.Teleport(foot + Vector3.up * RagdollBuilder.HipsY, Quaternion.identity);
            faceDir = Vector3.forward;

            AttachTo(vehicle.Body);
            MountedVehicle = vehicle;

            // 조종석 안에 있으므로 몸을 감춘다. 1인칭 시야를 제 몸이 가리지 않게 하는 것이
            // 첫 목적이고, 3인칭으로 강제 전환해도 기체 밖으로 튀어나온 몸이 보이지 않는다.
            RefreshDollVisibility();

            if (Cam != null) Cam.SetVehicleView(true);
            return true;
        }

        /// <summary>
        /// 우주선에 탑승한다. 붙는 곳은 최상단 견인 조종 모듈이다
        /// (기획 S15P21B202-107 §22.4 "플레이어들이 탑승한 최상단 견인 조종 모듈").
        ///
        /// 운반 장치와 달리 <see cref="MountedVehicle"/> 을 채우지 않는다 — 우주선은
        /// 조종하지 않고 조종석 시야도 쓰지 않는다. 실려 가기만 한다.
        /// </summary>
        public bool TryBoardShip(Rigidbody module, Vector3 seatPosition)
        {
            if (module == null || IsRiding || Doll == null || Doll.Hips == null) return false;

            ClearForRide();

            Doll.Teleport(seatPosition, Doll.Hips.rotation);
            AttachTo(module);

            // 모듈 안에 있는 몸이다. 껍데기를 통과해 보이면 안 된다.
            RefreshDollVisibility();
            return true;
        }

        /// <summary>우주선에서 내린다. 카운트다운을 취소하면 이 경로로 돌아온다.</summary>
        public void LeaveShip(Vector3 footPosition, Quaternion facing)
        {
            if (RidingBody == null || MountedVehicle != null) return;

            DisconnectTransportJoint();
            Doll.Teleport(footPosition + Vector3.up * RagdollBuilder.HipsY, facing);
            faceDir = facing * Vector3.forward;
            RefreshDollVisibility();

            if (Cam == null) return;
            Cam.Yaw = facing.eulerAngles.y;
            Cam.SnapToTarget();
        }

        /// <summary>
        /// 발사 실패로 우주선에서 튀어나온다(기획 §22.4 "모든 플레이어가 사방으로
        /// 튀어나온다 … 잠시 구르지만 기절하지 않고 자동으로 일어난다").
        ///
        /// <b>기절 처리를 새로 만들지 않는다.</b> <see cref="Balance"/> 가 이미 넘어진
        /// 자세를 스스로 세우므로, 조인트를 끊고 충격만 주면 구르다가 일어난다.
        /// 체력도 깎지 않는다 — 기획이 기절하지 않는다고 못박았고, 체력을 깎으면
        /// 운석 두 방 맞은 상태에서 발사 실패가 사망이 된다.
        ///
        /// 위치는 옮기지 않는다. 몸은 이미 모듈 안에 있고, 밖으로 순간이동시키면
        /// 지면이나 잔해 속에 박힐 자리를 다시 찾아야 한다 — 그 탐색이
        /// S15P21B202-132 의 흰 화면을 만든 곳이다. 충격으로 밀어내는 편이 안전하다.
        /// </summary>
        public void EjectFromShip(Vector3 direction)
        {
            if (RidingBody == null) return;

            DisconnectTransportJoint();
            MountedVehicle = null;

            var away = direction;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : faceDir;

            var impulse = away * PhysicsScale.EjectHorizontalSpeed
                          + Vector3.up * PhysicsScale.EjectVerticalSpeed;

            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                body.useGravity = true;
                body.linearVelocity = impulse;
            }

            Grounded = false;
            GroundDistance = float.MaxValue;
            GroundVelocity = Vector3.zero;
            Ragdolled = true;          // Balance 가 다음 틱부터 다시 세운다
            AirTime = 0f;
            faceDir = away;

            RefreshDollVisibility();
        }

        /// <summary>운반 장치가 표면에 닿은 뒤 플레이어를 그 옆에 내려놓는다.</summary>
        public void CompleteTransportVehicleExit(TransportVehicle vehicle)
        {
            if (vehicle == null || MountedVehicle != vehicle) return;
            DisconnectTransportJoint();
            MountedVehicle = null;

            PlaceAfterVehicleExit(vehicle);
        }

        /// <summary>부활·리셋 때 지연 없이 운반 장치에서 내린다.</summary>
        public void ForceExitTransportVehicle(TransportVehicle vehicle)
        {
            if (vehicle == null || MountedVehicle != vehicle) return;
            DisconnectTransportJoint();
            MountedVehicle = null;

            PlaceAfterVehicleExit(vehicle);
        }

        /// <summary>
        /// 제 몸을 그릴지 정한다. 가시성을 바꾸는 곳은 여기 한 군데다.
        ///
        /// <b>1인칭에서는 제 몸을 그리지 않는다.</b> 카메라는 골반 + 0.55m, 즉 발 기준
        /// 0.85m 에 서고 시선 방향으로 0.20m(<see cref="OrbitCamera.FirstPersonForward"/>)
        /// 내민다. 그런데 헬멧이 0.51~1.15m, 반지름 약 0.32m 다 — <b>카메라가 제 헬멧
        /// 안에 있다.</b> 귀는 1.33m 까지 올라가고 모델(<c>useModel</c>)을 씌우면 주둥이와
        /// 꼬리가 더 붙는다. 그래서 시선을 조금만 돌려도 제 몸이 화면을 덮는다.
        ///
        /// 내미는 거리를 키워서 피하는 것은 임시방편이다. 모델이 바뀌면 다시 깨지고,
        /// 카메라를 몸 밖으로 밀어낼수록 시점이 몸에서 떠 보인다. 1인칭에서 제 몸을
        /// 그리지 않는 것이 정석이다.
        ///
        /// 그림자는 남긴다 — 1인칭에서 발밑을 읽는 단서가 제 그림자다.
        ///
        /// 탑승 중에는 그림자까지 감춘다. 기체 안에 있는 몸이고 껍데기도 감춰 둔
        /// 상태(<c>SetHullVisible</c>)라, 그림자만 남으면 허공에 혼자 뜬 그림자가 된다.
        ///
        /// <see cref="Cam"/> 이 null 인 원격 플레이어는 그대로 보인다.
        /// </summary>
        void RefreshDollVisibility()
        {
            if (Doll == null) return;

            if (IsRiding) Doll.SetVisible(false);
            else if (Cam != null && Cam.FirstPerson) Doll.SetVisible(false, keepShadow: true);
            else Doll.SetVisible(true);
        }

        /// <summary>하차한 탑승자를 지면 위로 살짝 띄우는 여유. 발이 표면에 박히지 않게 한다.</summary>
        const float VehicleExitClearance = 0.06f;

        /// <summary>
        /// 하차 공통 처리. 운반체가 찾아 준 발 위치에 세우고 장치 바깥을 보게 돌린다.
        ///
        /// <b><see cref="Doll.Teleport"/> 의 첫 인수는 발이 아니라 골반 위치다.</b>
        /// 예전에는 발 높이 + 0.04m 를 그대로 넘겨서 골반이 지면 4cm 에 놓였다. 골반은
        /// 0.3m 높이여야 하므로 다리와 발이 지면 26cm 아래에 파묻히고, PhysX 가 그것을
        /// 밀어내면서 탑승자가 천장까지 날아갔다. 탑승할 때는 같은 함수에 HipsY 를 더해
        /// 넘기고 있었는데 하차 경로만 빠져 있었다.
        ///
        /// <c>fallPeakFootHeight</c> 를 내려놓은 높이로 맞추는 것이 중요하다. 구조물 위에서
        /// 내리면 지면보다 높은데, 이 값을 갱신하지 않으면 그 차이가 낙하로 집계돼
        /// HUD 의 추락 횟수가 오른다.
        /// </summary>
        void PlaceAfterVehicleExit(TransportVehicle vehicle)
        {
            var foot = vehicle.GroundExitFootPosition;

            var away = foot - vehicle.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = vehicle.ExitForward;
            away.Normalize();

            // 카메라 설정을 먼저 되돌린다. 1인칭인지 3인칭인지를 보고 서는 방향을 정한다.
            if (Cam != null) Cam.SetVehicleView(false);

            // 시점이 정해진 뒤에 몸을 그릴지 정한다. 순서가 바뀌면 조종석 1인칭 상태를
            // 보고 판단해 3인칭인데도 몸이 감춰진 채로 남는다.
            RefreshDollVisibility();

            // <b>인형의 방향과 카메라의 요를 반드시 맞춘다.</b> 이것이 이 버그의 핵심이었다.
            // 예전에는 인형만 돌리고 카메라 요는 조종석에서 보던 값을 그대로 뒀다. 두 값이
            // 어긋나면 1인칭 카메라가 머리 <b>앞</b> 20cm 가 아니라 머리 <b>뒤</b>로 밀려
            // 제 몸과 꼬리가 화면에 들어온다.
            //
            // 어느 쪽을 보고 설지는 1인칭과 3인칭에서 반대다.
            //
            //  · 1인칭 — 카메라가 머리에 있으므로 기체 <b>바깥</b>을 봐야 눈앞이 트인다.
            //    기체를 마주 보면 1.8m 앞의 기체(1.90 x 2.45m)가 화면을 가득 덮는다.
            //    걸어 나가는 방향이기도 해서 자연스럽다.
            //  · 3인칭 — 카메라가 등 뒤 3.6m 에 서므로 기체를 <b>마주</b> 봐야 한다.
            //    등을 지면 하차 지점 1.8m 에서 껍데기 반지름 0.95m 를 뺀 0.85m 밖에
            //    물러날 자리가 없어 최소 거리 1m 까지 밀려들어오고, 그 자리에서 보이는
            //    것은 제 등과 꼬리뿐이다.
            //
            // 씬의 SpaceBootstrap 은 firstPerson = 1 로 1인칭으로 시작한다. 3인칭 쪽도
            // 맞춰 두는 이유는 그 값이 씬 설정이라 언제든 바뀔 수 있어서다.
            var lookDir = Cam != null && Cam.FirstPerson ? away : -away;
            var facing = Quaternion.LookRotation(lookDir, Vector3.up);

            Doll.Teleport(foot + Vector3.up * (RagdollBuilder.HipsY + VehicleExitClearance), facing);
            faceDir = lookDir;

            if (Cam != null)
            {
                Cam.Yaw = facing.eulerAngles.y;

                // 조종석 자리에서 새 자리로 화면을 가로질러 날아오지 않게.
                Cam.SnapToTarget();
            }
            Grounded = false;
            GroundDistance = 0f;
            GroundVelocity = Vector3.zero;
            Speed = 0f;
            AirTime = 0f;
            fallPeakFootHeight = foot.y;
        }

        /// <summary>
        /// 좌석 조인트를 뗀다. <b>즉시</b> 없애야 한다.
        ///
        /// 예전 구현은 <c>connectedBody = null</c> 로 두고 <c>Destroy</c> 를 불렀다. 두 가지가
        /// 겹쳐 하차하면 캐릭터가 맵 원점으로 빨려갔다.
        ///
        ///   · <c>connectedBody</c> 가 null 이면 <c>connectedAnchor</c> 가 <b>월드 좌표</b>로
        ///     해석된다(Unity 문서). 탑승할 때 넣어 둔 값은 운반체 로컬 좌표, 즉 원점 근처의
        ///     작은 벡터였다. 그래서 조인트가 맵 원점을 앵커로 삼는 스프링이 됐다.
        ///   · <c>Destroy</c> 는 프레임 끝에 처리된다. 그 사이 물리 스텝이 돌면 위 앵커가
        ///     그대로 작동한다. 인형은 이미 하차 지점으로 옮겨 놨으므로 그대로 끌려간다.
        ///
        /// 계측값: 하차 지점 (18.00, 0.36, -12.20) 에서 1초 만에 (0.00, 0.06, -0.73) 로
        /// 21.3m 이동. <c>Exit_RiderStaysWhereItWasPlaced</c> 가 이 값을 지킨다.
        ///
        /// <c>connectedBody</c> 는 건드리지 않고 <c>DestroyImmediate</c> 로 지운다. 앵커를
        /// 옮겨 무해하게 만드는 방법도 있지만, 조인트를 떼는 시점이 인형을 옮기는 시점보다
        /// 앞이라 결국 "옮긴 뒤 원래 자리로 당겨지는" 문제가 남는다. 아예 없애는 편이 맞다.
        /// </summary>
        void DisconnectTransportJoint()
        {
            RidingBody = null;

            if (mountedJoint == null) return;

            DestroyImmediate(mountedJoint);
            mountedJoint = null;
        }

        void FixedUpdate() => Tick();

        /// <summary>
        /// 물리 스텝 한 번. 런타임에서는 <c>FixedUpdate</c> 가 부르고, EditMode 테스트는
        /// <c>Physics.Simulate</c> 와 짝지어 직접 부른다.
        ///
        /// 리포의 다른 물리 컴포넌트(<see cref="Parts.TractorBeam"/>,
        /// <see cref="Parts.TransportVehicle"/>, <see cref="Parts.LowGravityField"/>)가
        /// 모두 같은 방식이다. 시간은 인수로 받지 않는다 — 본문이 여러 곳에서
        /// <c>Time.fixedDeltaTime</c> 을 쓰고 있어 인수로 바꾸면 그 전부를 함께 손봐야 한다.
        /// </summary>
        public void Tick()
        {
            if (Doll == null || Doll.Hips == null) return;
            if (!JointTools.IsFinite(Doll.Hips.position)) { return; }

            // Release가 예약한 조인트 제거가 끝난 다음 틱에 속도를 넣어야 투척이 구속에 먹히지 않는다.
            if (pendingThrowBody != null)
            {
                if (!pendingThrowBody.isKinematic) pendingThrowBody.linearVelocity = pendingThrowVelocity;
                pendingThrowBody = null;
            }

            // 몸이 조인트로 붙어 있으면 보행 물리를 돌리지 않는다. 돌리면 부양·이동력이
            // 구속과 싸운다. 운반 장치는 제 속도를 알려 주고, 우주선은 강체에서 읽는다.
            if (RidingBody != null)
            {
                Grounded = true;
                GroundDistance = Tuning.RideHeight;

                if (MountedVehicle != null)
                {
                    GroundVelocity = MountedVehicle.HorizontalVelocity;
                    Speed = MountedVehicle.HorizontalSpeed;
                }
                else
                {
                    var flat = RidingBody.linearVelocity;
                    flat.y = 0f;
                    GroundVelocity = flat;
                    Speed = flat.magnitude;
                }

                AirTime = 0f;
                Thrusting = false;
                Ragdolled = false;
                return;
            }

            ProbeGround();
            UpdateFloating();
            UpdateSpeed();
            Hover();
            Locomotion();
            Balance();
            DriveLegs();
            DriveArms();
        }

        // ── 바닥 ────────────────────────────────────────────────
        void ProbeGround()
        {
            bool wasGrounded = Grounded;
            float fallSpeed = Mathf.Max(0f, -Doll.Hips.linearVelocity.y);

            groundBody = null;
            GroundVelocity = Vector3.zero;
            GroundDistance = float.MaxValue;

            Vector3 origin = Doll.Hips.position;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, Tuning.GroundProbe,
                                            RagdollBuilder.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (rayHits[i].distance >= GroundDistance) continue;
                GroundDistance = rayHits[i].distance;
                groundBody = rayHits[i].collider.attachedRigidbody;
            }

            Grounded = GroundDistance < Tuning.RideHeight + 0.22f && jumpLock <= 0f;
            if (groundBody != null) GroundVelocity = groundBody.linearVelocity;

            float footHeight = FootPosition.y;
            if (!Grounded)
            {
                fallPeakFootHeight = Mathf.Max(fallPeakFootHeight, footHeight);
            }
            else if (!wasGrounded)
            {
                float fallHeight = Mathf.Max(0f, fallPeakFootHeight - footHeight);
                ApplyFallDamage(fallHeight, fallSpeed);
                fallPeakFootHeight = footHeight;
            }
            else
            {
                fallPeakFootHeight = footHeight;
            }

            if (Grounded) AirTime = 0f; else AirTime += Time.fixedDeltaTime;

            // 착지 먼지 — 진공이라 흩어지지 않고 그대로 포물선을 그린다
            if (Grounded && !wasGrounded && fallSpeed > 1.6f)
                Puff.Regolith(FootPosition, Mathf.Clamp01(fallSpeed / 7f));
        }

        void ApplyFallDamage(float fallHeight, float impactSpeed)
        {
            if (fallHeight < Tuning.FallDamageMinHeight || impactSpeed < Tuning.FallDamageMinSpeed)
                return;

            float damage = Tuning.FallDamageBase +
                           (impactSpeed - Tuning.FallDamageMinSpeed) * Tuning.FallDamagePerSpeed;
            ApplyDamage(damage, "낙하");
        }

        void UpdateSpeed()
        {
            Vector3 v = Doll.Hips.linearVelocity;
            v.y = 0f;
            Speed = v.magnitude;

            if (jumpLock > 0f) jumpLock -= Time.fixedDeltaTime;
            if (regrabLock > 0f) regrabLock -= Time.fixedDeltaTime;

            // 머리가 골반보다 낮으면 완전히 자빠진 것 — 잠깐 조종을 놓았다가 천천히 일어난다
            bool down = Doll.Head.position.y < Doll.Hips.position.y + 0.12f;
            Ragdolled = down && !Hanging;
            float want = Ragdolled ? 0.18f : 1f;
            uprightBlend = Mathf.MoveTowards(uprightBlend, want, Time.fixedDeltaTime * (want > uprightBlend ? 0.8f : 4f));
        }

        // ── 서 있기 ─────────────────────────────────────────────
        /// <summary>
        /// 저중력장 안인지 보고 부유 상태를 갱신한다. 부유 중이면 세로 속도를 직접 준다.
        ///
        /// <b>부유하는 동안 인형의 중력을 끈다.</b> 처음에는 세로 속도만 덮어쓰면 될 줄 알았는데,
        /// 상승 가속(2m/s ÷ 0.5초 = 4m/s²)에서 행성 중력(2.8m/s²)을 빼면 순 상승이 1.2m/s² 뿐이라
        /// 관절과 접촉 저항에 먹혀 아예 뜨지 않았다(계측: 2초에 -0.01m). 중력을 끄면
        /// 기획이 정한 "0.5초 관성" 도 그대로 성립한다.
        ///
        /// 영역을 벗어나거나 저중력장이 꺼지면 중력을 다시 켜므로 그 즉시 떨어진다(기획 §14.3).
        ///
        /// 접지를 강제로 끄는 이유는 <see cref="Hover"/> 때문이다. 지면 가까이에서 떠 있으면
        /// 접지로 잡혀 지면 스프링이 부유를 밀어 올린다.
        ///
        /// 낙하 기준 높이도 계속 갱신한다. 떠 있는 것은 떨어지는 것이 아니므로,
        /// 그냥 두면 부유했다 내려올 때 그 높이차가 추락으로 집계된다.
        /// </summary>
        void UpdateFloating()
        {
            var field = Parts.LowGravityField.FindContaining(Doll.Hips.position);
            var wants = field != null && !Hanging && !IsRiding;

            // 들고 날 때 인형의 중력을 끄고 켠다. 상태가 바뀔 때만 건드려 매 스텝 강체를
            // 훑지 않는다.
            if (wants != floatingGravityOff)
            {
                for (int i = 0; i < Doll.Bodies.Count; i++)
                    if (Doll.Bodies[i] != null)
                        Doll.Bodies[i].useGravity = !wants;

                floatingGravityOff = wants;
            }

            Floating = wants;
            if (!Floating) return;

            Grounded = false;
            GroundDistance = float.MaxValue;
            GroundVelocity = Vector3.zero;
            AirTime = 0f;
            fallPeakFootHeight = Doll.Hips.position.y - RagdollBuilder.HipsY;

            // Space 로 올라가고 Ctrl 로 내려간다. 탑승 중 고도 조작과 같은 두 키다.
            float wish = (input.Jump ? 1f : 0f) - (input.ArmsUp ? 1f : 0f);
            float target = wish * PhysicsScale.FieldFloatVerticalSpeed;
            float rate = PhysicsScale.FieldFloatVerticalSpeed / PhysicsScale.FieldFloatStopSeconds;

            for (int i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null) continue;

                Vector3 v = body.linearVelocity;
                v.y = Mathf.MoveTowards(v.y, target, rate * Time.fixedDeltaTime);
                body.linearVelocity = v;
            }
        }

        void Hover()
        {
            if (!Grounded || Hanging) return;

            float offset = Tuning.RideHeight - GroundDistance;
            if (offset < -0.2f) return;

            float vy = Doll.Hips.linearVelocity.y - (groundBody != null ? groundBody.linearVelocity.y : 0f);
            float f = offset * Tuning.RideSpring - vy * Tuning.RideDamp;
            f = Mathf.Clamp(f, 0f, Tuning.RideMaxForce) * uprightBlend;

            Doll.Hips.AddForce(Vector3.up * f, ForceMode.Force);
            if (groundBody != null)
                groundBody.AddForceAtPosition(Vector3.down * f, Doll.Hips.position, ForceMode.Force);
        }

        void Locomotion()
        {
            Vector3 fwd = Cam != null ? Cam.PlanarForward : Vector3.forward;
            Vector3 right = Cam != null ? Cam.PlanarRight : Vector3.right;
            Vector3 wish = Cam != null
                ? Cam.CameraRelativeMove(input.Move)
                : right * input.Move.x + fwd * input.Move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float top = Floating
                ? PhysicsScale.FieldFloatSpeed
                : (input.Run ? Tuning.RunSpeed : Tuning.WalkSpeed);
            Vector3 targetVel = wish * top;
            targetVel.x += GroundVelocity.x;
            targetVel.z += GroundVelocity.z;

            if (Hanging)
            {
                // 매달린 동안에는 손 조인트가 이동을 결정해야 하므로 기존 물리 제어를 유지한다.
                Vector3 v = Doll.Hips.linearVelocity;
                Vector3 dv = new Vector3(targetVel.x - v.x, 0f, targetVel.z - v.z);
                Vector3 force = JointTools.Clamp(dv * Tuning.Accel * Tuning.AirControl, Tuning.AccelMax);
                if (JointTools.IsFinite(force))
                {
                    Doll.Hips.AddForce(force * 0.6f, ForceMode.Force);
                    Doll.Chest.AddForce(force * 0.4f, ForceMode.Force);
                }
            }
            else
            {
                DriveMovementVelocity(targetVel);
            }

            UpdateMovementDirection(wish, right, fwd);

            // 등짐의 자세제어 분사기가 공중에서 방향을 잡아준다는 설정
            Thrusting = !Grounded && !Hanging && wish.sqrMagnitude > 0.04f;
            if (Thrusting)
            {
                thrustPuff -= Time.fixedDeltaTime;
                if (thrustPuff <= 0f)
                {
                    thrustPuff = 0.09f;
                    Puff.Thruster(Doll.Chest.position - Doll.Chest.transform.forward * 0.26f, -wish);
                }
            }

            // 고정못을 잡고 있으면 그쪽을 계속 본다(기획 §12.1 "몸을 뒤로 젖히고").
            // 이동 입력 쪽으로 돌면 뒤로 당기는 동안 고정못을 등지게 되고, 조준이 벗어나
            // 놓았을 때 다시 잡을 수 없다.
            var facingClamp = Vector3.zero;
            if (clampGrabbed && clampGrabTarget != null && Doll.Hips != null)
            {
                facingClamp = clampGrabTarget.transform.position - Doll.Hips.position;
                facingClamp.y = 0f;
            }

            Vector3 cameraFacing = fwd;
            if (facingClamp.sqrMagnitude > 0.02f)
                cameraFacing = facingClamp.normalized;

            if (cameraFacing.sqrMagnitude > 0.02f)
                faceDir = Vector3.Slerp(faceDir, cameraFacing,
                                        1f - Mathf.Exp(-7f * Time.fixedDeltaTime));

            // 점프 — 몸 전체에 같은 속도를 얹어야 인형이 통째로 뜬다
            if (input.Jump && Grounded && !Ragdolled && jumpLock <= 0f)
            {
                for (int i = 0; i < Doll.Bodies.Count; i++)
                {
                    var b = Doll.Bodies[i];
                    var bv = b.linearVelocity;
                    bv.y = Mathf.Max(bv.y, 0f) + Tuning.JumpSpeed;
                    b.linearVelocity = bv;
                }
                jumpLock = 0.30f;
                Grounded = false;
                Puff.Regolith(FootPosition, 0.45f);
            }
        }

        /// <summary>
        /// 실제 수평 속도를 카메라 기준 좌우·전후 성분으로 바꿔 보행 포즈에 전달한다.
        /// 속도가 아직 가속 중인 첫 프레임에는 입력 방향을 사용해 애니메이션이 늦게
        /// 시작하지 않게 하고, 키를 놓은 뒤에는 남은 물리 관성도 자연스럽게 반영한다.
        /// </summary>
        void UpdateMovementDirection(Vector3 wish, Vector3 right, Vector3 forward)
        {
            Vector3 actual = Doll != null && Doll.Hips != null ? Doll.Hips.linearVelocity : Vector3.zero;
            actual.y = 0f;

            Vector3 movement = actual.sqrMagnitude > 0.0025f ? actual : wish;
            if (movement.sqrMagnitude < 0.0001f)
            {
                movementDirectionLocal = Vector2.zero;
                return;
            }

            movementDirectionLocal = new Vector2(Vector3.Dot(movement, right),
                                                 Vector3.Dot(movement, forward));
            movementDirectionLocal.Normalize();
        }

        /// <summary>
        /// 모든 몸 부위의 수평 속도를 같은 목표로 맞춰 이동 중 몸통이 뒤처지거나
        /// 관성 때문에 미끄러지는 문제를 막는다. 회전과 수직 속도는 물리 조인트에 맡긴다.
        /// </summary>
        void DriveMovementVelocity(Vector3 targetVelocity)
        {
            Vector3 target = new Vector3(targetVelocity.x, 0f, targetVelocity.z);

            // 부유 중에는 기획이 정한 관성으로 선다 — "입력을 놓으면 약 0.5초 관성 후 정지".
            float floatRate = PhysicsScale.FieldFloatSpeed / PhysicsScale.FieldFloatStopSeconds;

            float acceleration = Floating ? floatRate
                : (Grounded ? Tuning.GroundAcceleration : Tuning.AirAcceleration);
            float deceleration = Floating ? floatRate
                : (Grounded ? Tuning.GroundDeceleration : Tuning.AirAcceleration);

            for (int i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null) continue;

                Vector3 current = body.linearVelocity;
                Vector3 planar = new Vector3(current.x, 0f, current.z);
                float rate = target.sqrMagnitude < planar.sqrMagnitude ? deceleration : acceleration;
                planar = Vector3.MoveTowards(planar, target, rate * Time.fixedDeltaTime);
                body.linearVelocity = new Vector3(planar.x, current.y, planar.z);
            }
        }

        void Balance()
        {
            Quaternion desired = Quaternion.LookRotation(faceDir, Vector3.up);

            // 공중에서는 힘을 빼야 팔다리가 허우적거린다. 저중력이라 그 시간이 길다.
            float k = (Grounded ? 1f : 0.26f) * uprightBlend;
            if (Hanging) k = 0.40f;

            JointTools.SetDrive(Doll.Balance,
                                Tuning.UprightSpring * k,
                                Tuning.UprightDamp * Mathf.Max(k, 0.25f),
                                Tuning.UprightMaxTorque);
            JointTools.SetTargetLocalRotation(Doll.Balance, desired, Quaternion.identity);

            // 달릴수록 앞으로 기운다
            float lean = Mathf.Clamp(Speed / Tuning.RunSpeed, 0f, 1f) * 11f;
            JointTools.SetTargetLocalRotation(Doll.ChestJoint,
                                              Quaternion.AngleAxis(lean, Vector3.right),
                                              Quaternion.identity);

            // 고개는 카메라 피치를 조금 따라간다
            float pitch = Cam != null ? Mathf.Clamp(Cam.Pitch, -26f, 26f) : 0f;
            JointTools.SetTargetLocalRotation(Doll.HeadJoint,
                                              Quaternion.AngleAxis(pitch * 0.5f, Vector3.right),
                                              Quaternion.identity);
        }

        // ── 걷기 ────────────────────────────────────────────────
        void DriveLegs()
        {
            bool walking = Grounded && !Ragdolled && Speed > 0.40f;

            if (walking)
            {
                float rate = Mathf.Clamp(Speed / Tuning.WalkSpeed, 0.55f, 1.8f);
                StepPhase += Time.fixedDeltaTime * (Mathf.PI * 2f / Tuning.StepPeriod) * rate;
                if (StepPhase > Mathf.PI * 2f) StepPhase -= Mathf.PI * 2f;
            }
            else
            {
                StepPhase = Mathf.MoveTowardsAngle(StepPhase * Mathf.Rad2Deg, 0f,
                                                   240f * Time.fixedDeltaTime) * Mathf.Deg2Rad;
            }

            float swing = walking ? Mathf.Lerp(22f, 40f, Mathf.Clamp01(Speed / Tuning.RunSpeed)) : 0f;
            PoseLeg(Doll.LeftLeg, StepPhase, swing, walking);
            PoseLeg(Doll.RightLeg, StepPhase + Mathf.PI, swing, walking);
        }

        void PoseLeg(Leg leg, float phase, float swing, bool walking)
        {
            float thigh, knee;
            float lateral;

            if (!Grounded && !walking)
            {
                // 저중력에서 오래 떠 있다 — 다리를 살짝 웅크리면 '둥실' 하는 그림이 된다
                thigh = -14f; knee = 34f; lateral = 0f;
            }
            else if (!walking)
            {
                thigh = 0f; knee = 4f; lateral = 0f;             // 서 있을 땐 곧게
            }
            else
            {
                float forward = Mathf.Clamp(movementDirectionLocal.y, -1f, 1f);
                float side = Mathf.Clamp(movementDirectionLocal.x, -1f, 1f);

                // 전후 걸음과 좌우 걸음을 같은 주기로 합성한다. S는 전진 스텝을
                // 반전하고, A/D는 고관절의 Z축과 발목 보정으로 옆걸음 그림을 만든다.
                thigh = -Mathf.Sin(phase) * swing * forward;
                lateral = Mathf.Sin(phase) * Tuning.SideStepSwing * side;
                knee = Mathf.Max(0f, Mathf.Sin(phase + 1.1f))
                       * (swing * 1.7f * Mathf.Abs(forward)
                          + Tuning.SideStepKneeSwing * Mathf.Abs(side));
            }

            if (Ragdolled) { thigh *= 0.2f; knee *= 0.2f; lateral *= 0.2f; }

            Quaternion hipPose = Quaternion.AngleAxis(lateral, Vector3.forward)
                                 * Quaternion.AngleAxis(thigh, Vector3.right);
            JointTools.SetTargetLocalRotation(leg.Hip, hipPose, leg.HipStart);
            JointTools.SetTargetLocalRotation(leg.Knee, Quaternion.AngleAxis(knee, Vector3.right), leg.KneeStart);
            Quaternion anklePose = Quaternion.AngleAxis(
                                       Mathf.Clamp(-thigh * 0.35f, -20f, 20f), Vector3.right)
                                   * Quaternion.AngleAxis(
                                       Mathf.Clamp(-lateral * 0.45f, -12f, 12f), Vector3.forward);
            JointTools.SetTargetLocalRotation(leg.Ankle, anklePose, leg.AnkleStart);
        }

        // ── 팔 ──────────────────────────────────────────────────
        void DriveArms()
        {
            Vector3 aim = Cam != null ? Cam.AimDirection : Doll.Chest.transform.forward;
            if (input.ArmsUp) aim = Vector3.Slerp(aim, Vector3.up, 0.65f).normalized;

            bool leftPressed = input.GrabLeft && !previousGrabLeft;
            bool rightPressed = input.GrabRight && !previousGrabRight;
            previousGrabLeft = input.GrabLeft;
            previousGrabRight = input.GrabRight;

            if (toolThrowRegrabLock > 0f) toolThrowRegrabLock -= Time.fixedDeltaTime;

            // 도구는 반드시 양손이 같은 대상을 잡고 있어야 한다. 조인트가 하나라도 끊기면
            // 남은 손도 함께 놓아 다음 클릭에서 다시 양손으로 잡게 한다.
            if (toolHeld != null && (LeftHand.Held != toolHeld || RightHand.Held != toolHeld))
            {
                ReleaseHand(LeftHand);
                ReleaseHand(RightHand);
                ResetToolState();
            }

            if (toolHeld != null)
            {
                // Q를 놓는 순간에만 놓기/투척을 처리하고, 충전 중에는 두 손의 운반 자세를 유지한다.
                if (ProcessToolAction(aim))
                {
                    activeTool?.SetInput(new ToolInputFrame(), CreateToolAimContext(aim));
                    SyncHeldObjectOwnerCollision();
                    return;
                }
                SetActiveToolInput(aim, GetToolUtilityHeld());
                HoldToolPose(aim);
                DriveArm(Doll.Left, true, aim);
                DriveArm(Doll.Right, true, aim);
                SyncHeldObjectOwnerCollision();
                return;
            }

            chargingTool = false;
            toolCharge = 0f;
            toolActionHeld = false;

            // 고정쇠는 좌·우 어느 버튼이든 누르면 먼저 잡힌다. 이후 카메라를
            // 위로 일정 각도 들었을 때만 해제 유지 판정으로 넘어간다.
            // 고정쇠가 조준 중이면 일반 손잡이/도구 탐색보다 우선한다.
            bool clampInteractionActive = ProcessClampInteraction();
            if (clampInteractionActive)
            {
                DriveArm(Doll.Left, input.GrabLeft, aim);
                DriveArm(Doll.Right, input.GrabRight, aim);
                SyncHeldObjectOwnerCollision();
                return;
            }

            // 좌클릭과 우클릭 중 어느 쪽이든 Tool 역할의 대상을 조준하면 양손으로 함께 잡는다.
            if ((input.GrabLeft || input.GrabRight) && toolThrowRegrabLock <= 0f && TryGrabTool(aim))
            {
                DriveArm(Doll.Left, true, aim);
                DriveArm(Doll.Right, true, aim);
                if (leftPressed || rightPressed)
                    LogInteraction("양손 도구 집기", leftPressed, rightPressed, toolHeld, toolBody);
                SyncHeldObjectOwnerCollision();
                return;
            }

            DriveArm(Doll.Left, input.GrabLeft, aim);
            DriveArm(Doll.Right, input.GrabRight, aim);
            ClimbAssist();
            if (leftPressed) LogHandInteraction("좌클릭", LeftHand);
            if (rightPressed) LogHandInteraction("우클릭", RightHand);
            SyncHeldObjectOwnerCollision();
        }

        /// <summary>
        /// 고정쇠를 버튼으로 먼저 잡고, 카메라를 위로 일정 각도 들어 올리면
        /// 해제 유지 홀드를 시작한다. 기준을 넘은 뒤에는 위로 계속 움직이지 않아도
        /// 버튼을 누르고 있는 동안 유지 시간이 진행된다.
        /// </summary>
        bool ProcessClampInteraction()
        {
            if (ClampInteractor == null) return false;

            bool buttonHeld = input.GrabLeft || input.GrabRight;

            if (!buttonHeld)
            {
                ClampInteractor.Cancel();
                ResetClampGrab();
                ClampInteractor.Tick(0f);
                return false;
            }

            if (clampGrabTarget != null && clampGrabTarget.IsReleased)
            {
                ClampInteractor.Cancel();
                ResetClampGrab();
                ClampInteractor.Tick(0f);
                return false;
            }

            if (!clampGrabbed)
            {
                // 잡기 시작 순간의 포커스를 고정한다. 이후 무거운 카메라 반동으로
                // 조준점이 살짝 움직여도 처음 잡은 고정못을 놓치지 않는다.
                ClampInteractor.Tick(0f);
                var focus = ClampInteractor.Focus as PartClamp;
                if (focus != null && !focus.IsReleased && focus.TryJoinPull())
                {
                    clampGrabbed = true;
                    clampGrabTarget = focus;
                }
            }

            if (!clampGrabbed) return false;

            PullGrabbedClamp();
            return true;
        }

        /// <summary>
        /// 잡은 고정못을 뒤로 당긴다(기획 §12.1 "고정못 반대 방향으로 뒤로 이동해 뽑는다").
        ///
        /// <b>이동 입력의 방향을 본다. 실제로 움직인 거리를 보지 않는다.</b> 고정못이
        /// 몸을 붙들고 있으므로 아무리 밀어도 거의 제자리다 — 기획의 "발을 구르며
        /// 낑낑대는" 그림이 그것이다. 거리로 재면 진행이 사실상 멈춘다.
        ///
        /// 제자리에서 버튼만 누르고 있으면 진행되지 않는다. 예전 방식(마우스를 위로
        /// 16도)은 한 번 들어올린 뒤에는 가만히 있어도 시간이 흘렀다.
        /// </summary>
        void PullGrabbedClamp()
        {
            ClampPulling = false;
            if (clampGrabTarget == null || Doll == null || Doll.Hips == null) return;

            var fwd = Cam != null ? Cam.PlanarForward : Vector3.forward;
            var right = Cam != null ? Cam.PlanarRight : Vector3.right;
            var wish = Cam != null
                ? Cam.CameraRelativeMove(input.Move)
                : right * input.Move.x + fwd * input.Move.y;
            wish.y = 0f;
            if (wish.sqrMagnitude < 0.0001f) return;

            // 고정못에서 몸을 향하는 수평 방향이 "뒤" 다.
            var away = Doll.Hips.position - clampGrabTarget.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return;

            // 너무 멀어졌으면 손을 뗀다. 진행분은 남는다(기획 §12.1).
            // 잡은 뒤 범위를 다시 보지 않으면 고정못을 잡은 채 걸어서 멀어지면서
            // 계속 뽑히게 된다.
            if (away.magnitude > ClampInteractionRange)
            {
                ResetClampGrab();
                return;
            }

            var alignment = Vector3.Dot(wish.normalized, away.normalized);
            if (alignment < PhysicsScale.ClampPullAlignment) return;

            var wasLast = clampGrabTarget.Part != null
                          && clampGrabTarget.Part.RemainingRequiredClamps == 1;

            ClampPulling = clampGrabTarget.Pull(Time.fixedDeltaTime);
            if (ClampPulling) TetherToClamp(away.normalized);

            if (!clampGrabTarget.IsReleased) return;

            // 기획 §12.2 — "마지막 고정못이 빠지면 플레이어가 뒤로 넘어질 수 있다".
            // 뒤로 당기던 힘이 갑자기 풀리는 것이다. 넘어짐은 Balance 가 스스로 세운다.
            if (wasLast) SnapBackFromClamp(away.normalized);

            ResetClampGrab();
        }

        /// <summary>
        /// 당기는 동안 뒤로 밀려나는 속도를 묶는다.
        ///
        /// 기획 §12.1 의 "몸을 뒤로 젖히고 발을 구르며 낑낑대는" 은 뒤로 밀지만 거의
        /// 제자리라는 뜻이다. 고정못이 몸을 붙들고 있기 때문이다.
        ///
        /// 옆·앞 방향은 건드리지 않는다. 뽑는 것과 무관한 이동을 막으면 조작이 굳는다.
        /// </summary>
        void TetherToClamp(Vector3 away)
        {
            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                var velocity = body.linearVelocity;
                var outward = Vector3.Dot(velocity, away);
                if (outward <= PhysicsScale.ClampPullTetherSpeed) continue;

                body.linearVelocity = velocity - away * (outward - PhysicsScale.ClampPullTetherSpeed);
            }
        }

        /// <summary>마지막 고정못이 빠져 뒤로 넘어진다(기획 §12.2).</summary>
        void SnapBackFromClamp(Vector3 away)
        {
            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                body.linearVelocity += away * PhysicsScale.ClampSnapBackSpeed;
            }

            Ragdolled = true;
        }

        /// <summary>
        /// 고정못에서 손을 뗀다. <b>진행분은 지우지 않는다</b> — 기획 §12.1 "좌클릭을
        /// 놓거나 운석에 맞아도 빠진 정도는 유지된다".
        /// </summary>
        void ResetClampGrab()
        {
            if (clampGrabbed && clampGrabTarget != null) clampGrabTarget.LeavePull();

            clampGrabbed = false;
            clampGrabTarget = null;
            ClampPulling = false;
        }

        void LogHeldToolInteraction(bool leftPressed, bool rightPressed)
        {
            LogInteraction("도구 운반 중·양손 유지", leftPressed, rightPressed, toolHeld, toolBody);
        }

        void LogHandInteraction(string button, Hand hand)
        {
            if ((hand.Gripping || hand.Carrying) && hand.Held != null)
            {
                string branch = hand.HeldBody != null ? "개별 물체 운반" : "고정형 물체 짚기";
                LogInteraction(branch, button == "좌클릭", button == "우클릭", hand.Held, hand.HeldBody);
                return;
            }

            if (hand.Aimed != null)
            {
                LogInteraction("대상 탐색됨·잡기 실패", button == "좌클릭", button == "우클릭", hand.Aimed, null);
                return;
            }

            LogInteraction("대상 없음", button == "좌클릭", button == "우클릭", null, null);
        }

        static void LogInteraction(string branch, bool left, bool right, Grabbable target, Rigidbody body)
        {
            string buttons = left && right ? "좌+우클릭" : left ? "좌클릭" : "우클릭";
            string label = target != null && !string.IsNullOrEmpty(target.Label) ? target.Label : "없음";
            string mass = body != null ? body.mass.ToString("0.0") + "kg" : "고정";
            Debug.Log("[SpaceFlop][상호작용] " + buttons + " → " + branch
                      + " | 대상=" + label + " | 질량=" + mass);
        }

        bool TryGrabTool(Vector3 aim)
        {
            if (LeftHand.Gripping || LeftHand.Carrying || RightHand.Gripping || RightHand.Carrying)
                return false;

            Grabbable targetGrabbable;
            Rigidbody body;
            Vector3 point;
            bool found = false;

            // 두 버튼이 동시에 들어오는 경우에도 한쪽 검색이 빗나갔다고 실패하지 않도록
            // 클릭된 손을 순서대로 살핀다. 도구를 찾은 손이 어느 쪽인지는 중요하지 않다.
            if (input.GrabLeft)
                found = FindTool(Doll.Left, LeftHand, aim, out targetGrabbable, out body, out point);
            else
            {
                targetGrabbable = null;
                body = null;
                point = Vector3.zero;
            }

            if (!found && input.GrabRight)
                found = FindTool(Doll.Right, RightHand, aim, out targetGrabbable, out body, out point);
            if (!found) return false;

            // 도구는 역할에 따라 양손 논리 고정 그립으로 잡는다. 잡은 직후 Rigidbody를
            // 키네마틱으로 전환하고 포즈를 직접 제어하므로 양손 FixedJoint 반작용을 만들지 않는다.
            bool left = LeftHand.BraceTo(targetGrabbable, body);
            bool right = RightHand.BraceTo(targetGrabbable, body);
            if (left && right)
            {
                toolHeld = targetGrabbable;
                toolBody = body;
                toolBodyHadGravity = body.useGravity;
                activeTool = targetGrabbable.GetComponent<ToolBase>();
                if (activeTool == null)
                    activeTool = targetGrabbable.GetComponentInChildren<ToolBase>(true);

                ResetToolInputState();
                // The click that picked up the tool is consumed until that button is
                // released, so picking up with the primary button cannot also fire it.
                suppressToolPrimary = GetToolPrimaryHeld();
                suppressToolSecondary = GetToolSecondaryHeld();
                suppressToolUtility = GetToolUtilityHeld();

                BeginCarriedBody(body);
                activeTool?.Equip(this, body);
                HoldToolPose(aim);
                SyncHeldObjectOwnerCollision();
                return true;
            }

            if (left) ReleaseHand(LeftHand);
            if (right) ReleaseHand(RightHand);
            return false;
        }

        ToolAimContext CreateToolAimContext(Vector3 aim)
        {
            return new ToolAimContext(this, Cam, aim);
        }

        bool GetToolPrimaryHeld()
        {
            // Controls.Read fills both fields. The fallback preserves scripted callers
            // that still populate the legacy grab field directly.
            return input.ToolPrimary || input.GrabLeft;
        }

        bool GetToolSecondaryHeld()
        {
            return input.ToolSecondary || input.GrabRight;
        }

        bool GetToolUtilityHeld()
        {
            return input.ToolUtility || input.ToolAction;
        }

        void SetActiveToolInput(Vector3 aim, bool blockActions)
        {
            bool primaryHeld = GetToolPrimaryHeld();
            bool secondaryHeld = GetToolSecondaryHeld();
            bool utilityHeld = GetToolUtilityHeld();

            if (suppressToolPrimary && !primaryHeld) suppressToolPrimary = false;
            if (suppressToolSecondary && !secondaryHeld) suppressToolSecondary = false;
            if (suppressToolUtility && !utilityHeld) suppressToolUtility = false;

            bool primary = primaryHeld && !suppressToolPrimary && !blockActions;
            bool secondary = secondaryHeld && !suppressToolSecondary && !blockActions;
            bool utility = utilityHeld && !suppressToolUtility;

            var toolInput = new ToolInputFrame
            {
                PrimaryHeld = primary,
                SecondaryHeld = secondary,
                UtilityHeld = utility,
                PrimaryPressed = primary && !previousToolPrimary,
                SecondaryPressed = secondary && !previousToolSecondary,
                UtilityPressed = utility && !previousToolUtility,
                PrimaryReleased = !primary && previousToolPrimary,
                SecondaryReleased = !secondary && previousToolSecondary,
                UtilityReleased = !utility && previousToolUtility
            };

            previousToolPrimary = primary;
            previousToolSecondary = secondary;
            previousToolUtility = utility;

            if (activeTool != null)
                activeTool.SetInput(toolInput, CreateToolAimContext(aim));
        }

        void ResetToolInputState()
        {
            previousToolPrimary = false;
            previousToolSecondary = false;
            previousToolUtility = false;
            suppressToolPrimary = false;
            suppressToolSecondary = false;
            suppressToolUtility = false;
        }

        static bool FindTool(Arm arm, Hand hand, Vector3 aim,
                             out Grabbable targetGrabbable, out Rigidbody body, out Vector3 point)
        {
            Vector3 shoulder = arm.ShoulderAnchor != null ? arm.ShoulderAnchor.position : hand.Body.position;
            Vector3 target = shoulder + aim * Tuning.ArmReach;
            targetGrabbable = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim,
                                          Tuning.GrabAssistAngle, out point, out body);
            hand.Aimed = targetGrabbable;
            hand.AimedPoint = point;
            return IsUsableTool(targetGrabbable, body);
        }

        static bool IsUsableTool(Grabbable target, Rigidbody body)
        {
            // 고정 레일·케이블은 기존의 한 손 매달리기를 유지한다. 도구 역할로 지정된
            // 가벼운 동적 물체만 양손 고정 그립과 전방 조준 자세를 사용한다.
            return target != null && target.IsTool && !target.Climbable
                   && body != null && !body.isKinematic && body.mass < Tuning.FixedGripMass;
        }

        bool ProcessToolAction(Vector3 aim)
        {
            if (GetToolUtilityHeld())
            {
                toolCharge = Mathf.Clamp01(toolCharge + Time.fixedDeltaTime / Tuning.ToolChargeDuration);
                toolActionHeld = true;
                chargingTool = true;
                return false;
            }

            if (!toolActionHeld) { chargingTool = false; return false; }

            if (toolCharge >= Tuning.ToolDropChargeThreshold)
                ThrowTool(aim, toolCharge);
            else
                DropTool();
            return true;
        }

        void DropTool()
        {
            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);
            ResetToolState();
            toolThrowRegrabLock = Tuning.ToolThrowRegrabLock;
        }

        void ThrowTool(Vector3 aim, float charge)
        {
            var target = toolHeld;
            Rigidbody body = LeftHand.Held == target ? LeftHand.HeldBody : RightHand.HeldBody;
            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
            Vector3 direction = aim.sqrMagnitude > 1e-4f ? aim.normalized : Doll.Chest.transform.forward;

            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);
            ResetToolState();
            toolThrowRegrabLock = Tuning.ToolThrowRegrabLock;

            if (body != null && !body.isKinematic)
            {
                pendingThrowBody = body;
                pendingThrowVelocity = velocity + direction * (Tuning.ToolThrowSpeed * Mathf.Clamp01(charge));
            }
        }

        void ResetToolState()
        {
            ClearHeldObjectOwnerCollision();
            activeTool?.Unequip();
            activeTool = null;
            RestoreCarriedBodyIfUnused(toolBody);
            if (toolBody != null) toolBody.useGravity = toolBodyHadGravity;
            toolBody = null;
            toolBodyHadGravity = false;
            toolHeld = null;
            toolCharge = 0f;
            toolActionHeld = false;
            chargingTool = false;
            ResetToolInputState();
        }

        /// <summary>
        /// 현재 PlayerRig의 콜라이더와 현재 들고 있는 물체의 콜라이더 쌍만 충돌을 끈다.
        /// Carrying과 Gripping을 모두 포함하므로 운반체와 Tool에 공통 적용된다.
        /// 레이어 충돌 매트릭스는 건드리지 않으므로 다른 플레이어와 도구의 충돌은 유지된다.
        /// </summary>
        void SyncHeldObjectOwnerCollision()
        {
            // 논리 운반 상태와 도구의 논리 고정 그립 상태 모두 소유자-물체 충돌 무시 규칙을 탄다.
            Rigidbody left = GetHeldBody(LeftHand);
            Rigidbody right = GetHeldBody(RightHand);

            if (left == null)
            {
                left = right;
                right = null;
            }
            else if (left == right)
            {
                right = null;
            }

            if (left == ignoredHeldBodyA && right == ignoredHeldBodyB)
            {
                // A moving/reenabled collider can rebuild its contact state. Reassert
                // the explicit owner-held pairs even when the held-body cache is unchanged.
                IgnoreHeldObjectFromOwner(left);
                IgnoreHeldObjectFromOwner(right);
                return;
            }

            ClearHeldObjectOwnerCollision();
            ignoredHeldBodyA = left;
            ignoredHeldBodyB = right;
            IgnoreHeldObjectFromOwner(left);
            IgnoreHeldObjectFromOwner(right);
        }

        void IgnoreHeldObjectFromOwner(Rigidbody body)
        {
            if (body == null || Doll == null) return;

            // Include inactive child colliders too. If a tool model enables a collider
            // after pickup, the next physics tick will still restore the owner-only ignore.
            var heldColliders = body.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < Doll.Colliders.Count; i++)
            {
                var playerCollider = Doll.Colliders[i];
                if (playerCollider == null || !playerCollider.enabled || !playerCollider.gameObject.activeInHierarchy)
                    continue;

                for (int k = 0; k < heldColliders.Length; k++)
                {
                    var heldCollider = heldColliders[k];
                    if (heldCollider == null || playerCollider == heldCollider
                        || !heldCollider.enabled || !heldCollider.gameObject.activeInHierarchy)
                        continue;
                    if (Physics.GetIgnoreCollision(playerCollider, heldCollider)) continue;

                    Physics.IgnoreCollision(playerCollider, heldCollider, true);
                    if (!HasTrackedHeldObjectPair(playerCollider, heldCollider))
                    {
                        heldObjectCollisionPairs.Add(new CollisionPair
                        {
                            Player = playerCollider,
                            Held = heldCollider
                        });
                    }
                }
            }
        }

        bool HasTrackedHeldObjectPair(Collider playerCollider, Collider heldCollider)
        {
            for (int i = 0; i < heldObjectCollisionPairs.Count; i++)
            {
                var pair = heldObjectCollisionPairs[i];
                if (pair.Player == playerCollider && pair.Held == heldCollider)
                    return true;
            }
            return false;
        }

        void ClearHeldObjectOwnerCollision()
        {
            for (int i = heldObjectCollisionPairs.Count - 1; i >= 0; i--)
            {
                var pair = heldObjectCollisionPairs[i];
                if (pair.Player != null && pair.Held != null)
                    Physics.IgnoreCollision(pair.Player, pair.Held, false);
            }
            heldObjectCollisionPairs.Clear();
            ignoredHeldBodyA = null;
            ignoredHeldBodyB = null;
        }

        static Rigidbody GetHeldBody(Hand hand)
        {
            if (hand == null || hand.HeldBody == null) return null;
            return hand.Carrying || hand.Gripping ? hand.HeldBody : null;
        }

        void BeginCarriedBody(Rigidbody body)
        {
            if (body == null) return;

            if (!carriedBodies.ContainsKey(body))
            {
                carriedBodies.Add(body, new CarriedBodyState
                {
                    WasKinematic = body.isKinematic,
                    HadGravity = body.useGravity,
                    LinearVelocity = body.linearVelocity,
                    AngularVelocity = body.angularVelocity,
                    CollisionDetectionMode = body.collisionDetectionMode,
                    Interpolation = body.interpolation,
                    FollowPosition = body.position,
                    FollowInitialized = false
                });
            }

            body.isKinematic = true;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void MoveCarriedBody(Rigidbody body, Vector3 aim)
        {
            if (body == null) return;
            if (!carriedBodies.ContainsKey(body)) BeginCarriedBody(body);

            Vector3 direction = aim.sqrMagnitude > 1e-4f ? aim.normalized : Doll.Chest.transform.forward;
            Vector3 cameraOrigin = Cam != null ? Cam.transform.position : Doll.Chest.position;
            Vector3 holdPoint = cameraOrigin + direction * Tuning.ToolHoldDistance;

            body.isKinematic = true;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            CarriedBodyState state = carriedBodies[body];
            Vector3 desiredPosition = body.position + holdPoint - body.worldCenterOfMass;
            if (!state.FollowInitialized)
            {
                // Pickup is responsive, while every following physics step is smoothed.
                state.FollowPosition = desiredPosition;
                state.FollowInitialized = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-Tuning.CarryPositionSmoothing * Time.fixedDeltaTime);
                state.FollowPosition = Vector3.Lerp(state.FollowPosition, desiredPosition, blend);
            }

            carriedBodies[body] = state;
            body.MovePosition(state.FollowPosition);
        }

        void HoldToolPose(Vector3 aim)
        {
            if (toolBody == null) return;

            MoveCarriedBody(toolBody, aim);

            Vector3 direction = aim.sqrMagnitude > 1e-4f ? aim.normalized : Doll.Chest.transform.forward;
            Vector3 up = Cam != null ? Cam.transform.up : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.98f)
                up = Cam != null ? Cam.transform.right : Vector3.right;

            Quaternion targetRotation = Quaternion.LookRotation(direction, up);
            float blend = 1f - Mathf.Exp(-Tuning.CarryRotationSmoothing * Time.fixedDeltaTime);
            toolBody.MoveRotation(Quaternion.Slerp(toolBody.rotation, targetRotation, blend));
        }

        void ReleaseHand(Hand hand)
        {
            if (hand == null) return;

            Rigidbody body = hand.HeldBody;
            hand.Release();
            RestoreCarriedBodyIfUnused(body);
        }

        void RestoreCarriedBodyIfUnused(Rigidbody body)
        {
            if (body == null ||
                (LeftHand.HeldBody == body && (LeftHand.Carrying || LeftHand.Gripping)) ||
                (RightHand.HeldBody == body && (RightHand.Carrying || RightHand.Gripping)))
                return;

            CarriedBodyState state;
            if (!carriedBodies.TryGetValue(body, out state)) return;

            body.isKinematic = state.WasKinematic;
            body.useGravity = state.HadGravity;
            body.collisionDetectionMode = state.CollisionDetectionMode;
            body.interpolation = state.Interpolation;
            body.linearVelocity = state.LinearVelocity;
            body.angularVelocity = state.AngularVelocity;
            carriedBodies.Remove(body);
        }

        void RestoreAllCarriedBodies()
        {
            foreach (var pair in carriedBodies)
            {
                Rigidbody body = pair.Key;
                if (body == null) continue;

                CarriedBodyState state = pair.Value;
                body.isKinematic = state.WasKinematic;
                body.useGravity = state.HadGravity;
                body.collisionDetectionMode = state.CollisionDetectionMode;
                body.interpolation = state.Interpolation;
                body.linearVelocity = state.LinearVelocity;
                body.angularVelocity = state.AngularVelocity;
            }
            carriedBodies.Clear();
        }

        void DriveArm(Arm arm, bool reaching, Vector3 aim)
        {
            var hand = arm.Hand;

            if (!reaching)
            {
                if (hand.Gripping || hand.Carrying)
                {
                    ReleaseHand(hand);
                    regrabLock = Tuning.RegrabLock;
                }
                UpdateAimedTarget(arm, hand, aim);
                JointTools.SetDrive(arm.Shoulder, 26f, 4f);
                JointTools.SetDrive(arm.Elbow, 18f, 3f);
                JointTools.SetTargetLocalRotation(arm.Shoulder, Quaternion.identity, arm.ShoulderStart);
                JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);
                return;
            }

            // 어깨를 굳혀 팔을 조준 방향으로 세운다
            JointTools.SetDrive(arm.Shoulder, 220f, 18f);
            JointTools.SetDrive(arm.Elbow, 90f, 9f);

            Vector3 shoulder = arm.ShoulderAnchor != null ? arm.ShoulderAnchor.position : Doll.Chest.position;
            Vector3 target = shoulder + aim * Tuning.ArmReach;
            bool toolTarget = false;

            // ── 자석 손 ────────────────────────────────────────
            // 뻗은 손끝 근처에 잡을 것이 있으면 손 목표를 그쪽으로 옮긴다.
            // 목표를 팔 길이 안으로 자르는 것이 중요하다 — 넘기면 팔이 뻗다 못해 뜯긴다.
            if (!hand.Gripping && !hand.Carrying)
            {
                Vector3 p; Rigidbody b;
                var g = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim, Tuning.GrabAssistAngle, out p, out b);
                hand.Aimed = g;
                hand.AimedPoint = p;
                toolTarget = IsUsableTool(g, b);
                if (g != null)
                {
                    Vector3 dir = p - shoulder;
                    if (dir.sqrMagnitude > 1e-4f)
                        target = shoulder + dir.normalized * Mathf.Min(dir.magnitude, Tuning.ArmReach);
                }
            }
            else
            {
                hand.Aimed = hand.Held;
                hand.AimedPoint = hand.Body.position;
            }

            Vector3 armDir = target - shoulder;
            if (armDir.sqrMagnitude < 1e-4f) armDir = aim;
            armDir.Normalize();

            Quaternion aimWorld = Quaternion.FromToRotation(Vector3.down, armDir);
            Quaternion aimLocal = Quaternion.Inverse(Doll.Chest.rotation) * aimWorld;
            JointTools.SetTargetLocalRotation(arm.Shoulder, aimLocal, arm.ShoulderStart);
            JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);

            // Tool은 양손 고정 그립 상태지만 위치·회전은 PlayerRig가 직접 제어한다.
            // 손에 다시 힘을 가하면 도구 포즈 제어와 팔 물리가 서로 당겨 플레이어를 날린다.
            if (toolHeld != null && hand.Held == toolHeld)
                return;

            if (hand.Carrying)
            {
                if (toolHeld != null && hand.Held == toolHeld) CarryTool(aim);
                else Carry(hand, aim);
                return;
            }

            Vector3 dp = target - hand.Body.position;
            Vector3 dv = Doll.Chest.linearVelocity - hand.Body.linearVelocity;
            Vector3 f = JointTools.Clamp(dp * Tuning.HandForce + dv * Tuning.HandDamp, Tuning.HandMaxForce);
            if (!JointTools.IsFinite(f)) return;

            if (hand.Hanging)
            {
                // 고정물을 잡은 손은 이미 붙어 있다 — 힘을 몸쪽에 실어야 몸이 딸려 올라간다
                Doll.Chest.AddForce(-f * Tuning.HangPull, ForceMode.Force);
            }
            else
            {
                hand.Body.AddForce(f, ForceMode.Force);
                Doll.Chest.AddForce(-f * 0.25f, ForceMode.Force);
                // Transport 역할의 가벼운 대상은 한 손 운반 경로로 잡는다.
                if (regrabLock <= 0f && !toolTarget && hand.TryGrip() && hand.Carrying)
                    BeginCarriedBody(hand.HeldBody);
            }
        }

        void UpdateAimedTarget(Arm arm, Hand hand, Vector3 aim)
        {
            if (hand == null || hand.Body == null)
            {
                if (hand != null) hand.Aimed = null;
                return;
            }

            Vector3 shoulder = arm.ShoulderAnchor != null ? arm.ShoulderAnchor.position : Doll.Chest.position;
            Vector3 target = shoulder + aim * Tuning.ArmReach;
            Vector3 point;
            Rigidbody body;
            hand.Aimed = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim,
                                     Tuning.GrabAssistAngle, out point, out body);
            hand.AimedPoint = point;
        }

        /// <summary>
        /// Tool 역할의 도구를 중력과 누적 속도의 영향 없이 크로스헤드 앞에 고정하고,
        /// 모델의 +Z 전방이 조준 방향을 향하도록 회전한다.
        /// </summary>
        void CarryTool(Vector3 aim)
        {
            MoveCarriedBody(toolBody, aim);
        }

        void Carry(Hand hand, Vector3 aim)
        {
            MoveCarriedBody(hand != null ? hand.HeldBody : null, aim);
        }

        void ClimbAssist()
        {
            if (!Hanging) return;

            float best = float.MinValue;
            if (LeftHand.Hanging) best = Mathf.Max(best, LeftHand.Body.position.y);
            if (RightHand.Hanging) best = Mathf.Max(best, RightHand.Body.position.y);

            // 손이 골반보다 위에 있을 때만 기어오를 수 있다
            float over = best - (Doll.Hips.position.y + 0.16f);
            if (over <= 0f) return;

            float pull = Tuning.ClimbPull * Mathf.Clamp01(over / 0.35f);
            if (input.ArmsUp || input.Move.y > 0.1f) pull *= 2.2f;

            Doll.Chest.AddForce(Vector3.up * pull, ForceMode.Force);
            Doll.Hips.AddForce(Vector3.up * pull * 0.7f, ForceMode.Force);
        }
    }
}
