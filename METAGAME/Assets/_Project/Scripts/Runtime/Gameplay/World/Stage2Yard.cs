using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    /// <summary>Host-owned launch-yard simulation. Networking is supplied by Stage2Bridge.</summary>
    public sealed partial class Stage2Yard : MonoBehaviour
    {
        [SerializeField] Transform _cranePivot, _arm, _hook, _seat, _lift, _ladder, _launch, _shower;
        [SerializeField] Rigidbody[] _parts;
        [SerializeField] Transform[] _sockets, _lids;
        [SerializeField] BoxCollider[] _gasZones;
        [SerializeField] Stage2Target[] _targets;
        Stage2State _state = new Stage2State();
        readonly Dictionary<uint, Worker> _workers = new();
        readonly Dictionary<int, Stage2Target> _targetMap = new();
        Vector3[] _homes;
        Quaternion[] _homeRotations;
        Vector3 _craneVelocity;
        bool _authority;
        float _climbTime;
        Vector3 _climbStart, _climbEnd;
        Vector3 _ladderHome;
        Transform _platform, _ladderTop;
        Transform[] _ladderRails, _ladderSteps;
        Transform _liftGate, _upperGate, _lowerGate;
        ParticleSystem[] _gasParticles;
        Collider[] _ladderColliders;
        Renderer[] _boltRenderers;
        MaterialPropertyBlock _boltColor;
        LineRenderer _wire;
        readonly List<Stage2State> _replicas = new();
        Func<double> _clock;
        public void SetClock(Func<double> clock) => _clock = clock;
        public Stage2State State => _state;
        public Transform Seat => _seat;
        public Stage2Target[] Targets => _targets;
        public bool Authority => _authority;
        public event Action Completed;

        sealed class Worker
        {
            public PlayerRig Rig;
            public YardCommand Command;
            public YardPlayerState State;
            public float LastInput, LastUse = -10f, LastTurns;
        }

        void Awake()
        {
            _boltColor = new MaterialPropertyBlock();
            _homes = new Vector3[_parts.Length];
            _homeRotations = new Quaternion[_parts.Length];
            _state.Parts = new YardPartState[_parts.Length];
            for (int i = 0; i < _parts.Length; i++)
            {
                _homes[i] = _parts[i].position;
                _homeRotations[i] = _parts[i].rotation;
                _state.Parts[i] = new YardPartState { Position = _homes[i], Rotation = _homeRotations[i] };
            }
            _state.LadderPosition = _ladder.position;
            _ladderHome = _ladder.position;
            foreach (var target in _targets) _targetMap.Add(target.Id, target);
            _platform = _ladder.Find("Platform");
            _ladderTop = _ladder.Find("ClimbTop");
            _ladderColliders = _ladder.GetComponentsInChildren<Collider>();
            var rails = new List<Transform>(); var steps = new List<Transform>();
            foreach (Transform child in _ladder)
            { if (child.name == "Telescoping rail") rails.Add(child); if (child.name == "Step") steps.Add(child); }
            _ladderRails = rails.ToArray(); _ladderSteps = steps.ToArray();
            _liftGate = _lift.Find("Gate"); _upperGate = transform.Find("Upper lift gate"); _lowerGate = transform.Find("Lower lift gate");
            _gasParticles = new ParticleSystem[_gasZones.Length];
            for (int i=0;i<_gasZones.Length;i++) _gasParticles[i]=_gasZones[i].GetComponentInChildren<ParticleSystem>();
            _boltRenderers = new Renderer[_parts.Length * 3];
            foreach (var target in _targets)
                if (target.Kind == YardTargetKind.Bolt) _boltRenderers[target.Index] = target.GetComponent<Renderer>();
            // Resolve the hook mesh before adding the line, otherwise Renderer lookup can select
            // the newly created LineRenderer and retain its unsupported default material in URP.
            var hookMaterial = _hook.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            _wire = _hook.gameObject.AddComponent<LineRenderer>();
            _wire.sharedMaterial = hookMaterial;
            _wire.positionCount = 2; _wire.startWidth = _wire.endWidth = .06f;
        }

        public void SetAuthority(bool authority)
        {
            if (_authority == authority && _homes != null && _authorityConfigured) return;
            _authorityConfigured = true;
            _authority = authority;
            for (int i = 0; i < _parts.Length; i++)
                _parts[i].isKinematic = !authority || _state.Parts[i].Secured || !_state.Parts[i].Open;
        }
        bool _authorityConfigured;

        public void Register(uint id, PlayerRig rig)
        {
            if (rig == null) return;
            if (_workers.TryGetValue(id, out var existing)) { existing.Rig = rig; return; }
            _workers.Add(id, new Worker { Rig = rig, State = new YardPlayerState { Id = id } });
        }

        public void Remove(uint id)
        {
            if (_state.Pilot == id) { _state.Pilot = 0; _craneVelocity = Vector3.zero; }
            if (_state.LadderCarrier == id)
            { _state.LadderCarrier = 0; _state.LadderDeployed = true; _state.LadderPosition = _ladderHome; }
            if (_state.Climber == id) _state.Climber = 0;
            if (_workers.TryGetValue(id, out var worker) && worker.Rig != null) worker.Rig.WorkstationMovementLocked = false;
            _workers.Remove(id);
        }

        public YardPlayerState PlayerState(uint id)
        {
            if (_authority && _workers.TryGetValue(id, out var worker)) return worker.State;
            foreach (var player in _state.Players) if (player.Id == id) return player;
            return new YardPlayerState { Id = id };
        }

        public void Submit(uint id, YardCommand command)
        {
            if (!_authority || _state.Launched || !_workers.TryGetValue(id, out var w) || w.Rig == null) return;
            if (!Stage2Rules.Finite(command.Axes) || !Stage2Rules.Finite(command.Aim) || !Stage2Rules.Finite(command.Turns)) return;
            command.Axes = Vector3.ClampMagnitude(command.Axes, 1.7321f);
            command.Aim = command.Aim.sqrMagnitude > .5f ? command.Aim.normalized : w.Rig.FacingDirection;
            command.Turns = Mathf.Clamp(command.Turns, 0, .12f);
            w.Command = command;
            w.LastInput = Time.time;
            if (w.Rig.Downed) return;
            if (command.Action != YardAction.None && Time.time - w.LastUse >= .2f)
            {
                if (_targetMap.TryGetValue(command.Target, out var target) && CanReach(w.Rig, target))
                { w.LastUse = Time.time; Use(id, w, target, command.Action); }
            }
            if (command.Primary && command.Turns > 0 && w.State.Tool == YardTool.Wrench && _state.Climber != id
                && _targetMap.TryGetValue(command.Target, out var bolt) && bolt.Kind == YardTargetKind.Bolt && CanReach(w.Rig, bolt))
            {
                // Server caps work by elapsed time, including command flooding.
                float turns = Mathf.Min(command.Turns, Mathf.Clamp(Time.time - w.LastTurns, 0, .15f) * 1.5f);
                w.LastTurns = Time.time;
                Tighten(bolt.Index / 3, bolt.Index % 3, turns / Stage2Rules.BoltTurns);
            }
        }

        public bool CanReach(PlayerRig rig, Stage2Target target)
        {
            Vector3 start = rig.Position + Vector3.up * .45f;
            Vector3 end = target.transform.position;
            if (Vector3.Distance(start, end) > Stage2Rules.UseRange) return false;
            return ClearLine(start, end, rig, target.transform);
        }

        static bool ClearLine(Vector3 start, Vector3 end, PlayerRig source, Transform destination)
        {
            foreach (var hit in Physics.RaycastAll(start, (end - start).normalized, Vector3.Distance(start, end), ~0, QueryTriggerInteraction.Ignore))
            {
                if (source != null && hit.collider.GetComponentInParent<PlayerRig>() == source) continue;
                if (destination != null && (hit.transform == destination || hit.transform.IsChildOf(destination))) continue;
                return false;
            }
            return true;
        }

        void Use(uint id, Worker w, Stage2Target target, YardAction action)
        {
            int index = target.Index;
            switch (target.Kind)
            {
                case YardTargetKind.Container:
                    _state.Parts[index].Open = true;
                    _parts[index].isKinematic = _state.Parts[index].Secured;
                    break;
                case YardTargetKind.Ring:
                    if (_state.Attached == index)
                    {
                        if (CanDetach(index)) _state.Attached = -1;
                    }
                    else if (_state.Attached < 0 && _state.Parts[index].Open
                        && Vector3.Distance(_hook.position, RingPosition(index)) < 2.2f) _state.Attached = index;
                    break;
                case YardTargetKind.Crane:
                    if (_state.Pilot == id) { _state.Pilot = 0; w.Rig.WorkstationMovementLocked = false; }
                    else if (_state.Pilot == 0 && _state.LadderCarrier != id && _state.Climber != id) _state.Pilot = id;
                    break;
                case YardTargetKind.Lift:
                    _state.LiftGoal = _state.LiftGoal < 4 ? 8f : 0f;
                    break;
                case YardTargetKind.Ladder:
                    if (_state.LadderCarrier == id) TryPlaceLadder(w);
                    else if (_state.LadderCarrier == 0 && !LadderOccupied() && _state.Pilot != id)
                    { _state.LadderCarrier = id; _state.LadderDeployed = false; }
                    break;
                case YardTargetKind.LadderClimb:
                    if (_state.Climber == 0 && _state.LadderDeployed && _state.LadderCarrier == 0)
                    {
                        _state.Climber = id; _climbTime = 0; _climbStart = w.Rig.FootPosition;
                        bool atTop = w.Rig.FootPosition.y > _ladder.position.y + .5f;
                        _climbEnd = _ladder.position + (atTop ? new Vector3(0, .1f, -2.6f) : new Vector3(0, _state.LadderHeight + .2f, 0));
                    }
                    break;
                case YardTargetKind.Wrench: w.State.Tool = YardTool.Wrench; break;
                case YardTargetKind.Sprayer: w.State.Tool = YardTool.Sprayer; break;
                case YardTargetKind.Bolt:
                    if (action == YardAction.Unfasten && w.State.Tool == YardTool.Wrench) Unfasten(index / 3, index % 3);
                    break;
                case YardTargetKind.Recovery:
                    RecoverParts();
                    break;
                case YardTargetKind.Launch:
                    if (ReadyToLaunch()) { _state.Launched = true; Completed?.Invoke(); }
                    break;
            }
        }

        public bool CanDetach(int i) => _state.Parts[i].Secured
            ? _hook.position.y <= RingPosition(i).y + .25f && Vector3.ProjectOnPlane(_hook.position - RingPosition(i), Vector3.up).sqrMagnitude < .75f * .75f
            : _parts[i].linearVelocity.sqrMagnitude < .05f && Physics.Raycast(_parts[i].position, Vector3.down, 1.6f);

        Vector3 RingPosition(int i) => _parts[i].position + Vector3.up * 1.6f;
        bool PreviousSecured(int i) => i == 0 || _state.Parts[i - 1].Secured;

        public string BoltCondition(int i)
        {
            if (!PreviousSecured(i)) return "아래 파츠부터 체결";
            if (_state.Attached != i) return "크레인으로 하중 지지 필요";
            if (!Stage2Rules.Aligned(_parts[i].position, _parts[i].rotation, _sockets[i].position)) return "접합 거리 / 기울기 조정";
            return "좌클릭 유지 + 마우스 원형 회전";
        }

        public bool Tighten(int i, int bolt, float amount)
        {
            if (!_authority || i < 0 || i >= _parts.Length || bolt < 0 || bolt > 2 || !Stage2Rules.Finite(amount) || amount <= 0
                || !PreviousSecured(i) || _state.Attached != i
                || !Stage2Rules.Aligned(_parts[i].position, _parts[i].rotation, _sockets[i].position)) return false;
            ref var p = ref _state.Parts[i];
            if (bolt == 0) p.A = Mathf.Clamp01(p.A + amount);
            if (bolt == 1) p.B = Mathf.Clamp01(p.B + amount);
            if (bolt == 2) p.C = Mathf.Clamp01(p.C + amount);
            if (Stage2Rules.Fastened(p.A, p.B, p.C))
            {
                p.Secured = true;
                _parts[i].isKinematic = true;
                _parts[i].position = _sockets[i].position;
                _parts[i].rotation = Quaternion.identity;
                if (i == 0) _state.GasEnabled = true;
            }
            return true;
        }

        void Unfasten(int i, int bolt)
        {
            // Upper supported parts must be removed first; the hook must carry the load.
            if (_state.Attached != i || (i + 1 < _parts.Length && _state.Parts[i + 1].Secured)
                || _hook.position.y < RingPosition(i).y - .15f) return;
            ref var p = ref _state.Parts[i];
            if (bolt == 0) p.A = 0; else if (bolt == 1) p.B = 0; else p.C = 0;
            p.Secured = false; _parts[i].isKinematic = false;
        }

        public bool ReadyToLaunch()
        {
            if (_state.Attached >= 0 || _state.Radius < 5 || Vector3.Distance(_hook.position, _launch.position + Vector3.up * _state.Height) < 6f) return false;
            foreach (var part in _state.Parts) if (!part.Secured) return false;
            foreach (var w in _workers.Values)
                if (w.Rig != null && (w.Rig.Downed || Vector3.Distance(w.Rig.FootPosition, _launch.position) > 8f)) return false;
            return true;
        }

        public void RecoverParts()
        {
            if (!_authority) return;
            if (_state.LadderCarrier == 0 && !LadderOccupied())
            { _state.LadderPosition = _ladderHome; _state.LadderDeployed = true; }
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_state.Parts[i].Secured || _state.Attached == i) continue;
                var body = _parts[i];
                if (Vector3.Distance(body.position, _homes[i]) < .1f) continue;
                bool nearPlayer = false;
                foreach (var w in _workers.Values) if (w.Rig != null && Vector3.Distance(w.Rig.Position, body.position) < 4f) nearPlayer = true;
                if (nearPlayer) continue;
                body.position = _homes[i]; body.rotation = _homeRotations[i];
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                _state.Parts[i].A = _state.Parts[i].B = _state.Parts[i].C = 0;
            }
        }

        public Stage2State Capture()
        {
            for (int i = 0; i < _parts.Length; i++)
            { _state.Parts[i].Position = _parts[i].position; _state.Parts[i].Rotation = _parts[i].rotation; }
            _state.Players = new YardPlayerState[_workers.Count];
            int at = 0;
            foreach (var w in _workers.Values)
            {
                if (w.Rig != null) { w.State.Position = w.Rig.Position; w.State.Downed = w.Rig.Downed; }
                _state.Players[at++] = w.State;
            }
            if (GameSession.Instance != null)
            { _state.Elapsed = GameSession.Instance.Elapsed; _state.ClearTime = GameSession.Instance.ClearTime; _state.Falls = GameSession.Instance.Falls; }
            return _state;
        }

        public void ApplyReplica(Stage2State state)
        {
            if (_authority || state == null || state.Parts == null || state.Parts.Length != _parts.Length || state.Players == null) return;
            _state = state;
            _replicas.Add(state);
            if (_replicas.Count > 8) _replicas.RemoveAt(0);
        }

        void ReplicaWindow(out Stage2State a, out Stage2State b, out float t)
        {
            a = b = _state; t = 1;
            if (_replicas.Count == 0 || _clock == null) return;
            double renderAt = _clock() - .1d;
            while (_replicas.Count > 2 && _replicas[1].Time < renderAt) _replicas.RemoveAt(0);
            a = _replicas[0]; b = _replicas.Count > 1 ? _replicas[1] : a;
            t = b.Time > a.Time ? Mathf.Clamp01((float)((renderAt - a.Time) / (b.Time - a.Time))) : 1f;
        }

        void OnDestroy()
        {
            foreach (var w in _workers.Values) if (w.Rig != null) w.Rig.WorkstationMovementLocked = false;
        }
    }
}
