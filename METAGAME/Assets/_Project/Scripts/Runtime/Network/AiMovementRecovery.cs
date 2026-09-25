using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    /// <summary>Server-driven recovery only; never changes player physics or wind strength.</summary>
    public sealed class AiMovementRecovery
    {
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly Collider[] overlaps = new Collider[32];
        NavMeshPath path;
        Vector3 checkpoint, latestSafe, progressPosition, escapeDirection;
        bool hasCheckpoint, initialized, suspended;
        float progressTime, airborneUntil, nextProbe, nextRestoreProbe, unreachableSince = -1, escapeUntil, lastRestore = -100;
        int restores, escapeAttempts;
        float airborneSince = -1;
        public string Status { get; private set; }
        public int RestoreCount => restores;

        public void Reset()
        {
            hasCheckpoint = initialized = suspended = false;
            progressTime = airborneUntil = nextProbe = nextRestoreProbe = escapeUntil = 0;
            unreachableSince = -1; lastRestore = -100; restores = escapeAttempts = 0; Status = null;
            airborneSince = -1;
        }

        public bool Tick(PlayerRig self, PlayerRig speaker, bool wantsMovement, out InputFrame frame, Vector3? movementGoal = null)
        {
            frame = default; Status = null;
            if (self == null || speaker == null || self.Downed || self.IsRiding) return false;
            float now = Time.time;
            if (!initialized) { initialized = true; progressPosition = self.FootPosition; progressTime = now; }
            // Preserve the order while the character is airborne or recovering its balance.
            if ((!self.Grounded && self.AirTime > .12f) || self.Ragdolled || self.Doll.Hips.linearVelocity.sqrMagnitude > 64f)
            {
                if (airborneSince < 0) airborneSince = now;
                airborneUntil = now + .4f; suspended = true; progressTime = now;
                Status = "날아감 · 착지 후 작업 재개";
                if (now - airborneSince > 8f && !self.Floating)
                {
                    var recoveryWorld = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
                    if (recoveryWorld != null && recoveryWorld.Ready)
                    { path ??= new NavMeshPath(); Restore(self, movementGoal ?? speaker.FootPosition, recoveryWorld); }
                }
                return true;
            }
            airborneSince = -1;
            if (now < airborneUntil) { Status = "착지 · 자세 회복 중"; return true; }
            if (suspended)
            {
                suspended = false; progressPosition = self.FootPosition; progressTime = now; nextProbe = 0;
                Status = "착지 완료 · 현재 위치에서 경로 재탐색"; return true;
            }
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (world == null || !world.Ready) { progressTime = now; Status = "복구 경로 준비 중"; return true; }
            path ??= new NavMeshPath();
            // A pin order belongs to the selected work site, not the speaker's subsequent movement.
            Vector3 goal = movementGoal ?? speaker.FootPosition;
            if (now >= nextProbe)
            {
                nextProbe = now + .5f;
                bool connected = world.CalculatePath(self.FootPosition, goal, path);
                if (connected)
                {
                    unreachableSince = -1;
                    if (self.Grounded && escapeUntil == 0 && self.Doll.Hips.linearVelocity.sqrMagnitude < 36f
                        && SafeDestination(self, self.FootPosition, out var safe))
                    {
                        if (!hasCheckpoint) { checkpoint = latestSafe = safe; hasCheckpoint = true; }
                        // Keep a little history, rather than overwriting the fallback with the stuck position.
                        else if (Vector3.Distance(latestSafe, safe) >= 2f) { checkpoint = latestSafe; latestSafe = safe; }
                    }
                }
                // **내가 길찾기 바닥 위에 있는데 목적지까지만 길이 없으면 영역 이탈이 아니다** (S15P21B202-746).
                // 목적지(운석 착지점 · 높은 곳의 사람)가 막힌 것이다 — 그것은 작업이 판단한다. 2026-09-26 QA: 멀쩡한
                // 바닥에 선 동료가 "이동 영역 이탈 ↔ 안전 복귀 위치 없음" 을 수백 번 오가며 멈춰 있었다.
                else if (OnNavigation(self)) unreachableSince = -1;
                else if (unreachableSince < 0) unreachableSince = now;
            }
            if (unreachableSince >= 0)
            {
                progressTime = now;
                Status = "이동 영역 이탈 · 복귀 경로 확인";
                if (now - unreachableSince >= 3f) Restore(self, goal, world);
                return true;
            }
            if (escapeUntil > now)
            {
                Status = "끼임 복구 · 옆으로 빠져나오는 중";
                if (AiFollowController.SafeStep(self, self.FootPosition, escapeDirection, hits, .25f))
                    frame = new InputFrame { AimYaw = Mathf.Atan2(escapeDirection.x, escapeDirection.z) * Mathf.Rad2Deg,
                        Move = Vector2.up * .5f };
                return true;
            }
            if (escapeUntil > 0)
            {
                escapeUntil = 0; progressTime = now; progressPosition = self.FootPosition;
                Status = "끼임 복구 · 경로 재탐색"; return true;
            }
            if (!wantsMovement || Vector3.Distance(self.FootPosition, progressPosition) > .3f)
            { progressPosition = self.FootPosition; progressTime = now; escapeAttempts = 0; return false; }
            if (now - progressTime < 2f) return false;
            // Try physical movement before considering relocation. No jumping or obstacle bypass.
            for (int i = 0; i < 4 && escapeAttempts < 2; i++)
            {
                var direction = Quaternion.Euler(0, self.CurrentInput.AimYaw + 90f * (i + 1), 0) * Vector3.forward;
                if (!AiFollowController.SafeStep(self, self.FootPosition, direction, hits)) continue;
                escapeAttempts++; escapeDirection = direction; escapeUntil = now + .9f;
                Status = "끼임 복구 · 이동 방향 변경"; return true;
            }
            Status = "끼임 복구 · 안전 위치 확인";
            Restore(self, goal, world);
            return true;
        }

        void Restore(PlayerRig self, Vector3 goal, AiNavigationWorld world)
        {
            if (Time.time < nextRestoreProbe) return;
            nextRestoreProbe = Time.time + .5f;
            // Do not transport equipment, carried players, or an AI a player is holding.
            if (Time.time - lastRestore < 10f || !hasCheckpoint
                || self.HeldTool != null || self.Carrying || self.LeftHand.Gripping || self.RightHand.Gripping
                || self.GrabbedByOthers)
            { Status = "안전 복귀 대기 · 복귀 조건 확인 필요"; return; }
            if (!SafeDestination(self, checkpoint, out var destination)
                || !world.CalculatePath(destination, goal, path))
            { Status = "안전 복귀 위치 없음 · 대기"; return; }
            self.Teleport(destination + Vector3.up * .08f, Quaternion.Euler(0, self.CurrentInput.AimYaw, 0));
            restores++; lastRestore = Time.time; unreachableSince = -1; progressTime = Time.time;
            progressPosition = self.FootPosition; nextProbe = 0; escapeUntil = 0; escapeAttempts = 0;
            Status = "안전 위치 복귀 · 기존 작업 재개";
        }

        /// <summary>발밑이 길찾기 바닥인가 — 목적지가 막힌 것과 내가 벗어난 것을 가른다.</summary>
        static bool OnNavigation(PlayerRig self)
            => NavMesh.SamplePosition(self.FootPosition, out var nav, .6f, NavMesh.AllAreas)
               && Mathf.Abs(nav.position.y - self.FootPosition.y) < .5f;

        bool SafeDestination(PlayerRig self, Vector3 point, out Vector3 result)
        {
            result = default;
            if (!NavMesh.SamplePosition(point, out var nav, .5f, NavMesh.AllAreas)
                || Mathf.Abs(nav.position.y - point.y) > .35f) return false;
            if (!Physics.Raycast(nav.position + Vector3.up * .3f, Vector3.down, out var ground, .6f,
                GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore) || ground.normal.y < .77f
                || ground.collider.attachedRigidbody != null) return false;
            var foot = ground.point;
            int n = Physics.OverlapCapsuleNonAlloc(foot + Vector3.up * .48f, foot + Vector3.up * 1.4f, .38f,
                overlaps, ~((1 << GameLayers.Interactable) | (1 << GameLayers.BeamQuery)), QueryTriggerInteraction.Ignore);
            if (n == overlaps.Length) return false;
            for (int i = 0; i < n; i++)
                if (overlaps[i].GetComponentInParent<PlayerRig>() != self) return false;
            result = foot; return true;
        }
    }
}
