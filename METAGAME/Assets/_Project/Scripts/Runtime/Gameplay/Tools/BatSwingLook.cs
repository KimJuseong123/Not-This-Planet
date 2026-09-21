using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 방망이 스윙을 어떻게 보여줄지 계산한다(기획 §18.1).
    ///
    /// <b>그리지 않고 값만 낸다.</b> 실제로 선을 만드는 것은 <see cref="MeteorBat"/> 이고,
    /// 여기는 "궤적이 어느 점들을 지나고 줄기가 얼마나 밝은가" 만 정한다. 나눈 이유는
    /// <see cref="Hazards.MeteorWarningLook"/> 과 같다 — 렌더러를 세우지 않고도 규칙을
    /// 잠글 수 있고, [Art] 가 실제 VFX 로 바꿔도 이 규칙은 그대로 산다.
    ///
    /// <b>그리는 쪽을 별도 컴포넌트로 만들지 않았다.</b> S15P21B202-275 에서
    /// <c>MeteorWarningView</c> 를 아무도 만들지 않아 세 이슈가 죽어 있던 것을 보았다.
    /// 방망이는 이미 세계에 있고 이미 <c>ShowSwing</c> 을 부르므로, 그 안에서 그리면
    /// 붙이는 것을 잊을 자리가 없다.
    ///
    /// 상태를 갖지 않는다. 같은 값을 넣으면 같은 답이다.
    /// </summary>
    public static class BatSwingLook
    {
        public static Color ImpactColor(byte grade) => grade == 3
            ? new Color(1f, 0.88f, 0.12f, 0.95f)
            : grade == 2 ? new Color(0.3f, 0.65f, 1f, 0.9f)
            : new Color(0.35f, 1f, 0.8f, 0.85f);

        /// <summary>궤적 시작 색. 에너지가 실린 흰빛에 가깝다.</summary>
        public static readonly Color ArcStartColor = new(1f, 0.92f, 0.62f, 0.85f);

        /// <summary>궤적 끝 색. 꼬리로 갈수록 옅어져 지나간 자리로 읽힌다.</summary>
        public static readonly Color ArcEndColor = new(1f, 0.55f, 0.15f, 0f);

        /// <summary>방망이와 운석을 잇는 줄기의 색.</summary>
        public static readonly Color StreakColor = new(1f, 0.78f, 0.35f, 0.9f);

        /// <summary>
        /// 궤적이 지나는 점들을 채운다. 채운 개수를 돌려준다.
        ///
        /// <b>지나온 만큼만 그린다.</b> <paramref name="progress01"/> 에서
        /// <see cref="PhysicsScale.BatArcTrail01"/> 만큼 거슬러 올라간 구간을 훑는다 —
        /// 앞으로 갈 곳까지 그리면 아직 휘두르지 않은 자리가 빛나고, 맞는 순간이
        /// 어디인지 읽히지 않는다.
        ///
        /// 각 점은 <see cref="MeteorBat.SwingAim"/> 이 그 진행도에서 내는 방향으로
        /// <see cref="PhysicsScale.BatArcRadius"/> 만큼 뻗은 자리다. 스윙 동작과 궤적이
        /// <b>같은 함수</b>에서 나오므로 어긋날 수 없다.
        /// </summary>
        public static int ArcPoints(Vector3 origin, Vector3 aim, float progress01,
                                   Vector3[] into)
        {
            if (into == null || into.Length == 0) return 0;

            var head = Mathf.Clamp01(progress01);
            var tail = Mathf.Max(0f, head - PhysicsScale.BatArcTrail01);

            var count = Mathf.Min(into.Length, Mathf.Max(2, PhysicsScale.BatArcSamples));

            for (var i = 0; i < count; i++)
            {
                // 0 번이 꼬리, 마지막이 지금 방망이가 있는 자리다. 색이 꼬리에서
                // 옅어지므로 순서가 뒤바뀌면 방향이 거꾸로 보인다.
                var t = Mathf.Lerp(tail, head, i / (float)(count - 1));
                into[i] = origin + MeteorBat.SwingAim(aim, t) * PhysicsScale.BatArcRadius;
            }

            return count;
        }

        /// <summary>
        /// 맞은 뒤 <paramref name="sinceHit"/> 초가 지났을 때 줄기의 밝기 0~1.
        ///
        /// 0 이면 그리지 않는다. 선형으로 줄여 사라지는 시점을 예측 가능하게 둔다.
        /// </summary>
        public static float StreakFade(float sinceHit)
        {
            if (sinceHit < 0f) return 0f;
            if (PhysicsScale.BatStreakSeconds <= 0f) return 0f;

            return Mathf.Clamp01(1f - sinceHit / PhysicsScale.BatStreakSeconds);
        }

        /// <summary>궤적을 그릴 구간인가. 휘두르지 않을 때는 지운다.</summary>
        public static bool ShowsArc(bool swinging, float progress01) =>
            swinging && progress01 > 0f && progress01 < 1f;
    }
}
