using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운반 장치에 탄 사람이 <b>남의 화면에서</b> 보이지 않는 것을 검증한다
    /// (S15P21B202-305).
    ///
    /// <b>감추는 코드는 이미 있었다.</b> <c>PlayerRig.RefreshDollVisibility</c> 가
    /// <c>IsRiding</c> 이면 몸을 감췄고, <c>Board()</c> 의 주석도 "기체 밖으로 튀어나온
    /// 몸이 보이지 않는다" 를 의도로 적어 두었다.
    ///
    /// 문제는 판단 기준이었다. <c>IsRiding</c> 은 <c>RidingBody</c> 로 정해지고 그 값은
    /// <c>Board()</c> 가 채우므로 <b>탄 본인의 컴퓨터에서만</b> 참이다. 남의 화면에 있는
    /// 몸은 <c>NetPlayer</c> 가 따로 만든 <see cref="PlayerRig"/> 이라 계속 그려졌고,
    /// 조종석에 묻힌 몸통과 달리 <b>귀가 기체 밖으로 튀어나왔다.</b>
    ///
    /// <b>되돌리는 쪽이 더 위험하다.</b> 감추기만 하고 하차에서 되돌리지 않으면
    /// 투명인간이 생기고, 그것은 원래 버그보다 나쁘다. 이 스위트는 양방향을 모두 본다.
    /// </summary>
    public sealed class RemoteRidingVisibilityTests
    {
        readonly List<GameObject> _spawned = new();
        bool _puffWas;
        bool _ignoreWas;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();

            _puffWas = Puff.Enabled;
            Puff.Enabled = false;

            // 되돌리지 않으면 뒤에 도는 다른 스위트가 로그 실패를 놓친다.
            _ignoreWas = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWas;
            LogAssert.ignoreFailingMessages = _ignoreWas;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
        }

        // --- 감춘다 -----------------------------------------------------------

        /// <summary>
        /// 남의 몸은 카메라가 없다. 그래서 평소에는 보이고, 그것이 정상이다.
        /// </summary>
        [Test]
        public void ARemoteBodyIsVisibleByDefault()
        {
            var player = MakeRemoteBody();

            Assert.IsFalse(player.RemoteRiding);
            Assert.IsTrue(player.Doll.Visible, "남의 몸은 원래 보여야 한다.");
        }

        [Test]
        public void WhileRiding_TheRemoteBodyIsHidden()
        {
            var player = MakeRemoteBody();

            player.SetRemoteRiding(true);

            Assert.IsTrue(player.RemoteRiding);
            Assert.IsFalse(player.Doll.Visible,
                "조종석에 앉은 몸이 남의 화면에 보이면 귀가 기체 밖으로 나온다.");
        }

        /// <summary>
        /// <b>귀가 문제였으므로 렌더러를 실제로 꺼야 한다.</b> 1인칭처럼 그림자만 남기면
        /// (<c>keepShadow</c>) 렌더러가 켜진 채라 조종석 밖으로 그림자가 생긴다.
        /// </summary>
        [Test]
        public void WhileRiding_EveryRendererIsOff()
        {
            var player = MakeRemoteBody();

            player.SetRemoteRiding(true);

            foreach (var renderer in Renderers(player))
                Assert.IsFalse(renderer.enabled,
                    $"{renderer.name} 이 켜져 있으면 기체 밖에 그림자가 생긴다.");
        }

        // --- 되돌린다 (이쪽이 더 위험하다) -------------------------------------

        [Test]
        public void AfterDismount_TheBodyComesBack()
        {
            var player = MakeRemoteBody();

            player.SetRemoteRiding(true);
            Assert.IsFalse(player.Doll.Visible);

            player.SetRemoteRiding(false);

            Assert.IsTrue(player.Doll.Visible, "하차했는데 안 보이면 투명인간이 된다.");
            foreach (var renderer in Renderers(player))
                Assert.IsTrue(renderer.enabled);
        }

        [Test]
        public void BoardingAndDismountingRepeatedly_StaysConsistent()
        {
            var player = MakeRemoteBody();

            for (var i = 0; i < 4; i++)
            {
                player.SetRemoteRiding(true);
                Assert.IsFalse(player.Doll.Visible, $"{i}번째 탑승");

                player.SetRemoteRiding(false);
                Assert.IsTrue(player.Doll.Visible, $"{i}번째 하차");
            }
        }

        [Test]
        public void SettingTheSameValueTwice_ChangesNothing()
        {
            var player = MakeRemoteBody();

            player.SetRemoteRiding(true);

            // 밖에서 렌더러를 켜 둔다. 다시 적용되면 도로 꺼질 것이다.
            var renderer = Renderers(player)[0];
            renderer.enabled = true;

            player.SetRemoteRiding(true);

            Assert.IsTrue(renderer.enabled, "값이 안 바뀌었는데 다시 훑었다.");
        }

        // --- 다른 시점 규칙을 깨지 않는다 --------------------------------------

        /// <summary>
        /// 탄 본인은 <c>IsRiding</c> 으로 이미 감춰진다. 원격 플래그가 그것을 되돌리면
        /// 안 된다.
        /// </summary>
        [Test]
        public void TheLocalRidingRuleStillWins()
        {
            var player = MakeLocalBody();

            player.SetRemoteRiding(true);
            Assert.IsFalse(player.Doll.Visible);

            // 서버가 "안 타고 있다" 를 늦게 보내도 1인칭이면 여전히 감춰져 있어야 한다.
            player.SetRemoteRiding(false);

            Assert.IsTrue(player.Cam.FirstPerson);
            Assert.IsFalse(player.Doll.Visible, "1인칭에서 제 몸이 시야를 막으면 안 된다.");
        }

        /// <summary>
        /// 1인칭은 그림자를 남긴다(§10.1 "발밑을 읽는 단서가 제 그림자다").
        /// 탑승은 남기지 않는다. 두 규칙이 섞이면 안 된다.
        /// </summary>
        [Test]
        public void FirstPersonKeepsItsShadow_RidingDoesNot()
        {
            var local = MakeLocalBody();
            var renderer = Renderers(local)[0];

            Assert.IsTrue(renderer.enabled, "1인칭은 그림자를 남기므로 렌더러가 켜져 있다.");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly,
                            renderer.shadowCastingMode);

            local.SetRemoteRiding(true);

            Assert.IsFalse(renderer.enabled, "탑승 중에는 그림자도 남기지 않는다.");
        }

        [Test]
        public void AThirdPersonPlayerIsHiddenWhileRiding()
        {
            var player = MakeLocalBody(firstPerson: false);

            Assert.IsTrue(player.Doll.Visible);

            player.SetRemoteRiding(true);
            Assert.IsFalse(player.Doll.Visible);

            player.SetRemoteRiding(false);
            Assert.IsTrue(player.Doll.Visible, "3인칭이면 하차 후 다시 보여야 한다.");
        }

        // --- 몸이 남아 있어야 한다 --------------------------------------------

        /// <summary>
        /// <b>몸을 지우지 않는다.</b> 자세 동기화가 계속 돌아야 하차한 자리에 서 있고,
        /// 기절 판정과 구조 상호작용(§20)이 몸에 붙어 있다.
        /// </summary>
        [Test]
        public void HidingDoesNotRemoveTheBody()
        {
            var player = MakeRemoteBody();
            var hips = player.Doll.Hips;

            player.SetRemoteRiding(true);

            Assert.IsNotNull(player.Doll.Hips);
            Assert.AreSame(hips, player.Doll.Hips);
            Assert.IsTrue(player.Doll.Root.gameObject.activeInHierarchy,
                "오브젝트는 살아 있어야 한다.");
        }

        [Test]
        public void AHiddenBodyStillMoves()
        {
            var player = MakeRemoteBody();
            player.SetRemoteRiding(true);

            var before = player.Doll.Hips.position;
            player.Doll.Hips.position = before + new Vector3(2f, 0f, 0f);
            UnityEngine.Physics.SyncTransforms();

            Assert.AreNotEqual(before, player.Doll.Hips.position,
                "감췄다고 자세 동기화가 멈추면 하차 위치가 어긋난다.");
        }

        // --- 리그 -------------------------------------------------------------

        static Renderer[] Renderers(PlayerRig player)
        {
            var renderers = player.Doll.Root.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "인형에 렌더러가 있어야 의미가 있다.");
            return renderers;
        }

        PlayerRig MakeBody()
        {
            var player = PlayerRig.Create("테스트", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        /// <summary>남의 몸. <c>NetPlayer.BuildRemoteBody</c> 처럼 카메라가 없다.</summary>
        PlayerRig MakeRemoteBody() => MakeBody();

        /// <summary>내 몸. 카메라가 붙는다.</summary>
        PlayerRig MakeLocalBody(bool firstPerson = true)
        {
            var player = MakeBody();

            player.Cam = OrbitCamera.Create(player.Hips.transform, Color.black, firstPerson);
            _spawned.Add(player.Cam.gameObject);

            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
