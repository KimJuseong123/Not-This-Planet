using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>수동 운반 인원별 속도표(기획 §13.2). 솔로도 천천히 이동할 수 있게 조정한다.</summary>
    public static class PartPushTable
    {
        /// <summary>기획 §13.2 의 표. 첨자가 인원수다(0명은 0m/s).</summary>
        public static readonly float[] Speeds = { 0f, 0.6f, 0.8f, 1.2f, 1.5f };

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

        /// <summary>혼자서도 모든 자유 파츠를 밀거나 당길 수 있다(기획 §13).</summary>
        public const int MinPeople = 1;

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
        /// 이론상 필요한 힘에 곱하는 여유. 기존 접촉 구성 실측의
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
            // 한 명부터 정지 마찰을 이긴다. 기존 3·4인 힘은 유지하고
            // 1·2인에게도 같은 출발 힘을 주되 속도는 인원별 표로 제한한다.
            return needed * Mathf.Max(1f, people / 3f);
        }
    }
}
