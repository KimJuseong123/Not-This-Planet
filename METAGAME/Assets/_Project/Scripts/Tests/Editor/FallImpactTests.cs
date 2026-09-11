using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 낙하가 <b>피해가 아니라 자세</b>로 처리되는지 검증한다(기획 §20.2,
    /// S15P21B202-334).
    ///
    /// <code>
    /// 낮음        정상 착지
    /// 중간        잠시 휘청인다
    /// 높음        넘어지고 1~2초 뒤 스스로 일어난다
    /// 매우 높음   + <b>머리부터</b> 충돌이면 기절할 수 있다
    /// </code>
    ///
    /// <b>예전에는 2.75m·3.8m/s 를 넘으면 체력이 깎였고, 누적되면 기절까지 갔다.</b>
    /// §20.1 은 "체력, 사망과 영구 탈락은 없다" 이고 §20.2 는 기절 조건을 "매우 높은
    /// 곳에서 머리부터" 하나로 못박는다.
    ///
    /// 저중력에서 이 차이가 크다. 우주선 4층 접합부가 5.71m 에 있어 공중 용접(§14.3)을
    /// 하다 저중력장을 벗어나면 그 높이에서 떨어지는데, 예전 규칙에서는 그것이 상시
    /// 체력 소모였다.
    ///
    /// <b>낙하 판정을 직접 부른다.</b> 실제 낙하는 <c>ProbeGround</c> 가 접지 전이를
    /// 감지해야 하고 그것은 물리 스텝과 지면이 필요해 EditMode 에서 재현이 불안정하다.
    /// 단계 판정 자체는 순수 함수에 가까우므로 그것을 잠근다.
    /// </summary>
    public sealed class FallImpactTests
    {
        readonly List<GameObject> _spawned = new();
        bool _puffWas;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();

            _puffWas = Puff.Enabled;
            Puff.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWas;

            // 기절시킨 인형을 되살려 놓고 지운다. GoDown 은 인형의 레이어를
            // PlayerDowned 로 바꾸고 구조·운반 상호작용 대상을 켜므로, 그 상태로
            // 파괴하면 전역 등록이 남아 뒤에 도는 스위트에 새어 나간다.
            foreach (var player in PlayerRig.All)
                if (player != null && player.Downed)
                    player.Revive(player.MaxHealth);

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            // 씬에 남은 인형까지 치운다. PlayerRig.Create 가 만드는 것이 이 오브젝트
            // 하나가 아니어서, 목록만 비우면 뒤에 도는 스위트의 물리 질의에 남는다
            // (MeteorSpawnerTests 가 운석을 이렇게 정리하는 것과 같은 방식이다).
            foreach (var rig in Object.FindObjectsByType<PlayerRig>(FindObjectsSortMode.None))
                if (rig != null)
                    Object.DestroyImmediate(rig.gameObject);

            _spawned.Clear();
            PlayerRig.All.Clear();
        }

        // --- 경계값이 기획과 맞는가 ------------------------------------------

        /// <summary>
        /// <b>점프 최고점에서 떨어져도 정상 착지여야 한다.</b> §20.2 첫 줄이 "낮은
        /// 높이에서는 정상 착지한다" 이고, 저중력 게임에서 점프는 기본 조작이다.
        ///
        /// 예전 값(2.75m)은 점프 최고점(2.06m)보다 높아 평지 점프는 무사했지만, 조금
        /// 높은 지형에서 점프하면 넘었다.
        /// </summary>
        [Test]
        public void JumpApexIsBelowTheStaggerThreshold()
        {
            var apex = Tuning.JumpSpeed * Tuning.JumpSpeed
                       / (2f * Mathf.Abs(UnityEngine.Physics.gravity.y));

            Assert.AreEqual(2.06f, apex, 0.05f, "점프 최고점 v²/2g");
            Assert.Greater(PhysicsScale.FallStaggerHeight, apex,
                "점프 최고점에서 떨어지면 휘청이면 안 된다(§20.2 정상 착지).");
        }

        /// <summary>
        /// <b>우주선 4층(5.71m)은 넘어짐 구간이어야 한다.</b> 공중 용접 중 저중력장을
        /// 벗어나면 그 높이에서 떨어지는데, 기획이 요구하는 작업이 기절로 이어지면 안 된다.
        /// </summary>
        [Test]
        public void TheTopDeckHeightIsAKnockdownNotAStun()
        {
            const float TopDeck = 5.71f;

            Assert.Greater(TopDeck, PhysicsScale.FallKnockHeight, "넘어짐 구간에 든다.");
            Assert.Less(TopDeck, PhysicsScale.FallStunHeight, "기절 구간에는 들지 않는다.");
        }

        [Test]
        public void TheTiersAreOrdered()
        {
            Assert.Less(PhysicsScale.FallStaggerHeight, PhysicsScale.FallKnockHeight);
            Assert.Less(PhysicsScale.FallKnockHeight, PhysicsScale.FallStunHeight);
        }

        // --- 낮은 낙하는 아무 일도 없다 ---------------------------------------

        [Test]
        public void ALowFallDoesNothing()
        {
            var player = MakePlayer();

            Fall(player, PhysicsScale.FallStaggerHeight - 0.5f);

            Assert.IsFalse(player.Staggering, "낮은 높이에서는 정상 착지한다.");
            Assert.IsFalse(player.Downed);
            Assert.AreEqual(player.MaxHealth, player.Health, "낙하는 체력을 깎지 않는다.");
        }

        // --- 중간은 휘청인다 --------------------------------------------------

        [Test]
        public void AMidFallStaggers()
        {
            var player = MakePlayer();

            Fall(player, PhysicsScale.FallKnockHeight - 0.3f);

            Assert.IsTrue(player.Staggering, "중간 높이에서는 잠시 휘청인다.");
            Assert.IsFalse(player.Downed);
        }

        /// <summary>
        /// <b>휘청여도 조종을 잃지 않는다.</b> 그것이 넘어짐과 휘청임을 가르는 것이다.
        /// </summary>
        [Test]
        public void StaggeringKeepsControl()
        {
            var player = MakePlayer();

            Fall(player, PhysicsScale.FallKnockHeight - 0.3f);

            Assert.IsFalse(player.Ragdolled, "휘청임은 자빠짐이 아니다.");
        }

        [Test]
        public void StaggeringWearsOff()
        {
            var player = MakePlayer();
            Fall(player, PhysicsScale.FallKnockHeight - 0.3f);

            Advance(player, PhysicsScale.FallStaggerSeconds + 0.1f);

            Assert.IsFalse(player.Staggering, "잠시라고 했으므로 풀려야 한다.");
        }

        // --- 높으면 넘어진다 --------------------------------------------------

        [Test]
        public void AHighFallKnocksDownWithoutDamage()
        {
            var player = MakePlayer();

            Fall(player, PhysicsScale.FallKnockHeight + 1f);

            Assert.IsFalse(player.Downed, "넘어지는 것은 기절이 아니다(§20.2).");
            Assert.AreEqual(player.MaxHealth, player.Health, "체력을 깎지 않는다.");
        }

        // --- 매우 높아도 머리부터가 아니면 기절하지 않는다 ---------------------

        /// <summary>
        /// §20.2 "매우 높은 곳에서 <b>머리부터</b> 충돌하면 기절할 수 있다" 와
        /// "일반 몸통 충돌만으로 기절하지 않는다".
        /// </summary>
        [Test]
        public void AVeryHighFallOnTheFeetDoesNotStun()
        {
            var player = MakePlayer();
            StandUpright(player);

            Fall(player, PhysicsScale.FallStunHeight + 5f);

            Assert.IsFalse(player.Downed,
                "머리부터가 아니면 아무리 높아도 넘어짐이다.");
        }

        [Test]
        public void AVeryHighFallOnTheHeadStuns()
        {
            var player = MakePlayer();
            PutHeadBelowHips(player);

            Fall(player, PhysicsScale.FallStunHeight + 1f);

            Assert.IsTrue(player.Downed, "매우 높은 곳에서 머리부터면 기절한다.");
        }

        /// <summary>기절 구간 아래에서는 머리부터여도 넘어짐이다.</summary>
        [Test]
        public void HeadFirstBelowTheStunHeightOnlyKnocksDown()
        {
            var player = MakePlayer();
            PutHeadBelowHips(player);

            Fall(player, PhysicsScale.FallStunHeight - 1f);

            Assert.IsFalse(player.Downed);
        }

        // --- 기절 중에는 다시 판정하지 않는다 ----------------------------------

        [Test]
        public void AlreadyDownedIsNotStaggered()
        {
            var player = MakePlayer();
            player.GoDown("검증");

            Fall(player, PhysicsScale.FallKnockHeight - 0.3f);

            Assert.IsFalse(player.Staggering, "기절한 사람을 휘청이게 하지 않는다.");
        }

        // --- 체력은 운석 쪽에서 그대로 동작한다 --------------------------------

        /// <summary>
        /// 이 이슈는 <b>낙하만</b> 체력에서 떼어낸다. §20.1 의 체력 제거는 별건이므로
        /// 다른 피해원은 그대로 동작해야 한다.
        /// </summary>
        [Test]
        public void OtherDamageStillWorks()
        {
            var player = MakePlayer();

            player.ApplyDamage(12f, "운석");

            Assert.Less(player.Health, player.MaxHealth, "운석 피해는 그대로다.");
        }

        // --- 리그 -------------------------------------------------------------

        /// <summary>
        /// 단계 판정만 부른다. 접지 전이를 물리로 재현하지 않는 이유는 클래스 주석에 있다.
        /// </summary>
        static void Fall(PlayerRig player, float height)
        {
            var method = typeof(PlayerRig).GetMethod(
                "ApplyFallImpact", System.Reflection.BindingFlags.Instance
                                   | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(method, "ApplyFallImpact 가 있어야 한다.");
            method.Invoke(player, new object[] { height });
        }

        static void Advance(PlayerRig player, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Tuning.FixedStep);
            for (var i = 0; i < steps; i++) player.Tick();
        }

        static void StandUpright(PlayerRig player)
        {
            player.Doll.Head.transform.position =
                player.Doll.Hips.position + Vector3.up * 0.6f;
            UnityEngine.Physics.SyncTransforms();
        }

        static void PutHeadBelowHips(PlayerRig player)
        {
            player.Doll.Head.transform.position =
                player.Doll.Hips.position - Vector3.up * 0.3f;
            UnityEngine.Physics.SyncTransforms();
        }

        PlayerRig MakePlayer()
        {
            var player = PlayerRig.Create("테스트", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);

            StandUpright(player);
            return player;
        }
    }
}
