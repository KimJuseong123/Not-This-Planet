using System;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Three tactile circuit dials reveal a plate; only a living player holds the door.</summary>
    public sealed class AstraV2Puzzle : MonoBehaviour
    {
        [SerializeField] Grabbable[] _dials;
        [SerializeField] Transform[] _needles;
        [SerializeField] Renderer[] _indicators;
        [SerializeField] Material _waiting, _connected;
        [SerializeField] Transform _plate;
        [SerializeField] Rigidbody _door;
        [SerializeField] BoxCollider _doorway;
        [SerializeField] Vector3 _plateRest, _doorClosed;
        [SerializeField] Vector3 _plateSize = new Vector3(1.6f, .25f, 1.6f);
        [SerializeField] float _doorTravel = 3.8f;
        int _a = 2, _b = 3, _c;
        bool _solved, _occupied;
        float _reveal, _open, _nextGrab;
        readonly Collider[] _obstructions = new Collider[64];

        [Serializable] public struct State
        {
            public int A, B, C;
            public bool Solved, Occupied;
            public float Reveal, Open;
        }
        public State Snapshot => new State { A = _a, B = _b, C = _c, Solved = _solved,
            Occupied = _occupied, Reveal = _reveal, Open = _open };
        public bool Solved => _solved;
        public bool Occupied => _occupied;
        public float OpenFraction => _open;
        public static bool CircuitMatches(int a, int b, int c) => a == 0 && b == 1 && c == 2;

        public void Configure(Grabbable[] dials, Transform[] needles, Renderer[] indicators,
            Material waiting, Material connected, Transform plate, Rigidbody door, BoxCollider doorway)
        {
            _dials = dials; _needles = needles; _indicators = indicators;
            _waiting = waiting; _connected = connected; _plate = plate; _door = door; _doorway = doorway;
            _plateRest = plate.position; _doorClosed = door.position;
        }
        void OnEnable() => Grabbable.AnyGrabbed += OnGrabbed;
        void OnDisable() => Grabbable.AnyGrabbed -= OnGrabbed;
        void OnGrabbed(Grabbable target, Hand hand)
        {
            if (WorldEvents.RemoteDecides || _solved || _dials == null || Time.time < _nextGrab) return;
            int index = Array.IndexOf(_dials, target);
            if (index < 0) return;
            TurnDial(index);
            _nextGrab = Time.time + .25f;
        }
        public void TurnDial(int index)
        {
            if (WorldEvents.RemoteDecides || _solved) return;
            if (index == 0) _a = (_a + 1) % 4;
            else if (index == 1) _b = (_b + 1) % 4;
            else if (index == 2) _c = (_c + 1) % 4;
            else return;
            _solved = CircuitMatches(_a, _b, _c);
        }
        public static bool FootOnPlate(Vector3 foot, Vector3 top, Vector3 size)
        {
            var delta = foot - top;
            return Mathf.Abs(delta.x) <= size.x * .5f && Mathf.Abs(delta.z) <= size.z * .5f
                && delta.y >= -.18f && delta.y <= .38f;
        }
        void FixedUpdate()
        {
            if (_plate == null || _door == null) return;
            if (!WorldEvents.RemoteDecides)
            {
                _reveal = Mathf.MoveTowards(_reveal, _solved ? 1f : 0f, Time.fixedDeltaTime);
                _occupied = false;
                if (_reveal >= .99f)
                    foreach (var player in PlayerRig.All)
                        if (player != null && player.isActiveAndEnabled && !player.IsDead && player.Grounded
                            && player.gameObject.scene == gameObject.scene
                            && FootOnPlate(player.FootPosition, _plateRest + Vector3.up * .65f, _plateSize))
                        { _occupied = true; break; }
                bool blocked = DoorwayBlocked();
                // A safety beam stops closure on cargo; it cannot open a closed gate.
                float target = _occupied ? 1f : blocked ? _open : 0f;
                _open = Mathf.MoveTowards(_open, target, Time.fixedDeltaTime * .6f);
            }
            _door.MovePosition(_doorClosed + Vector3.up * (_open * _doorTravel));
        }
        bool DoorwayBlocked()
        {
            if (_doorway == null) return false;
            var bounds = _doorway.bounds;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, _obstructions,
                Quaternion.identity, GameLayers.PlayerMask | GameLayers.PartMask | GameLayers.PropMask,
                QueryTriggerInteraction.Ignore);
            return count > 0;
        }
        void LateUpdate()
        {
            if (_plate != null) _plate.position = _plateRest + Vector3.up * (_reveal * .65f - (_occupied ? .07f : 0f));
            for (int i = 0; _needles != null && i < _needles.Length; i++)
            {
                int value = i == 0 ? _a : i == 1 ? _b : _c;
                _needles[i].localRotation = Quaternion.Euler(0f, 0f, -90f * value);
                if (_indicators != null && i < _indicators.Length)
                    _indicators[i].sharedMaterial = value == i ? _connected : _waiting;
            }
        }
        public void ApplySnapshot(State state)
        {
            _a = Mathf.Clamp(state.A, 0, 3); _b = Mathf.Clamp(state.B, 0, 3); _c = Mathf.Clamp(state.C, 0, 3);
            _solved = state.Solved; _occupied = state.Occupied;
            _reveal = Mathf.Clamp01(state.Reveal); _open = Mathf.Clamp01(state.Open);
        }
    }
}
