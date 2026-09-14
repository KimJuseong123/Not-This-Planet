using System;
using UnityEngine;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    /// <summary>Top-only release, hold-to-lower brake, and empty-platform reset. No tractor force.</summary>
    public sealed class AstraV2FreightLift : MonoBehaviour
    {
        [SerializeField] Rigidbody _platform;
        [SerializeField] Grabbable _release, _brake, _reset;
        [SerializeField] SpaceshipPart _cargo;
        [SerializeField] Vector3 _top, _bottom;
        [SerializeField] Renderer _signal;
        [SerializeField] Material _lockedMaterial, _readyMaterial;
        [SerializeField] Vector3 _deckSize = new Vector3(5.5f, .4f, 5f);
        bool _unlocked, _returning;
        float _travel;
        readonly Collider[] _probe = new Collider[64];
        [Serializable] public struct State { public bool Unlocked, Returning; public float Travel; }
        public State Snapshot => new State { Unlocked = _unlocked, Returning = _returning, Travel = _travel };
        public bool Unlocked => _unlocked;
        public float Travel => _travel;
        public void Configure(Rigidbody platform, Grabbable release, Grabbable brake, Grabbable reset,
            SpaceshipPart cargo, Vector3 bottom, Renderer signal, Material locked, Material ready)
        {
            _platform = platform; _release = release; _brake = brake; _reset = reset; _cargo = cargo;
            _top = platform.position; _bottom = bottom; _signal = signal;
            _lockedMaterial = locked; _readyMaterial = ready;
        }
        void OnEnable() => Grabbable.AnyGrabbed += OnGrabbed;
        void OnDisable() => Grabbable.AnyGrabbed -= OnGrabbed;
        void OnGrabbed(Grabbable control, Hand hand)
        {
            if (WorldEvents.RemoteDecides) return;
            if (control == _release) TryUnlock();
            if (control == _reset && _unlocked && DeckEmpty()) _returning = true;
        }
        public bool TryUnlock()
        {
            if (WorldEvents.RemoteDecides || (_cargo != null && _cargo.RemainingRequiredClamps > 0)) return false;
            _unlocked = true;
            return true;
        }
        public static float StepTravel(float current, bool unlocked, bool held, bool returning, float dt, float height)
        {
            if (!unlocked || height <= 0f || dt <= 0f) return current;
            if (returning) return Mathf.MoveTowards(current, 0f, dt * .75f / height);
            return held ? Mathf.MoveTowards(current, 1f, dt * .75f / height) : current;
        }
        bool DeckEmpty()
        {
            if (_platform == null) return true;
            return Physics.OverlapBoxNonAlloc(_platform.position + Vector3.up * 1.5f,
                new Vector3(_deckSize.x / 2, 1.3f, _deckSize.z / 2), _probe, Quaternion.identity,
                GameLayers.PlayerMask | GameLayers.PartMask | GameLayers.PropMask, QueryTriggerInteraction.Ignore) == 0;
        }
        void FixedUpdate()
        {
            if (_platform == null) return;
            if (!WorldEvents.RemoteDecides)
            {
                if (_returning && !DeckEmpty()) _returning = false;
                bool held = _brake != null && _brake.HandCount > 0;
                bool underDeck = Physics.OverlapBoxNonAlloc(_platform.position + Vector3.down * .35f,
                    new Vector3(_deckSize.x / 2, .18f, _deckSize.z / 2), _probe, Quaternion.identity,
                    GameLayers.PlayerMask | GameLayers.PartMask | GameLayers.PropMask,
                    QueryTriggerInteraction.Ignore) > 0;
                _travel = StepTravel(_travel, _unlocked, held && !underDeck, _returning,
                    Time.fixedDeltaTime, _top.y - _bottom.y);
                if (_travel <= 0f) _returning = false;
            }
            _platform.MovePosition(Vector3.Lerp(_top, _bottom, _travel));
            if (_signal != null) _signal.sharedMaterial = _unlocked ? _readyMaterial : _lockedMaterial;
        }
        public void ApplySnapshot(State state)
        { _unlocked = state.Unlocked; _returning = state.Returning; _travel = Mathf.Clamp01(state.Travel); }
    }
}
