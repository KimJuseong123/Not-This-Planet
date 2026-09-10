using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 미는 인원수에 따른 파츠 속도(기획 §13.2).
    ///
    /// <b>계산식을 만들지 않는다.</b> 표가 선형이 아니다 — 1·2·3명은 0.4씩 오르는데
    /// 4명은 0.3만 오른다. 식으로 바꾸면 기획이 정한 값이 아니라 내가 정한 값이 된다.
    /// <see cref="Hazards.MeteorWaveTable"/> 이 §17.4 를 표로 둔 것과 같은 방식이다
    /// (S15P21B202-234).
    /// </summary>
    public static class PartPushTable
    {
        /// <summary>기획 §13.2 의 표. 첨자가 인원수다(0명은 0m/s).</summary>
        public static readonly float[] Speeds = { 0f, 0.4f, 0.8f, 1.2f, 1.5f };

        /// <summary>
        /// <paramref name="pushers"/> 명이 같은 방향으로 밀 때의 파츠 속도(m/s).
        ///
        /// 표를 넘는 인원은 마지막 값으로 묶는다. 1장은 최대 4인이지만(§3.3) 관전자나
        /// 재접속으로 그 이상이 세어지는 것을 막는 것이 아니라, <b>표에 없는 값을
        /// 만들어내지 않기</b> 위한 것이다.
        /// </summary>
        public static float SpeedFor(int pushers)
        {
            if (pushers <= 0) return 0f;
            return Speeds[Mathf.Min(pushers, Speeds.Length - 1)];
        }

        /// <summary>
        /// 협동 밀기가 발동하는 최소 인원. 둘까지는 붙잡고 버티기만 한다.
        ///
        /// <b>이 문턱을 물리에 맡길 수 없다.</b> 그러면 좋겠지만 — "둘은 무게를 못
        /// 이기고 셋은 이긴다" 를 마찰이 저절로 갈라 주면 규칙이 필요 없다 — PhysX 의
        /// 정지 마찰이 요구하는 힘이 <b>접촉 구성에 따라 이론값의 1.35 ~ 2.05배로
        /// 흔들린다.</b> 접촉점이 많은 평평한 바닥일수록 크다. 실측하면 이렇다.
        ///
        /// <list type="bullet">
        /// <item>Part_1 — 최하단이 0.94 x 0.94 뿐이라 접촉점이 적다. 이론값의 1.35배로 밀린다</item>
        /// <item>평평한 2 x 2 바닥 — 이론값의 2배를 주어도 꿈쩍하지 않는다</item>
        /// </list>
        ///
        /// 둘이 이론값의 2/3 를 낼 때 안 밀리고 셋이 1배를 낼 때 밀리게 하려면 여유가
        /// 2.05배보다 크고 3.08배보다 작아야 하는데, 그 두 조건은 동시에 만족되지
        /// 않는다. 그래서 인원 문턱은 <b>규칙으로</b> 두고, 셋 이상에게는 어떤 형태의
        /// 파츠든 확실히 이길 힘을 준다(S15P21B202-394).
        /// </summary>
        public const int MinPeople = 3;

        /// <summary>파츠가 움직이기 시작하는 최소 인원.</summary>
        public static int MinPeopleToMove => MinPeople;

        /// <summary>
        /// 파츠를 정지 마찰에서 떼어 내는 데 필요한 가속(m/s²). <b>질량과 무관하다</b> —
        /// <c>μ·g</c> 이기 때문이다. 그래서 745kg 파츠와 406kg 파츠의 문턱이 같다.
        ///
        /// 지면 재질(<see cref="Core.Prim.GroundPhysics"/>)의 정지 마찰이 0.95 이고
        /// 합성이 <c>Maximum</c> 이라 <b>파츠에 미끄러운 재질을 붙여도 이 값이 지배한다.</b>
        /// </summary>
        public static float GroundFrictionAccel =>
            Core.Prim.GroundPhysics.staticFriction * Mathf.Abs(Core.Tuning.Gravity);

        /// <summary>
        /// 이론상 필요한 힘에 곱하는 여유. 위 <see cref="MinPeople"/> 주석의 실측
        /// 최악값(2.05배)보다 크게 잡는다. 남는 여유는 경사면과 용접 비드 몫이다 —
        /// 20도 경사에서 필요한 값은 <c>g(μ·cos20° + sin20°) / (μ·g) = 1.30배</c> 다.
        /// </summary>
        public const float FrictionMargin = 2.5f;

        /// <summary>
        /// <paramref name="people"/> 명이 <paramref name="mass"/> kg 파츠에 넣는 힘(N).
        /// <see cref="MinPeople"/> 미만은 0 이다 — 위 주석의 이유로 문턱은 규칙이다.
        ///
        /// <b>가속이 아니라 힘으로 주는 이유.</b> 속도를 직접 대입하면 마찰이 같은
        /// 스텝에 지우는지가 접촉점 개수에 달려, 같은 값이 어떤 파츠는 밀고 어떤
        /// 파츠는 못 민다. 힘으로 주면 solver 가 마찰과 한 층에서 풀어 형태와 무관하게
        /// 일관되고, <b>질량이 실제로 의미를 갖는다</b> — 무거운 파츠에는 더 큰 힘이
        /// 들어간다.
        /// </summary>
        public static float ForceFor(int people, float mass)
        {
            if (people < MinPeople || mass <= 0f) return 0f;

            var needed = mass * GroundFrictionAccel * FrictionMargin;
            return needed * people / MinPeople;
        }
    }
}
