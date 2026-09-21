using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// kinematic 강체에 속도를 쓰지 않는다 (S15P21B202-433).
    ///
    /// <para><b>왜 소스를 읽어 단정하나.</b> 이 결함은 동작이 틀리는 것이 아니라
    /// <b>Unity 가 경고를 찍고 값을 무시하는 것</b>이다. 강체를 만들어 돌려도 단정할
    /// 것이 없고 잡히는 것은 로그뿐이다. 그래서 <b>순서를 소스에서</b> 단정한다 —
    /// <c>ScarChannelsAreChosenOnPurpose</c> 가 같은 방식을 쓴다.</para>
    ///
    /// <para><b>실측 (2026-09-11 4인 QA).</b> 호스트 로그 109,643줄 중
    /// <b>106,652줄(97%)</b>, 참가자 29,497줄 중 28,176줄(96%)이
    /// <c>Setting linear/angular velocity of a kinematic body is not supported.</c>
    /// 한 줄이었다. <c>MoveCarriedBody</c> 가 운반 중 매 틱 · 운반물마다 돌면서
    /// kinematic 으로 바꾼 <b>뒤</b> 속도를 썼기 때문이다.</para>
    ///
    /// <para><b>프레임 문제는 아니었다.</b> 5초 구간으로 나눠 세어 보면 경고가 있는
    /// 구간 58.7fps · 없는 구간 58.8fps 로 차이가 없다(참가자는 119.8 · 120.0).
    /// 고치는 이유는 <b>다른 진단이 묻히는 것</b>이다 — <c>-390</c> 은 계측이 0건이어서
    /// QA 신고를 확인할 수 없었고, 이번에는 반대 방향으로 같은 일이 났다.</para>
    /// </summary>
    public sealed class KinematicVelocityOrderTests
    {
        const string Rig = "Runtime/Gameplay/Player/PlayerRig.cs";
        const string MeteorFile = "Runtime/Gameplay/Hazards/Meteor.cs";

        [Test]
        public void MovingACarriedBodySkipsVelocityWhenAlreadyKinematic()
        {
            // **폭주의 출처.** 운반 중 매 틱 · 운반물마다 돈다. 그래서 조건 없이
            // 속도를 쓰면 경고가 초당 수백 줄 나간다.
            var scope = TestSource.Method(TestSource.Read(Rig), "void MoveCarriedBody(");

            int guard = scope.IndexOf("if (!body.isKinematic)", System.StringComparison.Ordinal);
            Assert.Greater(guard, 0,
                "MoveCarriedBody 가 `if (!body.isKinematic)` 로 감싸지 않는다 — "
                + "매 틱 경고 두 줄이 나가고 QA 로그가 그것으로 덮인다(실측 97%)");

            int linear = scope.IndexOf("linearVelocity =", System.StringComparison.Ordinal);
            Assert.Greater(linear, guard, "속도 대입이 가드보다 앞에 있다 — 가드 안으로 넣어야 한다");
        }

        [Test]
        public void CarryingClearsVelocityBeforeGoingKinematic()
        {
            // 운반을 시작하는 곳. 한 번만 돌지만 순서가 같았고, 여기서는 순서가
            // **뜻도 바꾼다** — 남은 운동량을 없애려는 대입인데 거부되면 남는다.
            var scope = TestSource.Method(TestSource.Read(Rig), "void BeginCarriedBody(");

            int linear = scope.IndexOf("linearVelocity =", System.StringComparison.Ordinal);
            int angular = scope.IndexOf("angularVelocity =", System.StringComparison.Ordinal);
            int kinematic = scope.IndexOf("isKinematic = true", System.StringComparison.Ordinal);

            Assert.Greater(linear, 0, "BeginCarriedBody 가 속도를 비우지 않는다");
            Assert.Greater(kinematic, 0, "BeginCarriedBody 가 kinematic 으로 바꾸지 않는다");
            Assert.Less(linear, kinematic, "linearVelocity 를 kinematic 전환 뒤에 쓴다 — 거부된다");
            Assert.Less(angular, kinematic, "angularVelocity 를 kinematic 전환 뒤에 쓴다 — 거부된다");
        }

        [Test]
        public void SettlingAMeteorTurnsItDynamicBeforeClearingVelocity()
        {
            // 들고 있던 돌은 kinematic 이다(BeginCarriedBody). 그 상태로 속도를 쓰면
            // 거부되어 **착지 순간의 남은 운동량이 지워지지 않는다.**
            var scope = TestSource.Method(TestSource.Read(MeteorFile), "void Settle(");

            int dynamic = scope.IndexOf("isKinematic = false", System.StringComparison.Ordinal);
            int linear = scope.IndexOf("linearVelocity =", System.StringComparison.Ordinal);

            Assert.Greater(dynamic, 0, "Settle 이 isKinematic = false 를 하지 않는다");
            Assert.Greater(linear, 0, "Settle 이 속도를 비우지 않는다");
            Assert.Less(dynamic, linear,
                "Settle 이 속도를 먼저 쓰고 그 뒤에 동적으로 돌린다 — 들고 있던 돌에는 "
                + "그 대입이 거부되어 운동량이 남는다");
        }

        [Test]
        public void NoVelocityIsEverWrittenToAKinematicGhost()
        {
            // **이 검사가 바뀐 이유** (S15P21B202-658). 예전에는 "그림은 동적이다" 를
            // 지켰다 — 흔적·낙하 그림 **둘 다** 속도를 받았기 때문이다.
            //
            // `-658` 이 흔적 그림만 kinematic 으로 바꿨다. 참가자가 착지한 돌을 몸으로
            // 통과하고 있었고(콜라이더가 꺼져 있었다), 콜라이더를 켜면서 동적으로 두면
            // 이번엔 참가자가 돌을 **밀어 버린다.**
            //
            // 그때 이 이슈의 폭주가 돌아오지 않게 **속도 대입을 함께 걷어냈다** — 흔적의
            // 이동은 `_scarMotion` 에 적고 `AdvanceScarGhosts` 가 `MovePosition` 으로 밟는다.
            //
            // 그래서 지켜야 할 것이 "그림은 동적이다" 가 아니라 이 클래스가 원래 내건
            // **"kinematic 강체에 속도를 쓰지 않는다"** 로 좁혀졌다. 둘 중 하나만 바뀌면
            // — 흔적을 동적으로 되돌리거나, kinematic 인 채로 속도를 다시 쓰거나 —
            // 여기서 걸린다.
            var src = TestSource.Read("Runtime/Gameplay/Hazards/MeteorSpawner.cs");

            // ① 낙하 그림은 **동적**이다. 그쪽은 속도를 받으므로 그래야 합법이다.
            var ghost = TestSource.Method(src, "Meteor CreateGhost(");
            Assert.That(ghost, Does.Contain("isKinematic = false"),
                "낙하 그림을 동적으로 두지 않는다 — ApplyNetworkMeteors 의 속도 대입이 매 묶음 거부된다");
            Assert.That(TestSource.Method(src, "public void ApplyNetworkMeteors("),
                Does.Contain("linearVelocity"),
                "낙하 그림에 속도를 주지 않는다 — 묶음 사이 궤적이 멈춘다");

            // ② 흔적 그림은 **kinematic** 이다(`-658`).
            Assert.That(TestSource.Method(src, "void PrepareSolidGhostBody("),
                Does.Contain("isKinematic = true"),
                "흔적 그림이 kinematic 이 아니다 — 참가자가 돌을 밀어 서버 자세와 다툰다");

            // ③ 그러므로 흔적 경로 **어디에도** 속도 대입이 없어야 한다. 하나라도 남으면
            //    묶음마다 경고가 나가고, 그것이 이 이슈가 없앤 로그 97% 다.
            foreach (var method in new[] { "public void ApplyNetworkScars(",
                                           "void SetScarAtRest(",
                                           "void DeadReckonScar(",
                                           "public void AdvanceScarGhosts(" })
            {
                var scope = TestSource.Method(src, method);
                Assert.That(scope, Does.Not.Contain("linearVelocity"),
                    method + " 가 kinematic 인 흔적에 속도를 쓴다 — 경고가 매 묶음 나간다");
                Assert.That(scope, Does.Not.Contain("angularVelocity"),
                    method + " 가 kinematic 인 흔적에 각속도를 쓴다 — 같은 폭주다");
            }
        }
    }
}
