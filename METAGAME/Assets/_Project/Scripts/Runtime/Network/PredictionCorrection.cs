using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 예측한 내 몸과 서버 자세의 오차를 **조금씩 갚는** 계산 (S15P21B202-363).
    ///
    /// <para><b>왜 이 방식인가.</b> 정석 P1 은 서버 상태로 되감고 미확인 입력을 다시
    /// 적용한다(rollback and re-simulate). 그러려면 <b>래그돌 전체의 물리 상태</b>를
    /// 저장·복원해야 하는데, 우리 몸은 <c>RagdollBuilder</c> 로 런타임 조립하는 조인트
    /// 덩어리라 그 비용이 크고 Mirror 의 되감기도 벡터 근사다("not 100% correct").
    /// 그리고 우리 게임에는 <b>히트 판정이 없다</b> — 되감기의 가치 대부분이 "맞췄나" 를
    /// 정확히 하는 것인데 그것이 필요하지 않다.</para>
    ///
    /// <para>그래서 되감지 않고 <b>오차만 부드럽게 얹는다.</b> 로컬 물리는 계속 돌고,
    /// 서버와 벌어진 만큼을 매 물리 스텝에 일부씩 갚는다. <c>docs/network-sync-policy.md</c></para>
    ///
    /// <para>계산만 여기 두는 이유는 <b>테스트할 수 있게</b> 하려는 것이다 —
    /// <c>RemoteAvatar</c> 는 리그·인형이 있어야 세워지므로 EditMode 에서 다루기 어렵다.</para>
    /// </summary>
    public static class PredictionCorrection
    {
        /// <summary>
        /// 한 물리 스텝에 오차를 몇 분의 몇 갚나.
        ///
        /// <para>1 이면 즉시 갚아 순간이동처럼 보이고, 0 이면 갚지 않아 서버와 점점
        /// 벌어진다. 0.1 은 오차의 절반을 약 7스텝(70ms)에 갚는다 — 로컬 물리와 서버
        /// 물리의 차이는 보통 몇 cm 라 눈에 띄지 않는다.</para>
        ///
        /// <para><b>0 으로 두면 예측 이전과 같아지지 않는다</b> — 예측은 계속 돌고 보정만
        /// 멈춘다. 되돌리려면 <c>RemoteAvatar.Configure</c> 의 <c>mine</c> 분기를 지워야 한다.</para>
        /// </summary>
        // ── 실험 중 (S15P21B202-374) — 이 브랜치는 머지하지 않는다 ──────────────
        // 원래 값은 0.1f 다. 0 으로 두면 예측 경로는 살아 있고 **수렴만 멈춘다** —
        // 그것으로 프레임이 돌아오면 -363 의 수렴이 원인이고, 아니면 다른 원인이다.
        // 되돌리기가 한 줄이어야 하므로 값만 바꾼다.
        public const float Rate = 0f;

        /// <summary>이 정도로 좁혀졌으면 멈춘다(m). 끝없이 미세하게 미는 것을 막는다.</summary>
        public const float Epsilon = 0.01f;

        /// <summary>
        /// 물리로 이어질 수 없는 거리(m). 리스폰·순간이동·탑승이 그렇다.
        /// 이보다 멀면 부드럽게 좁히지 않고 그 자리로 옮겨야 한다 — 좁히려 하면 벽을
        /// 뚫고 기어간다.
        /// </summary>
        public const float SnapDistance = 3f;

        /// <summary>부드럽게 좁힐 수 있는 오차인가. 거짓이면 부르는 쪽이 스냅한다.</summary>
        public static bool CanConverge(Vector3 error)
        {
            return error.sqrMagnitude <= SnapDistance * SnapDistance;
        }

        /// <summary>
        /// 이번 스텝에 옮길 양과 남는 오차.
        /// </summary>
        /// <param name="remaining">아직 갚지 않은 오차.</param>
        /// <param name="left">이번 스텝 뒤에 남는 오차.</param>
        /// <returns>이번 스텝에 위치에 더할 양.</returns>
        public static Vector3 Step(Vector3 remaining, out Vector3 left)
        {
            // 다 갚았다고 볼 만큼 작으면 딱 끊는다. 안 끊으면 지수적으로 줄어들기만 하고
            // 영원히 0 이 되지 않아, 매 스텝 미세하게 미는 일이 계속된다.
            if (remaining.sqrMagnitude < Epsilon * Epsilon)
            {
                left = Vector3.zero;
                return Vector3.zero;
            }

            Vector3 step = remaining * Rate;
            left = remaining - step;
            return step;
        }
    }
}
