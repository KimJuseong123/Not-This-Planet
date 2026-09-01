using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 흔적이 식는 모습을 정하는 순수 계산(기획 §19).
    ///
    /// §19 는 "약 5초 동안 <b>붉은 빛, 연기와 불꽃</b>이 약해진다" 고 정한다. 그런데
    /// 프로젝트에 <c>Light</c> 도 <c>ParticleSystem</c> 도 없다 — 운석 프리팹은
    /// Rigidbody · SphereCollider · 아트 프리팹 자식뿐이고, 파티클은 즉발 헬퍼
    /// <see cref="World.Puff"/> 하나다. 그래서 <b>색으로 식힌다.</b>
    /// <see cref="MeteorWarningLook"/> 이 낙하 예고를 색으로 표현한 것과 같은 방식이고
    /// (S15P21B202-241), [Art] 가 실제 연출을 주면 이 값을 버린다.
    ///
    /// 상태가 없으므로 검증이 시간을 직접 넣어 잠근다.
    /// </summary>
    public static class MeteorScarLook
    {
        /// <summary>막 박힌 색. 달아오른 자리다.</summary>
        public static readonly Color HotColor = new(1f, 0.35f, 0.1f, 0.75f);

        /// <summary>다 식은 색. 그을린 지면이다.</summary>
        public static readonly Color ColdColor = new(0.12f, 0.1f, 0.1f, 0.5f);

        /// <summary>
        /// 박힌 뒤 <paramref name="age"/> 초가 지난 흔적의 색.
        ///
        /// 처음이 빠르게 식는다. 등속으로 두면 5초 내내 붉어 보여서 "약해진다" 가
        /// 읽히지 않는다.
        /// </summary>
        public static Color ColorOf(float age)
        {
            // sqrt 라서 처음이 빠르게 식는다. 등속으로 두면 5초 내내 붉어 보여
            // "약해진다" 가 읽히지 않는다.
            return Color.Lerp(HotColor, ColdColor, Mathf.Sqrt(Progress01(age)));
        }

        /// <summary>식은 정도 0~1.</summary>
        public static float Progress01(float age)
        {
            if (PhysicsScale.MeteorScarCoolSeconds <= 0f) return 1f;
            return Mathf.Clamp01(age / PhysicsScale.MeteorScarCoolSeconds);
        }

        /// <summary>
        /// 분화구 지름(m). 충격 반경과 같다.
        ///
        /// 낙하 예고(§17.2 "실제 충격 범위와 같은 크기")와 같은 값을 쓰므로, 맞은 자리와
        /// 예고가 어긋나 보이지 않는다.
        /// </summary>
        public static float DiameterOf(float impactRadius) => Mathf.Max(0.1f, impactRadius * 2f);
    }
}
