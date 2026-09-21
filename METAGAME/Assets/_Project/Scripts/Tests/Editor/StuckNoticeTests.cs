using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metagame.Core;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// "끼어 보인다" 판정을 지킨다 (S15P21B202-669).
    ///
    /// <para><c>StuckEscape</c> 에는 <b>"지금 끼었는가" 를 판단하는 것이 없었다</b> —
    /// T 를 누르고 있어야만 진행한다. 그래서 끼인 사람은 T 의 존재를 모른 채 계속
    /// 낀 상태로 있었다.</para>
    ///
    /// <para><b>제자리가 정상인 구간을 빼는 것이 이 판정의 본체다.</b> 파츠를 밀 때와
    /// 고정못을 당길 때는 원래 거의 안 움직인다(S15P21B202-667 에서 쟀다). 거기서
    /// "끼었습니다" 가 뜨면 <b>게임이 제 기능을 오작동으로 알리는 것</b>이 된다.</para>
    /// </summary>
    public sealed class StuckNoticeTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            UnityEngine.Physics.SyncTransforms();
            PlayerRig.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        PlayerRig NewPlayer()
        {
            var player = PlayerRig.Create("끼임 검증", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            return player;
        }

        static void Set(PlayerRig player, string name, object value)
        {
            var property = typeof(PlayerRig).GetProperty(name);
            if (property != null && property.CanWrite) { property.SetValue(player, value); return; }
            property = typeof(PlayerRig).GetProperty(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            property.GetSetMethod(true).Invoke(player, new[] { value });
        }

        /// <summary>가고 싶은데 못 가는 상태를 만든다.</summary>
        static void WantsToMoveButCannot(PlayerRig player)
        {
            Set(player, "MoveWish", Vector3.right);
            Set(player, "Grounded", true);
            Set(player, "Speed", 0f);
        }

        static bool StuckAfter(PlayerRig player, float seconds)
        {
            var escape = new StuckEscape(player);
            var step = 0.02f;
            for (var t = 0f; t < seconds; t += step) escape.Tick(step, false);
            return escape.LooksStuck;
        }

        // --- 끼면 알려 준다 -------------------------------------------------------------

        [Test]
        public void StandingStillWhileTryingToWalkLooksStuck()
        {
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            Assert.That(StuckAfter(player, PhysicsScale.StuckNoticeSeconds + 0.2f), Is.True,
                "가려는데 안 가는 상태가 이어졌는데 안 알려 준다.");
        }

        [Test]
        public void ItWaitsBeforeSayingAnything()
        {
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            Assert.That(StuckAfter(player, PhysicsScale.StuckNoticeSeconds - 0.3f), Is.False,
                "문틈에 잠깐 걸린 것에도 뜬다.");
        }

        [Test]
        public void ItTellsYouSoonerThanTheEscapeItselfTakes()
        {
            // 3초를 눌러야 빠져나온다. 알려 주는 것이 그보다 늦으면 뜻이 없다.
            Assert.That(PhysicsScale.StuckNoticeSeconds,
                Is.LessThan(PhysicsScale.StuckEscapeSeconds));
        }

        [Test]
        public void NotMovingOnPurposeIsNotStuck()
        {
            var player = NewPlayer();
            Set(player, "Grounded", true);
            Set(player, "Speed", 0f);
            Set(player, "MoveWish", Vector3.zero);   // 갈 생각이 없다
            Assert.That(StuckAfter(player, 5f), Is.False, "가만히 서 있는데 끼었다고 한다.");
        }

        // --- 제자리가 정상인 구간에서는 안 뜬다 ------------------------------------------

        [Test]
        public void PushingAPartIsNotStuck()
        {
            // 파츠를 미는 사람은 0.73m/s 로 끌려간다 — 느린 것이 정상이다.
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            player.SetHaulState(true, Vector3.right, Vector3.zero);
            Assert.That(StuckAfter(player, 5f), Is.False,
                "파츠를 미는데 끼었다고 한다 — 제 기능을 오작동으로 알리는 것이다.");
        }

        [Test]
        public void PullingAClampIsNotStuck()
        {
            // 고정못을 당기면 손이 파츠에 묶여 제자리다. 기획 §12.1 의 "발을 구르며
            // 낑낑대는" 그림이 그것이다.
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            player.ClampGrabbedForTests = true;
            Assert.That(StuckAfter(player, 5f), Is.False, "고정못을 당기는데 끼었다고 한다.");
        }

        [Test]
        public void FallingIsNotStuck()
        {
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            Set(player, "Grounded", false);
            Assert.That(StuckAfter(player, 5f), Is.False, "떨어지는 중에 끼었다고 한다.");
        }

        [Test]
        public void CrawlingIsNotStuck()
        {
            // 엎드리면 0.9m/s 다. 느린 것이 그 자세의 값이다.
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            Set(player, "Prone", true);
            Assert.That(StuckAfter(player, 5f), Is.False, "기어가는데 끼었다고 한다.");
        }

        [Test]
        public void BeingDownedIsNotStuck()
        {
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            Set(player, "Downed", true);
            Assert.That(StuckAfter(player, 5f), Is.False, "기절했는데 끼었다고 한다.");
        }

        [Test]
        public void GettingFreeClearsTheNotice()
        {
            var player = NewPlayer();
            WantsToMoveButCannot(player);
            var escape = new StuckEscape(player);
            for (var t = 0f; t < PhysicsScale.StuckNoticeSeconds + 0.2f; t += 0.02f)
                escape.Tick(0.02f, false);
            Assert.That(escape.LooksStuck, Is.True);

            Set(player, "Speed", Tuning.WalkSpeed);   // 다시 걷는다
            escape.Tick(0.02f, false);
            Assert.That(escape.LooksStuck, Is.False, "빠져나왔는데 안내가 안 사라진다.");
        }
    }
}
