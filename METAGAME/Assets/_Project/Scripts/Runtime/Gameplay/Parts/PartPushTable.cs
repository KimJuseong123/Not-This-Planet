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
    }
}
