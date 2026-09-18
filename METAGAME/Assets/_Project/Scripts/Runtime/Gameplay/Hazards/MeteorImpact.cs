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
            => TierFor(distance, radius, heavyImpactFraction, PhysicsScale.MeteorShockwaveRadiusScale);

        /// <summary>
        /// 세 개의 동심원이다. 안쪽부터 강한 충격 · 충격파 · 아무것도 아님.
        ///
        /// <code>
        /// 0 ────── radius×fraction ────── radius ────── radius×scale ──────
        ///   Heavy                Shockwave ────────────┘   None
        /// </code>
        ///
        /// <b>바깥 원이 충격 반경 밖까지 나간다</b>(2026-09-18). 예전에는
        /// <paramref name="radius"/> 가 곧 경계라 소형 운석은 2m 밖이 완전히 안전했고,
        /// 그 안에서도 바깥 절반은 아무 일도 하지 않아(<c>Meteor.ApplyTier</c> 가
        /// Heavy 가 아니면 그냥 돌아갔다) <b>실질 판정 범위가 1m 였다.</b> 기획 §20.1 이
        /// 약한 단계를 "충격<b>파</b>" 라고 부르는 이상 퍼지는 거리가 있어야 한다.
        ///
        /// <b>강한 충격 경계는 그대로 <paramref name="radius"/> 에서 잰다.</b> §17.7 의
        /// "소형 충격 반경 2m" 가 가리키는 것은 실제로 아픈 범위이고, 낙하 예고 원도
        /// 그 값을 쓴다(§17.2 "실제 충격 범위와 같은 크기") — 여기를 함께 넓히면
        /// 예고와 실제가 어긋난다. 넓어진 것은 밀리기만 하는 바깥 띠뿐이다.
        /// </summary>
        public static MeteorImpactTier TierFor(
            float distance, float radius, float heavyImpactFraction, float shockwaveRadiusScale)
        {
            if (radius <= 0f) return MeteorImpactTier.None;

            var heavy = radius * Mathf.Clamp01(heavyImpactFraction);
            if (distance <= heavy) return MeteorImpactTier.Heavy;

            return distance <= ShockwaveRadiusFor(radius, shockwaveRadiusScale)
                ? MeteorImpactTier.Shockwave
                : MeteorImpactTier.None;
        }

        /// <summary>
        /// 충격파가 닿는 가장 바깥 거리(m).
        ///
        /// 배율이 1 보다 작게 들어와도 충격 반경 아래로는 줄지 않는다 — 그러면 강한
        /// 충격 경계가 충격파 바깥에 놓여 위 동심원의 순서가 뒤집힌다.
        /// </summary>
        public static float ShockwaveRadiusFor(float radius, float shockwaveRadiusScale)
            => radius * Mathf.Max(1f, shockwaveRadiusScale);

        public static float ShockwaveRadiusFor(float radius)
            => ShockwaveRadiusFor(radius, PhysicsScale.MeteorShockwaveRadiusScale);

        /// <summary>
        /// 착탄점에서 <paramref name="distance"/> 떨어진 곳이 받는 세기(0~1).
        ///
        /// <c>AddExplosionForce</c> 가 쓰는 것과 <b>같은 선형 감쇠</b>다. 규칙이 둘이면
        /// 폭발력과 넉백 속도가 서로 다른 지점에서 사라져, 가장자리에서 "밀리지는
        /// 않는데 몸만 흔들린다" 가 생긴다.
        /// </summary>
        public static float FalloffAt(float distance, float reach)
            => reach <= 0f ? 0f : Mathf.Clamp01(1f - distance / reach);

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
