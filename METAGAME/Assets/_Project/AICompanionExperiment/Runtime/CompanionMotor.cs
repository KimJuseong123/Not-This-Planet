using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.AIExperiment
{
    /// <summary>Navigation chooses movement input; existing PlayerRig owns physics and animation.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CompanionMotor : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float _stopDistance = 1.8f;
        [SerializeField, Range(0.1f, 1f)] float _walkInput = 0.7f;
        PlayerRig _rig;
        Transform _target;
        NavMeshData _data;
        NavMeshDataInstance _instance;
        NavMeshPath _path;
        Vector3[] _corners = System.Array.Empty<Vector3>();
        int _corner;
        float _nextPath;
        PartClamp _pin;
        Vector3 _approach;
        enum PinPhase { Approach, Grip, Pull }
        PinPhase _pinPhase;
        float _phaseStart;
        float _taskStart;
        float _lastProgressTime;
        float _lastProgress;
        public CompanionOrder Order { get; private set; }
        public string Status { get; private set; } = "대기";
        public PlayerRig Rig => _rig;
        public PartClamp TargetPin => _pin;

        public void Initialize(PlayerRig rig)
        {
            _rig = rig;
            _path = new NavMeshPath();
            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(Vector3.zero, new Vector3(25, 10, 21));
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            sources.RemoveAll(s => s.component is Collider c && (c.isTrigger || c.attachedRigidbody != null));
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.45f;
            settings.agentHeight = 1.5f;
            settings.agentClimb = 0.25f;
            settings.agentSlope = 40;
            _data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (_data != null) _instance = NavMesh.AddNavMeshData(_data);
            SetOrder(CompanionOrder.Wait, null);
        }

        public void SetOrder(CompanionOrder order, Transform speaker)
        {
            Order = order;
            _pin = null;
            _target = order == CompanionOrder.Follow ? speaker : null;
            _corners = System.Array.Empty<Vector3>();
            _nextPath = 0;
            Status = order == CompanionOrder.Follow ? "따라오는 중" : order == CompanionOrder.Cancel ? "작업 중단" : "여기서 대기";
            if (_rig != null) _rig.SetInput(default);
        }

        public bool TryStartPin(PartClamp pin, out string reason)
        {
            reason = "뽑을 수 있는 고정못이 아니에요";
            if (_rig == null || !CompanionPinTarget.Eligible(pin)) return false;
            Vector3 center = CompanionPinTarget.Center(pin);
            Vector3 outward = center - pin.Part.transform.position; outward.y = 0;
            if (outward.sqrMagnitude < 0.01f) outward = _rig.Hips.position - center;
            outward.y = 0;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.back;
            Vector3 approach = center + outward.normalized * 0.55f;
            if (!NavMesh.SamplePosition(approach, out var spot, 1.2f, NavMesh.AllAreas))
            { reason = "고정못 앞에 설 수 있는 곳이 없어요"; return false; }
            if (!NavMesh.SamplePosition(_rig.Hips.position, out var from, 2f, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(from.position, spot.position, NavMesh.AllAreas, _path)
                || _path.status != NavMeshPathStatus.PathComplete)
            { reason = "고정못까지 갈 수 있는 길이 없어요"; return false; }
            SetOrder(CompanionOrder.PullPin, null);
            _pin = pin; _approach = spot.position;
            _pinPhase = PinPhase.Approach;
            _phaseStart = _taskStart = _lastProgressTime = Time.time;
            _lastProgress = pin.Progress;
            Status = "고정못으로 이동 중";
            reason = "고정못으로 이동해서 잡고 뒤로 당길게요";
            return true;
        }

        void FixedUpdate()
        {
            if (_rig == null || _rig.Hips == null) return;
            var input = new InputFrame { AimYaw = _rig.Hips.rotation.eulerAngles.y, AimFrom = _rig.Hips.position };
            if (Order == CompanionOrder.PullPin)
            {
                TickPin(ref input);
                _rig.SetInput(input);
                return;
            }
            if (Order != CompanionOrder.Follow || _target == null)
            {
                if (Order == CompanionOrder.Follow) SetOrder(CompanionOrder.Cancel, null);
                _rig.SetInput(input); return;
            }
            if (Navigate(_target.position, _stopDistance, ref input)) Status = "도착 · 거리를 유지해요";
            else Status = input.Move.sqrMagnitude > 0 ? "따라오는 중" : "이동 가능한 길을 찾는 중";
            _rig.SetInput(input);
        }

        bool Navigate(Vector3 destination, float stopDistance, ref InputFrame input)
        {
            Vector3 position = _rig.Hips.position;
            Vector3 delta = destination - position;
            delta.y = 0;
            if (delta.magnitude <= stopDistance && Mathf.Abs(destination.y - position.y) < 1.5f) return true;
            if (Time.time >= _nextPath)
            {
                _nextPath = Time.time + 0.35f;
                bool valid = NavMesh.SamplePosition(position, out var from, 2f, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(destination, out var to, 2f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _path)
                    && _path.status == NavMeshPathStatus.PathComplete;
                _corners = valid ? _path.corners : System.Array.Empty<Vector3>();
                _corner = 1;
            }
            while (_corner < _corners.Length)
            {
                delta = _corners[_corner] - position; delta.y = 0;
                float cornerDistance = _corner == _corners.Length - 1 ? Mathf.Min(0.1f, stopDistance * 0.5f) : 0.35f;
                if (delta.magnitude > cornerDistance) break;
                _corner++;
            }
            if (_corner < _corners.Length)
            {
                input.AimYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                input.Move = new Vector2(0, _walkInput);
            }
            return false;
        }

        void TickPin(ref InputFrame input)
        {
            if (_pin == null || !_pin.isActiveAndEnabled)
            { FinishPin("작업 실패 · 대상 고정못이 없어졌어요"); return; }
            if (_pin.IsReleased)
            { FinishPin("작업 완료 · 고정못을 뽑았어요"); return; }
            if (_rig.Downed || _rig.IsRiding || Time.time - _taskStart > 30)
            { FinishPin("작업 실패 · 이동/작업 상태를 확인해 주세요"); return; }
            if (_pinPhase == PinPhase.Approach)
            {
                if (!Navigate(_approach, 0.16f, ref input))
                { Status = "고정못으로 이동 중"; return; }
                _pinPhase = PinPhase.Grip; _phaseStart = Time.time;
            }
            // Use the same camera-less aim and hand-grip input path as a server-side human player.
            input.AimFrom = _rig.Doll.Head.position;
            Vector3 toPin = CompanionPinTarget.Center(_pin) - input.AimFrom;
            var aim = Quaternion.LookRotation(toPin).eulerAngles;
            input.AimYaw = aim.y;
            input.AimPitch = Mathf.DeltaAngle(0, aim.x);
            if (_rig.ClampGrabbed && _rig.ClampGrabTarget == _pin)
            {
                if (_pinPhase != PinPhase.Pull) _lastProgressTime = Time.time;
                _pinPhase = PinPhase.Pull;
                input.GrabLeft = input.GrabRight = true;
                input.Move = Vector2.down;
                Status = "고정못을 잡고 뒤로 당기는 중";
                if (_pin.Progress > _lastProgress + 0.001f)
                { _lastProgress = _pin.Progress; _lastProgressTime = Time.time; }
                if (Time.time - _lastProgressTime > 4)
                { input = default; FinishPin("작업 실패 · 잡았지만 당기기가 진행되지 않아요"); }
                return;
            }
            if (_pinPhase == PinPhase.Pull) { _pinPhase = PinPhase.Grip; _phaseStart = Time.time; }
            Status = "고정못에 손을 뻗는 중";
            // Never blindly click: another prop or player could have entered the aim ray.
            if (_rig.CurrentInteractionTarget.Clamp == _pin && _rig.CurrentInteractionTarget.CanInteract)
                input.GrabLeft = input.GrabRight = true;
            if (Time.time - _phaseStart > 5)
            { input = default; FinishPin("작업 실패 · 손이 고정못에 닿지 않아요"); }
        }

        void FinishPin(string status)
        {
            SetOrder(CompanionOrder.Wait, null);
            Status = status;
        }

        void OnDisable() { if (_rig != null) _rig.SetInput(default); }
        void OnDestroy()
        {
            if (_instance.valid) _instance.Remove();
            if (_data != null) Destroy(_data);
        }
    }
}
