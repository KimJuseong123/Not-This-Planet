using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// Part_1의 고정못이 모두 풀렸을 때만 파츠를 견인 조종 모듈로 전환한다.
    ///
    /// 전환 전에는 일반 SpaceshipPart로만 동작한다. 마지막 고정못 해제 이벤트 안에서
    /// 기존 몸체 충돌과 밀기·손잡이를 교체하고, 같은 루트에 TractorBeam과 TransportVehicle을
    /// 붙인다. 그래서 씬 시작부터 별도 견인 장치를 둘 필요가 없고, Part_1 하나가 해제
    /// 이후의 탑승·운전·견인을 모두 맡으며 엔진 조립 소켓과 용접 판정도 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpaceshipPart))]
    public sealed class PartTractorActivator : MonoBehaviour
    {
        SpaceshipPart part;
        TransportVehicle vehicle;
        bool activated;

        public bool IsActivated => activated;
        public TransportVehicle Vehicle => vehicle;

        void Awake() => part = GetComponent<SpaceshipPart>();

        bool initialized;

        void OnEnable() => Initialize();

        public void Initialize()
        {
            if (initialized) return;
            if (part == null) part = GetComponent<SpaceshipPart>();
            if (part == null) return;

            initialized = true;
            part.StateChanged += OnPartStateChanged;
            part.Initialize();

            // 저장된/테스트 상태가 이미 Free인 경우에도 컨트롤러가 놓치지 않게 한다.
            if (part.State == PartState.Free) Activate();
        }

        void OnDisable()
        {
            if (part != null) part.StateChanged -= OnPartStateChanged;
            initialized = false;
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
            var hull = new System.Collections.Generic.List<BoxCollider>();
            var partBody = part.GetComponent<Rigidbody>();
            foreach (var box in GetComponentsInChildren<BoxCollider>(true))
                if (box.enabled && box.gameObject.activeInHierarchy && !box.isTrigger
                    && box.attachedRigidbody == partBody
                    && box.GetComponentInParent<PartClamp>() == null
                    && box.GetComponentInParent<PartSocket>() == null)
                    hull.Add(box);

            // 몸체 충돌만 운반체로 교체한다. 엔진의 조립·용접 자격은 유지한다.
            DisablePartRuntime();
            Physics.SyncTransforms();

            var beam = GetComponent<TractorBeam>();
            if (beam == null) beam = gameObject.AddComponent<TractorBeam>();
            beam.Configure(transform, PhysicsScale.BeamRange, TransportVehicle.BeamHoldDistance);

            vehicle = GetComponent<TransportVehicle>();
            if (vehicle == null) vehicle = gameObject.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            vehicle.ConfigureHull(hull);
            vehicle.PlaceOnGround(startPosition, startYaw);

            activated = true;
            vehicle.NotifyActivated();
        }

        void DisablePartRuntime()
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = false;

            var push = GetComponent<PartPush>();
            if (push != null) push.enabled = false;

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
