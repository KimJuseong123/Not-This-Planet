using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 1인칭에서 제 캐릭터를 그리지 않는 것을 검증한다.
    ///
    /// 1인칭 카메라는 골반 + 0.55m(발 기준 0.85m)에 서고 시선 방향으로 0.20m 내민다.
    /// 헬멧이 0.51~1.15m, 반지름 약 0.32m 이므로 카메라가 제 헬멧 안에 있다. 귀는
    /// 1.33m 까지 올라가고 모델을 씌우면 주둥이와 꼬리가 더 붙는다. 그래서 제 몸을
    /// 그리면 시선을 조금만 돌려도 화면을 덮는다.
    ///
    /// 여기서 보는 것은 세 가지다 — 1인칭이면 감추는지, 그림자는 남기는지,
    /// 3인칭과 원격 플레이어는 그대로 보이는지.
    /// </summary>
    public sealed class FirstPersonVisibilityTests
    {
        readonly List<GameObject> spawned = new();
        bool savedIgnoreFailingMessages;

        [SetUp]
        public void SetUp()
        {
            savedIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = savedIgnoreFailingMessages;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        PlayerRig MakePlayer()
        {
            LogAssert.ignoreFailingMessages = true;

            var player = PlayerRig.Create("테스트", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            return player;
        }

        OrbitCamera Attach(PlayerRig player, bool firstPerson)
        {
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, firstPerson);
            spawned.Add(camera.gameObject);
            player.Cam = camera;              // 세터가 가시성을 다시 정한다
            return camera;
        }

        [Test]
        public void FirstPerson_HidesOwnDoll()
        {
            var player = MakePlayer();
            Assert.IsTrue(player.Doll.Visible, "카메라를 붙이기 전에는 보여야 한다.");

            Attach(player, true);

            Assert.IsFalse(player.Doll.Visible,
                "1인칭이면 제 몸을 그리지 않아야 한다. 카메라가 제 헬멧 안에 있어 "
                + "시선을 돌리면 제 몸이 화면을 덮는다.");

            foreach (var renderer in Renderers(player))
                Assert.AreNotEqual(ShadowCastingMode.On, renderer.shadowCastingMode,
                    $"{renderer.name} 이 여전히 메시를 그린다.");
        }

        /// <summary>
        /// 1인칭에서 제 그림자는 발밑을 읽는 단서다. 메시만 빼고 그림자는 남겨야 한다.
        /// </summary>
        [Test]
        public void FirstPerson_KeepsOwnShadow()
        {
            var player = MakePlayer();
            Attach(player, true);

            var shadowed = 0;
            foreach (var renderer in Renderers(player))
            {
                Assert.IsTrue(renderer.enabled,
                    $"{renderer.name} 을 끄면 그림자도 사라진다. ShadowsOnly 로 두어야 한다.");

                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) shadowed++;
            }

            Assert.Greater(shadowed, 0, "그림자를 던지는 부위가 하나도 없다.");
        }

        [Test]
        public void ThirdPerson_ShowsOwnDoll()
        {
            var player = MakePlayer();
            Attach(player, false);

            Assert.IsTrue(player.Doll.Visible, "3인칭이면 제 몸이 보여야 한다.");

            var drawn = 0;
            foreach (var renderer in Renderers(player))
                if (renderer.enabled && renderer.shadowCastingMode == ShadowCastingMode.On) drawn++;

            Assert.Greater(drawn, 0, "3인칭인데 그려지는 부위가 하나도 없다.");
        }

        /// <summary>
        /// 원격 플레이어는 <see cref="PlayerRig.Cam"/> 이 null 이다. 1인칭 숨김이
        /// 남의 캐릭터까지 지워 버리면 안 된다.
        /// </summary>
        [Test]
        public void RemotePlayer_StaysVisible()
        {
            var player = MakePlayer();

            Assert.IsNull(player.Cam, "이 검증은 카메라가 없는 리그에서 해야 의미가 있다.");
            Assert.IsTrue(player.Doll.Visible, "카메라가 없는 리그는 그대로 보여야 한다.");
        }

        static Renderer[] Renderers(PlayerRig player)
        {
            var renderers = player.Doll.Root.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "인형에 렌더러가 있어야 검증이 성립한다.");
            return renderers;
        }
    }
}
