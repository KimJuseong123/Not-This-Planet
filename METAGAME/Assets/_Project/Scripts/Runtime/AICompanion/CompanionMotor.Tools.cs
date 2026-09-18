using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.AICompanion
{
    // Chooses input only. Equip, drop, damage and deflection remain owned by PlayerRig/ToolBase.
    public sealed partial class CompanionMotor
    {
        enum ToolPhase { Drop, Approach, Grip, Ready, ToTarget, Swing, Defend }
        ToolPhase _toolPhase;
        ToolBase _wantedTool;
        Grabbable _toolGrab;
        Collider _toolCollider;
        Rigidbody _practiceBody;
        Vector3 _toolStand, _guardPoint;
        float _toolStarted, _toolPhaseAt, _nextMeteorCheck;
        bool _dropPulse;
        uint _swingBefore;
        public string ToolJobTarget => _wantedTool != null ? _wantedTool.name : "";

        public static bool IsToolOrder(CompanionOrder order)
            => order >= CompanionOrder.PickUpTool && order <= CompanionOrder.DefendMeteors;

        void ResetToolJob()
        {
            _wantedTool = null; _toolGrab = null; _toolCollider = null; _practiceBody = null;
            _dropPulse = false; _nextMeteorCheck = 0;
        }

        static bool Matches(ToolBase tool, CompanionOrder order)
            => tool != null && (order == CompanionOrder.PickUpTool
                || (order == CompanionOrder.PickUpWelder ? tool is PlasmaWeldingTool : tool is MeteorBat));

        public static bool ToolIsFree(Grabbable grab)
        {
            if (grab == null || !grab.isActiveAndEnabled || !grab.CanGrab || !grab.IsTool
                || grab.Climbable || grab.HandCount != 0) return false;
            var body = grab.GetComponent<Rigidbody>() ?? grab.GetComponentInParent<Rigidbody>();
            var tool = grab.GetComponent<ToolBase>() ?? grab.GetComponentInChildren<ToolBase>();
            return body != null && !body.isKinematic && body.mass < Tuning.FixedGripMass
                && tool != null && !tool.IsEquipped && !tool.IsHeldForView;
        }

        public bool TryStartTool(CompanionOrder order, Transform speaker, out string reason)
        {
            reason = "도구 작업을 시작할 수 없어요";
            if (_rig == null || _rig.Downed || _rig.IsRiding || !IsToolOrder(order)) return false;
            if (order == CompanionOrder.DropTool)
            {
                SetOrder(order, null);
                BeginToolPhase(ToolPhase.Drop);
                _toolStarted = Time.time;
                reason = _rig.HeldTool != null ? "들고 있는 도구를 내려놓을게요" : "들고 있는 도구가 없어요";
                return true;
            }
            if (speaker == null) { reason = "요청한 플레이어가 없어요"; return false; }
            Rigidbody target = null;
            if (order == CompanionOrder.HitTarget)
            {
                float nearest = 12 * 12;
                foreach (var practice in FindObjectsByType<PracticeMeteorTarget>())
                {
                    float distance = (practice.transform.position - speaker.position).sqrMagnitude;
                    if (practice.gameObject.scene != speaker.gameObject.scene || distance > nearest) continue;
                    var body = practice.GetComponent<Rigidbody>();
                    if (body == null || body.isKinematic) continue;
                    nearest = distance; target = body;
                }
                if (target == null) { reason = "12m 안에 연습용 공이 없어요"; return false; }
            }
            ToolBase chosen = _rig.ActiveTool;
            Grabbable chosenGrab = _rig.HeldTool;
            Vector3 stand = _rig.Position;
            if (!Matches(chosen, order))
            {
                chosen = null; chosenGrab = null;
                float nearest = 12 * 12;
                foreach (var grab in Grabbable.All)
                {
                    if (grab.gameObject.scene != speaker.gameObject.scene || !ToolIsFree(grab)) continue;
                    var tool = grab.GetComponent<ToolBase>() ?? grab.GetComponentInChildren<ToolBase>();
                    if (!Matches(tool, order)) continue;
                    float distance = (grab.transform.position - speaker.position).sqrMagnitude;
                    if (distance >= nearest || !TryStandNear(grab.transform.position, .75f, out var spot)) continue;
                    chosen = tool; chosenGrab = grab; stand = spot; nearest = distance;
                }
            }
            if (chosen == null) { reason = "12m 안에 접근 가능한 빈 도구가 없어요"; return false; }
            SetOrder(order, null);
            _wantedTool = chosen; _toolGrab = chosenGrab; _practiceBody = target;
            // Authored tools use compound child colliders; their root pivot is not the grip surface.
            float closest = float.MaxValue;
            foreach (var collider in chosenGrab.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.GetComponentInParent<Grabbable>() != chosenGrab) continue;
                float distance = (collider.ClosestPoint(_rig.Doll.Head.position) - _rig.Doll.Head.position).sqrMagnitude;
                if (distance < closest) { closest = distance; _toolCollider = collider; }
            }
            _toolStand = stand; _guardPoint = speaker.position; _guardPoint.y = _rig.Position.y;
            _toolStarted = Time.time;
            BeginToolPhase(_rig.HeldTool == chosenGrab ? ToolPhase.Ready
                : _rig.HeldTool != null ? ToolPhase.Drop : ToolPhase.Approach);
            reason = "작업 수락 · " + chosenGrab.Label;
            return true;
        }

        bool TryStandNear(Vector3 center, float radius, out Vector3 result)
        {
            result = default;
            if (!NavMesh.SamplePosition(_rig.Position, out var from, 2f, NavMesh.AllAreas)) return false;
            float best = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                candidate.y = from.position.y;
                if (!NavMesh.SamplePosition(candidate, out var spot, .45f, NavMesh.AllAreas)
                    || !NavMesh.CalculatePath(from.position, spot.position, NavMesh.AllAreas, _path)
                    || _path.status != NavMeshPathStatus.PathComplete) continue;
                float distance = (from.position - spot.position).sqrMagnitude;
                if (distance >= best) continue;
                result = spot.position; best = distance;
            }
            return best < float.MaxValue;
        }

        void BeginToolPhase(ToolPhase phase)
        {
            _toolPhase = phase; _toolPhaseAt = Time.time; _nextPath = 0;
            _dropPulse = false;
        }

        void AimAt(Vector3 point, ref InputFrame input)
        {
            input.AimFrom = _rig.Doll.Head.position;
            var delta = point - input.AimFrom;
            if (delta.sqrMagnitude < .001f) return;
            var angle = Quaternion.LookRotation(delta).eulerAngles;
            input.AimYaw = angle.y; input.AimPitch = Mathf.DeltaAngle(0, angle.x);
        }

        void ToolDone(string status, ref InputFrame input)
        { input = default; SetOrder(CompanionOrder.Wait, null); Status = status; }

        void TickTool(ref InputFrame input)
        {
            if (_rig.Downed || _rig.IsRiding)
            { ToolDone("작업 중단 · 현재 몸 상태에서 실행할 수 없어요", ref input); return; }
            if (_toolPhase != ToolPhase.Defend && Time.time - _toolStarted > 35)
            { ToolDone("작업 실패 · 도구 접근/타격 시간 초과", ref input); return; }
            if (_toolPhase == ToolPhase.Drop)
            {
                Status = "도구를 내려놓는 중";
                if (_rig.HeldTool == null)
                {
                    if (Order == CompanionOrder.DropTool) ToolDone("내려놓기 완료", ref input);
                    else BeginToolPhase(ToolPhase.Approach);
                    return;
                }
                // Q tap then release: existing charge threshold selects drop, never throw.
                if (!_dropPulse) { input.ToolUtility = true; _dropPulse = true; Debug.Log("[AI_TOOL] drop press " + Order); }
                if (Time.time - _toolPhaseAt > 1) ToolDone("작업 실패 · 도구를 놓지 못했어요", ref input);
                return;
            }
            if (_wantedTool == null || _toolGrab == null)
            { ToolDone("작업 실패 · 대상 도구가 없어졌어요", ref input); return; }
            bool held = _rig.HeldTool == _toolGrab;
            if (!held && !ToolIsFree(_toolGrab))
            { ToolDone("작업 실패 · 다른 사람이 도구를 먼저 잡았어요", ref input); return; }
            if (_toolPhase == ToolPhase.Approach || _toolPhase == ToolPhase.Grip)
            {
                if (held) { BeginToolPhase(ToolPhase.Ready); return; }
                if (_toolPhase == ToolPhase.Approach)
                {
                    Status = "도구로 이동 중 · " + _toolGrab.Label;
                    if (!Navigate(_toolStand, .15f, ref input)) return;
                    BeginToolPhase(ToolPhase.Grip);
                }
                Status = "도구에 손을 뻗는 중 · " + _toolGrab.Label;
                var collider = _toolCollider;
                AimAt(collider != null ? collider.bounds.center : _toolGrab.transform.position, ref input);
                if (_rig.CurrentInteractionTarget.Grabbable == _toolGrab && _rig.CurrentInteractionTarget.CanInteract)
                    input.GrabLeft = true;
                if (Time.time - _toolPhaseAt > 5)
                {
                    Debug.Log($"[AI_TOOL] grip timeout: feet={_rig.FootPosition:F2}, head={_rig.Doll.Head.position:F2}, tool={_toolGrab.transform.position:F2}, bounds={(collider != null ? collider.bounds.ToString() : "none")}, focus={_rig.CurrentInteractionTarget.Kind}/{_rig.CurrentInteractionTarget.Grabbable}, aim={_rig.AimForward:F2}, hands={_rig.LeftHand.Gripping}/{_rig.RightHand.Gripping}");
                    ToolDone("작업 실패 · 도구에 손이 닿지 않아요", ref input);
                }
                return;
            }
            if (!held) { ToolDone("작업 중단 · 들고 있던 도구를 놓쳤어요", ref input); return; }
            if (_toolPhase == ToolPhase.Ready)
            {
                // One neutral frame releases the pickup click suppression before using the tool.
                if (Order == CompanionOrder.PickUpTool || Order == CompanionOrder.PickUpBat || Order == CompanionOrder.PickUpWelder)
                { ToolDone("집기 완료 · " + _toolGrab.Label, ref input); return; }
                if (Order == CompanionOrder.HitTarget)
                {
                    if (_practiceBody == null || !TryStandNear(_practiceBody.position, 1.3f, out _toolStand))
                    { ToolDone("작업 실패 · 연습용 공에 접근할 수 없어요", ref input); return; }
                }
                else _toolStand = _guardPoint;
                BeginToolPhase(ToolPhase.ToTarget);
                return;
            }
            var bat = _wantedTool as MeteorBat;
            if (bat == null) { ToolDone("작업 실패 · 몽둥이가 필요해요", ref input); return; }
            if (_toolPhase == ToolPhase.ToTarget)
            {
                Status = Order == CompanionOrder.HitTarget ? "연습용 공으로 이동 중" : "방어 위치로 이동 중";
                if (!Navigate(_toolStand, .2f, ref input)) return;
                BeginToolPhase(Order == CompanionOrder.HitTarget ? ToolPhase.Swing : ToolPhase.Defend);
                _swingBefore = bat.RhythmSequence;
            }
            if (_toolPhase == ToolPhase.Swing)
            {
                if (_practiceBody == null) { ToolDone("작업 실패 · 연습용 공이 없어졌어요", ref input); return; }
                AimAt(_practiceBody.worldCenterOfMass, ref input);
                Status = "연습용 공 타격 준비";
                if (bat.RhythmSequence != _swingBefore)
                {
                    if (!bat.IsSwinging) ToolDone(bat.LastPushed == _practiceBody ? "타격 완료 · 연습용 공" : "타격 실패 · 대상이 벗어났어요", ref input);
                    return;
                }
                if (FriendlyInFront(_practiceBody.position - _rig.Position)) { Status = "타격 대기 · 앞에 동료가 있어요"; return; }
                if (bat.CanSwing) input.ToolPrimary = true;
                return;
            }
            Status = "근거리 운석 방어 중 · 현재 위치 유지";
            if (Time.time < _nextMeteorCheck || !bat.CanSwing || bat.IsSwinging) return;
            _nextMeteorCheck = Time.time + .1f;
            var meteor = MeteorBat.FindTarget(_rig.Position);
            if (meteor == null || meteor.WasDeflected || !meteor.TryPredictImpact(out var seconds, out var impact)
                || seconds > meteor.BatSwingWindowSeconds * .65f) return;
            Vector3 facing = impact - _rig.Position; facing.y = 0;
            if (facing.sqrMagnitude < .01f) facing = Vector3.forward;
            if (FriendlyInFront(facing)) { Status = "방어 대기 · 타격 방향에 동료가 있어요"; return; }
            AimAt(_rig.Doll.Head.position + facing.normalized * 5, ref input);
            input.ToolPrimary = true;
            Status = "운석 타격 · 기존 방망이 판정 사용";
        }

        bool FriendlyInFront(Vector3 facing)
        {
            facing.y = 0;
            foreach (var player in PlayerRig.All)
            {
                if (player == null || player == _rig) continue;
                var offset = player.Position - _rig.Position; offset.y = 0;
                if (offset.magnitude < PhysicsScale.BatStrikeReach && Vector3.Dot(offset, facing) > 0) return true;
            }
            return false;
        }
    }
}
