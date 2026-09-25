using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    public sealed partial class AiWorkTask
    {
        ToolBase tool;
        Grabbable toolGrab;
        Collider toolCollider;
        Rigidbody practice;
        uint swingBefore;
        float lastWeldProgress;
        Vector3 weldPoint;
        Transform weldSurface;
        int beadsBefore;
        SpaceshipPart weldPartner;
        float pairStrengthBefore;
        bool ToolMatches(ToolBase candidate) => candidate != null &&
            ((action == "pickup_tool" || action == "bring_tool") ||
             (action == "pickup_welder" || action == "bring_welder" || action == "weld" || action == "cut_weld"
                ? candidate is PlasmaWeldingTool : candidate is MeteorBat));
        static bool ToolFree(Grabbable item)
        {
            if (item == null || !item.CanGrab || !item.isActiveAndEnabled || !item.IsTool || item.HandCount != 0) return false;
            var value = item.GetComponent<ToolBase>() ?? item.GetComponentInChildren<ToolBase>();
            return value != null && !value.IsEquipped && !value.IsHeldForView;
        }
        bool BeginTool()
        {
            PhaseAfterDelivery = false;
            if (action == "drop_tool") { Phase(1); return true; }
            practice = null; weldSurface = null; weldPartner = null;
            if (action == "hit_target")
            {
                float distance = 30f * 30f;
                foreach (var target in Object.FindObjectsByType<PracticeMeteorTarget>(FindObjectsSortMode.None))
                {
                    float d = (target.transform.position - speaker.FootPosition).sqrMagnitude;
                    if (target.gameObject.scene == self.gameObject.scene && d < distance)
                    { practice = target.GetComponent<Rigidbody>(); distance = d; }
                }
                if (practice == null) { Finish(false, "타격 가능한 연습 대상 없음"); return false; }
            }
            if (action == "weld" || action == "cut_weld")
            {
                part = NearestPart();
                if (part == null || !FindWeldPoint()) { Finish(false, "접근 가능한 접합 표면 없음"); return false; }
                if (!Claim(part)) { Finish(false, "다른 AI가 이 파츠 용접 중"); return false; }
                if (action == "weld" && !Claim(weldPartner)) { Finish(false, "다른 AI가 상대 파츠 작업 중"); return false; }
                beadsBefore = part.GetComponent<WeldBead>()?.BlobCount ?? 0;
                pairStrengthBefore = part.GetComponent<WeldBead>()?.StrengthTo(weldPartner) ?? 0;
            }
            if (!FindTool())
            {
                // 운석 막기는 계속하는 일이다 (S15P21B202-746) — 방망이가 지금 없으면(누가 들었거나 멀면)
                // 실패하지 않고 기다렸다 다시 찾는다. 2026-09-25 QA: "사용 가능한 도구 없음" 으로 곧바로 끝났다.
                if (action == "defend_meteors") { Phase(7); Status = "쓸 수 있는 방망이 기다리는 중"; return true; }
                Finish(false, "사용 가능한 도구 없음"); return false;
            }
            SetupToolGrip();
            Phase(self.HeldTool == toolGrab ? 4 : self.HeldTool != null ? 1 : 2);
            return true;
        }

        void SetupToolGrip()
        {
            toolCollider = null; float closest = float.PositiveInfinity;
            foreach (var collider in toolGrab.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                float d = (collider.ClosestPoint(self.Position) - self.Position).sqrMagnitude;
                if (d < closest) { closest = d; toolCollider = collider; }
            }
        }

        /// <summary>
        /// 쓸 도구를 고른다. 이미 든 것이 맞으면 그것. 아니면 <b>이 동료에게서</b> 가까운 빈 도구(전에는 말한 사람
        /// 기준 40m — 방망이가 그 밖이면 운석 막기가 곧바로 실패했다).
        /// </summary>
        bool FindTool()
        {
            tool = ToolMatches(self.ActiveTool) ? self.ActiveTool : null;
            toolGrab = tool != null ? self.HeldTool : null;
            float nearest = ToolSearchRange * ToolSearchRange;
            if (tool == null)
            foreach (var item in Grabbable.All)
            {
                if (item == null || item.gameObject.scene != self.gameObject.scene || !ToolFree(item)) continue;
                var candidate = item.GetComponent<ToolBase>() ?? item.GetComponentInChildren<ToolBase>();
                float distance = (item.transform.position - self.FootPosition).sqrMagnitude;
                if (!ToolMatches(candidate) || distance >= nearest || (claims.TryGetValue(item, out var other) && other != null && other != self)) continue;
                if (!StandNear(item.transform.position, .6f, out var spot)) continue;
                tool = candidate; toolGrab = item; goal = spot; nearest = distance;
            }
            return tool != null && Claim(toolGrab);
        }

        /// <summary>도구를 찾는 반경(m) — 이 동료 기준.</summary>
        public const float ToolSearchRange = 90f;

        /// <summary>쓸 방망이가 없을 때 다시 찾는 간격(초).</summary>
        public const float ToolRetrySeconds = 2f;

        InputFrame TickTool()
        {
            var input = new InputFrame();
            if (phase == 7)
            {
                // 방망이를 기다리는 중 (운석 막기). 가끔 다시 찾는다 — 매 틱 훑으면 비싸다.
                if (Time.time - phaseAt < ToolRetrySeconds) return input;
                if (!FindTool()) { Phase(7); Status = "쓸 수 있는 방망이 기다리는 중"; return input; }
                SetupToolGrip();
                Phase(self.HeldTool == toolGrab ? 4 : self.HeldTool != null ? 1 : 2);
                return input;
            }
            if (PhaseAfterDelivery)
            {
                if (self.HeldTool == null) Finish(true, "도구 운반·내려놓기 완료");
                else if (Time.time - phaseAt > 2f) Finish(false, "운반 후 내려놓기 실패");
                return input;
            }
            if (phase == 1)
            {
                if (self.HeldTool == null)
                { if (action == "drop_tool") Finish(true, "도구 내려놓기 완료"); else Phase(2); return input; }
                if (!pulse) { input.ToolUtility = true; pulse = true; }
                if (Time.time - phaseAt > 2f) Finish(false, "도구 내려놓기 실패");
                return input;
            }
            if (tool == null || toolGrab == null) { Finish(false, "대상 도구 사라짐"); return input; }
            bool held = self.HeldTool == toolGrab;
            if (!held && !ToolFree(toolGrab))
            {
                if (action == "defend_meteors") { ReleaseClaims(); tool = null; toolGrab = null; Phase(7); Status = "쓸 수 있는 방망이 기다리는 중"; return input; }
                Finish(false, "다른 플레이어가 도구 사용 중"); return input;
            }
            if (phase == 2 || phase == 3)
            {
                if (held) { Phase(4); return input; }
                if (phase == 2)
                {
                    Status = "도구로 이동 중";
                    if (!Navigate(goal, .15f, ref input)) return input;
                    Phase(3);
                }
                input = Aim(toolCollider != null ? toolCollider.bounds.center : toolGrab.transform.position);
                if (self.CurrentInteractionTarget.Grabbable == toolGrab && self.CurrentInteractionTarget.CanInteract) input.GrabLeft = true;
                Status = "도구 잡는 중";
                if (Time.time - phaseAt > 5f) Finish(false, "도구에 손이 닿지 않음");
                return input;
            }
            if (!held)
            {
                // 운석 막기는 계속하는 일이다 — 맞고 방망이를 떨어뜨렸으면 다시 줍는다 (S15P21B202-746).
                if (action == "defend_meteors" && ToolFree(toolGrab) && StandNear(toolGrab.transform.position, .6f, out goal))
                { Phase(2); Status = "떨어뜨린 방망이 다시 줍는 중"; return input; }
                if (action == "defend_meteors") { ReleaseClaims(); tool = null; toolGrab = null; Phase(7); Status = "쓸 수 있는 방망이 기다리는 중"; return input; }
                Finish(false, "도구를 놓쳐 작업 중단"); return input;
            }
            if (phase == 4)
            {
                // The pickup click must be released before the ordinary tool action can fire.
                if (action.StartsWith("pickup_")) { Finish(true, "도구 집기 완료"); return input; }
                if (action.StartsWith("bring_")) goal = destinationPoint;
                else if (action == "hit_target")
                {
                    if (practice == null || !StandNear(practice.position, 1.2f, out goal))
                    { Finish(false, "타격 대상 접근 불가"); return input; }
                }
                else if (action == "weld" || action == "cut_weld")
                {
                    if (weldSurface == null || !StandNear(weldSurface.TransformPoint(weldPoint), .65f, out goal))
                    { Finish(false, "용접 지점 접근 불가"); return input; }
                }
                else if (action == "defend_meteors")
                {
                    // 방망이를 들었으면 곧장 운석을 본다 (S15P21B202-746). 전에는 말한 사람 옆까지 걸어간 뒤에야
                    // 막기 시작했고, 그 자리에 선 채로 머리 위에 오는 것만 쳤다.
                    Phase(6); defendTarget = null; defendSwingPrimed = false; return input;
                }
                else goal = destinationPoint;
                Phase(5); return input;
            }
            if (phase == 5)
            {
                Status = "작업 위치로 이동 중";
                if (!Navigate(goal, .2f, ref input)) return input;
                Phase(6); lastWeldProgress = Time.time;
                swingBefore = tool is MeteorBat readyBat ? readyBat.RhythmSequence : 0;
                return input;
            }
            if (action.StartsWith("bring_"))
            {
                if (!pulse) { input.ToolUtility = true; pulse = true; }
                // Check success before the generic 'lost tool' check on the following tick.
                PhaseAfterDelivery = true;
                return input;
            }
            if (action == "weld" || action == "cut_weld") return TickWelding();
            var bat = tool as MeteorBat;
            if (bat == null) { Finish(false, "방망이 필요"); return input; }
            if (action == "hit_target")
            {
                if (practice == null) { Finish(false, "타격 대상 사라짐"); return input; }
                input = Aim(practice.worldCenterOfMass);
                if (bat.RhythmSequence != swingBefore)
                { if (!bat.IsSwinging) Finish(bat.LastPushed == practice, bat.LastPushed == practice ? "타격 완료" : "타격 빗나감"); return input; }
                if (bat.CanSwing && !FriendlyInFront(practice.position - self.Position)) input.ToolPrimary = true;
                Status = "연습 대상 타격"; return input;
            }
            return TickDefend(bat);
        }

        // ── 운석 쳐내기 (S15P21B202-746) ────────────────────────────────────────
        //
        // ① 방망이 집기(위) → ② 떨어질 자리로 뛰기 → ③ 가장 좋은 때 휘두르기. ②③을 계속 한다.
        // 전에는 ②가 없었다 — 선 자리에서 머리 위로 오는 것만 쳤으므로, 옆에 떨어지는 운석은 그냥 봤다.

        /// <summary>뛰는 속도를 이만큼만 믿는다 — 돌아가는 길·출발·멈춤에 잃는 몫.</summary>
        public const float ChaseSpeedFactor = .8f;

        /// <summary>보고 움직이기까지 드는 시간(초). 닿을 수 있나를 셀 때 뺀다.</summary>
        public const float ChaseReactionSeconds = .25f;

        /// <summary>
        /// 떨어질 자리에서 이만큼 떨어져 선다(m). 방망이는 착지점이 <c>BatAssistRadius</c>(2.5m) 안이면 맞는데,
        /// 가장자리에 서면 예측이 조금만 흔들려도 빠진다.
        /// </summary>
        public const float StrikeStandOffset = 1f;

        /// <summary>휘두르기를 한 틱 앞당긴다 — 입력은 다음 물리 틱에 들어간다.</summary>
        public static float StrikeLead => Time.fixedDeltaTime;

        /// <summary>길이 안 날 때 곧장 걸어갈 만한 거리(m). 더 멀면 벽 너머일 수 있다.</summary>
        public const float DirectChaseRange = 8f;

        /// <summary>쫓을 것이 없을 때 말한 사람에게서 이보다 멀면 돌아간다(m).</summary>
        public const float DefendLeash = 7f;

        static readonly System.Collections.Generic.Dictionary<Meteor, PlayerRig> chasing =
            new System.Collections.Generic.Dictionary<Meteor, PlayerRig>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetChasing() => chasing.Clear();

        Meteor defendTarget;
        float nextPick;

        /// <summary>방망이를 들고 운석을 막는 중인가 — 이때는 운석을 피하지 않는다(일부러 착지점 앞에 선다).</summary>
        public bool Striking => action == "defend_meteors" && tool is MeteorBat && self != null && self.HeldTool == toolGrab;

        /// <summary>
        /// 이 운석을 칠 수 있게 제때 닿을 수 있나. <b>순수 함수</b>다 — 검사가 값으로 부른다.
        ///
        /// <para>휘두르는 때는 떨어지기 <c>window × PerfectAt</c> 초 전이다(판정이 가장 좋은 때). 그때까지
        /// 착지점에서 <c>assist − StrikeStandOffset</c> 안에 들어가야 한다. 이미 그 안이면 시간이 없어도 된다.</para>
        /// </summary>
        public static bool CanReach(float planarDistance, float secondsToImpact, float window, float assistRadius, float runSpeed)
        {
            float inside = Mathf.Max(.3f, assistRadius - StrikeStandOffset);
            float need = Mathf.Max(0f, planarDistance - inside);
            if (need <= 0f) return secondsToImpact > 0f;
            float time = secondsToImpact - window * MeteorBat.PerfectAt - ChaseReactionSeconds;
            return time > 0f && need <= runSpeed * ChaseSpeedFactor * time;
        }

        uint defendSwingSeen;
        bool defendSwingPrimed;

        InputFrame TickDefend(MeteorBat bat)
        {
            var input = default(InputFrame);
            // 휘두른 것이 운석을 맞혔으면 센다 — 화면이 "운석 N개 쳐냄" 을 띄운다. 처음 보는 방망이의 번호는 셈에 넣지 않는다.
            if (!defendSwingPrimed) { defendSwingPrimed = true; defendSwingSeen = bat.RhythmSequence; }
            else if (bat.RhythmSequence != defendSwingSeen)
            {
                defendSwingSeen = bat.RhythmSequence;
                if (bat.LastDeflected != null) Tally++;
            }
            // ③ 칠 수 있는 운석이 들어왔고 때가 되었으면 휘두른다. 판정과 같은 함수로 고른다 —
            //    방망이가 실제로 맞힐 운석이어야 한다.
            var strike = MeteorBat.FindTarget(self.Position);
            if (strike != null && bat.CanSwing && !bat.IsSwinging && strike.TryPredictImpact(out var left, out _)
                && left <= strike.BatSwingWindowSeconds * MeteorBat.PerfectAt + StrikeLead)
            {
                input = Aim(strike.transform.position);
                input.ToolPrimary = true;
                Status = "운석 쳐내는 중";
                return input;
            }
            if (bat.IsSwinging) { Status = "운석 쳐내는 중"; return input; }

            // ② 쫓을 운석을 고른다. 경로 계산이 있어 매 틱은 하지 않는다.
            if (Time.time >= nextPick || !Chaseable(defendTarget))
            { nextPick = Time.time + .1f; Choose(PickMeteor()); }
            if (defendTarget == null) return Guard();
            defendTarget.TryPredictImpact(out var seconds, out var impact);
            var stand = StandFor(impact);
            var offset = stand - self.FootPosition; offset.y = 0;
            if (offset.magnitude <= .35f)
            {
                Status = "운석 기다리는 중 · " + seconds.ToString("0.0") + "초";
                return Aim(defendTarget.transform.position);
            }
            Status = "운석 쪽으로 뛰는 중 · " + seconds.ToString("0.0") + "초";
            if (!Chase(stand, .3f, ref input, true)) { }
            if (seconds < .9f) { var aim = Aim(defendTarget.transform.position); input.AimYaw = aim.AimYaw; input.AimPitch = aim.AimPitch; }
            return input;
        }

        /// <summary>쫓을 수 있는 운석인가 — 아직 날아오고, 누가 쳐내지 않았고, 다른 동료가 쫓지 않는다.</summary>
        bool Chaseable(Meteor meteor)
        {
            if (meteor == null || meteor.Harmless || meteor.GroundProp || meteor.WasDeflected || !meteor.isActiveAndEnabled) return false;
            if (chasing.TryGetValue(meteor, out var other) && other != null && other != self && other.isActiveAndEnabled) return false;
            if (!meteor.TryPredictImpact(out var seconds, out var impact)) return false;
            var offset = StandFor(impact) - self.FootPosition; offset.y = 0;
            return CanReach(offset.magnitude, seconds, meteor.BatSwingWindowSeconds, meteor.BatAssistRadius, Tuning.RunSpeed);
        }

        /// <summary>제때 닿을 수 있는 운석 중 <b>가장 먼저 떨어질</b> 것. 없으면 null.</summary>
        Meteor PickMeteor()
        {
            if (Chaseable(defendTarget)) return defendTarget;   // 쫓던 것을 쉽게 바꾸지 않는다 — 오락가락하면 둘 다 놓친다
            Meteor best = null; float soonest = float.PositiveInfinity;
            foreach (var meteor in Meteor.All)
            {
                if (!Chaseable(meteor)) continue;
                meteor.TryPredictImpact(out var seconds, out var impact);
                if (seconds >= soonest) continue;
                if (!NavMesh.SamplePosition(StandFor(impact), out var standHit, 1.5f, NavMesh.AllAreas)) continue;   // 설 수 없는 곳
                if (!ReachableStand(standHit.position)) continue;
                soonest = seconds; best = meteor;
            }
            return best;
        }

        NavMeshPath standPath;

        /// <summary>
        /// 그 자리까지 길이 나는가 — 한 운석을 고를 때 한 번만 잰다(0.1초마다 여럿은 아니다). 가까우면 곧장 걸어가므로 길을
        /// 묻지 않는다. 2026-09-26 QA: 길이 없는 착지점으로 가려다 막힘 복구가 "이동 영역 이탈" 에 갇혔다.
        /// </summary>
        bool ReachableStand(Vector3 stand)
        {
            var offset = stand - self.FootPosition; offset.y = 0;
            if (offset.magnitude <= DirectChaseRange * .5f) return true;
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (world == null || !world.Ready) return false;
            standPath ??= new NavMeshPath();
            return world.CalculatePath(self.FootPosition, stand, standPath);
        }

        void ReleaseChase()
        {
            if (defendTarget != null && chasing.TryGetValue(defendTarget, out var mine) && mine == self) chasing.Remove(defendTarget);
            defendTarget = null;
        }

        void Choose(Meteor meteor)
        {
            if (meteor == defendTarget) return;
            if (defendTarget != null && chasing.TryGetValue(defendTarget, out var mine) && mine == self) chasing.Remove(defendTarget);
            defendTarget = meteor;
            if (meteor != null) chasing[meteor] = self;
            route.Reset();
        }

        /// <summary>떨어질 자리 앞, 동료 쪽으로 <see cref="StrikeStandOffset"/> 만큼. 운석이 방망이 앞에 떨어진다.</summary>
        Vector3 StandFor(Vector3 impact)
        {
            var back = self.FootPosition - impact; back.y = 0;
            if (back.sqrMagnitude < .01f) return impact;
            if (back.magnitude <= StrikeStandOffset) return self.FootPosition;
            return impact + back.normalized * StrikeStandOffset;
        }

        /// <summary>쫓을 것이 없을 때: 말한 사람 곁을 지키며 가장 먼저 올 운석 쪽을 본다.</summary>
        InputFrame Guard()
        {
            var input = default(InputFrame);
            var toSpeaker = speaker.FootPosition - self.FootPosition; toSpeaker.y = 0;
            if (toSpeaker.magnitude > DefendLeash)
            {
                Status = "운석 기다리며 곁으로 가는 중";
                Chase(speaker.FootPosition, DefendLeash - 3f, ref input, false);
                return input;
            }
            Status = "운석 기다리는 중";
            Meteor watch = null; float soonest = float.PositiveInfinity;
            foreach (var meteor in Meteor.All)
                if (meteor != null && !meteor.Harmless && !meteor.WasDeflected && meteor.TryPredictImpact(out var s, out _) && s < soonest)
                { soonest = s; watch = meteor; }
            if (watch != null) { var aim = Aim(watch.transform.position); input.AimYaw = aim.AimYaw; }
            return input;
        }

        /// <summary>
        /// <see cref="Navigate"/> 와 같게 걷되 <b>막혀도 작업을 끝내지 않는다</b> — 운석 하나를 못 쫓았다고
        /// 막기 전체를 버리면 안 된다. 닿았으면 참.
        /// </summary>
        bool Chase(Vector3 point, float stop, ref InputFrame input, bool run)
        {
            goal = point; WantsMovement = true;
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (!route.Steer(world, self.FootPosition, point, stop, out var direction))
            {
                // 길이 안 나면(방망이를 집은 자리처럼 길찾기 바닥 밖에 서 있으면) 가까운 곳은 곧장 걷는다 —
                // 운석은 기다려 주지 않는다. 앞이 막혔는지는 SafeStep 이 본다.
                var straight = point - self.FootPosition; straight.y = 0;
                if (straight.magnitude <= stop) { WantsMovement = false; return true; }
                if (straight.magnitude > DirectChaseRange) { WantsMovement = false; return false; }
                direction = straight.normalized; WantsMovement = false;
            }
            else if (route.Arrived) { WantsMovement = false; return true; }
            if (!AiFollowController.SteerAround(self, self.FootPosition, ref direction, stepHits, .18f)) return false;
            input = Aim(self.AimOrigin + direction);
            input.Move = Vector2.up; input.Run = run;
            return false;
        }


        bool PhaseAfterDelivery;
        bool FriendlyInFront(Vector3 direction)
        {
            direction.y = 0;
            foreach (var other in PlayerRig.All)
            {
                if (other == null || other == self) continue;
                var delta = other.Position - self.Position; delta.y = 0;
                if (delta.magnitude < PhysicsScale.BatStrikeReach && Vector3.Dot(delta, direction) > 0) return true;
            }
            return false;
        }

        bool FindWeldPoint()
        {
            bool cut = action == "cut_weld";
            // Inspect real collider surfaces; arbitrary nearby points are not valid weld contacts.
            foreach (var surface in part.GetComponentsInChildren<Collider>())
            {
                if (!surface.enabled || surface.isTrigger || surface.GetComponentInParent<PartClamp>() != null) continue;
                var bounds = surface.bounds;
                for (int side = 0; side < 8; side++)
                for (int height = 0; height < 9; height++)
                {
                    var outward = Quaternion.Euler(0, side * 45f, 0) * Vector3.forward;
                    var center = bounds.center; center.y = Mathf.Lerp(bounds.min.y, bounds.max.y, height / 8f);
                    var origin = center + outward * (bounds.extents.magnitude + .3f);
                    if (!surface.Raycast(new Ray(origin, -outward), out var hit, bounds.size.magnitude + 1f)) continue;
                    if (!WeldBead.CanApplyAt(hit, cut) || (!cut && !WeldBead.CanStartJoiningAt(hit))) continue;
                    var partner = cut ? null : WeldBead.JoiningTargetAt(hit);
                    if (!cut && (partner == null || (weldPartner != null && partner != weldPartner))) continue;
                    if (!StandNear(hit.point, .65f, out var stand) || Mathf.Abs(hit.point.y - (stand.y + 1f)) > 1.1f) continue;
                    if (!cut) weldPartner = partner;
                    weldSurface = surface.transform; weldPoint = weldSurface.InverseTransformPoint(hit.point); return true;
                }
            }
            return false;
        }
        InputFrame TickWelding()
        {
            if (part == null || weldSurface == null || !(tool is PlasmaWeldingTool welder))
            { Finish(false, "용접 대상 사라짐"); return default; }
            bool cut = action == "cut_weld";
            if (!cut && weldPartner == null) { Finish(false, "연결할 상대 파츠 사라짐"); return default; }
            var bead = part.GetComponent<WeldBead>();
            if (cut ? (bead == null || bead.BlobCount == 0) : (bead != null && bead.HasLiveBridge
                && bead.StrengthTo(weldPartner) >= Mathf.Min(1f, Mathf.Max(.25f, pairStrengthBefore + .15f))))
            { Finish(true, cut ? "용접 비드 제거 완료" : "용접 연결 확인"); return default; }
            var frame = Aim(weldSurface.TransformPoint(weldPoint));
            if (welder.TryGetBeamHit(self.AimForward, out var hit) && hit.collider.GetComponentInParent<SpaceshipPart>() == part
                && WeldBead.CanApplyAt(hit, cut)
                && (cut || (Vector3.Distance(hit.point, weldSurface.TransformPoint(weldPoint)) < .25f
                    && WeldBead.CanJoinTargetAt(hit, weldPartner))))
            { frame.ToolPrimary = !cut; frame.ToolSecondary = cut; }
            if (cut ? welder.CuttingProgressed : welder.WeldingProgressed) lastWeldProgress = Time.time;
            if (Time.time - lastWeldProgress > 4f)
            {
                if (FindWeldPoint()) { Phase(4); lastWeldProgress = Time.time; }
                else Finish(false, "접합 가능한 지점 없음 · 부분 작업 종료");
            }
            Status = cut ? "기존 용접건으로 절단 중" : "기존 용접건으로 용접 중";
            return frame;
        }
    }
}
