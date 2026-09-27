using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 떨어지는 운석을 <b>피하려고는 한다</b> (S15P21B202-746).
    ///
    /// <para>2026-09-26 QA: AI 동료가 운석을 너무 잘 맞았다. 완벽히 피할 필요는 없지만 피하려는 시도는 보여야
    /// 사람처럼 보인다. 곧 떨어질 운석의 충격 반경 안에 있으면 착지점 반대쪽으로 뛴다.</para>
    ///
    /// <para><b>가볍게.</b> 판에 동시에 떠 있는 운석은 몇 개뿐이고, 착지 예측은 방망이 안내가 이미 쓰는 산술이다.
    /// 그래도 0.1초에 한 번만 본다 — 프레임을 낮추지 않는다.</para>
    ///
    /// <para><b>방망이를 들고 칠 참이면 피하지 않는다.</b> 쳐내기는 일부러 착지점 앞에 서는 일이다.</para>
    /// </summary>
    public sealed class AiMeteorDodge
    {
        /// <summary>떨어지기 이만큼 전부터 피한다(초). 더 이르면 헛걸음이 많고, 늦으면 못 벗어난다.</summary>
        public const float Horizon = 1.4f;

        /// <summary>충격 반경에 이만큼 더해 위험으로 본다(m) — 몸통 반지름과 예측 오차.</summary>
        public const float Margin = .7f;

        /// <summary>다시 보는 간격(초).</summary>
        public const float Interval = .1f;

        float nextCheck;
        bool dodging;
        Vector3 away;

        public bool Active => dodging;

        /// <summary>
        /// 이 자리가 위험한가. <b>순수 함수</b>다 — 검사가 값으로 부른다. 위험하면 피할 방향(평면)을 준다.
        /// </summary>
        public static bool Threatens(Vector3 self, Vector3 impact, float seconds, float radius, out Vector3 escape)
        {
            escape = default;
            if (seconds <= 0f || seconds > Horizon) return false;
            var offset = self - impact; offset.y = 0;
            if (offset.magnitude > radius + Margin) return false;
            escape = offset.sqrMagnitude > .01f ? offset.normalized : Vector3.right;
            return true;
        }

        /// <param name="striking">방망이를 들고 운석을 칠 참이다 — 피하지 않는다.</param>
        public bool Tick(PlayerRig self, bool allowed, bool striking, out InputFrame frame)
        {
            frame = default;
            if (!allowed || self == null || self.Downed || self.IsRiding || self.Ragdolled || striking)
            { dodging = false; return false; }
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + Interval;
                dodging = false;
                float soonest = float.PositiveInfinity;
                foreach (var meteor in Meteor.All)
                {
                    if (meteor == null || meteor.Harmless || meteor.GroundProp || meteor.WasDeflected) continue;
                    if (!meteor.TryPredictImpact(out var seconds, out var impact) || seconds >= soonest) continue;
                    float radius = Mathf.Max(meteor.ImpactRadius, PhysicsScale.MeteorSmallImpactRadius);
                    if (!Threatens(self.FootPosition, impact, seconds, radius, out var escape)) continue;
                    soonest = seconds; away = escape; dodging = true;
                }
            }
            if (!dodging) return false;
            var hits = Hits;
            var direction = away;
            if (!AiFollowController.SteerAround(self, self.FootPosition, ref direction, hits, .5f)) return false;
            frame = new InputFrame { AimYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, Move = Vector2.up, Run = true };
            return true;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[24];
    }
}
