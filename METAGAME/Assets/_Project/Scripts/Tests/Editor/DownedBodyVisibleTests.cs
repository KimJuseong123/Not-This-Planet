using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 기절 3인칭에서 쓰러진 몸이 보이는 것을 검증한다(기획 §10.3, S15P21B202-267).
    ///
    /// <b>S15P21B202-259 가 3인칭 전환을 만들었는데 몸이 안 보였다.</b> 카메라가 물러나도
    /// 그림자만 남았다 — <c>RefreshDollVisibility()</c> 가 <b>사건마다</b> 불리고
    /// (<c>Teleport</c> · 탑승 · 하차 …) <c>GoDown</c> 에도, 시점이 바뀔 때도 없었기
    /// 때문이다.
    ///
    /// <b>259 의 검증이 이것을 놓친 이유</b>는 범위를 카메라 상태로만 잡았기 때문이다.
    /// 카메라는 <c>LateUpdate</c> 표현이라 EditMode 로 화면을 못 보지만, <b>인형의 렌더러
    /// 상태는 볼 수 있다.</b> 이 스위트가 그것을 본다.
    /// </summary>
    public sealed class DownedBodyVisibleTests
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

        // --- 1인칭에서는 감춘다 (기존 동작) ---------------------------------

        [Test]
        public void StandingInFirstPerson_TheBodyIsHidden()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            Assert.IsFalse(player.Doll.Visible, "제 몸이 시야를 막으면 안 된다.");
        }

        /// <summary>기획 §10.1 의 "발밑을 읽는 단서가 제 그림자다".</summary>
        [Test]
        public void HiddenInFirstPerson_TheShadowRemains()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            var renderer = AnyRenderer(player);

            Assert.IsTrue(renderer.enabled, "그림자를 남기려면 렌더러는 켜져 있어야 한다.");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly,
                            renderer.shadowCastingMode);
        }

        [Test]
        public void WhileFalling_TheBodyStaysHidden()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.GoDown("운석 직접 충돌");

            Assert.IsTrue(player.Cam.DownedFalling, "아직 1인칭이다.");
            Assert.IsFalse(player.Doll.Visible);
        }

        // --- 3인칭이 되면 보인다 (이 이슈의 본체) ----------------------------

        [Test]
        public void OnceTheViewGoesThirdPerson_TheBodyBecomesVisible()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("운석 직접 충돌");

            Assert.IsFalse(player.Doll.Visible, "넘어지는 동안은 감춰져 있다.");

            GoThirdPerson(player);

            Assert.IsFalse(player.Cam.FirstPerson);
            Assert.IsTrue(player.Doll.Visible, "쓰러진 내 모습이 보여야 한다(§10.3).");
        }

        [Test]
        public void TheVisibleBodyCastsNormalShadows()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");
            GoThirdPerson(player);

            var renderer = AnyRenderer(player);

            Assert.IsTrue(renderer.enabled);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On,
                            renderer.shadowCastingMode);
        }

        /// <summary>동료가 끌고 옮겨도(S15P21B202-249) 보이는 상태가 유지된다.</summary>
        [Test]
        public void BeingCarriedDoesNotHideTheBodyAgain()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");
            GoThirdPerson(player);

            for (var i = 0; i < 30; i++)
            {
                player.Doll.Hips.position += new Vector3(0.05f, 0f, 0f);
                player.Tick();
            }

            Assert.IsTrue(player.Doll.Visible);
        }

        // --- 구조받으면 다시 감춘다 (§10.3) ---------------------------------

        [Test]
        public void BeingRescued_HidesTheBodyAgain()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");
            GoThirdPerson(player);
            Assert.IsTrue(player.Doll.Visible);

            player.Revive(player.MaxHealth);

            Assert.IsTrue(player.Cam.FirstPerson, "§10.3 구조되면 1인칭으로 돌아온다.");
            Assert.IsFalse(player.Doll.Visible);
        }

        [Test]
        public void GoingDownTwice_ShowsTheBodyAgain()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.GoDown("첫 번째");
            GoThirdPerson(player);
            player.Revive(player.MaxHealth);

            player.GoDown("두 번째");
            Assert.IsFalse(player.Doll.Visible, "다시 넘어지는 동안은 감춰진다.");

            GoThirdPerson(player);
            Assert.IsTrue(player.Doll.Visible);
        }

        // --- 다른 시점을 깨지 않는다 ----------------------------------------

        /// <summary>
        /// 처음부터 3인칭인 사람(원격 플레이어는 <c>Cam</c> 이 없다)은 늘 보인다.
        /// </summary>
        [Test]
        public void APlayerWithNoCameraIsAlwaysVisible()
        {
            var player = MakePlayer(Vector3.zero);   // 카메라를 붙이지 않는다

            Assert.IsTrue(player.Doll.Visible);

            player.GoDown("검증");

            Assert.IsTrue(player.Doll.Visible, "남의 몸은 늘 보여야 한다.");
        }

        [Test]
        public void AThirdPersonPlayerStaysVisible()
        {
            var player = MakePlayerWithCamera(Vector3.zero, firstPerson: false);

            Assert.IsTrue(player.Doll.Visible);

            player.GoDown("검증");
            GoThirdPerson(player);

            Assert.IsTrue(player.Doll.Visible);
        }

        // --- 매 프레임 갱신하지 않는다 --------------------------------------

        /// <summary>
        /// <c>RefreshDollVisibility</c> 는 인형 아래 <b>모든</b> 렌더러를 훑는다(장식이
        /// 강체의 자식이라 그래야 한다). 상태가 안 바뀌면 훑지 않아야 한다.
        /// </summary>
        [Test]
        public void WithNoViewChange_NothingIsTouched()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");
            GoThirdPerson(player);

            // 렌더러를 밖에서 끈다. 갱신이 돌면 다시 켜질 것이다.
            var renderer = AnyRenderer(player);
            renderer.enabled = false;

            for (var i = 0; i < 20; i++) player.Tick();

            Assert.IsFalse(renderer.enabled,
                "시점이 안 바뀌었는데 렌더러를 다시 훑었다.");
        }

        // --- 프레이밍: 쓰러진 몸이 화면에 들어온다 ---------------------------

        /// <summary>
        /// <b>쓰러진 머리의 회전을 이어받으면 안 된다.</b> 얼굴이 땅을 보고 있으면 각도가
        /// 극단으로 가 <c>MinPitch</c> 에서 잘리고 카메라가 지면 아래를 향한다 — 렌더링을
        /// 살려도 몸이 프레임에 없다.
        /// </summary>
        [Test]
        public void TheViewLooksDownAtTheBody()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            // 얼굴이 땅을 보게 머리를 처박는다.
            player.Doll.Head.transform.rotation = Quaternion.Euler(89f, 0f, 0f);

            player.GoDown("검증");
            GoThirdPerson(player);

            Assert.AreEqual(PhysicsScale.DownedThirdPersonPitchDegrees, player.Cam.Pitch, 0.5f,
                "몸을 내려다보는 고정 각도여야 한다.");
        }

        /// <summary>옆으로 누워 roll 이 섞여도 각도가 흔들리지 않는다.</summary>
        [Test]
        public void LyingSideways_StillLooksDownAtTheBody()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.Doll.Head.transform.rotation = Quaternion.Euler(0f, 0f, 92f);

            player.GoDown("검증");
            GoThirdPerson(player);

            Assert.AreEqual(PhysicsScale.DownedThirdPersonPitchDegrees, player.Cam.Pitch, 0.5f);
        }

        /// <summary>
        /// 좌우는 <b>몸이 누운 방향</b>에서 가져온다 — 머리의 회전이 아니라 골반에서
        /// 머리로 향하는 벡터다. 어떤 자세로 넘어져도 신뢰할 수 있다.
        /// </summary>
        [Test]
        public void TheViewLooksAlongTheFallenBody()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            // 몸이 +X 로 누운 상태를 만든다.
            player.Doll.Hips.position = Vector3.zero;
            player.Doll.Head.transform.position = new Vector3(1.1f, 0.15f, 0f);
            UnityEngine.Physics.SyncTransforms();

            player.GoDown("검증");
            GoThirdPerson(player);

            Assert.AreEqual(90f, player.Cam.Yaw, 5f,
                "머리가 누운 쪽을 바라봐야 몸이 세로로 들어온다.");
        }

        [Test]
        public void AfterFraming_TheMouseStillWorks()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.GoDown("검증");
            GoThirdPerson(player);

            var before = player.Cam.Yaw;
            player.Cam.Steer(new Vector2(40f, 0f));

            Assert.AreNotEqual(before, player.Cam.Yaw,
                "고정하는 것은 전환 순간뿐이다(§10.3 자유 시점).");
        }

        // --- 리그 ---------------------------------------------------------

        static void GoThirdPerson(PlayerRig player)
        {
            var steps = Mathf.CeilToInt((PhysicsScale.DownedFirstPersonSeconds + 0.05f) / Step);
            for (var i = 0; i < steps; i++) player.Cam.TickDownedView(Step);

            // 소유자가 시점 변화를 알아채는 자리가 Tick 이다.
            player.Tick();
        }

        static Renderer AnyRenderer(PlayerRig player)
        {
            var renderers = player.Doll.Root.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "인형에 렌더러가 있어야 의미가 있다.");
            return renderers[0];
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        PlayerRig MakePlayerWithCamera(Vector3 foot, bool firstPerson = true)
        {
            var player = MakePlayer(foot);

            player.Cam = OrbitCamera.Create(player.Hips.transform, Color.black, firstPerson);
            _spawned.Add(player.Cam.gameObject);

            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
