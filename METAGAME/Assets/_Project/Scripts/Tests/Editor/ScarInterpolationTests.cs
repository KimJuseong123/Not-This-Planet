using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 굴러가는 흔적이 묶음 사이에 멈춰 있지 않는다 (S15P21B202-434).
    ///
    /// <para><b>신고.</b> 2026-09-11 QA — "운석 이제 보이는데 굴러가는게 프레임이
    /// 낮음". 전체 프레임은 정상이었다(호스트 58 · 참가자 120fps 실측). 끊긴 것은
    /// <b>흔적 그림의 움직임</b>뿐이다.</para>
    ///
    /// <para><b>원인.</b> <c>ApplyNetworkScars</c> 가 자리를 찍고 속도를 0 으로 두었다.
    /// 움직임 묶음은 전송 틱(30Hz · 33ms)마다 오므로 그림이 그 사이 완전히 멈춰 있다가
    /// 순간이동한다 — 120fps 로 그려도 초당 30번만 움직인다.</para>
    ///
    /// <para><b>고친 방향.</b> 두 자세의 차이를 시간으로 나눠 속도를 만든다. 대역폭은
    /// 늘지 않는다 — <c>ScarState</c> 에 속도 칸을 더하면 묶음당 12바이트가 늘어 한
    /// 묶음에 담는 개수가 35 → 24 로 줄어든다(2026-09-11 로그에 이미 "36개 중 35개만
    /// 보낸다" 가 떴다). 받는 쪽에서 재는 것이 공짜다.</para>
    /// </summary>
    public sealed class ScarInterpolationTests
    {
        const string Spawner = "Runtime/Gameplay/Hazards/MeteorSpawner.cs";

        [Test]
        public void TheMovingPacketGivesVelocityAndTheFullListDoesNot()
        {
            // 전체 목록은 권위다 — 그 자리에 세운다. 움직임 묶음은 다음 33ms 를 채운다.
            var scope = TestSource.Method(
                TestSource.Read(Spawner),
                "public void ApplyNetworkScars(");

            Assert.That(scope, Does.Contain("if (full) SetScarAtRest(state);"),
                "전체 목록과 움직임 묶음을 가르지 않는다 — 둘을 같게 다루면 굴러가는 돌이 끊긴다");
            Assert.That(scope, Does.Contain("else DeadReckonScar(state);"),
                "움직임 묶음이 속도를 주지 않는다");

            // 자리는 여전히 받은 값으로 찍는다(권위). 속도는 그 사이를 채우는 것뿐이다.
            Assert.That(scope, Does.Contain("body.position = state.Position"),
                "자리를 받은 값으로 찍지 않는다 — 오차가 누적된다");

            // **속도를 강체에 넣지 않는다** (S15P21B202-658). 흔적 그림은 콜라이더를
            // 켜면서 키네마틱이 됐고, 키네마틱 강체는 `linearVelocity` 를 무시한다 —
            // 예전처럼 넣어 두면 굴러가는 돌이 묶음 사이에 **멈춘다**. 이 이슈가 고친
            // 증상이 그대로 돌아오는 길이다.
            Assert.That(scope, Does.Not.Contain("body.linearVelocity"),
                "키네마틱 강체에 속도를 넣는다 — 무시되어 돌이 묶음 사이에 멈춘다");
            Assert.That(scope, Does.Not.Contain("body.angularVelocity"),
                "키네마틱 강체에 각속도를 넣는다 — 무시되어 돌이 안 구른다");
        }

        [Test]
        public void RestingScarsGetNoVelocity()
        {
            // 쉬는 돌에 속도가 남으면 땅에 박힌 것이 흘러간다.
            //
            // 속도가 사는 곳이 강체에서 **기록**으로 옮겨졌다(S15P21B202-658) — 뜻은
            // 그대로다: 전체 목록이 말한 자리에 세우고 다음 묶음까지 아무 데도 안 간다.
            var scope = TestSource.Method(TestSource.Read(Spawner), "void SetScarAtRest(");

            Assert.That(scope, Does.Contain("RememberScarMotion(state, Vector3.zero, Vector3.zero)"),
                "쉬는 돌에 0 이 아닌 속도가 남는다 — 땅에 박힌 것이 흘러간다");
        }

        [Test]
        public void ATeleportGetsNoVelocity()
        {
            // 자고 있다 깨어난 돌은 최대 2초(ScarKeepalive) 낡은 자세에서 출발한다.
            // 그 차이를 묶음 하나의 간격으로 나누면 속도가 터져 그림이 화면 밖으로
            // 날아가고 다음 묶음까지 돌아오지 않는다.
            var scope = TestSource.Method(TestSource.Read(Spawner), "void DeadReckonScar(");

            Assert.That(scope, Does.Contain("ScarInterpolationSpeedLimit"),
                "속도 상한을 보지 않는다 — 순간이동이 속도로 변환된다");
            Assert.That(scope, Does.Contain("ScarInterpolationMaxInterval"),
                "시간 창 상한을 보지 않는다 — 오래 전 자세로 속도를 잰다");
            Assert.That(scope, Does.Contain("ScarInterpolationMinInterval"),
                "시간 창 하한을 보지 않는다 — 0 으로 나눌 수 있다");
        }

        [Test]
        public void TurningTheShortWayRound()
        {
            // `ToAngleAxis` 는 0~360 도를 준다. 180 도를 넘으면 **반대로 돌린 것**이고,
            // 그대로 쓰면 돌이 긴 쪽으로 휙 돈다 — 굴러가는 것이 더 이상해진다.
            var from = Quaternion.identity;
            var to = Quaternion.AngleAxis(350f, Vector3.up);   // = -10도

            var angular = InvokeAngularFrom(from, to, 1f);

            // -10도/초 ≈ -0.1745 rad/s. 부호가 뒤집히면 +6.1 rad/s 가 된다.
            Assert.That(angular.y, Is.EqualTo(-10f * Mathf.Deg2Rad).Within(0.01f),
                "짧은 쪽으로 돌지 않는다 — 350도를 그대로 써서 긴 쪽으로 돈다");
        }

        [Test]
        public void NoRotationGivesNoSpin()
        {
            // 각이 0 이면 축이 유효하지 않다. NaN 을 강체에 넣으면 그 프레임부터
            // 물리가 **조용히** 죽는다 — 경고도 없다.
            var angular = InvokeAngularFrom(Quaternion.identity, Quaternion.identity, 0.033f);

            Assert.That(angular, Is.EqualTo(Vector3.zero),
                "회전이 없는데 각속도가 나온다: " + angular);
            Assert.That(float.IsNaN(angular.x) || float.IsNaN(angular.y) || float.IsNaN(angular.z),
                Is.False, "NaN 이 나왔다 — 강체에 넣으면 물리가 조용히 죽는다");
        }

        [Test]
        public void AQuarterTurnPerTickBecomesTheRightSpin()
        {
            // 33ms 에 90도 = 2727도/초 ≈ 47.6 rad/s. 굴러가는 돌은 이만큼 돈다.
            var angular = InvokeAngularFrom(
                Quaternion.identity, Quaternion.AngleAxis(90f, Vector3.right), 0.033f);

            Assert.That(angular.x, Is.EqualTo(90f * Mathf.Deg2Rad / 0.033f).Within(0.5f),
                "각속도가 어긋난다: " + angular);
        }

        static Vector3 InvokeAngularFrom(Quaternion from, Quaternion to, float dt)
        {
            var method = typeof(MeteorSpawner).GetMethod(
                "AngularFrom",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, "AngularFrom 을 찾지 못했다 — 이름이 바뀌었으면 이 테스트도 고쳐야 한다");
            return (Vector3)method.Invoke(null, new object[] { from, to, dt });
        }
    }
}
