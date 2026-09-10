using System;
using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 고정쇠 당기기 자리의 <b>반납</b>과 거부 사유 <b>로그</b>를 검증한다
    /// (S15P21B202-383).
    ///
    /// <para><b>왜 필요했나.</b> 2026-09-10 QA 에서 "참가자 한 명은 고정쇠가 됐고 다른
    /// 한 명은 안 됐다" 는 신고가 왔다. 기획 §12.2 로 고정쇠 하나에 최대 2명인데,
    /// 자리를 반납하는 <c>LeavePull()</c> 이 <c>PlayerRig.ResetClampGrab()</c> 에서만
    /// 불리고 <b><c>OnDestroy()</c> 는 그것을 부르지 않았다.</b> 접속이 끊기면 자리가
    /// 영구히 남아, 한 번 새면 딱 한 명만 더 잡을 수 있다.</para>
    ///
    /// <para>그리고 그 신고를 로그로 진단할 수 없었다 — 점유 거부와 거리 거부가
    /// <b>같은 문구</b>(<c>고정못 접근 불가</c>)로 찍히고 대상도 <c>null</c> 로 버려졌다.
    /// 그래서 이 스위트가 <b>사유가 갈라져 있는지</b>도 지킨다.</para>
    /// </summary>
    public sealed class ClampOccupancyTests
    {
        /// <summary>
        /// <b>파괴될 때 당기기 자리를 반납한다.</b> 이것이 이 이슈의 본론이다 —
        /// 접속이 끊긴 사람의 자리가 남으면 되돌아오지 않는다(레벨을 다시 시작해야 한다).
        /// </summary>
        [Test]
        public void DestroyingTheBodyReturnsThePullSlot()
        {
            var body = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs"),
                "void OnDestroy()");

            Assert.That(body, Does.Contain("ResetClampGrab()"),
                "몸이 파괴될 때 당기기 자리를 반납하지 않으면, 접속이 끊긴 사람의 자리가 "
                + "영구히 남아 그 고정쇠를 아무도(또는 한 명만) 잡을 수 없게 된다");
        }

        /// <summary>최대 인원이 2 라는 전제가 유지된다 — 위 누수의 파급이 그 값에 달렸다.</summary>
        [Test]
        public void TwoPullersIsStillTheCap()
        {
            Assert.That(PhysicsScale.ClampMaxPullers, Is.EqualTo(2),
                "기획 §12.2. 이 값이 커지면 누수의 파급이 달라지므로 함께 다시 본다");
        }

        /// <summary>
        /// <b>모든 거부 사유가 자기 말을 갖는다.</b> 예전에는 점유·거리·해제됨이 모두
        /// <c>고정못 접근 불가</c> 하나로 찍혀 로그로 원인을 가릴 수 없었다.
        ///
        /// <para>열거형을 훑기 때문에 <b>사유가 새로 생기면 여기서 실패한다</b> —
        /// 소스에서 문구를 찾아보는 방식으로는 그것을 잡을 수 없다.</para>
        /// </summary>
        [Test]
        public void EveryRefusalHasItsOwnWords()
        {
            var seen = new Dictionary<string, InteractionFailure>();

            foreach (InteractionFailure failure in Enum.GetValues(typeof(InteractionFailure)))
            {
                if (failure == InteractionFailure.None) continue;

                var words = PlayerRig.RefusalReason(failure, pullers: 0);

                Assert.That(words, Is.Not.Null.And.Not.Empty);
                Assert.That(words, Does.Not.Contain("모르는 사유"),
                    $"{failure} 에 사람 말이 없다 — 로그에 열거형 이름만 찍힌다");
                if (seen.TryGetValue(words, out var other))
                    Assert.Fail($"{other} 와 {failure} 가 같은 문구('{words}')다"
                                + " — 로그로 두 원인을 가릴 수 없다");

                seen[words] = failure;
            }
        }

        /// <summary>
        /// <b>점유는 인원을 적는다.</b> "몇 명이 잡고 있어서" 가 남지 않으면 다음 신고에서
        /// 자리 누수(이 이슈)인지 정상 만원인지 구별할 수 없다.
        /// </summary>
        [Test]
        public void OccupancySaysHowManyArePulling()
        {
            var full = PlayerRig.RefusalReason(
                InteractionFailure.Occupied, PhysicsScale.ClampMaxPullers);

            Assert.That(full, Does.Contain(PhysicsScale.ClampMaxPullers.ToString()),
                "점유 사유에 인원이 없으면 누수 진단에 쓸 수 없다");
        }

        /// <summary>
        /// <b>단일 선점과 만원은 다른 규칙이다.</b> <c>InteractableBase</c> 의
        /// <c>Occupant</c> 잠금과 기획 §12.2 의 "최대 2명" 이 같은
        /// <see cref="InteractionFailure.Occupied"/> 를 낸다. 이 테스트가 없으면 둘이
        /// 한 문구로 합쳐져도 아무도 모른다 — 실제로 이 함수의 첫 판이 그랬다.
        /// </summary>
        [Test]
        public void TheSingleLockIsNotTheSameAsAFullClamp()
        {
            var lockedByOther = PlayerRig.RefusalReason(InteractionFailure.Occupied, pullers: 0);
            var full = PlayerRig.RefusalReason(
                InteractionFailure.Occupied, PhysicsScale.ClampMaxPullers);

            Assert.That(lockedByOther, Is.Not.EqualTo(full),
                "겹쳐 놓으면 '두 명이 붙어서' 와 '다른 사람이 먼저 집어서' 가 구별되지 않는다");
        }

        /// <summary>
        /// <b>판정은 규칙의 주인에게 묻는다.</b> 이 함수의 첫 판은
        /// <c>IsReleased</c>·<c>Pullers</c> 를 직접 보고 사유를 다시 짜맞췄고, 그래서
        /// <c>base.GetAvailability</c> 의 단일 <c>Occupant</c> 잠금이
        /// <b>"너무 멀다" 로 찍혔다.</b> 판정을 두 곳에 두면 반드시 갈라진다.
        ///
        /// <para>어느 고정쇠였는지도 남긴다 — 예전 호출은 대상을 <c>null</c> 로 넘겨
        /// <c>대상=없음</c> 만 찍혔고, 그것은 상태가 아니라 로거가 찍은 빈 값이었다.</para>
        /// </summary>
        [Test]
        public void TheLogAsksTheOwnerOfTheRule()
        {
            var body = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs"),
                "void LogClampRefusal(bool leftPressed, bool rightPressed)");

            Assert.That(body, Does.Contain("GetAvailability"),
                "사유를 손으로 다시 짜맞추면 규칙과 갈라진다 — 첫 판이 그래서 틀렸다");
            Assert.That(body, Does.Contain("RefusalReason("),
                "사유 문구는 검증되는 순수 함수 한 곳에서만 만든다");
            Assert.That(body, Does.Contain("clamp.name"),
                "대상을 버리면 어느 고정쇠가 막혔는지 알 수 없다 — 여러 개가 있다");
            Assert.That(body, Does.Contain("CanReachClamp"),
                "거리는 GetAvailability 가 보지 않는다 — 따로 적어야 사유가 완결된다");
        }
    }
}
