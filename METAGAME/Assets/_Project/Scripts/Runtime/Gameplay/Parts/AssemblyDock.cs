using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Audio;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>첫 파츠만 고정하는 낮은 조립대 받침. 상부 파츠는 자유롭게 적층/용접한다.</summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyDock : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _captureRadius = 0.9f;
        [SerializeField, Min(0.01f)] private float _heightTolerance = 0.18f;
        [SerializeField, Range(0f, 45f)] private float _maxTilt = 18f;
        [SerializeField, Min(0.1f)] private float _maxCaptureSpeed = 1.8f;
        [SerializeField, Min(0.05f)] private float _snapDuration = 0.35f;
        private ShipAssembly _assembly;
        private SpaceshipPart _engine;
        private Rigidbody _body;
        private Vector3 _fromPosition, _toPosition;
        private Quaternion _fromRotation, _toRotation;
        private float _snapElapsed;
        private bool _snapping, _shownDocked;
        private Transform _visualRoot;
        private readonly Transform[] _jaws = new Transform[4];
        private readonly Renderer[] _lamps = new Renderer[4];
        private Material _housing, _waiting, _locked;
        private SoundRuntime _sound;
        private float _nextEngineSearch;
        private static readonly List<SpaceshipPart> Connected = new();
        private static readonly RaycastHit[] SupportHits = new RaycastHit[32];

        public SpaceshipPart DockedPart => _engine != null && _engine.IsDockedToPad ? _engine : null;
        public bool IsSnapping => _snapping;

        public void Configure(ShipAssembly assembly)
        {
            _assembly = assembly;
            _nextEngineSearch = 0f;
            ResolveEngine();
        }

        private void ResolveEngine()
        {
            if (_engine != null) return;
            if (Application.isPlaying && Time.unscaledTime < _nextEngineSearch) return;
            _nextEngineSearch = Time.unscaledTime + 0.5f;
            foreach (var part in FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None))
            {
                if (part == null || part.name != PartSpec.All[0].Name
                    || part.gameObject.scene != gameObject.scene) continue;
                _engine = part;
                _body = part.GetComponent<Rigidbody>();
                break;
            }
        }

        private void FixedUpdate() => Tick(Time.fixedDeltaTime);

        public void Tick(float deltaTime)
        {
            if (_assembly == null || WorldEvents.RemoteDecides || deltaTime <= 0f) return;
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            ResolveEngine();
            if (_body == null) return;
            if (_snapping)
            {
                _snapElapsed += deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_snapElapsed / _snapDuration));
                _body.MovePosition(Vector3.Lerp(_fromPosition, _toPosition, t));
                _body.MoveRotation(Quaternion.Slerp(_fromRotation, _toRotation, t));
                if (_snapElapsed >= _snapDuration)
                {
                    _snapping = false;
                    _body.position = _toPosition;
                    _body.rotation = _toRotation;
                    if (_sound == null) _sound = FindAnyObjectByType<SoundRuntime>();
                    _sound?.EmitWorldOneShot(SoundEventId.ImpactMetalMetal, _toPosition, 0.8f);
                }
                return;
            }
            if (!CanCapture(_engine)) return;
            _fromPosition = _body.position;
            _fromRotation = _body.rotation;
            // 첫 파츠의 yaw는 유지한다. 위쪽 파츠에는 이 보정을 적용하지 않는다.
            _toRotation = Quaternion.Euler(0f, _body.rotation.eulerAngles.y, 0f);
            if (!_engine.TryGetHullBoundsAtRotation(_toRotation, out var uprightBounds)) return;
            // Bounds는 보간된 Transform 기준이다. Rigidbody.position과 섞으면 이동 중 높이가 튄다.
            _toPosition = _assembly.LaunchPad.position
                + Vector3.up * (_engine.transform.position.y - uprightBounds.min.y);
            if (_engine.State == PartState.Free) _engine.TryTransitionTo(PartState.Carried);
            _engine.TryTransitionTo(PartState.Placed);
            _engine.ApplyDockedToPad(true);
            _snapElapsed = 0f;
            _snapping = true;
        }

        public bool CanCapture(SpaceshipPart part)
        {
            if (_assembly == null || part == null || part.IsDockedToPad
                || part.name != PartSpec.All[0].Name
                || (part.State != PartState.Free && part.State != PartState.Placed)
                || part.RemainingRequiredClamps > 0) return false;
            // 구형 Main의 Part_1은 견인 탈것이다. 이름만 같다고 운반 장치를 영구 고정하지 않는다.
            if (part.TryGetComponent<TransportVehicle>(out _)) return false;
            if (!part.TryGetHullBounds(out var bounds)) return false;
            var body = part.GetComponent<Rigidbody>();
            if (body == null || body.linearVelocity.magnitude > _maxCaptureSpeed) return false;
            var offset = bounds.center - _assembly.LaunchPad.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= _captureRadius * _captureRadius
                && Mathf.Abs(bounds.min.y - _assembly.LaunchPad.position.y) <= _heightTolerance
                && Vector3.Angle(part.AssemblyUp, Vector3.up) <= _maxTilt;
        }

        /// <summary>도킹한 바닥과 접합된 덩어리는 통째로 저중력/명시적 견인에서 제외한다.</summary>
        public static bool IsAnchored(SpaceshipPart part)
        {
            if (part == null) return false;
            if (part.IsDockedToPad) return true;
            PartGraph.Collect(part, Connected);
            foreach (var connected in Connected)
                if (connected != null && connected.IsDockedToPad) return true;
            return false;
        }

        public static bool IsSupportedByAssembly(SpaceshipPart part) => IsSupported(part, 0);

        private static bool IsSupported(SpaceshipPart part, int depth)
        {
            if (part == null || depth >= PartSpec.All.Length || part.State == PartState.Carried) return false;
            if (IsAnchored(part)) return true;
            if (!part.TryGetHullBounds(out var bounds)) return false;
            // 다섯 점의 짧은 접지 검사. 단순히 조립대 위 공중에 있다는 이유로 제외하지 않는다.
            for (var sample = 0; sample < 5; sample++)
            {
                var origin = bounds.center;
                if (sample > 0)
                {
                    origin.x += ((sample & 1) == 0 ? 1f : -1f) * bounds.extents.x * 0.65f;
                    origin.z += (sample <= 2 ? 1f : -1f) * bounds.extents.z * 0.65f;
                }
                origin.y = bounds.min.y + 0.08f;
                var count = Physics.RaycastNonAlloc(origin, Vector3.down, SupportHits, 0.22f,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                SpaceshipPart below = null;
                var closest = float.PositiveInfinity;
                for (var i = 0; i < count; i++)
                {
                    var hit = SupportHits[i];
                    var other = hit.rigidbody != null ? hit.rigidbody.GetComponent<SpaceshipPart>() : null;
                    if (other == part || hit.distance >= closest) continue;
                    // 플레이어나 장식이 아니라 실제 파츠 받침으로 이어져야 한다.
                    closest = hit.distance;
                    below = other;
                }
                if (below != null && below.transform.position.y < part.transform.position.y
                    && IsSupported(below, depth + 1)) return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            if (_assembly == null) return;
            ResolveEngine();
            if (_visualRoot == null) BuildVisual();
            var docked = DockedPart != null;
            var radius = docked ? 0.6f : 0.85f;
            for (var i = 0; i < _jaws.Length; i++)
            {
                var angle = i * Mathf.PI * 0.5f;
                var target = new Vector3(Mathf.Cos(angle) * radius, 0.07f, Mathf.Sin(angle) * radius);
                _jaws[i].localPosition = Vector3.Lerp(_jaws[i].localPosition, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
                if (docked != _shownDocked) _lamps[i].sharedMaterial = docked ? _locked : _waiting;
            }
            _shownDocked = docked;
        }

        private void BuildVisual()
        {
            _visualRoot = new GameObject("AssemblyDock_LowProfile").transform;
            _visualRoot.SetParent(transform, false);
            _visualRoot.position = _assembly.LaunchPad.position;
            _housing = new Material(Prim.DoughShader) { color = new Color(0.16f, 0.2f, 0.26f) };
            _waiting = new Material(Prim.FlatShader) { color = new Color(0.85f, 0.12f, 0.08f) };
            _locked = new Material(Prim.FlatShader) { color = new Color(0.12f, 0.85f, 0.72f) };
            for (var i = 0; i < 4; i++)
            {
                var jaw = GameObject.CreatePrimitive(PrimitiveType.Cube);
                jaw.name = "DockJaw_" + i;
                jaw.transform.SetParent(_visualRoot, false);
                jaw.transform.localRotation = Quaternion.Euler(0f, -90f * i, 0f);
                jaw.transform.localScale = new Vector3(0.28f, 0.12f, 0.42f);
                var collider = jaw.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider); // 시각 장식만: 경사로/화물 진입을 막지 않는다.
                jaw.GetComponent<Renderer>().sharedMaterial = _housing;
                _jaws[i] = jaw.transform;
                _jaws[i].localPosition = new Vector3(Mathf.Cos(i * Mathf.PI * 0.5f) * 0.85f, 0.07f,
                    Mathf.Sin(i * Mathf.PI * 0.5f) * 0.85f);
                var light = GameObject.CreatePrimitive(PrimitiveType.Cube);
                light.name = "DockStatus";
                light.transform.SetParent(jaw.transform, false);
                light.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                light.transform.localScale = new Vector3(0.75f, 0.08f, 0.75f);
                var lightCollider = light.GetComponent<Collider>();
                lightCollider.enabled = false;
                Destroy(lightCollider);
                _lamps[i] = light.GetComponent<Renderer>();
                _lamps[i].sharedMaterial = _waiting;
            }
        }

        private void OnDestroy()
        {
            if (_visualRoot != null) Destroy(_visualRoot.gameObject);
            if (_housing != null) Destroy(_housing);
            if (_waiting != null) Destroy(_waiting);
            if (_locked != null) Destroy(_locked);
        }
    }
}
