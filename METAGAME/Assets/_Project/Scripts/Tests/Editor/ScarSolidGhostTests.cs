using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 참가자가 착지한 돌을 <b>몸으로 통과하지 않는다</b> (S15P21B202-658).
    ///
    /// <para><b>무엇이 잘못됐었나.</b> 참가자 화면의 흔적 그림은 콜라이더가 전부 꺼져
    /// 있었다. 그래서 참가자는 돌을 뚫고 지나갔고, 피해 판정은 호스트가 하므로
    /// <b>부딪히지 않았는데 체력만 깎였다.</b> 호스트에게는 막힌 벽이다.
    /// 기획 확인 결과 의도가 아니었다(<c>-654</c> Q7).</para>
    ///
    /// <para><b>왜 그렇게 돼 있었나.</b> "판정에 관여하지 않는다" 를 <b>콜라이더까지
    /// 끄는 것</b>으로 구현했다. 판정을 막는 데 필요한 것은 <c>Meteor</c> 컴포넌트를 끄는
    /// 것 하나다 — 콜라이더는 <b>막는</b> 일만 하고 아무것도 판정하지 않는다.
    /// 같은 저장소의 부품은 이미 옳게 돼 있었다(<c>WorldSync.PrepareClientBody</c> 는
    /// 키네마틱으로만 세우고 콜라이더는 손대지 않는다) — 한 문제를 두 곳이 다르게 푼 것이
    /// 원인이다.</para>
    ///
    /// <para><b>낙하 중인 돌은 그대로 둔다.</b> 흔적은 멈춰 있어 참가자 화면의 자리가
    /// 서버와 같지만, 낙하 중인 돌은 ~166ms(≈1.6m) 낡은 자리에 있어서 막기 시작하면
    /// "호스트에는 안 맞았는데 참가자만 막히는" 새 어긋남이 생긴다. 그쪽은 실제 플레이로
    /// 확인한 뒤 정한다.</para>
    /// </summary>
    public sealed class ScarSolidGhostTests
    {
        const string Spawner = "Runtime/Gameplay/Hazards/MeteorSpawner.cs";
        const string Bridge = "Runtime/Network/MeteorClockBridge.cs";

        [Test]
        public void ScarGhostsAreBuiltAsSolidAndFallingOnesAreNot()
        {
            var scars = TestSource.Method(TestSource.Read(Spawner), "public void ApplyNetworkScars(");
            Assert.That(scars, Does.Contain("CreateGhost(solid: true)"),
                "흔적 그림을 막히는 돌로 만들지 않는다 — 참가자가 돌을 통과한다");

            // 낙하 그림은 **일부러** 그대로다. 함께 켜면 낡은 자리에서 막힌다.
            var falling = TestSource.Method(TestSource.Read(Spawner),
                                            "public void ApplyNetworkMeteors(");
            Assert.That(falling, Does.Not.Contain("solid: true"),
                "낙하 그림까지 막히게 만들었다 — 이 결정은 플레이 확인 뒤에 한다");
        }

        [Test]
        public void JudgementStaysOffEvenThoughTheCollidersAreOn()
        {
            // **이 한 줄이 판정을 막는다.** 콜라이더를 켜도 안전한 것은 이것 때문이고,
            // `if (!solid)` 보다 **앞에** 있어야 두 갈래 모두에 걸린다.
            var scope = TestSource.Method(TestSource.Read(Spawner), "Meteor CreateGhost(");

            int off = scope.IndexOf("ghost.enabled = false", System.StringComparison.Ordinal);
            int split = scope.IndexOf("if (!solid)", System.StringComparison.Ordinal);

            Assert.That(off, Is.GreaterThanOrEqualTo(0),
                "Meteor 컴포넌트를 끄지 않는다 — 참가자 기계가 착탄을 스스로 판정하게 된다");
            Assert.That(split, Is.GreaterThanOrEqualTo(0), "두 갈래로 가르지 않는다");
            Assert.That(off, Is.LessThan(split),
                "판정 끄기가 갈림길 뒤에 있다 — 막히는 돌이 로컬로 판정하게 된다");
        }

        [Test]
        public void OnlyTheFallingGhostTurnsCollidersOff()
        {
            var scope = TestSource.Method(TestSource.Read(Spawner), "Meteor CreateGhost(");

            int split = scope.IndexOf("if (!solid)", System.StringComparison.Ordinal);
            int disabled = scope.IndexOf("collider.enabled = false", System.StringComparison.Ordinal);

            Assert.That(disabled, Is.GreaterThan(split),
                "콜라이더를 갈림길보다 먼저 끈다 — 막히는 돌에도 걸려 통과가 그대로 남는다");

            // 두 번 끄면 한쪽이 갈림길 밖에 있다는 뜻이다.
            Assert.That(scope.IndexOf("collider.enabled = false", disabled + 1,
                                      System.StringComparison.Ordinal),
                Is.LessThan(0), "콜라이더를 끄는 곳이 둘이다 — 하나는 막히는 돌에도 걸린다");
        }

        [Test]
        public void ASolidGhostIsKinematicSoPlayersCannotShoveIt()
        {
            var scope = TestSource.Method(TestSource.Read(Spawner), "void PrepareSolidGhostBody(");

            Assert.That(scope, Does.Contain("isKinematic = true"),
                "동적 강체로 둔다 — 참가자가 돌을 밀어 버리고 다음 묶음이 되돌려 덜덜 떨린다");
            Assert.That(scope, Does.Contain("detectCollisions = true"),
                "충돌 감지를 켜지 않는다 — 콜라이더가 있어도 통과한다");
            Assert.That(scope, Does.Contain("interpolation = RigidbodyInterpolation.Interpolate"),
                "보간을 켜지 않는다 — `-538` 이 고친 떨림이 돌아온다");
            Assert.That(scope, Does.Contain("useGravity = false"),
                "중력을 끄지 않는다 — 강체가 되돌려졌을 때 그림이 지면을 뚫는다");
        }

        [Test]
        public void ASolidGhostSitsOnThePropLayerLikeTheHostsRock()
        {
            // 호스트에서는 `Meteor.Settle` 이 착지한 돌을 Prop 으로 옮긴다. 그림은
            // 그 컴포넌트가 꺼져 있어 **그 줄을 절대 지나지 않는다** — 옮겨 주지 않으면
            // 방망이 타격 범위(BatStrikeMask 는 Meteor 를 빼고 Prop 은 넣는다)와
            // 상호작용이 호스트와 다르게 잡힌다. 콜라이더만 켜고 끝내면 조용히 어긋난다.
            var scope = TestSource.Method(TestSource.Read(Spawner), "Meteor CreateGhost(");

            Assert.That(scope, Does.Contain("SetLayerDeep(ghost.gameObject, GameLayers.Prop)"),
                "막히는 그림을 Prop 레이어로 옮기지 않는다 — 호스트의 돌과 다른 레이어가 된다");
        }

        [Test]
        public void TheGhostIsSteppedWithMovePositionNotAssignment()
        {
            // 키네마틱 강체에 자리를 직접 대입하면 순간이동으로 취급되어 그 스텝의
            // 보간이 건너뛰어진다 — 보간을 켠 의미가 없어진다
            // (`WorldSync.FollowOnClient` 의 같은 주석).
            var scope = TestSource.Method(TestSource.Read(Spawner), "public void AdvanceScarGhosts(");

            Assert.That(scope, Does.Contain("body.MovePosition("),
                "MovePosition 을 쓰지 않는다 — 보간이 건너뛰어져 다시 뚝뚝 끊긴다");
            Assert.That(scope, Does.Contain("body.MoveRotation("),
                "회전을 밟지 않는다 — 굴러가는 돌의 절반이 멈춘다");
            Assert.That(scope, Does.Not.Contain("body.position ="),
                "자리를 직접 대입한다 — 순간이동이라 보간이 죽는다");
        }

        [Test]
        public void TheClientStepsTheGhostEveryPhysicsStep()
        {
            // 속도를 기록에만 두었으므로 **누군가 밟아 주지 않으면 돌이 아예 안 움직인다.**
            // 참가자 갈래 안이어야 한다 — 호스트에는 그림이 없다.
            var scope = TestSource.Method(TestSource.Read(Bridge), "void FixedUpdate(");

            int client = scope.IndexOf("if (!authority)", System.StringComparison.Ordinal);
            int step = scope.IndexOf("spawner.AdvanceScarGhosts(", System.StringComparison.Ordinal);

            Assert.That(step, Is.GreaterThanOrEqualTo(0),
                "흔적 그림을 밟지 않는다 — 속도가 기록에만 있어 굴러가는 돌이 멈춘다");
            Assert.That(client, Is.GreaterThanOrEqualTo(0), "참가자 갈래가 없다");
            Assert.That(step, Is.GreaterThan(client),
                "참가자 갈래 밖에서 밟는다 — 호스트에서도 돌아 진짜 돌과 다툰다");
            Assert.That(scope, Does.Contain("spawner.AdvanceScarGhosts(Time.fixedDeltaTime)"),
                "물리 스텝의 폭으로 밟지 않는다 — 프레임 폭을 쓰면 `-538` 의 톱니가 돌아온다");
        }
    }
}
