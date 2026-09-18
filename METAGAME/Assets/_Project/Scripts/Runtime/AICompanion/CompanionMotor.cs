using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.AICompanion
{
    /// <summary>Navigation chooses movement input; existing PlayerRig owns physics and animation.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed partial class CompanionMotor : MonoBehaviour
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
        readonly Queue<PartClamp> _pendingPins = new Queue<PartClamp>();
        bool _advanceBatch;
        int _completedPins;
        public int PendingPins => _pendingPins.Count;
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

        /// <summary>
        /// 할 일이 없을 때 따라다닐 대상. <c>null</c> 이면 그 자리에 선다.
        ///
        /// <para>값은 밖에서(자리 관리자) 가장 가까운 사람으로 갱신해 준다 — 이 어셈블리는
        /// 누가 사람인지 모른다.</para>
        /// </summary>
        public Transform StandbyAnchor { get; set; }

        /// <summary>
        /// 따라다닐 때 유지할 거리(m). 자리 관리자가 <b>AI 마다 다르게</b> 준다.
        ///
        /// <para>셋이 같은 거리를 노리면 한 점에 겹쳐 서로 밀어낸다. 조금씩 다르게 두면
        /// 줄줄이 늘어선다.</para>
        /// </summary>
        public float StandbyDistance { get; set; } = 2.5f;

        /// <summary>유지 거리보다 이만큼 더 벌어지면 따라 걷기 시작한다(m).</summary>
        ///
        /// <para>딱 맞춰 쫓으면 사람이 한 발 떼자마자 같이 움직여 들러붙은 것처럼 보인다.
        /// 여유를 둬야 사람이 서 있을 때 AI 도 선다.</para>
        const float StandbySlack = 1.2f;

        /// <summary>이보다 가까우면 비켜선다(m). 길을 막지 않으려는 것이다.</summary>
        const float StandbyNear = 1.6f;

        /// <summary>
        /// 사람이 <b>직접</b> "여기서 기다려" 라고 했나. 그때는 곁에 머무르지 않고 선 자리를
        /// 지킨다 — 명령이 느슨한 대기보다 우선이다.
        ///
        /// <para>명령인지 아닌지는 <see cref="SetOrder"/> 의 speaker 로 가른다. 작업이 끝나
        /// 내부적으로 Wait 로 돌아올 때는 speaker 가 없다.</para>
        /// </summary>
        bool _holdStill;

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
            ResetToolJob();
            _pendingPins.Clear();
            _advanceBatch = false;
            _completedPins = 0;
            _pin = null;
            _target = order == CompanionOrder.Follow ? speaker : null;

            // 사람이 직접 "여기서 기다려" 라고 한 경우에만 선 자리를 지킨다. 작업이 끝나
            // 내부적으로 Wait 로 돌아온 것(speaker 없음)은 느슨한 대기로 이어진다.
            _holdStill = order == CompanionOrder.Wait && speaker != null;

            _corners = System.Array.Empty<Vector3>();
            _nextPath = 0;
            Status = order == CompanionOrder.Follow ? "따라오는 중" : order == CompanionOrder.Cancel ? "작업 중단" : "여기서 대기";
            if (_rig != null) _rig.SetInput(default);
        }

        public bool TryStartPin(PartClamp pin, out string reason)
            => TryStartPin(pin, CompanionOrder.PullPin, false, out reason);

        public bool TryMoveToPin(PartClamp pin, out string reason)
            => TryStartPin(pin, CompanionOrder.MoveToPin, false, out reason);

        public bool TryStartNearbyPins(IReadOnlyList<PartClamp> pins, out string reason)
        {
            reason = "주변에 뽑을 고정못이 없어요";
            if (pins == null || pins.Count == 0) return false;
            if (!TryStartPin(pins[0], CompanionOrder.PullNearbyPins, false, out reason)) return false;
            // Bounded snapshot: moving the speaker never adds new work to an accepted request.
            for (int i = 1; i < pins.Count && i < 32; ++i) _pendingPins.Enqueue(pins[i]);
            reason = $"주변 고정못 {_pendingPins.Count + 1}개를 차례로 뽑을게요";
            return true;
        }

        bool TryStartPin(PartClamp pin, CompanionOrder order, bool continuing, out string reason)
        {
            reason = "뽑을 수 있는 고정못이 아니에요";
            if (_rig != null && _rig.HeldTool != null)
            { reason = "도구를 든 상태예요 · 먼저 내려놓아 주세요"; return false; }
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
            if (!continuing) SetOrder(order, null);
            else { _corners = System.Array.Empty<Vector3>(); _nextPath = 0; _rig.SetInput(default); }
            _pin = pin; _approach = spot.position;
            _pinPhase = PinPhase.Approach;
            _phaseStart = _taskStart = _lastProgressTime = Time.time;
            _lastProgress = pin.Progress;
            Status = "고정못으로 이동 중";
            reason = order == CompanionOrder.MoveToPin ? "고정못 앞으로 가서 기다릴게요" : "고정못으로 이동해서 잡고 뒤로 당길게요";
            return true;
        }

        void FixedUpdate()
        {
            if (_rig == null || _rig.Hips == null) return;
            var input = new InputFrame { AimYaw = _rig.Hips.rotation.eulerAngles.y, AimFrom = _rig.Hips.position };
            if (IsToolOrder(Order))
            {
                TickTool(ref input);
                _rig.SetInput(input);
                return;
            }
            if (Order == CompanionOrder.PullPin || Order == CompanionOrder.MoveToPin || Order == CompanionOrder.PullNearbyPins)
            {
                if (_advanceBatch)
                {
                    _advanceBatch = false;
                    bool next = false;
                    while (_pendingPins.Count > 0)
                    {
                        var pin = _pendingPins.Dequeue();
                        if (!CompanionPinTarget.Eligible(pin)) continue;
                        if (!TryStartPin(pin, CompanionOrder.PullNearbyPins, true, out var reason))
                        { FinishPin("작업 실패 · " + reason); _rig.SetInput(default); return; }
                        next = true; break;
                    }
                    if (!next)
                    {
                        int completed = _completedPins;
                        SetOrder(CompanionOrder.Wait, null);
                        Status = $"작업 완료 · 고정못 {completed}개를 뽑았어요";
                    }
                    _rig.SetInput(default);
                    return;
                }
                TickPin(ref input);
                _rig.SetInput(input);
                return;
            }
            if (Order != CompanionOrder.Follow || _target == null)
            {
                if (Order == CompanionOrder.Follow) SetOrder(CompanionOrder.Cancel, null);
                TickStandby(ref input);
                _rig.SetInput(input); return;
            }
            if (Navigate(_target.position, _stopDistance, ref input)) Status = "도착 · 거리를 유지해요";
            else Status = input.Move.sqrMagnitude > 0 ? "따라오는 중" : "이동 가능한 길을 찾는 중";
            _rig.SetInput(input);
        }

        /// <summary>
        /// 할 일이 없을 때는 사람을 따라다닌다. <b>벌어지면 쫓아가고, 너무 붙으면 비켜선다.</b>
        ///
        /// <para>유지 거리에서 <see cref="StandbySlack"/> 만큼은 그냥 둔다 — 딱 맞춰 쫓으면
        /// 사람이 한 발 뗄 때마다 같이 움직여 들러붙은 것처럼 보이고, 멈춰 서 있을 때도
        /// 옆에서 꼼지락댄다.</para>
        /// </summary>
        void TickStandby(ref InputFrame input)
        {
            if (_holdStill || StandbyAnchor == null) return;

            Vector3 delta = StandbyAnchor.position - _rig.Hips.position;
            delta.y = 0;
            float distance = delta.magnitude;

            if (distance > StandbyDistance + StandbySlack)
            {
                Navigate(StandbyAnchor.position, StandbyDistance, ref input);
                Status = "따라가는 중";
                return;
            }

            if (distance < StandbyNear && distance > 0.05f)
            {
                // 길을 막고 있다. 경로를 찾을 것도 없이 사람 반대쪽으로 한 걸음 물러난다.
                input.AimYaw = Mathf.Atan2(-delta.x, -delta.z) * Mathf.Rad2Deg;
                input.Move = new Vector2(0, _walkInput * 0.5f);
                Status = "비켜서는 중";
                return;
            }

            // 사람을 바라보고 선다. 등지고 서 있으면 말을 걸어도 반응이 없어 보인다.
            if (distance > 0.05f) input.AimYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            Status = "대기 중";
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
            { FinishPin("작업 완료 · 고정못을 뽑았어요", true); return; }
            if (_rig.Downed || _rig.IsRiding || Time.time - _taskStart > 30)
            { FinishPin("작업 실패 · 이동/작업 상태를 확인해 주세요"); return; }
            if (_pinPhase == PinPhase.Approach)
            {
                if (!Navigate(_approach, 0.16f, ref input))
                { Status = "고정못으로 이동 중"; return; }
                if (Order == CompanionOrder.MoveToPin)
                { input = default; FinishPin("도착 · 고정못 앞에서 기다려요"); return; }
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

        void FinishPin(string status, bool success = false)
        {
            if (success && Order == CompanionOrder.PullNearbyPins)
            {
                ++_completedPins;
                _pin = null;
                _advanceBatch = true;
                _rig.SetInput(default);
                Status = $"{_completedPins}개 완료 · 다음 고정못 확인 중";
                return;
            }
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
