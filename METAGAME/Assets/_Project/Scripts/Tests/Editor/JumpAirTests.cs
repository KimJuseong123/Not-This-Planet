using System.Linq;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 점프와 낙하의 공중 자세를 지킨다 (S15P21B202-628).
    ///
    /// <para>이 스위트의 무게는 "점프가 예쁜가" 가 아니라 <b>"난간에서 떨어질 때도
    /// 같은 길을 타는가"</b> 에 있다. 공중 자세를 세로 속도 하나로 정하기로 한
    /// 이유가 그것이고, 시간이나 점프 이벤트로 고르면 낙하에서 맞출 수가 없다.</para>
    /// </summary>
    public sealed class JumpAirTests
    {
        PlayerRig _player;
        GameObject _ground;
        bool _puffEnabled;

        SpaceSuit Suit => _player.Doll.Suit;

        [SetUp]
        public void SetUp()
        {
            _puffEnabled = Puff.Enabled;
            Puff.Enabled = false;
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "~검증용 바닥";
            _ground.layer = GameLayers.Ground;
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();

            _player = PlayerRig.Create("Jump test", Vector3.zero, Quaternion.identity,
                                       Color.white, Color.gray);
            Suit.InitializeLocomotion();
        }

        [TearDown]
        public void TearDown()
        {
            Suit?.Locomotion?.Dispose();
            if (_player != null) Object.DestroyImmediate(_player.gameObject);
            if (_ground != null) Object.DestroyImmediate(_ground);
            // 지운 콜라이더가 물리 씬에 남아 다음 스위트의 레이캐스트를 맞힌다 (616 에서 겪음).
            Physics.SyncTransforms();
            Puff.Enabled = _puffEnabled;
        }

        static RabbitLocomotion.State Air(float vy, bool jumped = false, float ground = 9f) => new()
        { Grounded = false, VerticalSpeed = vy, JumpedUp = jumped, GroundDistance = ground };

        Quaternion Bone(string name) => Suit.Model
            .GetComponentsInChildren<Transform>(true).Single(t => t.name == name).localRotation;

        void Advance(RabbitLocomotion.State state, int frames = 12)
        {
            for (var i = 0; i < frames; i++)
            {
                Suit.Sync();
                Suit.Locomotion.Apply(state, 1f / 60f);
            }
        }

        // ── 클립 ──────────────────────────────────────────────────────

        [Test]
        public void AllFourJumpClipsShip()
        {
            foreach (var path in new[]
            {
                RabbitLocomotion.AirResource, RabbitLocomotion.JumpLaunchResource,
                RabbitLocomotion.JumpLandResource, RabbitLocomotion.JumpHeelResource,
            })
            {
                var clip = Resources.Load<AnimationClip>(path);
                Assert.NotNull(clip, path + " 클립이 없다.");
                Assert.Greater(clip.length, 0.05f, path + " 이 비었다.");
            }
        }

        /// <summary>
        /// <b>공중 클립은 순환이 아니다.</b> 시간축이 시간이 아니라 세로 속도축이라,
        /// 순환으로 두면 최고점에서 하강 자세로 되감긴다.
        /// </summary>
        [Test]
        public void TheAirClipIsAVelocityAxisNotATimeline()
        {
            var air = Resources.Load<AnimationClip>(RabbitLocomotion.AirResource);
            Assert.IsFalse(air.isLooping,
                "공중 클립이 순환이면 속도축이 되감긴다 — 최고점에서 자세가 튄다.");
        }

        // ── 속도로 자세가 정해진다 ────────────────────────────────────

        /// <summary><b>이 검증이 이 티켓의 핵심이다.</b> 같은 속도면 점프든 낙하든 같은 자세여야 한다.</summary>
        [Test]
        public void TheSameFallSpeedGivesTheSamePoseWhetherYouJumpedOrNot()
        {
            Advance(Air(-2.0f, jumped: true), 30);
            var afterJump = Bone("R_Upperarm");

            TearDown();
            SetUp();

            Advance(Air(-2.0f, jumped: false), 30);
            var afterFall = Bone("R_Upperarm");

            Assert.Less(Quaternion.Angle(afterJump, afterFall), 1f,
                "같은 속도인데 자세가 다르다 — 낙하가 점프와 다른 길을 타고 있다.");
        }

        [Test]
        public void RisingAndFallingAreDifferentPoses()
        {
            Advance(Air(3.0f), 30);
            var rising = Bone("R_Upperarm");

            TearDown();
            SetUp();

            Advance(Air(-3.0f), 30);
            var falling = Bone("R_Upperarm");

            Assert.Greater(Quaternion.Angle(rising, falling), 15f,
                "상승과 하강의 자세가 사실상 같다 — 속도축이 안 먹고 있다.");
        }

        /// <summary>
        /// 난간에서 걸어 나가면 세로 속도가 0 에서 시작한다. 그 자리가
        /// <b>상승 자세에 가까우면 안 된다</b> — 뛰지도 않았는데 팔이 올라간다.
        /// </summary>
        [Test]
        public void SteppingOffALedgeDoesNotLookLikeALaunch()
        {
            Advance(Air(3.0f), 30);
            var rising = Bone("R_Upperarm");

            TearDown();
            SetUp();

            Advance(Air(0.0f), 30);
            var neutral = Bone("R_Upperarm");

            Assert.Greater(Quaternion.Angle(rising, neutral), 10f,
                "속도 0 의 자세가 상승과 같다 — 난간에서 걸어 나가면 만세를 한다.");
        }

        /// <summary>
        /// <b>뛰어오름·뒤꿈치 딱이 실제로 화면에 나오는가</b> (AI 리뷰가 물은 것).
        ///
        /// <para>공중 블록은 <c>OneShots</c> 가중치를 전부 0 으로 눌러 놓는다.
        /// 한 번짜리 루프가 그보다 <b>먼저 돌고 return</b> 하면 문제가 없지만,
        /// 아니라면 두 연출이 조용히 죽는다 — 말로 따질 것이 아니라 재서 가린다.</para>
        /// </summary>
        [Test]
        public void TheLaunchClipActuallyShowsInsteadOfThePlainAirPose()
        {
            // 공중에 뜬 채 순수 공중 자세를 먼저 잡는다
            Advance(Air(3.0f), 20);
            var airOnly = Bone("R_Upperarm");

            // 같은 속도에서 뛰어오름 한 번짜리를 켠다
            Suit.Locomotion.PlayJumpLaunch();
            Advance(Air(3.0f), 3);
            var withLaunch = Bone("R_Upperarm");

            Assert.Greater(Quaternion.Angle(airOnly, withLaunch), 3f,
                "뛰어오름 클립을 켰는데 자세가 공중 자세 그대로다 — 공중 블록이 눌러 죽이고 있다.");
        }

        /// <summary>뒤꿈치 딱은 <b>점프에서만</b> 나온다. 떨어질 때는 최고점이 없다.</summary>
        [Test]
        public void TheHeelClickOnlyHappensOnARealJump()
        {
            // 뛴 점프: 최고점(속도 +에서 -로)을 지난다
            Advance(Air(1.0f, jumped: true), 4);
            Advance(Air(-1.0f, jumped: true), 4);
            Assert.IsTrue(Suit.Locomotion.HeelClickPlayed,
                "점프 최고점을 지났는데 뒤꿈치 딱이 안 켜졌다.");

            TearDown();
            SetUp();

            // 그냥 낙하: 속도가 내내 음수라 최고점이 없다
            Advance(Air(-1.0f, jumped: false), 10);
            Assert.IsFalse(Suit.Locomotion.HeelClickPlayed,
                "떨어지기만 했는데 뒤꿈치 딱이 나왔다.");
        }

        /// <summary>
        /// 클립을 아직 안 구운 저장소에서도 공중에서 다리가 걸음을 걸으면 안 된다.
        /// </summary>
        [Test]
        public void WithoutTheAirClipTheBodyGoesBackToPhysicsInsteadOfWalking()
        {
            var source = TestSource.Read("Runtime/Gameplay/Player/RabbitLocomotion.cs");
            StringAssert.Contains("if (!AirReady)", source,
                "공중 클립이 없을 때 물리로 돌려보내는 분기가 없다 — 공중에서 걷기가 돈다.");
        }

        /// <summary>
        /// 착지 동작이 <b>닿기 전에</b> 올라온다 (S15P21B202-628).
        ///
        /// <para>접지한 뒤에 켜면 예고 없이 붙는다. 지라 완료 조건이었는데 처음
        /// 구현에서 빠져 있었다 — AI 리뷰가 잡았다.</para>
        /// </summary>
        [Test]
        public void TheLandingStartsBeforeTheFeetTouch()
        {
            // 아직 멀다 — 착지 동작이 켜지면 안 된다
            Advance(Air(-3.0f, ground: 4f), 4);
            var high = Bone("R_Thigh");

            // 남은 시간이 여유보다 짧아지면 켜진다 (3m/s 로 0.3m 남음 = 0.10초)
            Advance(Air(-3.0f, ground: 0.3f), 4);
            var low = Bone("R_Thigh");

            Assert.Greater(Quaternion.Angle(high, low), 3f,
                "접지 직전인데 자세가 그대로다 — 착지 동작이 닿은 뒤에야 붙는다.");
        }

        // ── 물리와의 몫 ───────────────────────────────────────────────

        /// <summary>
        /// <b>공중에서 애니메이션이 물리를 덮으면 안 된다.</b> 공중에서 Balance 힘이
        /// 26% 로 떨어져 팔다리가 허우적거리는 것은 의도된 톤이다.
        /// </summary>
        [Test]
        public void TheAirPoseLeavesRoomForThePhysicsWobble()
        {
            Assert.Less(RabbitLocomotion.AirPoseWeight, 0.8f,
                "공중 자세를 너무 세게 덮으면 허우적임이 죽는다.");
            Assert.Greater(RabbitLocomotion.AirPoseWeight, 0.3f,
                "너무 약하면 자세가 안 읽힌다.");
        }

        [Test]
        public void TheAirSpanMatchesTheJumpSpeed()
        {
            Assert.AreEqual(Tuning.JumpSpeed, RabbitLocomotion.AirSpeedSpan, 0.001f,
                "공중 자세가 최대로 벌어지는 속도가 점프 속도와 다르면, 뛰자마자 자세가 포화되거나 모자란다.");
        }

        // ── 실측값이 주석·검증과 맞는가 ───────────────────────────────

        /// <summary>
        /// 점프 최고점. <b>주석과 검증이 오래 낡아 있었다</b> — 2.06m 라고 적혀
        /// 있었는데 실제는 1.156m 다. 누가 중력이나 점프 속도를 바꾸고 둘 다 안 고쳤다.
        /// </summary>
        [Test]
        public void TheJumpApexIsWhatTheAnimationWasBuiltOn()
        {
            var apex = Tuning.JumpSpeed * Tuning.JumpSpeed
                       / (2f * Mathf.Abs(UnityEngine.Physics.gravity.y));
            Assert.AreEqual(1.156f, apex, 0.02f, "점프 최고점 v²/2g");

            var air = 2f * Tuning.JumpSpeed / Mathf.Abs(UnityEngine.Physics.gravity.y);
            Assert.AreEqual(1.36f, air, 0.02f, "체공 2v/g — 공중 자세가 보이는 시간");
        }
    }
}
