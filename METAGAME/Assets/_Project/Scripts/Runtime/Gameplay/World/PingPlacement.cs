using UnityEngine;
using Metagame.Core;

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
                                                PingMask, QueryTriggerInteraction.Ignore);

            float nearest = maxDistance;
            point = origin + direction * maxDistance;
            hitSurface = false;

            for (int i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (collider == null || Hits[i].distance >= nearest) continue;

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
