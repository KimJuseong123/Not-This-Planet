using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>경로의 다음 모서리를 향해 기존 PlayerRig 입력으로 걷는다. 위치를 직접 변경하지 않는다.</summary>
    public sealed class AiFollowController
    {
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly AiNavigationRoute route = new AiNavigationRoute();
        PlayerRig target;
        float bestRemaining;
        Vector3 progressGoal;
        float progressTime;
        bool moving;
        Vector3? fixedGoal;
        Vector3 formationOffset;
        public Vector3? Goal => fixedGoal ?? (target != null ? target.FootPosition + formationOffset : (Vector3?)null);
        public string Status { get; private set; } = "대기";
        public bool Active => target != null;
        public bool Arrived { get; private set; }
        public bool WantsMovement => Active && !Arrived;
        public void SuspendMovement() { Arrived = false; route.Reset(); moving = false; progressTime = Time.time; }

        public bool Apply(string action, PlayerRig speaker)
        {
            if (action == "cancel" || action == "wait") { Stop(); return true; }
            if (action != "follow" || speaker == null) return false;
            target = speaker;
            Arrived = false;
            fixedGoal = null; formationOffset = Vector3.zero;
            progressTime = Time.time;
            moving = false;
            route.Reset();
            Status = "따라오기";
            return true;
        }

        public void SetFormation(Vector3 offset, bool gather)
        { Arrived = false; formationOffset = offset; fixedGoal = gather && target != null ? target.FootPosition + offset : (Vector3?)null; route.Reset(); }
        public void Stop() { Arrived = false; target = null; fixedGoal = null; formationOffset = Vector3.zero; moving = false; route.Reset(); Status = "대기"; }

        public static InputFrame Direction(Vector3 from, Vector3 to, float stopDistance)
        {
            Vector3 d = to - from;
            if (Mathf.Abs(d.y) > 1.5f) return default;
            d.y = 0;
            if (d.magnitude <= stopDistance || d.magnitude > 30f) return default;
            return new InputFrame { AimYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Move = Vector2.up };
        }

        public InputFrame Tick(PlayerRig self, bool allowed)
        {
            Arrived = false;
            if (!allowed || self == null || self.Downed || self.IsRiding) { Stop(); return default; }
            if (target == null) return default;
            if (target.Downed) { Stop(); return default; }
            var from = self.FootPosition;
            var to = Goal.Value;
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (world == null || !world.Ready) { Status = "이동 경로 준비 중"; return default; }
            if (!route.Steer(world, from, to, formationOffset == Vector3.zero ? 2.3f : .65f, out var direction))
            {
                Status = "도달 가능한 경로 없음 · 대기";
                moving = false;
                return default;
            }
            if (route.Arrived) { Arrived = true; moving = false; Status = "곁에서 대기"; return default; }
            if (!moving || (to - progressGoal).sqrMagnitude > 4f)
            { bestRemaining = route.Remaining; progressGoal = to; progressTime = Time.time; moving = true; }
            if (route.Remaining < bestRemaining - .25f) { bestRemaining = route.Remaining; progressTime = Time.time; }
            if (Time.time - progressTime > 6f) { Stop(); Status = "이동 불가 · 다시 지시해 주세요"; return default; }
            if (!SteerAround(self, from, ref direction, hits)) { Status = "장애물 앞에서 경로 재탐색"; return default; }
            Status = "따라오기";
            return new InputFrame { AimYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, Move = Vector2.up };
        }

        static readonly float[] AroundAngles = { 30f, -30f, 60f, -60f, 90f, -90f };

        /// <summary>
        /// 앞이 막혔으면 비스듬히 돌아 나갈 방향을 찾는다 (S15P21B202-746). 길찾기 바닥은 움직이는 것(떨어진 운석 ·
        /// 밀린 파츠)을 모른다 — 2026-09-26 QA 에서 갈 수 있는 곳 앞에 운석이 떨어지자 동료가 선 채로 멈췄다.
        /// 돌아 나갈 곳이 있으면 참이고 <paramref name="direction"/> 을 그쪽으로 바꾼다.
        /// </summary>
        internal static bool SteerAround(PlayerRig self, Vector3 from, ref Vector3 direction, RaycastHit[] hits, float lookAhead = .6f)
        {
            if (SafeStep(self, from, direction, hits, lookAhead)) return true;
            foreach (float angle in AroundAngles)
            {
                var turned = Quaternion.Euler(0, angle, 0) * direction;
                if (SafeStep(self, from, turned, hits, lookAhead)) { direction = turned; return true; }
            }
            return false;
        }

        internal static bool SafeStep(PlayerRig self, Vector3 from, Vector3 direction, RaycastHit[] hits, float lookAhead = .6f)
        {
            int mask = ~((1 << GameLayers.Interactable) | (1 << GameLayers.BeamQuery) | (1 << GameLayers.Meteor));
            int n = Physics.SphereCastNonAlloc(from + Vector3.up * .85f, .3f, direction, hits,
                lookAhead, mask, QueryTriggerInteraction.Ignore);
            if (n == hits.Length) return false;
            for (int i = 0; i < n; i++)
                if (hits[i].normal.y < .7f && hits[i].collider.GetComponentInParent<PlayerRig>() != self) return false;
            n = Physics.RaycastNonAlloc(from + direction * (lookAhead + .05f) + Vector3.up, Vector3.down,
                hits, 1.8f, mask, QueryTriggerInteraction.Ignore);
            if (n == hits.Length) return false;
            for (int i = 0; i < n; i++)
                if (hits[i].normal.y >= .77f && hits[i].collider.attachedRigidbody == null
                    && hits[i].collider.GetComponentInParent<PlayerRig>() == null) return true;
            return false;
        }
    }
}
