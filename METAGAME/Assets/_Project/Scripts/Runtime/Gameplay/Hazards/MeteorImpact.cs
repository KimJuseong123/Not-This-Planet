using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 운석 충격의 세기 단계(기획 §20.1).
    ///
    /// §20.1 은 "체력 없는 상태 기반 피해" 라는 제목 아래 세 단계를 정한다. 체력을 깎아
    /// 넷째 번에 기절하는 것이 아니라 <b>맞은 방식이 결과를 정한다.</b>
    /// </summary>
    public enum MeteorImpactTier
    {
        /// <summary>반경 밖. 아무 일도 없다.</summary>
        None,

        /// <summary>약한 충격파 — "밀려나지만 바로 행동 가능".</summary>
        Shockwave,

        /// <summary>강한 충격 — "크게 날아가고 들고 있던 장비를 떨어뜨림".</summary>
        Heavy,

        /// <summary>운석 직접 충돌 — "날아간 뒤 기절".</summary>
        Direct,
    }

    /// <summary>
    /// 충격 단계와 크기별 힘을 정하는 순수 계산.
    ///
    /// <b>상태가 없다.</b> <see cref="MeteorWarningLook"/> 이 낙하 예고의 표시값을 순수
    /// 함수로 빼둔 것과 같은 방식이다(S15P21B202-241) — 검증이 거리를 직접 넣어 경계를
    /// 잠글 수 있고, 렌더러도 플레이어도 필요 없다.
    /// </summary>
    public static class MeteorImpact
    {
        /// <summary>
        /// 착탄 지점에서 <paramref name="distance"/> 만큼 떨어진 곳이 받는 단계.
        ///
        /// 직접 충돌은 거리로 알 수 없다 — 운석 본체가 몸에 닿았는지는 충돌 콜백만 안다.
        /// 그래서 <see cref="MeteorImpactTier.Direct"/> 는 여기서 나오지 않는다.
        /// </summary>
        public static MeteorImpactTier TierFor(float distance, float radius)
            => TierFor(distance, radius, PhysicsScale.MeteorHeavyImpactFraction);

        public static MeteorImpactTier TierFor(float distance, float radius, float heavyImpactFraction)
        {
            if (radius <= 0f || distance > radius) return MeteorImpactTier.None;

            var heavy = radius * Mathf.Clamp01(heavyImpactFraction);
            return distance <= heavy ? MeteorImpactTier.Heavy : MeteorImpactTier.Shockwave;
        }

        /// <summary>
        /// 크기에 해당하는 폭발력. 소형이 기준이고 <b>반경 비율만큼</b> 커진다.
        ///
        /// 새 상수를 만들지 않는다. 중형의 반경(3.5m)이 이미 정해져 있으므로
        /// (기획 §17.7) 힘도 거기서 끌어낸다 — 상수를 하나 더 두면 반경만 바꿨을 때
        /// 힘이 따라오지 않는다.
        /// </summary>
        public static float ForceFor(MeteorSize size)
        {
            var scale = Meteor.RadiusFor(size) / PhysicsScale.MeteorSmallImpactRadius;
            return PhysicsScale.MeteorImpactForce * scale;
        }
    }
}
