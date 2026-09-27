using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Network
{
    public sealed partial class AiWorkTask
    {
        TransportVehicle vehicle;
        Rigidbody cargo;
        SpaceshipPart stackBase;
        float flightHeight, cargoBottomOffset, cargoStartY, lostSince, boardPressAt, fieldPressAt;

        /// <summary>
        /// 파츠를 든 채 실패해 **내려놓는 중**이면 그 실패 이유 (S15P21B202-712). 전에는 장을 켠 채로
        /// 끝내서 파츠가 주인 없이 공중에 떠 있었다. 사람을 태웠을 때의 "높이서 떨어뜨리지 않는다"
        /// 규칙은 그대로다 — 이것은 파츠에만 쓴다.
        /// </summary>
        string abortReason;

        /// <summary>저중력장 켜기를 다시 누르는 간격(초). 매 틱 누르면 켜짐이 한 틱 늦을 때 도로 꺼질 수 있다.</summary>
        public const float FieldRepress = .5f;

        /// <summary>
        /// 탑승 E 를 다시 누르는 간격(초) (S15P21B202-712). 전에는 한 번만 눌러서, 그 한 번이 씹히면
        /// 5초를 다 기다린 뒤 실패했다. 타는 데 걸리는 시간보다 길게 두어 탄 뒤에 또 눌러 내리지 않는다.
        /// </summary>
        public const float BoardRepress = 1.5f;

        /// <summary>
        /// 내려놓기 정렬에 쓰는 시간(초) (S15P21B202-712). 전에는 제한이 없어 정렬이 안 맞으면 작업
        /// 전체 제한(180초)까지 떠 있었다. 넘기면 쥔 채로 다시 접근한다 — 재시도 한도는 안착 확인과 같이 쓴다.
        /// </summary>
        public const float LowerTimeout = 15f;

        /// <summary>견인할 수 있는 파츠인가 — 걷기 운반과 같은 이유로 가장 가까운 것 하나에 매달리지 않는다.</summary>
        bool Liftable(SpaceshipPart item)
            => !item.IsDockedToPad && item.RemainingRequiredClamps == 0 && item.State != PartState.Welded
               && !ClaimedByOther(item);
        readonly RaycastHit[] flightHits = new RaycastHit[64];
        readonly Collider[] flightOverlaps = new Collider[64];
        Collider[] vehicleColliders;
        Vector3[] flightRoute;
        Vector3 routeDestination;
        int flightWaypoint, landingRetries;
        float nextFlightPlan, settledSince;
        bool BeginVehicle()
        {
            vehicle = self.MountedVehicle;
            cargo = null; stackBase = null; lostSince = -1;
            flightRoute = null; flightWaypoint = landingRetries = 0; nextFlightPlan = 0; settledSince = -1;
            abortReason = null; fieldPressAt = 0;
            if (action == "leave_tractor" || action == "hold_tractor")
            {
                if (vehicle == null) { Finish(false, "견인 장치에 탑승하지 않음"); return false; }
                Phase(2); return true;
            }
            if (vehicle == null)
            {
                float best = float.PositiveInfinity;
                bool available = false;
                foreach (var item in Object.FindObjectsByType<TransportVehicle>(FindObjectsSortMode.None))
                {
                    if (!item.isActiveAndEnabled || item.IsOccupied || item.IsReturning || item.gameObject.scene != self.gameObject.scene) continue;
                    if (claims.TryGetValue(item, out var owner) && owner != null && owner != self) continue;
                    available = true;
                    float d = (item.transform.position - self.Position).sqrMagnitude;
                    if (d < best && BoardingSpot(item, out var stand))
                    { vehicle = item; goal = stand; best = d; }
                }
                if (vehicle == null && available) { Finish(false, "견인 장치는 있지만 걸어서 접근할 탑승 지점 없음"); return false; }
            }
            if (vehicle == null || !Claim(vehicle)) { Finish(false, "사용 가능한 견인 장치 없음"); return false; }
            vehicleColliders = vehicle.GetComponentsInChildren<Collider>();
            if (action == "lift_player")
            {
                if (speaker.IsRiding || speaker == self) { Finish(false, "운반할 플레이어가 탑승 중"); return false; }
                cargo = speaker.Hips; cargoBottomOffset = RagdollBuilder.HipsY;
            }
            else if (action == "lift_part" || action == "transport_part" || action == "stack_part")
            {
                part = NearestPart(false, Liftable);
                if (part == null || !Claim(part))
                { Finish(false, "견인 가능한 빈 파츠 없음"); return false; }
                cargo = part.GetComponent<Rigidbody>();
                if (cargo == null || cargo.isKinematic) { Finish(false, "움직일 수 없는 파츠"); return false; }
                cargoBottomOffset = part.TryGetHullBounds(out var bounds) ? cargo.position.y - bounds.min.y : .5f;
                if (action == "stack_part")
                {
                    float best = float.PositiveInfinity;
                    foreach (var candidate in Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None))
                    {
                        if (candidate == part || !candidate.CanReceiveAssembly || !candidate.TryGetHullBounds(out var support)) continue;
                        float d = (candidate.transform.position - speaker.FootPosition).sqrMagnitude;
                        if (d < best) { best = d; stackBase = candidate; destinationPoint = new Vector3(support.center.x, support.max.y, support.center.z); }
                    }
                    if (stackBase == null) { Finish(false, "아래에 놓을 조립 파츠 없음"); return false; }
                }
            }
            if (cargo != null)
            {
                cargoStartY = cargo.position.y;
                if (action == "lift_part") destinationPoint = cargo.position - Vector3.up * cargoBottomOffset;
                // '올려줘' without a named destination means a small vertical lift, not an invented hill.
                if (action == "lift_player" && destination == "default") destinationPoint = speaker.FootPosition + Vector3.up * 3f;
            }
            Phase(self.MountedVehicle == vehicle ? 2 : 0); return true;
        }
        void StopVehicle()
        {
            if (vehicle != null && speaker != null) speaker.ReleaseAssistedFloat(vehicle.Field);
            // Cancellation brakes but preserves the field: never drop a passenger from altitude.
            if (vehicle != null && vehicle.Rider == self)
            { vehicle.SetDriveVector(Vector3.zero); vehicle.SetVerticalInput(0); vehicle.SetBoost(false); }
        }
        bool BoardingSpot(TransportVehicle item, out Vector3 spot)
        {
            spot = default; float best = float.PositiveInfinity;
            var hull = new Bounds(item.transform.position, Vector3.one);
            var vehiclePart = item.GetComponent<SpaceshipPart>();
            if (vehiclePart != null && vehiclePart.TryGetHullBounds(out var authored)) hull = authored;
            // Probe around the actual hull at the ground/navmesh, never the emergency exit in mid-air.
            for (int i = 0; i < 16; i++)
            {
                var direction = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
                var edge = hull.ClosestPoint(hull.center + direction * (hull.extents.magnitude + 2f));
                edge.y = hull.min.y;
                if (!StandNear(edge + direction * .5f, .35f, out var stand)) continue;
                float distance = (stand - self.FootPosition).sqrMagnitude;
                if (distance < best) { best = distance; spot = stand; }
            }
            return !float.IsPositiveInfinity(best);
        }
        InputFrame TickVehicle()
        {
            var input = new InputFrame();
            if (vehicle == null || vehicle.Body == null) { Finish(false, "견인 장치 사라짐"); return input; }
            if (phase < 2)
            {
                if (self.MountedVehicle == vehicle) { Phase(2); return input; }
                if (vehicle.IsOccupied) { Finish(false, "다른 플레이어가 탑승함"); return input; }
                Status = "견인 장치 탑승 위치로 이동";
                if (phase == 0)
                {
                    if (!Navigate(goal, .2f, ref input)) return input;
                    Phase(1);
                }
                input = Aim(vehicle.Body.worldCenterOfMass);
                if (pulse && Time.time - boardPressAt > BoardRepress) pulse = false;
                if (self.CurrentInteractionTarget.Transport == vehicle && self.CurrentInteractionTarget.CanInteract && !pulse)
                { input.TransportAction = true; pulse = true; boardPressAt = Time.time; }
                if (Time.time - phaseAt > 5f) Finish(false, "견인 탑승 불가 · 고정/잠금/거리 확인 필요");
                return input;
            }
            if (self.MountedVehicle != vehicle) { Finish(false, "견인 장치에서 이탈함"); return input; }
            if (action == "board_tractor") { Finish(true, "견인 탑승 완료"); return input; }
            if (action == "hold_tractor") { Status = "견인 위치·고도 유지 중"; return input; }
            if (action == "leave_tractor")
            {
                // Descend before using the same E exit input as the player.
                Status = "안전하게 하강 후 하차";
                if (!vehicle.IsTouchingSurface) input.ArmsUp = true;
                else { input.TransportAction = true; Finish(true, "견인 하차 요청"); }
                return input;
            }
            bool needsCargo = action != "move_tractor";
            if (needsCargo && (cargo == null || (part != null && ((part.IsDockedToPad && phase != 6) || part.RemainingRequiredClamps != 0))))
            { Finish(false, "운반 대상 사라짐/고정됨"); return input; }
            var field = vehicle.Field;
            if (cargo == speaker.Hips && field.IsActive && phase >= 3 && phase < 6)
            {
                var floatTarget = field.Origin.position - Vector3.up * 3.5f;
                if (phase == 3) floatTarget.y = Mathf.Max(cargoStartY + .75f, floatTarget.y);
                if (phase == 5) floatTarget = destinationPoint + Vector3.up * (cargoBottomOffset + .35f);
                if (speaker.CurrentInput.Move.sqrMagnitude > .01f || speaker.CurrentInput.Jump || speaker.CurrentInput.ArmsUp)
                { Finish(false, "플레이어 직접 조작 · 운반 보조 해제"); return input; }
                speaker.RequestAssistedFloat(field, floatTarget);
            }
            if (phase == 6)
            {
                if (part != null && part.TryGetHullBounds(out var landed))
                {
                    if (stackBase != null && stackBase.TryGetHullBounds(out var support))
                        destinationPoint = new Vector3(support.center.x, support.max.y, support.center.z);
                    var error = landed.center - destinationPoint; error.y = 0;
                    bool stable = AiFlightCorridor.Settled(landed.min.y - destinationPoint.y,
                        error.magnitude, cargo.linearVelocity.magnitude, cargo.angularVelocity.magnitude)
                        && HasLandingSupport(landed);
                    if (!stable) settledSince = -1;
                    else if (settledSince < 0) settledSince = Time.time;
                    if (settledSince >= 0 && Time.time - settledSince >= 1.2f)
                    {
                        if (abortReason != null) Finish(false, abortReason + " · 파츠는 바닥에 내려놓음");
                        else Finish(true, "파츠 안착 확인 완료 · 용접은 별도 작업");
                        return input;
                    }
                }
                else if (cargo == speaker.Hips && AiFlightCorridor.Settled(
                    speaker.FootPosition.y - destinationPoint.y, Vector2.Distance(
                    new Vector2(cargo.position.x, cargo.position.z), new Vector2(destinationPoint.x, destinationPoint.z)),
                    cargo.linearVelocity.magnitude, 0))
                { Finish(true, "플레이어 착지 확인 완료"); return input; }
                Status = "하역 후 실제 안착 확인 중";
                if (Time.time - phaseAt > 7f)
                {
                    if (abortReason == null && landingRetries++ < 2 && part != null && !part.IsDockedToPad)
                    { flightRoute = null; Phase(2); Status = "안착 불안정 · 재인양 시도"; }
                    else Finish(false, abortReason ?? "안착 확인 실패 · 수동 확인 필요");
                }
                return input;
            }
            float originOffset = field.Origin.position.y - vehicle.Body.position.y;
            if (phase == 2)
            {
                if (!needsCargo)
                { flightHeight = Mathf.Max(vehicle.Body.position.y, destinationPoint.y + 5f); Phase(4); }
                else
                {
                    // Enter the cone above the target before enabling the field.
                    var pickup = cargo.position; pickup.y += 3.5f - originOffset;
                    if (!FlyCorridor(pickup, ref input, false)) return input;
                    flightRoute = null;
                    Phase(3); cargoStartY = cargo.position.y;
                }
                return input;
            }
            if (phase == 3)
            {
                Status = "저중력장 인양 확인 중";
                if (!field.IsActive && Time.time >= fieldPressAt)
                { input.FieldToggle = true; fieldPressAt = Time.time + FieldRepress; }
                if (field.IsActive && field.Contains(cargo.position) && cargo.position.y > cargoStartY + .25f)
                {
                    if (action == "lift_part") { action = "hold_tractor"; return input; }
                    flightHeight = Mathf.Max(vehicle.Body.position.y, destinationPoint.y + cargoBottomOffset + 4f - originOffset);
                    Phase(4); return input;
                }
                if (Time.time - phaseAt > 15f) FailVehicle("대상이 떠오르지 않음 · 인양 실패");
                return input;
            }
            if (phase == 4)
            {
                if (needsCargo)
                {
                    var delta = cargo.position - field.Origin.position; delta.y = 0;
                    float depth = field.Origin.position.y - cargo.position.y;
                    if (!field.Contains(cargo.position) || delta.magnitude > field.RadiusAt(depth) * .6f)
                    {
                        if (lostSince < 0) lostSince = Time.time;
                        Status = "대상 이탈 · 정지 후 재확보";
                        var reacquire = cargo.position + Vector3.up * (3.5f - originOffset);
                        Fly(reacquire, ref input, false);
                        if (Time.time - lostSince > 10f) Finish(false, "견인 대상 재확보 실패");
                        return input;
                    }
                    lostSince = -1;
                }
                var target = destinationPoint; target.y = flightHeight;
                if (!FlyCorridor(target, ref input, needsCargo)) return input;
                if (!needsCargo) { Finish(true, "견인 장치 이동 완료"); return input; }
                Phase(5); return input;
            }
            if (Time.time - phaseAt > LowerTimeout)
            {
                if (abortReason == null && landingRetries++ < 2 && needsCargo)
                { flightRoute = null; Phase(4); Status = "하역 정렬 실패 · 다시 접근"; return input; }
                FailVehicle("하역 정렬 시간 초과"); return input;
            }
            Status = "목적지 하역 중";
            if (stackBase != null && stackBase.TryGetHullBounds(out var liveSupport))
                destinationPoint = new Vector3(liveSupport.center.x, liveSupport.max.y, liveSupport.center.z);
            if (part != null && part.TryGetHullBounds(out var liveCargo))
                cargoBottomOffset = cargo.position.y - liveCargo.min.y;
            if (action == "lift_player" && destination == "default")
            { action = "hold_tractor"; Status = "플레이어 승강 · 고도 유지"; return input; }
            // Lower until the cargo's bottom is near the support. Do not release from cruise altitude.
            float gap = cargo.position.y - cargoBottomOffset - destinationPoint.y;
            var lowering = destinationPoint;
            lowering.y = vehicle.Body.position.y - Mathf.Clamp(gap - .35f, -.4f, .4f);
            Fly(lowering, ref input, true);
            if (!Active) return default;
            var alignment = cargo.position - destinationPoint; alignment.y = 0;
            if (gap <= (part != null ? PhysicsScale.FieldSurfaceLift + .25f : .5f) && gap >= -.25f
                && alignment.magnitude < .6f && cargo.linearVelocity.magnitude < .4f && cargo.angularVelocity.magnitude < .4f)
            {
                if (field.IsActive) { input.FieldToggle = true; Phase(6); }
            }
            return input;
        }
        bool Fly(Vector3 target, ref InputFrame input, bool loaded)
        {
            Status = "견인 이동 · 장애물/적재물 감시";
            var from = vehicle.Body.position; var delta = target - from;
            input = Aim(self.AimOrigin + new Vector3(delta.x, 0, delta.z));
            input.Jump = delta.y > .35f; input.ArmsUp = delta.y < -.35f;
            if (!ClearFlight(from, from + Vector3.ClampMagnitude(delta, 2f), loaded))
            {
                input.Jump = input.ArmsUp = false;
                if (Time.time - movedAt > 8f) FailVehicle("견인 통과 공간 부족 · 안전 정지");
                return false;
            }
            var horizontal = new Vector3(delta.x, 0, delta.z);
            if (horizontal.magnitude < .4f && Mathf.Abs(delta.y) < .5f) return true;
            if (Mathf.Abs(delta.y) > 1f) return false;
            if (horizontal.magnitude < bestDistance - .2f) { bestDistance = horizontal.magnitude; movedAt = Time.time; }
            if (Time.time - movedAt > 12f) { FailVehicle("견인 이동 진행 불가"); return false; }
            input.Move = Vector2.up * (loaded ? .18f : .35f) * Mathf.Clamp01(horizontal.magnitude);
            return false;
        }

        float FlightRadius(bool loaded)
        {
            float radius = 1.6f;
            if (loaded && part != null && part.TryGetHullBounds(out var bounds))
                radius = Mathf.Max(radius, Mathf.Max(bounds.extents.x, bounds.extents.z) + .3f);
            return radius;
        }
        /// <summary>
        /// 견인 중 실패 (S15P21B202-712). **파츠를 장에 든 채였으면** 바로 아래 바닥으로 내려놓고 장을
        /// 끈 뒤에 그 이유로 실패한다 — 내려놓기는 평소 하역(5단계)과 안착 확인(6단계)을 그대로 쓴다.
        /// 사람을 태웠거나, 들고 있지 않았거나, 이미 내려놓는 중이면 바로 실패한다.
        /// </summary>
        void FailVehicle(string why)
        {
            if (abortReason == null && part != null && cargo != null && vehicle != null && vehicle.Field != null
                && vehicle.Field.IsActive && phase >= 3 && phase <= 5 && vehicle.Field.Contains(cargo.position)
                && GroundBelow(cargo.position, out var ground))
            {
                abortReason = why; stackBase = null; destinationPoint = ground; flightRoute = null;
                Phase(5); Status = why + " · 파츠를 내려놓는 중";
                return;
            }
            Finish(false, abortReason != null ? abortReason + " · " + why : why);
        }

        /// <summary>파츠 바로 아래의 가장 가까운 평평한 바닥.</summary>
        bool GroundBelow(Vector3 from, out Vector3 ground)
        {
            ground = default; float best = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, flightHits, 60f,
                GameLayers.SurfaceMask | GameLayers.PartMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = flightHits[i];
                if (IgnoreFlight(hit.collider) || hit.normal.y < .7f) continue;
                if (hit.distance < best) { best = hit.distance; ground = hit.point; }
            }
            return best < float.PositiveInfinity;
        }

        bool HasLandingSupport(Bounds hull)
        {
            var origin = new Vector3(hull.center.x, hull.min.y + .15f, hull.center.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, flightHits, .5f,
                GameLayers.SurfaceMask | GameLayers.PartMask, QueryTriggerInteraction.Ignore);
            if (count == flightHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var hit = flightHits[i];
                if (IgnoreFlight(hit.collider) || hit.normal.y < .7f) continue;
                if (stackBase == null || hit.collider.GetComponentInParent<SpaceshipPart>() == stackBase) return true;
            }
            return false;
        }
        bool IgnoreFlight(Collider hit)
            => hit.attachedRigidbody == vehicle.Body || hit.attachedRigidbody == cargo
                || hit.transform.IsChildOf(self.transform);
        bool ClearFlight(Vector3 from, Vector3 to, bool loaded)
        {
            // Use the actual hull envelope: a sphere around the pivot falsely intersects the launch floor.
            var envelope = new Bounds(vehicle.Body.position, Vector3.one * .5f);
            if (vehicleColliders != null)
                foreach (var collider in vehicleColliders)
                    if (collider != null && collider.enabled && !collider.isTrigger && collider.attachedRigidbody == vehicle.Body)
                        envelope.Encapsulate(collider.bounds);
            if (loaded && part != null && part.TryGetHullBounds(out var hull)) envelope.Encapsulate(hull);
            else if (loaded && cargo != null) envelope.Encapsulate(new Bounds(cargo.position, new Vector3(1f, 2f, 1f)));
            var center = from + envelope.center - vehicle.Body.position;
            var half = envelope.extents + new Vector3(.2f, -.04f, .2f);
            half.y = Mathf.Max(.1f, half.y);
            int mask = GameLayers.SurfaceMask | GameLayers.PartMask;
            int count = Physics.OverlapBoxNonAlloc(center, half, flightOverlaps, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            if (count == flightOverlaps.Length) return false;
            for (int i = 0; i < count; i++) if (!IgnoreFlight(flightOverlaps[i])) return false;
            var delta = to - from;
            if (delta.sqrMagnitude < .001f) return true;
            count = Physics.BoxCastNonAlloc(center, half, delta.normalized, flightHits,
                Quaternion.identity, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
            if (count == flightHits.Length) return false;
            for (int i = 0; i < count; i++) if (!IgnoreFlight(flightHits[i].collider)) return false;
            return true;
        }
        bool FlyCorridor(Vector3 target, ref InputFrame input, bool loaded)
        {
            if (flightRoute != null && Vector3.Distance(routeDestination, target) > 1f) flightRoute = null;
            if (flightRoute == null)
            {
                if (Time.time < nextFlightPlan) return false;
                nextFlightPlan = Time.time + 1f;
                flightRoute = AiFlightCorridor.Find(vehicle.Body.position, target, FlightRadius(loaded),
                    (a, b) => ClearFlight(a, b, loaded));
                routeDestination = target; flightWaypoint = 0;
                if (flightRoute == null)
                {
                    Status = "안전한 상승·우회 항로 탐색 중";
                    if (Time.time - movedAt > 8f) FailVehicle("통과 가능한 견인 항로 없음 · 안전 정지");
                    return false;
                }
                movedAt = Time.time; bestDistance = float.PositiveInfinity;
            }
            var waypoint = flightRoute[flightWaypoint];
            if (!ClearFlight(vehicle.Body.position, waypoint, loaded))
            { flightRoute = null; input = default; Status = "항로 변경 · 재탐색"; return false; }
            if (!Fly(waypoint, ref input, loaded)) return false;
            flightWaypoint++; movedAt = Time.time; bestDistance = float.PositiveInfinity;
            if (flightWaypoint < flightRoute.Length) return false;
            flightRoute = null; return true;
        }
    }
}
