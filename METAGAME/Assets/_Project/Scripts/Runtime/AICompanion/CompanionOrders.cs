using UnityEngine;

namespace Metagame.AICompanion
{
    /// <summary>
    /// 분류 결과(<c>action</c> 문자열)를 <see cref="CompanionMotor"/> 의 일로 옮긴다.
    ///
    /// <para><b>왜 따로 뺐나.</b> 이 분기는 <c>NetworkCompanion</c>(AI 실험 씬 전용) 안에
    /// 있었다. Stage1 에서도 같은 명령을 써야 하는데 그 클래스를 가져올 수는 없다 —
    /// 실험용 스폰·동기화가 딸려 온다. 판단만 여기로 옮겨 양쪽이 함께 쓴다.</para>
    ///
    /// <para><b>고르기만 한다.</b> 대상 선택·이동·물리는 게임 로직이 한다
    /// (CLAUDE.md 설계 경계). 여기서는 어느 <c>Try…</c> 를 부를지만 정한다.</para>
    /// </summary>
    public static class CompanionOrders
    {
        /// <summary>
        /// 이 동료에게 명령을 넘긴다. 받아들였으면 <c>true</c>.
        /// </summary>
        /// <param name="motor">명령을 받을 동료의 두뇌.</param>
        /// <param name="action">분류가 고른 행동 이름.</param>
        /// <param name="speaker">말한 사람. 대상(고정못·도구)을 고르는 기준이 된다.</param>
        /// <param name="feedback">사람에게 보여 줄 한 줄. 실패해도 채운다.</param>
        public static bool TryApply(CompanionMotor motor, string action, Transform speaker,
                                    out string feedback)
        {
            feedback = "지원하지 않는 요청 · 기존 행동 유지";
            if (motor == null) { feedback = "동료가 준비되지 않았어요"; return false; }
            if (!CompanionPolicy.TryParse(action, out var order)) return false;

            if (CompanionMotor.IsToolOrder(order))
                return motor.TryStartTool(order, speaker, out feedback);

            if (order == CompanionOrder.PullNearbyPins)
                return motor.TryStartNearbyPins(CompanionPinTarget.Nearby(speaker), out feedback);

            if (order == CompanionOrder.PullPin || order == CompanionOrder.MoveToPin)
            {
                var pin = CompanionPinTarget.Resolve(speaker, out feedback);
                if (pin == null) return false;
                return order == CompanionOrder.MoveToPin
                    ? motor.TryMoveToPin(pin, out feedback)
                    : motor.TryStartPin(pin, out feedback);
            }

            motor.SetOrder(order, speaker);
            feedback = order == CompanionOrder.Cancel ? "중단 명령을 확인했어요"
                : order == CompanionOrder.Wait ? "대기 명령을 확인했어요"
                : "따라오기 명령을 확인했어요";
            return true;
        }
    }
}
