using System.Linq;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// Z 로 엎드리는 자세를 지킨다 (S15P21B202-616).
    ///
    /// <para>이 스위트의 무게는 "엎드려지는가" 가 아니라 <b>"엎드린 것이
    /// 조종을 잃는 것과 구별되는가"</b> 에 있다. 엎드리면 머리가 골반보다
    /// 낮아져 <c>Ragdolled</c> 가 저절로 켜지는데, 그러면 <c>Blocked</c> 에
    /// 걸려 기어가기가 통째로 죽는다 — 눈으로는 "엎드려는 있는데 안 움직인다"
    /// 로만 보인다.</para>
    /// </summary>
    public sealed class ProneTests
    {
        PlayerRig _player;
        GameObject _ground;
        bool _puffEnabled;

        [SetUp]
        public void SetUp()
        {
            _puffEnabled = Puff.Enabled;
            Puff.Enabled = false;
            // **바닥이 있어야 한다.** 엎드리기는 접지 상태에서만 켜진다(공중에서
            // 엎드리면 떨어지는 동안 폭풍 면제가 붙는다). 바닥을 안 놓으면
            // ProbeGround 가 아무것도 못 찾아 Grounded 가 끝내 false 다 —
            // 처음에 이걸 빼먹고 "토글이 안 된다" 를 네 번 봤다.
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "~검증용 바닥";
            _ground.layer = GameLayers.Ground;
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();

            _player = PlayerRig.Create("Prone test", Vector3.zero, Quaternion.identity,
                                       Color.white, Color.gray);
        }

        [TearDown]
        public void TearDown()
        {
            _player?.Doll?.Suit?.Locomotion?.Dispose();
            if (_player != null) Object.DestroyImmediate(_player.gameObject);
            if (_ground != null) Object.DestroyImmediate(_ground);

            // **지우는 것만으로는 부족하다.** 물리 씬은 다음 동기화까지 콜라이더를
            // 들고 있어서, 다음 스위트의 레이캐스트가 이미 지운 바닥을 맞힌다 —
            // StuckEscapeTests 의 "지면이 아무 데도 없을 때" 가 실제로 깨졌다.
            Physics.SyncTransforms();
            Puff.Enabled = _puffEnabled;
        }

        /// <summary>키를 누른 프레임과 뗀 프레임을 한 번씩 흘린다.</summary>
        void Press()
        {
            _player.SetInput(new InputFrame { ProneHeld = true });
            _player.Tick();
            _player.SetInput(new InputFrame());
            _player.Tick();
        }

        [Test]
        public void ZTogglesProneOnAndOffAgain()
        {
            _player.Tick();
            Assert.IsTrue(_player.Grounded,
                $"바닥을 못 찾았다(거리 {_player.GroundDistance:0.###}) — 아래 검증이 전부 뜻을 잃는다.");
            Assert.IsFalse(_player.Prone, "처음부터 엎드려 있으면 안 된다.");
            Press();
            Assert.IsTrue(_player.Prone, "Z 를 눌렀는데 안 엎드린다.");
            Press();
            Assert.IsFalse(_player.Prone, "다시 눌렀는데 안 일어난다.");
        }

        /// <summary>
        /// <b>누르고 있는 동안 계속 뒤집히면 안 된다.</b> 눌린 상태를 나르고
        /// 가장자리를 서버가 잡는 구조라, 이걸 안 지키면 키를 쥐고 있는 동안
        /// 매 틱 엎드렸다 일어난다.
        /// </summary>
        [Test]
        public void HoldingTheKeyDownDoesNotToggleEveryTick()
        {
            _player.SetInput(new InputFrame { ProneHeld = true });
            for (var i = 0; i < 12; i++) _player.Tick();

            Assert.IsTrue(_player.Prone, "누르고 있는 동안 자세가 뒤집혔다.");
        }

        /// <summary>
        /// <b>이 검증이 이 티켓의 핵심이다.</b> 엎드리면 머리가 골반 아래로 가서
        /// <c>Ragdolled</c> 가 저절로 켜진다 — 그러면 <c>Blocked</c> 에 걸려
        /// 기어갈 수 없다.
        /// </summary>
        [Test]
        public void LyingDownIsNotTheSameAsLosingControl()
        {
            Press();
            for (var i = 0; i < 30; i++) _player.Tick();

            Assert.IsTrue(_player.Prone);
            Assert.IsFalse(_player.Ragdolled,
                "엎드렸더니 조종을 잃은 것으로 처리됐다 — 기어가기가 죽는다.");
            Assert.IsFalse(_player.Downed, "엎드린 것은 기절이 아니다.");
        }

        [Test]
        public void TheCrawlSpeedIsWellBelowWalking()
        {
            Assert.Less(PlayerRig.CrawlSpeed, Tuning.WalkSpeed * 0.4f,
                "기어가는 속도가 걷기와 비슷하면 엎드릴 이유가 없다.");
            Assert.Greater(PlayerRig.CrawlSpeed, 0.3f, "너무 느리면 조작이 안 된다.");
        }

        [Test]
        public void ProneRidesLowerThanStanding()
        {
            Assert.Less(PlayerRig.ProneRideHeight, Tuning.RideHeight * 0.6f,
                "엎드려도 골반이 선 자세만큼 떠 있으면 낮아진 것이 아니다.");
        }

        /// <summary>몸을 가져가는 것이 끼어들면 자세가 풀린다 — 기절한 채로 면제를 받으면 안 된다.</summary>
        [Test]
        public void BeingDownedClearsTheProneState()
        {
            Press();
            Assert.IsTrue(_player.Prone);

            _player.GoDown("검증");
            _player.Tick();

            Assert.IsFalse(_player.Prone, "기절했는데 엎드린 상태가 남아 있다 — 폭풍 면제가 따라온다.");
        }

        /// <summary>남의 몸에도 그대로 선다. 안 그러면 남의 화면에서 그 사람만 서 있다.</summary>
        [Test]
        public void TheStateCanBeAppliedToSomeoneElsesBody()
        {
            _player.SetRemoteProne(true);
            Assert.IsTrue(_player.Prone);

            _player.SetRemoteProne(false);
            Assert.IsFalse(_player.Prone);
        }

        /// <summary>
        /// 눌린 상태가 <c>Same()</c> 에 들어 있는가.
        ///
        /// <para>빠지면 <b>눌러도 서버가 모른다</b> — 같은 프레임으로 보고
        /// 보내지 않는다. <c>StuckEscapeHeld</c>(T)가 지금 그 상태로 죽어 있다.</para>
        /// </summary>
        [Test]
        public void TheKeyActuallyReachesTheServer()
        {
            var source = TestSource.Read("Runtime/Network/NetPlayer.cs");
            StringAssert.Contains("a.ProneHeld == b.ProneHeld", source,
                "Same() 에 ProneHeld 가 없다 — 눌러도 서버로 안 간다.");
        }

        [Test]
        public void ZIsBoundAndDoesNotCollideWithTheClimbKey()
        {
            Assert.AreEqual(KeyCode.Z, Controls.Prone);
            Assert.AreNotEqual(Controls.ArmsUp, Controls.Prone,
                "기어오르기(LeftCtrl)와 같은 키면 하나는 영영 안 된다.");
        }

        // ── 클립 (S15P21B202-616) ──────────────────────────────────────

        [Test]
        public void AllFourProneClipsShip()
        {
            foreach (var path in new[]
            {
                RabbitLocomotion.ProneIdleResource, RabbitLocomotion.CrawlResource,
                RabbitLocomotion.LieDownResource, RabbitLocomotion.StandUpResource,
            })
            {
                var clip = Resources.Load<AnimationClip>(path);
                Assert.NotNull(clip, path + " 클립이 없다.");
                Assert.Greater(clip.length, 0.1f, path + " 이 비었다.");
            }
        }

        /// <summary>
        /// <b>대기와 기어가기는 순환이어야 한다.</b> 자세는 뗄 때까지 유지되는데
        /// 순환이 아니면 클립이 끝나는 자리에서 굳는다 — 에러 없이 "엎드려서
        /// 안 움직이는" 그림만 남는다.
        /// </summary>
        [Test]
        public void TheHeldPosesLoopAndTheTransitionsDoNot()
        {
            Assert.IsTrue(Resources.Load<AnimationClip>(RabbitLocomotion.ProneIdleResource).isLooping,
                "엎드린 대기가 순환이 아니다 — 2초 뒤에 굳는다.");
            Assert.IsTrue(Resources.Load<AnimationClip>(RabbitLocomotion.CrawlResource).isLooping,
                "기어가기가 순환이 아니다.");
            Assert.IsFalse(Resources.Load<AnimationClip>(RabbitLocomotion.LieDownResource).isLooping,
                "엎드리기는 한 번만 돈다.");
            Assert.IsFalse(Resources.Load<AnimationClip>(RabbitLocomotion.StandUpResource).isLooping,
                "일어서기는 한 번만 돈다.");
        }

        /// <summary>엎드린 자세가 실제로 뼈를 움직이는가 — 믹서에 못 붙어도 에러는 안 난다.</summary>
        [Test]
        public void TheProneposeActuallyMovesBones()
        {
            var suit = _player.Doll.Suit;
            suit.InitializeLocomotion();
            Assert.NotNull(suit.Locomotion, "구운 클립이 빌드에 없다.");

            var standing = new RabbitLocomotion.State { Grounded = true, Speed = 0f };
            for (var i = 0; i < 6; i++) { suit.Sync(); suit.Locomotion.Apply(standing, 1f / 60f); }
            suit.Sync();
            var before = suit.Model.GetComponentsInChildren<Transform>(true)
                .Single(t => t.name == "R_Thigh").localRotation;

            var prone = new RabbitLocomotion.State { Grounded = true, Speed = 0f, Prone = true };
            for (var i = 0; i < 40; i++) { suit.Sync(); suit.Locomotion.Apply(prone, 1f / 60f); }
            var after = suit.Model.GetComponentsInChildren<Transform>(true)
                .Single(t => t.name == "R_Thigh").localRotation;

            Assert.Greater(Quaternion.Angle(before, after), 5f,
                "엎드린 자세가 다리를 안 움직인다 — 믹서 입력이 모자랄 수 있다.");
        }

        /// <summary>폭풍이 자세를 보는가. 판정이 한 곳뿐이라 그 자리를 글자로 지킨다.</summary>
        [Test]
        public void TheStormLetsAProneBodyPass()
        {
            var source = TestSource.Read("Runtime/Gameplay/World/Atmosphere/Stage1DustStormHazard.cs");
            StringAssert.Contains("player.Prone", source,
                "폭풍 판정이 자세를 보지 않는다 — 엎드려도 그대로 날아간다.");
        }
    }
}
