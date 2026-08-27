using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// GameLayers 상수와 ProjectSettings 의 실제 물리 설정이 일치하는지 검증한다.
    /// 누군가 Project Settings 를 되돌리거나 레이어 순서를 바꾸면 여기서 실패한다.
    /// </summary>
    public sealed class GameLayersTests
    {
        [Test]
        public void LayerConstants_MatchTagManager()
        {
            foreach (var (index, name) in GameLayers.All)
            {
                Assert.AreEqual(index, LayerMask.NameToLayer(name),
                    $"Layer '{name}' 의 인덱스가 GameLayers 상수와 다릅니다. " +
                    "Project Settings > Tags and Layers 를 확인하세요.");
            }
        }

        [Test]
        public void LayerIndices_AreUnique()
        {
            CollectionAssert.AllItemsAreUnique(System.Array.ConvertAll(GameLayers.All, e => e.Index));
        }

        [Test]
        public void CarriedPart_CollidesWithStandingPlayer()
        {
            // 견인 중인 파츠로 동료를 툭 치며 방해할 수 있어야 한다.
            // 압사 방지는 레이어가 아니라 PhysicsScale.CarriedPartMass 로 처리한다.
            AssertCollides(GameLayers.PartCarried, GameLayers.Player);
        }

        [Test]
        public void CarriedPart_IgnoresDownedPlayer()
        {
            // 기절한 플레이어는 스스로 빠져나올 수 없다.
            // 파츠에 눌려 구조가 불가능해지는 상황을 막는다.
            AssertIgnored(GameLayers.PartCarried, GameLayers.PlayerDowned);
        }

        [Test]
        public void CarriedPartMass_IsFarLighterThanPlacedPart()
        {
            Assert.LessOrEqual(PhysicsScale.CarriedPartMass, PhysicsScale.PlayerMass,
                "견인 중 파츠는 플레이어보다 무거우면 안 된다. 밀칠 수는 있되 깔아뭉갤 수 없어야 한다.");
            Assert.Less(PhysicsScale.CarriedPartMass, PhysicsScale.PartMass * 0.1f,
                "견인 중 질량은 놓인 상태보다 확연히 가벼워야 한다.");
        }

        [Test]
        public void DownedPlayer_IgnoresOtherPlayers()
        {
            // 기절한 동료를 구조할 때 캡슐끼리 끼는 상황을 방지한다.
            AssertIgnored(GameLayers.PlayerDowned, GameLayers.Player);
            AssertIgnored(GameLayers.PlayerDowned, GameLayers.PlayerDowned);
        }

        [Test]
        public void Meteor_DoesNotCollideWithOtherMeteors()
        {
            AssertIgnored(GameLayers.Meteor, GameLayers.Meteor);
        }

        [Test]
        public void QueryOnlyLayers_HaveNoCollisionsAtAll()
        {
            // Interactable / BeamQuery 는 물리 충돌 없이 레이어마스크 쿼리로만 쓴다.
            for (var other = 0; other < 32; other++)
            {
                AssertIgnored(GameLayers.Interactable, other);
                AssertIgnored(GameLayers.BeamQuery, other);
            }
        }

        [Test]
        public void GameplayCollisions_RemainEnabled()
        {
            // 매트릭스를 과하게 꺼서 게임이 성립하지 않게 되는 회귀를 막는다.
            AssertCollides(GameLayers.Player, GameLayers.Ground);
            AssertCollides(GameLayers.Player, GameLayers.Part);
            AssertCollides(GameLayers.Player, GameLayers.PartCarried);
            AssertCollides(GameLayers.Part, GameLayers.Ground);
            AssertCollides(GameLayers.Part, GameLayers.PartCarried);
            AssertCollides(GameLayers.Meteor, GameLayers.Player);
            AssertCollides(GameLayers.Meteor, GameLayers.Part);
        }

        [Test]
        public void GlobalPhysicsSettings_MatchRecordedBaseline()
        {
            Assert.AreEqual(PhysicsScale.Gravity, UnityEngine.Physics.gravity.y, 0.001f);
            Assert.AreEqual(PhysicsScale.FixedTimestep, Time.fixedDeltaTime, 0.0005f);
            Assert.AreEqual(PhysicsScale.SolverIterations, UnityEngine.Physics.defaultSolverIterations);
            Assert.AreEqual(PhysicsScale.SolverVelocityIterations, UnityEngine.Physics.defaultSolverVelocityIterations);
        }

        static void AssertIgnored(int a, int b) =>
            Assert.IsTrue(UnityEngine.Physics.GetIgnoreLayerCollision(a, b),
                $"{Name(a)} x {Name(b)} 는 충돌이 꺼져 있어야 합니다.");

        static void AssertCollides(int a, int b) =>
            Assert.IsFalse(UnityEngine.Physics.GetIgnoreLayerCollision(a, b),
                $"{Name(a)} x {Name(b)} 는 충돌이 켜져 있어야 합니다.");

        static string Name(int layer)
        {
            var n = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(n) ? $"Layer{layer}" : n;
        }
    }
}
