using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 예측한 내 몸과 서버 자세의 오차를 갚는 계산 (S15P21B202-363).
    ///
    /// <para><b>왜 이 계산만 따로 재나.</b> `RemoteAvatar` 는 리그·인형이 서 있어야
    /// 만들어지고 물리 스텝과 짝지어야 돌아간다 — EditMode 에서 다루기 어렵다. 그런데
    /// 여기서 틀리면 증상이 <b>조용하다</b>: 너무 빨리 갚으면 순간이동처럼 보이고, 너무
    /// 느리면 서버와 점점 벌어지고, 안 끊으면 가만히 서 있어도 미세하게 계속 밀린다.
    /// 셋 다 "버그" 로 신고되지 않고 "뭔가 이상하다" 로만 남는다.</para>
    /// </summary>
    public sealed class PredictionCorrectionTests
    {
        [Test]
        public void AimChangesRespectSendCadenceButEveryButtonEdgeBypassesIt()
        {
            var same = typeof(NetPlayer).GetMethod("SameControls", System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic);
            var idle = new Metagame.Core.InputFrame();
            var aim = new Metagame.Core.InputFrame
            {
                Look = Vector2.one, AimYaw = 90f, AimPitch = 20f, AimFrom = Vector3.one
            };
            Assert.IsTrue((bool)same.Invoke(null, new object[] { idle, aim }));
            foreach (var field in typeof(Metagame.Core.InputFrame).GetFields())
            {
                if (field.FieldType != typeof(bool)) continue;
                object pressed = idle;
                field.SetValue(pressed, true);
                Assert.IsFalse((bool)same.Invoke(null, new object[] { idle, pressed }), field.Name);
                Assert.IsFalse((bool)same.Invoke(null, new object[] { pressed, idle }), field.Name + " release");
            }
            Assert.IsFalse((bool)same.Invoke(null, new object[]
                { idle, new Metagame.Core.InputFrame { Move = Vector2.up } }));
        }

        /// <summary>오차의 일부만 갚고 나머지는 남는다 — 합이 원래 오차와 같아야 한다.</summary>
        [Test]
        public void 한_스텝은_오차의_일부만_갚는다()
        {
            var error = new Vector3(1f, 0f, 0f);

            Vector3 step = PredictionCorrection.Step(error, out Vector3 left);

            Assert.AreEqual(PredictionCorrection.Rate, step.x, 1e-5f,
                "한 스텝에 Rate 만큼만 갚아야 한다 — 전부 갚으면 순간이동으로 보인다.");
            Assert.AreEqual(error.x, step.x + left.x, 1e-5f,
                "갚은 것과 남은 것의 합이 원래 오차여야 한다. 어긋나면 지나치거나 모자란다.");
        }

        /// <summary>
        /// 계속 돌리면 0 으로 수렴한다. 수렴하지 않으면 서버와 영원히 벌어진 채로 남는다.
        /// </summary>
        [Test]
        public void 반복하면_수렴한다()
        {
            var left = new Vector3(0f, 0f, 2f);

            // 100스텝(1초)이면 충분히 좁혀져야 한다.
            for (var i = 0; i < 100; i++)
                PredictionCorrection.Step(left, out left);

            Assert.AreEqual(Vector3.zero, left,
                "1초 안에 오차를 다 갚아야 한다. 남으면 서버와 벌어진 채로 플레이한다.");
        }

        /// <summary>
        /// <b>딱 끊어야 한다.</b> 지수적으로 줄기만 하면 영원히 0 이 되지 않고, 매 물리
        /// 스텝마다 골반을 미세하게 미는 일이 계속된다 — 가만히 서 있어도 떨린다.
        /// </summary>
        [Test]
        public void 충분히_좁혀지면_딱_끊는다()
        {
            var tiny = new Vector3(PredictionCorrection.Epsilon * 0.5f, 0f, 0f);

            Vector3 step = PredictionCorrection.Step(tiny, out Vector3 left);

            Assert.AreEqual(Vector3.zero, step, "이만큼 좁혀졌으면 더 밀지 않아야 한다.");
            Assert.AreEqual(Vector3.zero, left, "남은 오차도 0 으로 끊어야 한다.");
        }

        /// <summary>
        /// 물리로 이어질 수 없는 거리는 부드럽게 좁히지 않는다 — 그러면 벽을 뚫고 기어간다.
        /// 리스폰·순간이동·탑승이 그런 경우다.
        /// </summary>
        [Test]
        public void 너무_멀면_좁힐_수_없다고_말한다()
        {
            Assert.IsTrue(PredictionCorrection.CanConverge(new Vector3(0.5f, 0f, 0f)),
                "몇십 cm 는 로컬 물리와 서버 물리의 흔한 차이다 — 부드럽게 좁혀야 한다.");

            float far = PredictionCorrection.SnapDistance + 1f;
            Assert.IsFalse(PredictionCorrection.CanConverge(new Vector3(far, 0f, 0f)),
                "이 거리는 물리로 이어질 수 없다 — 부르는 쪽이 스냅해야 한다.");
        }

        /// <summary>
        /// 되돌릴 수 있는 값인지 못 박는다. <c>Rate</c> 가 1 이면 즉시 갚아 예측의 뜻이
        /// 사라지고, 0 이면 서버를 무시해 점점 벌어진다. 그 사이여야 한다.
        /// </summary>
        [Test]
        public void 상수가_뜻있는_범위에_있다()
        {
            Assert.Greater(PredictionCorrection.Rate, 0f,
                "0 이면 보정을 안 한다 — 서버와 점점 벌어진다.");
            Assert.Less(PredictionCorrection.Rate, 1f,
                "1 이면 즉시 갚아 순간이동처럼 보인다.");
            Assert.Greater(PredictionCorrection.Epsilon, 0f, "0 이면 영원히 끊지 못한다.");
            Assert.Greater(PredictionCorrection.SnapDistance, PredictionCorrection.Epsilon,
                "스냅 거리가 끊는 거리보다 작으면 모든 오차를 스냅해 버린다.");
        }
    }
}
