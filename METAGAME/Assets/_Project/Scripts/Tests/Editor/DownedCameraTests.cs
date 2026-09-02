using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 기절 카메라를 검증한다(기획 §10.1 · §10.3, S15P21B202-259).
    ///
    /// <b>예전에는 <see cref="PlayerRig.GoDown"/> 이 카메라를 건드리지 않았다.</b> 몸은
    /// 래그돌로 쓰러지는데 시야는 서 있을 때와 똑같이 수평을 봤다 — 자기가 기절했는지
    /// 화면으로 알 수 없었다.
    ///
    /// 카메라는 <c>LateUpdate</c> 로 도는 표현이라 EditMode 로 화면을 볼 수 없다.
    /// <b>상태와 전환을</b> 잠근다 — S15P21B202-243 에서 스윙 궤적을 순수 함수로 빼
    /// 검증한 것과 같은 방식이다.
    /// </summary>
    public sealed class DownedCameraTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWasEnabled;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
        }

        // --- 진입 (§10.1) ---------------------------------------------------

        [Test]
        public void StandingUp_ThereIsNoDownedView()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            Assert.IsFalse(player.Cam.DownedView);
            Assert.IsFalse(player.Cam.DownedFalling);
        }

        /// <summary>§10.1 "기절 중에는 자동으로 3인칭 전환한다".</summary>
        [Test]
        public void GoingDown_EntersTheDownedView()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.GoDown("운석 직접 충돌");

            Assert.IsTrue(player.Cam.DownedView);
        }

        /// <summary>
        /// 넘어지는 동안은 1인칭이다. <b>기획에 없는 연출이다</b> — §10.1 · §10.3 은
        /// 결과만 정하고 전환 과정을 정하지 않는다(S15P21B202-259 에서 정했다).
        /// </summary>
        [Test]
        public void WhileFalling_TheViewIsStillFirstPerson()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.GoDown("검증");

            Assert.IsTrue(player.Cam.DownedFalling);
            Assert.IsTrue(player.Cam.FirstPerson);
        }

        // --- 넘어지는 동안 (기획에 없는 연출) --------------------------------

        /// <summary>
        /// 몸이 넘어지는 회전과 마우스 회전이 섞이면 무엇이 넘어지는 것인지 알 수 없다.
        /// </summary>
        [Test]
        public void WhileFalling_TheMouseDoesNotTurnTheView()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");

            var before = player.Cam.Yaw;
            player.Cam.Steer(new Vector2(50f, 30f));

            Assert.AreEqual(before, player.Cam.Yaw, 1e-4f,
                "넘어지는 동안 마우스는 먹히지 않는다.");
        }

        [Test]
        public void AfterTheFall_TheMouseWorksAgain()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");

            Fall(player, PhysicsScale.DownedFirstPersonSeconds + Step);
            Assert.IsFalse(player.Cam.DownedFalling);

            var before = player.Cam.Yaw;
            player.Cam.Steer(new Vector2(50f, 0f));

            Assert.AreNotEqual(before, player.Cam.Yaw,
                "§10.3 은 자유 시점이다.");
        }

        // --- 3인칭 전환 (§10.3) --------------------------------------------

        [Test]
        public void AfterFiveSeconds_ItBecomesThirdPerson()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");

            Fall(player, PhysicsScale.DownedFirstPersonSeconds - 0.1f);
            Assert.IsTrue(player.Cam.FirstPerson, "아직 넘어지는 중이다.");

            Fall(player, 0.2f);

            Assert.IsFalse(player.Cam.FirstPerson, "§10.3 3인칭 자유 시점.");
            Assert.IsTrue(player.Cam.DownedView, "기절 시점은 유지된다.");
        }

        /// <summary>
        /// §20.1 "기절한 플레이어도 다른 물체의 충격으로 다시 밀릴 수 있다". "몸이 멈추면"
        /// 으로 끊으면 운석에 굴러다니는 동안 영영 1인칭에 머문다.
        /// </summary>
        [Test]
        public void BeingPushedAroundDoesNotDelayTheSwitch()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");

            for (var i = 0; i < Mathf.CeilToInt(PhysicsScale.DownedFirstPersonSeconds / Step) + 2; i++)
            {
                // 매 프레임 몸을 밀어 계속 움직이게 한다.
                player.Doll.Hips.linearVelocity = new Vector3(3f, 0f, 0f);
                player.Cam.TickDownedView(Step);
            }

            Assert.IsFalse(player.Cam.FirstPerson, "시간 기준이어야 한다.");
        }

        [Test]
        public void TheElapsedTimeIsReported()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");

            Assert.AreEqual(0f, player.Cam.DownedSeconds, 1e-3f);

            Fall(player, 1f);

            Assert.AreEqual(1f, player.Cam.DownedSeconds, 0.05f);
        }

        // --- 카메라가 몸을 따라간다 (§10.3) ---------------------------------

        /// <summary>
        /// §10.3 "손이나 견인빔으로 이동되면 카메라도 캐릭터를 따라간다".
        /// 타깃이 골반이므로 저절로 된다 — S15P21B202-249 의 동료 운반이 골반을 끈다.
        /// </summary>
        [Test]
        public void TheCameraTargetIsTheBody()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");

            Assert.AreSame(player.Hips.transform, player.Cam.Target,
                "동료가 끌고 옮기면 시야도 함께 가야 한다.");
        }

        // --- 구조 복귀 (§10.3) ---------------------------------------------

        /// <summary>§10.3 "구조되면 1인칭으로 돌아온다".</summary>
        [Test]
        public void BeingRescued_ReturnsToFirstPerson()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            var wasFirstPerson = player.Cam.FirstPerson;

            player.GoDown("검증");
            Fall(player, PhysicsScale.DownedFirstPersonSeconds + Step);
            Assert.IsFalse(player.Cam.FirstPerson);

            player.Revive(player.MaxHealth);

            Assert.IsFalse(player.Cam.DownedView);
            Assert.AreEqual(wasFirstPerson, player.Cam.FirstPerson,
                "기절 전 시점으로 복원한다.");
        }

        [Test]
        public void BeingRescuedWhileStillFalling_AlsoReturns()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.GoDown("검증");
            Fall(player, 1f);

            player.Revive(player.MaxHealth);

            Assert.IsFalse(player.Cam.DownedView);
            Assert.IsFalse(player.Cam.DownedFalling);
            Assert.AreEqual(0f, player.Cam.DownedSeconds, 1e-3f);
        }

        [Test]
        public void GoingDownTwice_RestartsTheFall()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.GoDown("검증");
            Fall(player, PhysicsScale.DownedFirstPersonSeconds + Step);
            player.Revive(player.MaxHealth);

            player.GoDown("두 번째");

            Assert.IsTrue(player.Cam.DownedFalling, "다시 몸과 함께 넘어간다.");
            Assert.AreEqual(0f, player.Cam.DownedSeconds, 1e-3f);
        }

        // --- 다른 카메라 모드를 깨지 않는다 ---------------------------------

        [Test]
        public void FreeLookIsTurnedOffWhileDowned()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.Cam.SetFreeLook(true);

            player.GoDown("검증");

            Assert.IsFalse(player.Cam.FreeLook, "Alt 시점과 겹치지 않는다.");
        }

        [Test]
        public void TheVehicleViewStillWorks()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.Cam.SetVehicleView(true);
            Assert.IsTrue(player.Cam.VehicleView);
            Assert.IsTrue(player.Cam.FirstPerson);

            player.Cam.SetVehicleView(false);
            Assert.IsFalse(player.Cam.VehicleView);
        }

        [Test]
        public void APlayerWhoIsNotDownedIsUnaffected()
        {
            var downed = MakePlayerWithCamera(Vector3.zero);
            var other = MakePlayerWithCamera(new Vector3(5f, 0f, 0f));

            downed.GoDown("검증");

            Assert.IsTrue(downed.Cam.DownedView);
            Assert.IsFalse(other.Cam.DownedView, "남의 카메라는 건드리지 않는다.");
        }

        // --- 기존 금지가 유지된다 (§10.3) -----------------------------------

        [Test]
        public void WhileDowned_YouCannotMoveOrUseTools()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.GoDown("검증");

            Assert.IsTrue(player.Downed);
            Assert.IsNull(player.HeldTool, "§10.3 장비 사용 불가 — GoDown 이 놓게 한다.");
        }

        // --- 리그 ---------------------------------------------------------

        static void Fall(PlayerRig player, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Step);
            for (var i = 0; i < steps; i++) player.Cam.TickDownedView(Step);
        }

        PlayerRig MakePlayerWithCamera(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);

            player.Cam = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            _spawned.Add(player.Cam.gameObject);

            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
