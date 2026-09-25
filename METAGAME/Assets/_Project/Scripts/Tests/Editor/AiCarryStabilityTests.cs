using System;
using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 파츠 잡기 · 운반 · 견인이 한 번의 어긋남으로 끝나지 않는가 (S15P21B202-712).
    ///
    /// <para>실제로 끄는 시험은 플레이 모드의 몸과 파츠가 필요하다. 여기서는 <b>고친 자리가 그대로
    /// 있는지</b>와 수치가 말이 되는지를 고정한다 — 누가 되돌리면 여기서 먼저 깨진다.</para>
    /// </summary>
    public sealed class AiCarryStabilityTests
    {
        static string Carry => TestSource.Read("Runtime/Network/AiWorkTask.Carry.cs");
        static string Work => TestSource.Read("Runtime/Network/AiWorkTask.cs");
        static string Vehicle => TestSource.Read("Runtime/Network/AiWorkTask.Vehicle.cs");

        static int Count(string text, string what)
        {
            int n = 0;
            for (int i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        [Test]
        public void TheNumbersMakeSense()
        {
            Assert.That(AiWorkTask.CarryRetries, Is.InRange(1, 3), "너무 많으면 안 되는 일을 오래 붙잡는다");
            Assert.That(AiWorkTask.CarryStandReach, Is.InRange(.25f, .5f), "0.12m 는 몸통이 못 닿아 이동 막힘이 났다");
            Assert.That(AiWorkTask.CarryReplanDistance, Is.InRange(.2f, 1f));
            Assert.That(AiWorkTask.CarrySettleSpeed, Is.GreaterThan(0f));
            Assert.That(AiWorkTask.CarrySettleSeconds, Is.InRange(.3f, 2f));
            Assert.That(AiWorkTask.BoardRepress, Is.InRange(1f, 4f), "탑승 제한(5초) 안에 한 번은 다시 눌러야 한다");
            Assert.That(AiWorkTask.LowerTimeout, Is.InRange(8f, 30f), "없으면 작업 전체 제한(180초)까지 떠 있다");
        }

        [Test]
        public void ThePartPickerSkipsWhatCannotBeUsed()
        {
            var nearest = TestSource.Method(Work, "SpaceshipPart NearestPart(bool pins = false, System.Predicate<SpaceshipPart> accept = null)");
            int accept = nearest.IndexOf("if (accept != null && !accept(item)) continue;", StringComparison.Ordinal);
            int focus = nearest.IndexOf("if (item == focusedPart) return item;", StringComparison.Ordinal);
            Assert.That(accept, Is.GreaterThanOrEqualTo(0), "쓸 수 없는 파츠를 건너뛰지 않는다");
            Assert.That(accept, Is.LessThan(focus), "바라보는 파츠도 조건을 통과해야 한다 — 먼저 돌려주면 용접된 것을 고른다");
            Assert.That(TestSource.Method(Carry, "bool BeginCarry()"), Does.Contain("NearestPart(false, Carriable);"));
            Assert.That(TestSource.Method(Vehicle, "bool BeginVehicle()"), Does.Contain("NearestPart(false, Liftable);"));
            Assert.That(TestSource.Method(Carry, "bool Carriable(SpaceshipPart item)"), Does.Contain("!ClaimedByOther(item)"),
                "다른 AI 가 견인 중인 파츠를 걸어서 잡으러 간다");
        }

        [Test]
        public void GrabbingAndCarryingTryAgainInsteadOfEndingAtTheFirstSlip()
        {
            var tick = TestSource.Method(Carry, "InputFrame TickCarry()");
            Assert.That(Count(tick, "RetryCarry("), Is.GreaterThanOrEqualTo(2), "놓침과 못 잡음 둘 다 다시 시도해야 한다");
            Assert.That(tick, Does.Contain("Navigate(goal, CarryStandReach, ref input)"));
            Assert.That(tick, Does.Contain("PickCarrySurface()"), "파츠가 밀려나면 서는 자리를 다시 골라야 한다");
            Assert.That(tick, Does.Contain("part.State == PartState.Welded"), "운반 중 용접되면 멈춰야 한다");
            Assert.That(tick, Does.Contain("return TickCarrySettle();"), "목적지 안에 들어와도 멈추기를 보고 끝낸다");
            Assert.That(tick, Does.Contain("++pushTries > 2"), "몸만 닿았을 때 바로 실패하지 않는다");
            var retry = TestSource.Method(Carry, "void RetryCarry(string why)");
            Assert.That(retry, Does.Contain("carryRetries >= CarryRetries"), "다시 시도에 한도가 있어야 한다");
            Assert.That(retry, Does.Contain("Phase(0);"));
        }

        [Test]
        public void TheTractorPressesAgainAndLoweringHasATimeLimit()
        {
            var tick = TestSource.Method(Vehicle, "InputFrame TickVehicle()");
            Assert.That(tick, Does.Contain("if (pulse && Time.time - boardPressAt > BoardRepress) pulse = false;"));
            Assert.That(tick, Does.Contain("Time.time - phaseAt > LowerTimeout"));
            Assert.That(tick, Does.Contain("landingRetries++ < 2 && needsCargo"), "정렬 재시도는 안착 확인과 같은 한도를 쓴다");
        }
    }
}
