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

        /// <summary>
        /// 겹침 검사 캡슐을 발보다 띄우는 높이(m).
        ///
        /// 발밑에 딛고 있는 것을 장애물로 세지 않기 위한 것이다. 너무 크면 발목 높이의
        /// 장애물을 놓친다.
        /// </summary>
        const float FootSkin = 0.05f;

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

            // <b>GroundMask 로는 씬에서 아무것도 못 찾았다.</b> 지면
            // (ENV_Ground_Arena_50m)이 레이어 0(Default)에 있고 Ground(8) 레이어
            // 오브젝트가 씬에 하나도 없다 — 그래서 끼임 탈출이 조용히 죽어 있었다
            // (2026-09-02 실측, S15P21B202-254).
            //
            // 플레이어의 접지 판정과 같은 기준을 쓴다. 설 수 있는 곳이면 탈출해서 설
            // 수도 있어야 한다.
            if (!UnityEngine.Physics.Raycast(from, Vector3.down, out var hit, distance,
                                             GameLayers.SurfaceMask,
                                             QueryTriggerInteraction.Ignore))
                return false;

            foot = hit.point;
            return IsClear(foot);
        }

        /// <summary>
        /// 발 위치에 몸이 들어갈 자리가 있는가.
        ///
        /// 발이 딛는 것 자체는 세지 않는다 — 지면에 닿아 있는 것이 정상이다.
        ///
        /// <b>예전에는 <see cref="GameLayers.GroundMask"/> 를 마스크에서 빼서 그것을
        /// 표현했다.</b> 그런데 씬의 지면은 레이어 0(Default)이라 빠지지 않았고, 지면이
        /// 곧 장애물로 걸려 어디에도 설 수 없었다(S15P21B202-254). 레이어로 가르지 않고
        /// <b>캡슐을 발보다 조금 위에서 시작</b>한다 — 어느 레이어에 있든 발밑은 세지 않고,
        /// 무릎 높이의 장애물은 그대로 걸린다.
        /// </summary>
        public static bool IsClear(Vector3 foot)
        {
            var radius = BodyRadius;
            var bottom = foot + Vector3.up * (radius + FootSkin);
            var top = foot + Vector3.up * Mathf.Max(BodyHeight - radius, radius + FootSkin);

            // 사람은 컴포넌트로 한 번 더 거르지만, 마스크에서 미리 빼는 것이 싸다.
            var mask = ~GameLayers.PlayerMask;

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
