using System.Linq;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 옆걸음과 공중 자세의 전환을 지킨다 (S15P21B202-670).
    ///
    /// <para><b>옆으로 가면 예전에는 애니메이션이 통째로 꺼졌다</b> — <c>CanAnimate</c>
    /// 에 <c>|x| &lt;= |y|</c> 항이 있어 가중치가 0 으로 떨어졌고, 그 0 이 날것
    /// 래그돌이라 다리가 멈추고 그대로 미끄러졌다.</para>
    ///
    /// <para><b>전환이 이 작업의 본체다.</b> 클립을 만드는 것보다 앞걸음·점프와 어떻게
    /// 이어지는지가 어렵다 — 628 에서 점프를 둘로 나눴다가 "너무 둘로 나누니까 끊겨"
    /// 를 들었다.</para>
    /// </summary>
    public sealed class SideWalkTests
    {
        PlayerRig _player;
        GameObject _ground;
        bool _puff;

        SpaceSuit Suit => _player.Doll.Suit;

        [SetUp]
        public void SetUp()
        {
            _puff = Puff.Enabled; Puff.Enabled = false;
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "~검증용 바닥";
            _ground.layer = GameLayers.Ground;
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();
            _player = PlayerRig.Create("옆걸음 검증", Vector3.zero, Quaternion.identity,
                                       Color.white, Color.gray);
            Suit.InitializeLocomotion();
        }

        [TearDown]
        public void TearDown()
        {
            Suit?.Locomotion?.Dispose();
            if (_player != null) Object.DestroyImmediate(_player.gameObject);
            if (_ground != null) Object.DestroyImmediate(_ground);
            Physics.SyncTransforms();
            Puff.Enabled = _puff;
        }

        static RabbitLocomotion.State Walking(Vector2 direction, float speed = 2.9f) => new()
        {
            Grounded = true, Speed = speed, Direction = direction, MoveInput = true
        };

        static RabbitLocomotion.State Air(float vy) => new()
        { Grounded = false, VerticalSpeed = vy, JumpedUp = true, GroundDistance = 9f };

        Quaternion Bone(string name) => Suit.Model
            .GetComponentsInChildren<Transform>(true).Single(t => t.name == name).localRotation;

        void Advance(RabbitLocomotion.State state, int frames = 30)
        {
            for (var i = 0; i < frames; i++) Suit.Locomotion.Apply(state, 0.02f);
        }

        /// <summary>그 상태로 도는 동안 프레임당 최대 각도. 전환이 매끄러운지 잰다.</summary>
        float WorstStep(RabbitLocomotion.State from, RabbitLocomotion.State to, int frames = 30)
        {
            Advance(from, 40);
            var names = new[] { "L_Thigh", "R_Thigh", "L_Upperarm", "R_Upperarm" };
            var last = names.Select(Bone).ToArray();
            float worst = 0f;
            for (var i = 0; i < frames; i++)
            {
                Suit.Locomotion.Apply(to, 0.02f);
                for (var k = 0; k < names.Length; k++)
                {
                    worst = Mathf.Max(worst, Quaternion.Angle(last[k], Bone(names[k])));
                    last[k] = Bone(names[k]);
                }
            }
            return worst;
        }

        // --- 옆으로 가도 애니메이션이 꺼지지 않는다 -------------------------------------

        [Test]
        public void WalkingSidewaysStillAnimates()
        {
            // 예전에는 이 조건이 거짓이라 가중치가 0 으로 떨어졌다.
            Assert.That(RabbitLocomotion.CanAnimate(Walking(new Vector2(1f, 0f))), Is.True,
                "옆으로 가면 애니메이션이 꺼진다 — 다리가 멈추고 미끄러진다.");
            Assert.That(RabbitLocomotion.CanAnimate(Walking(new Vector2(0.7f, 0.7f))), Is.True,
                "대각선에서 꺼진다.");
        }

        [Test]
        public void StandingStillStillDoesNotAnimate()
        {
            // 멈춰 있을 때까지 켜면 제자리에서 걷는다.
            Assert.That(RabbitLocomotion.CanAnimate(Walking(Vector2.zero, 0.05f)), Is.False);
        }

        [Test]
        public void SidewaysLooksDifferentFromForward()
        {
            Advance(Walking(new Vector2(0f, 1f)), 60);
            var forwardThigh = Bone("L_Thigh");
            Advance(Walking(new Vector2(1f, 0f)), 60);
            var sideThigh = Bone("L_Thigh");
            Assert.That(Quaternion.Angle(forwardThigh, sideThigh), Is.GreaterThan(5f),
                "옆으로 갈 때와 앞으로 갈 때의 자세가 같다 — 옆걸음 클립이 안 섞이고 있다.");
        }

        [Test]
        public void LeftAndRightUseDifferentClips()
        {
            Advance(Walking(new Vector2(1f, 0f)), 60);
            var left = Bone("L_Thigh");
            Advance(Walking(new Vector2(-1f, 0f)), 60);
            var right = Bone("L_Thigh");
            Assert.That(Quaternion.Angle(left, right), Is.GreaterThan(3f),
                "좌우가 같은 자세다 — 가는 쪽 발이 앞발이어야 한다.");
        }

        // --- 전환이 매끄럽다 -------------------------------------------------------------

        [Test]
        public void TurningFromForwardToSidewaysDoesNotSnap()
        {
            var worst = WorstStep(Walking(new Vector2(0f, 1f)), Walking(new Vector2(1f, 0f)));
            Assert.That(worst, Is.LessThan(20f), $"앞→옆 전환에서 {worst:0.0}도/프레임 튄다.");
        }

        [Test]
        public void GoingDiagonalIsBlendedNotSwitched()
        {
            // 대각선은 두 클립 사이 어딘가여야 한다. 갈아끼우면 여기서 딱딱 튄다.
            var worst = WorstStep(Walking(new Vector2(0f, 1f)), Walking(new Vector2(0.7f, 0.7f)));
            Assert.That(worst, Is.LessThan(20f), $"대각선에서 {worst:0.0}도/프레임 튄다.");
        }

        [Test]
        public void JumpingWhileMovingSidewaysDoesNotSnap()
        {
            var worst = WorstStep(Walking(new Vector2(1f, 0f)), Air(3.4f));
            Assert.That(worst, Is.LessThan(25f), $"옆걸음→점프에서 {worst:0.0}도/프레임 튄다.");
        }

        // --- 점프 팔 (S15P21B202-670) ----------------------------------------------------

        [Test]
        public void ArmsBlendIntoTheAirPoseSlowerThanTheBody()
        {
            // 공중 자세가 켜지는 0.15초에 팔이 프레임당 21~32도 움직였다 — 그것이
            // "점프할 때 팔이 파닥거린다" 의 정체였다.
            Assert.That(RabbitLocomotion.AirArmBlendTime,
                Is.GreaterThan(RabbitLocomotion.AirBlendTime * 1.5f),
                "팔이 몸과 같은 속도로 섞이면 파닥거린다.");
        }

        [Test]
        public void TheAirPoseNeverFreezesHoweverFastYouFall()
        {
            // AirSpeedSpan(3.4)을 넘으면 예전에는 자세가 얼어붙었다 — 아홉 프레임
            // 연속 0.00도. 점프는 −5.8m/s 까지 떨어진다.
            Advance(Air(-3.4f), 40);
            var atSpan = Bone("L_Thigh");
            Advance(Air(-5.8f), 40);
            var beyond = Bone("L_Thigh");
            Assert.That(Quaternion.Angle(atSpan, beyond), Is.GreaterThan(0.5f),
                "더 빨리 떨어지는데 자세가 그대로다 — 얼어붙었다.");
        }
    }
}
