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
    /// <b>두 단계로 없어진다</b> — 식고(§19), 그 뒤 옅어진다(S15P21B202-438).
    /// 두 번째가 없으면 검은 원이 수명이 끝날 때까지 지면에 박혀 있다.
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
        public static Color ColorOf(float age) =>
            ColorOf(age, PhysicsScale.MeteorScarCoolSeconds, PhysicsScale.MeteorScarFadeSeconds);

        /// <summary>
        /// 식는 단계만 본다. <b>새 코드는 세 인수 판을 쓴다</b> — 이 판은 옅어지는
        /// 단계가 없어 다 식은 뒤 <see cref="ColdColor"/> 로 굳는다.
        /// </summary>
        public static Color ColorOf(float age, float coolSeconds)
        {
            // sqrt 라서 처음이 빠르게 식는다. 등속으로 두면 5초 내내 붉어 보여
            // "약해진다" 가 읽히지 않는다.
            return Color.Lerp(HotColor, ColdColor, Mathf.Sqrt(Progress01(age, coolSeconds)));
        }

        /// <summary>
        /// 박힌 뒤 <paramref name="age"/> 초가 지난 흔적의 색.
        ///
        /// 두 단계다.
        ///
        /// <list type="number">
        /// <item><b>식는다</b>(<paramref name="coolSeconds"/>) — 주황에서
        /// <see cref="ColdColor"/> 로. 기획 §19 "약 5초 동안 … 약해진다"</item>
        /// <item><b>옅어진다</b>(<paramref name="fadeSeconds"/>) — 알파를 0 으로</item>
        /// </list>
        ///
        /// <b>왜 옅어지는 단계가 필요한가.</b> 예전에는 1단계만 있었다. 그러면 5초 뒤
        /// 색이 <see cref="ColdColor"/> = (0.12, 0.10, 0.10) 으로 굳고 알파 0.5 로
        /// 남는다 — <b>밝기 0.106 의 검은 원</b>이 수명이 끝날 때까지 지면에 박혀
        /// 있어서 "잔상" 으로 보였다(S15P21B202-438).
        ///
        /// <b>알파만 뺀다 — 색은 그대로 둔다.</b> 흰색으로 밝히며 지우면 타다 남은
        /// 자리가 아니라 빛나는 원이 되고, 지면색으로 맞추려면 지면마다 값이 달라야
        /// 한다. 투명도는 어느 지면에서나 같은 뜻이다.
        /// </summary>
        public static Color ColorOf(float age, float coolSeconds, float fadeSeconds)
        {
            var color = ColorOf(age, coolSeconds);
            color.a *= Opacity01(age, coolSeconds, fadeSeconds);
            return color;
        }

        /// <summary>
        /// 남은 불투명도 0~1. 식는 동안은 1 이고, 그 뒤 <paramref name="fadeSeconds"/>
        /// 에 걸쳐 0 이 된다.
        ///
        /// <b>선형이다.</b> 식는 쪽은 sqrt 로 앞을 빠르게 했지만 여기는 반대다 —
        /// 사라지는 것이 빠르면 결국 툭 끊기는 것으로 보인다.
        /// </summary>
        public static float Opacity01(float age, float coolSeconds, float fadeSeconds)
        {
            // **페이드 시간이 없으면 불투명을 유지한다.** 예전에는 여기서 0 을 돌려
            // 식는 순간 사라지게 했는데, 그러면 페이드 값이 저장되지 않은 씬·프리팹의
            // 흔적이 5초에 툭 사라진다 — "툭 사라지지 않는다" 는 이 이슈의 목적과
            // 정반대다(!347 AI 리뷰). 소멸은 수명(ScarDespawnSeconds)이 맡는다.
            if (fadeSeconds <= 0f) return 1f;
            if (age <= coolSeconds) return 1f;

            return Mathf.Clamp01(1f - (age - coolSeconds) / fadeSeconds);
        }

        /// <summary>
        /// 색이 더 이상 변하지 않는 시점을 지났는가. 표시를 갱신할 이유가 없어지는 때다.
        ///
        /// <b>투명도로 판정하지 않는다.</b> 예전에는 <c>Opacity01(...) &lt;= 0</c> 이었는데,
        /// 페이드 값이 없는 씬에서는 <see cref="Opacity01"/> 가 계속 1 을 돌려주므로
        /// 이 값이 <b>영영 false</b> 가 된다. 그러면 흔적 200개에 매 프레임 색을 넣게
        /// 되어 §19 의 성능 기준이 깨진다(!347 AI 리뷰).
        ///
        /// 시간으로 재면 그런 구멍이 없다 — 페이드가 0 이면 식는 순간이 곧 끝이다.
        /// <see cref="MeteorSpawner.Scar.ColorIsFinal"/> 도 이 계산을 그대로 쓴다.
        /// </summary>
        public static bool ColorIsFinal(float age, float coolSeconds, float fadeSeconds) =>
            age >= coolSeconds + Mathf.Max(fadeSeconds, 0f);

        /// <summary>식은 정도 0~1.</summary>
        public static float Progress01(float age) => Progress01(age, PhysicsScale.MeteorScarCoolSeconds);

        public static float Progress01(float age, float coolSeconds)
        {
            if (coolSeconds <= 0f) return 1f;
            return Mathf.Clamp01(age / coolSeconds);
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
