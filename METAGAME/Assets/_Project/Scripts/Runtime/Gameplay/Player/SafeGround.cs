using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 캐릭터가 설 수 있는 빈 자리를 찾는다(기획 §21 "가까운 안전 지면").
    ///
    /// <b>스폰 지점으로 보내지 않는다.</b> 예전 <c>GameSession.Respawn</c> 이 그렇게 했고,
    /// 20분짜리 스테이지에서 끼었다고 처음 자리로 돌아가는 것은 복구가 아니라 처벌이다
    /// (S15P21B202-248).
    ///
    /// 상태가 없다. 검증이 지면과 장애물을 직접 놓고 결과를 볼 수 있다.
    /// </summary>
    public static class SafeGround
    {
        const int ProbeSize = 8;
        static readonly Collider[] Probe = new Collider[ProbeSize];

        /// <summary>몸이 들어갈 반지름(m). 가장 넓은 곳은 헬멧이다.</summary>
        public static float BodyRadius => RagdollBuilder.HelmetD * 0.5f;

        /// <summary>몸이 들어갈 높이(m).</summary>
        public static float BodyHeight => RagdollBuilder.TotalHeight;

        /// <summary>
        /// <paramref name="fromFoot"/> 에서 가까운 순서로 안전한 발 위치를 찾는다.
        ///
        /// 반지름을 <see cref="PhysicsScale.StuckEscapeSearchStep"/> 씩 늘려 가며
        /// 방향을 훑는다. <b>제자리는 후보가 아니다</b> — 끼어 있다는 것은 지금 자리가
        /// 안전하지 않다는 뜻이다.
        ///
        /// 못 찾으면 <c>false</c> 다. 벽 속으로 옮기는 것보다 아무것도 하지 않는 것이 낫다.
        /// </summary>
        public static bool TryFind(Vector3 fromFoot, out Vector3 foot)
        {
            foot = fromFoot;

            var directions = Mathf.Max(1, PhysicsScale.StuckEscapeSearchDirections);
            var step = Mathf.Max(0.1f, PhysicsScale.StuckEscapeSearchStep);

            for (var radius = step; radius <= PhysicsScale.StuckEscapeSearchRadius; radius += step)
            {
                for (var i = 0; i < directions; i++)
                {
                    var angle = 360f / directions * i;
                    var offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;

                    if (!TryLand(fromFoot + offset, out var candidate)) continue;

                    foot = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 한 후보 지점에 설 수 있는가. 지면을 찾고 그 위가 비어 있는지 본다.
        /// </summary>
        static bool TryLand(Vector3 near, out Vector3 foot)
        {
            foot = near;

            // 끼었다는 것은 대개 지면보다 아래에 박혀 있다는 뜻이므로 위에서 훑는다.
            var from = near + Vector3.up * PhysicsScale.StuckEscapeGroundProbe;
            var distance = PhysicsScale.StuckEscapeGroundProbe * 2f;

            if (!UnityEngine.Physics.Raycast(from, Vector3.down, out var hit, distance,
                                             GameLayers.GroundMask,
                                             QueryTriggerInteraction.Ignore))
                return false;

            foot = hit.point;
            return IsClear(foot);
        }

        /// <summary>
        /// 발 위치에 몸이 들어갈 자리가 있는가.
        ///
        /// 지면 자신은 세지 않는다 — 발이 지면에 닿아 있는 것이 정상이다. 그래서 캡슐을
        /// 지면 위로 올려 잡고 <see cref="GameLayers.GroundMask"/> 를 마스크에서 뺀다.
        /// </summary>
        public static bool IsClear(Vector3 foot)
        {
            var radius = BodyRadius;
            var bottom = foot + Vector3.up * radius;
            var top = foot + Vector3.up * Mathf.Max(BodyHeight - radius, radius);

            var mask = ~GameLayers.GroundMask;

            var count = UnityEngine.Physics.OverlapCapsuleNonAlloc(
                bottom, top, radius, Probe, mask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var collider = Probe[i];
                if (collider == null) continue;

                // 자기 몸은 세지 않는다. 검증과 실제 모두 플레이어가 후보 지점 근처에
                // 있을 수 있고, 자기 팔다리 때문에 "자리가 없다" 가 되면 영영 못 나온다.
                if (collider.GetComponentInParent<PlayerRig>() != null) continue;

                return false;
            }

            return true;
        }
    }
}
