using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 카메라 중앙 조준선이 가리키는 핑 위치를 계산한다.
    /// 표면을 찾지 못해도 최대 거리의 가상 끝점을 반환하므로 하늘을 가리켜도
    /// 팀원이 같은 방향을 읽을 수 있다.
    /// </summary>
    public static class PingPlacement
    {
        const int IgnoreRaycastLayer = 2;
        const int PingMask = ~((1 << GameLayers.Player)
                             | (1 << GameLayers.PlayerDowned)
                             | (1 << IgnoreRaycastLayer));
        const int HitBufferSize = 32;
        static readonly RaycastHit[] Hits = new RaycastHit[HitBufferSize];

        public static bool TryResolve(Vector3 origin, Vector3 direction, PlayerRig ignoredPlayer,
                                      out Vector3 point, out bool hitSurface,
                                      float maxDistance = Tuning.PingMaxDistance)
        {
            if (direction.sqrMagnitude < 1e-6f || maxDistance <= 0f)
            {
                point = origin;
                hitSurface = false;
                return false;
            }

            direction.Normalize();
            maxDistance = Mathf.Max(0.01f, maxDistance);
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, maxDistance,
                                                PingMask, QueryTriggerInteraction.Collide);

            float nearest = maxDistance;
            point = origin + direction * maxDistance;
            hitSurface = false;

            for (int i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (collider == null || Hits[i].distance >= nearest) continue;

                // 박힌 Clamp는 바닥과 실제 충돌하지 않도록 Trigger로 둔다. 핑은
                // 그 Trigger를 표면으로 써야 고정못 뒤의 바닥에 찍히지 않는다.
                // 구조 지점·소켓·기타 질의 전용 Trigger까지 핑 표면이 되면 안 되므로
                // Clamp 외의 Trigger는 기존처럼 건너뛴다.
                if (collider.isTrigger && collider.GetComponentInParent<PartClamp>() == null)
                    continue;

                var player = collider.GetComponentInParent<PlayerRig>();
                // 전달받은 플레이어 자신뿐 아니라 다른 플레이어의 몸도 핑 표면으로 쓰지 않는다.
                if (player != null) continue;

                nearest = Hits[i].distance;
                point = Hits[i].point;
                hitSurface = true;
            }

            return true;
        }
    }
}
