using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// Part_1의 고정못이 모두 풀렸을 때만 파츠를 견인 조종 모듈로 전환한다.
    ///
    /// 전환 전에는 일반 SpaceshipPart로만 동작한다. 마지막 고정못 해제 이벤트 안에서
    /// 기존 파츠용 물리와 상호작용을 끄고, 같은 루트에 TractorBeam과 TransportVehicle을
    /// 붙인다. 그래서 씬 시작부터 별도 견인 장치를 둘 필요가 없고, Part_1 하나가 해제
    /// 이후의 탑승·운전·견인을 모두 맡는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpaceshipPart))]
    public sealed class PartTractorActivator : MonoBehaviour
    {
        SpaceshipPart part;
        TransportVehicle vehicle;
        bool activated;
        bool subscribed;
        [SerializeField, Tooltip("운반 후 상단 조립 파츠로 되돌아갈 수 있는 기체 (컨셉 씬)")]
        bool _assembleAsFinalModule;
        [SerializeField] SpaceshipPart _dockTarget;
        Transform visual;
        Vector3 visualRootLocal;
        float visualRelativeYaw;
        Transform[] partChildren;
        Vector3[] childPositions;
        Quaternion[] childRotations;
        Collider[] partColliders;
        bool[] colliderEnabled;
        RigidbodyConstraints partConstraints;

        public bool IsDocked { get; private set; }
        public void ConfigureFinalModule(bool enabled, SpaceshipPart dockTarget = null)
        { _assembleAsFinalModule = enabled; _dockTarget = dockTarget; }

        public bool IsActivated => activated;
        public TransportVehicle Vehicle => vehicle;

        void Awake() => part = GetComponent<SpaceshipPart>();

        void OnEnable() => Initialize();

        public void Initialize()
        {
            if (part == null) part = GetComponent<SpaceshipPart>();
            if (part == null) return;

            if (!subscribed) { part.StateChanged += OnPartStateChanged; subscribed = true; }
            part.Initialize();

            // 저장된/테스트 상태가 이미 Free인 경우에도 컨트롤러가 놓치지 않게 한다.
            if (part.State == PartState.Free) Activate();
        }

        void OnDisable()
        {
            if (part != null) part.StateChanged -= OnPartStateChanged;
            subscribed = false;
        }

        void OnPartStateChanged(PartState previous, PartState next)
        {
            if (next == PartState.Free) Activate();
        }

        void Activate()
        {
            if (activated || part == null || part.State != PartState.Free) return;

            var startPosition = transform.position;
            var startYaw = transform.eulerAngles.y;

            if (_assembleAsFinalModule)
            {
                CapturePartPose();
                if (visual != null) startYaw = visual.eulerAngles.y;
            }

            // 기존 파츠의 몸체·소켓·밀기·손잡이를 먼저 끈다. 새 운반 플랫폼과 충돌하거나
            // E 입력을 가로채지 않도록 같은 루트에 두 물리 표현을 동시에 남기지 않는다.
            DisablePartRuntime();
            Physics.SyncTransforms();

            var beam = GetComponent<TractorBeam>();
            if (beam == null) beam = gameObject.AddComponent<TractorBeam>();
            beam.Configure(transform, PhysicsScale.BeamRange, TransportVehicle.BeamHoldDistance);

            vehicle = GetComponent<TransportVehicle>();
            if (vehicle == null) vehicle = gameObject.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            vehicle.PlaceOnGround(startPosition, startYaw);

            activated = true;
            vehicle.NotifyActivated();
        }

        void CapturePartPose()
        {
            partConstraints = GetComponent<Rigidbody>().constraints;
            partChildren = new Transform[transform.childCount];
            childPositions = new Vector3[partChildren.Length];
            childRotations = new Quaternion[partChildren.Length];
            for (int i = 0; i < partChildren.Length; i++)
            {
                partChildren[i] = transform.GetChild(i);
                childPositions[i] = partChildren[i].localPosition;
                childRotations[i] = partChildren[i].localRotation;
            }
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child != transform && child.name == "Visual") { visual = child; break; }
            if (visual != null)
            {
                visualRootLocal = transform.InverseTransformPoint(visual.position);
                visualRelativeYaw = Mathf.DeltaAngle(transform.eulerAngles.y, visual.eulerAngles.y);
            }
            partColliders = GetComponentsInChildren<Collider>(true);
            colliderEnabled = new bool[partColliders.Length];
            for (int i = 0; i < partColliders.Length; i++) colliderEnabled[i] = partColliders[i].enabled;
        }

        // E uses this only with the field off and an actual socket immediately below.
        // Restore the part pivot around its existing visual pose: no teleport to the tower,
        // no duplicate model and no automatic weld completion.
        public bool TryDock()
        {
            if (!_assembleAsFinalModule || !activated || IsDocked || vehicle == null
                || WorldEvents.RemoteDecides || vehicle.Tumbling || vehicle.HorizontalSpeed > 2f
                || vehicle.Field.IsActive || vehicle.Beam.IsHolding || visual == null) return false;
            var vehiclePosition = transform.position;
            var vehicleRotation = transform.rotation;
            var vehicleChildPositions = new Vector3[partChildren.Length];
            var vehicleChildRotations = new Quaternion[partChildren.Length];
            var rotation = Quaternion.Euler(0, visual.eulerAngles.y - visualRelativeYaw, 0);
            var position = visual.position - rotation * Vector3.Scale(visualRootLocal, transform.lossyScale);
            for (int i = 0; i < partChildren.Length; i++)
            {
                if (partChildren[i] == null) continue;
                vehicleChildPositions[i] = partChildren[i].localPosition;
                vehicleChildRotations[i] = partChildren[i].localRotation;
                partChildren[i].SetLocalPositionAndRotation(childPositions[i], childRotations[i]);
            }
            transform.SetPositionAndRotation(position, rotation);
            var match = PartAssembler.FindMatch(part);
            if (!match.HasValue || (_dockTarget != null && match.TargetSocket.Owner != _dockTarget))
            {
                transform.SetPositionAndRotation(vehiclePosition, vehicleRotation);
                for (int i = 0; i < partChildren.Length; i++)
                    if (partChildren[i] != null)
                        partChildren[i].SetLocalPositionAndRotation(vehicleChildPositions[i], vehicleChildRotations[i]);
                return false;
            }

            vehicle.FinishForAssembly();
            foreach (string childName in new[] { "VehiclePlatform", "VehicleSeat" })
            {
                var child = transform.Find(childName);
                if (child != null) child.gameObject.SetActive(false);
            }
            for (int i = 0; i < partColliders.Length; i++)
                if (partColliders[i] != null) partColliders[i].enabled = colliderEnabled[i];
            foreach (var socket in part.Sockets) { socket.enabled = true; socket.Register(); }
            part.enabled = true;
            var body = GetComponent<Rigidbody>();
            body.position = position;
            body.rotation = rotation;
            body.constraints = partConstraints;
            var grip = GetComponent<Grabbable>();
            if (grip != null) { grip.enabled = true; grip.CanGrab = true; grip.Climbable = true; }
            var push = GetComponent<PartPush>();
            if (push != null) push.enabled = true;
            var hover = GetComponent<PartGroundHover>();
            if (hover != null) hover.enabled = true;
            part.TryTransitionTo(PartState.Carried);
            var link = PartAssembler.Assemble(match);
            IsDocked = link != null;
            Physics.SyncTransforms();
            return IsDocked;
        }

        void DisablePartRuntime()
        {
            if (part != null) part.enabled = false;

            var colliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = false;

            var sockets = GetComponentsInChildren<PartSocket>(true);
            for (var i = 0; i < sockets.Length; i++)
                if (sockets[i] != null) { sockets[i].enabled = false; sockets[i].Unregister(); }

            var push = GetComponent<PartPush>();
            if (push != null) push.enabled = false;
            var hover = GetComponent<PartGroundHover>();
            if (hover != null) hover.enabled = false;

            var grabbable = GetComponent<Grabbable>();
            if (grabbable != null)
            {
                grabbable.CanGrab = false;
                grabbable.Climbable = false;
                grabbable.enabled = false;
            }
        }
    }
}
