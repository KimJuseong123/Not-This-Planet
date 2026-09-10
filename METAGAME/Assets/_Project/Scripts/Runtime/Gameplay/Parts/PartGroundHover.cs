using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>해제된 파츠의 지면 근처 부유. 견인·조립의 상태/질량 소유권은 변경하지 않는다.</summary>
    [RequireComponent(typeof(SpaceshipPart))]
    [DisallowMultipleComponent]
    public sealed class PartGroundHover : MonoBehaviour
    {
        [SerializeField, Range(.05f, .6f), Tooltip("파츠 밑면과 지면 사이 간격(m). 기본 0.25m")]
        private float _hoverHeight = .25f;
        [SerializeField, Min(1f)] private float _spring = 30f;
        [SerializeField, Min(1f)] private float _damping = 10f;
        private SpaceshipPart _part;
        private Rigidbody _body;
        private Collider[] _solids;
        [SerializeField] private Transform _assemblyPad;
        private bool _parked;
        private float _settledTime;
        private readonly RaycastHit[] _supportHits = new RaycastHit[24];

        public float HoverHeight => _hoverHeight;
        public bool IsSupporting { get; private set; }
        public bool IsParked => _parked && _assemblyPad != null && _part != null
            && _part.State == PartState.Placed
            && _body != null && Vector3.ProjectOnPlane(_body.position - _assemblyPad.position, Vector3.up).sqrMagnitude < 16f;

        public void ConfigureAssemblyPad(Transform pad) => _assemblyPad = pad;

        public void Initialize()
        {
            if (_part == null) _part = GetComponent<SpaceshipPart>();
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_solids == null) _solids = GetComponentsInChildren<Collider>(true);
        }

        private void Awake() => Initialize();
        private void FixedUpdate() => Tick();

        public void Tick()
        {
            Initialize();
            IsSupporting = false;
            if (WorldEvents.RemoteDecides || _body.isKinematic
                || (_part.State != PartState.Free && _part.State != PartState.Placed)) return;
            foreach (var socket in _part.Sockets)
                if (socket != null && socket.ConnectedTo != null) return;
            if (!IsParked && LowGravityField.FindContaining(_body.worldCenterOfMass) != null)
            { _settledTime = 0; return; }

            bool found = false;
            Bounds bounds = default;
            foreach (var collider in _solids)
            {
                if (collider == null || !collider.enabled || collider.isTrigger
                    || collider.attachedRigidbody != _body || collider.GetComponentInParent<PartClamp>() != null) continue;
                if (!found) { bounds = collider.bounds; found = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            if (!found) return;
            TryPark(bounds);
            // 지면 가까이에서만 받친다. 절벽 밖/공중에서는 낙하하며 원래 고도에 고정하지 않는다.
            var origin = new Vector3(bounds.center.x, bounds.min.y + .1f, bounds.center.z);
            if (!Physics.Raycast(origin, Vector3.down, out var ground, _hoverHeight + .9f,
                    GameLayers.GroundMask, QueryTriggerInteraction.Ignore)) return;
            float clearance = bounds.min.y - ground.point.y;
            float acceleration = (_hoverHeight - clearance) * _spring - _body.linearVelocity.y * _damping;
            if (_body.useGravity) acceleration -= Physics.gravity.y;
            _body.AddForce(Vector3.up * Mathf.Clamp(acceleration, -25f, 25f), ForceMode.Acceleration);
            // 수직 자세만 부드럽게 안정화한다. 좌우 이동과 Y축 회전은 플레이어에게 남긴다.
            var tilt = Vector3.Cross(transform.up, Vector3.up);
            var spin = _body.angularVelocity;
            _body.AddTorque(tilt * 12f - new Vector3(spin.x, 0f, spin.z) * 5f, ForceMode.Acceleration);
            IsSupporting = true;
        }

        // A field release does not use TractorBeam.Release. Once a dropped/manual part
        // settles at the assembly pad, enter the same Placed state without snapping it.
        private void TryPark(Bounds bounds)
        {
            if (_assemblyPad == null || Vector3.ProjectOnPlane(_body.position - _assemblyPad.position, Vector3.up).sqrMagnitude >= 16f)
            { _parked = false; _settledTime = 0; return; }
            if (IsParked) return;
            if (_body.linearVelocity.magnitude > .65f) { _settledTime = 0; return; }
            var origin = new Vector3(bounds.center.x, bounds.min.y + .1f, bounds.center.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _supportHits, _hoverHeight + .35f,
                GameLayers.GroundMask | GameLayers.PartMask, QueryTriggerInteraction.Ignore);
            bool supported = false;
            for (int i = 0; i < count; i++)
            {
                var hit = _supportHits[i];
                if (hit.rigidbody == _body) continue;
                if (hit.rigidbody == null) { supported = true; break; }
                var below = hit.rigidbody.GetComponent<SpaceshipPart>();
                if (below != null && LowGravityField.IsAssemblyProtected(below)) { supported = true; break; }
            }
            if (!supported) { _settledTime = 0; return; }
            _settledTime += Time.fixedDeltaTime;
            if (_settledTime < .5f) return;
            if (_part.State == PartState.Free) _part.TryTransitionTo(PartState.Carried);
            if (_part.State == PartState.Carried) _part.TryTransitionTo(PartState.Placed);
            _parked = _part.State == PartState.Placed;
            var match = PartAssembler.FindMatch(_part);
            if (match.HasValue) PartAssembler.Assemble(match);
        }
    }
}
