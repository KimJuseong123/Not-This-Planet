using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 지면 판정을 씬 현실과 맞춘 것을 검증한다(S15P21B202-254).
    ///
    /// <b>저장소에 지면 정의가 둘 있었고 서로 달랐다.</b> 플레이어 접지는
    /// <see cref="RagdollBuilder.WorldMask"/>(Default 포함)를 썼고, 낙하 예고는 스폰 영역
    /// transform 의 y 를, 끼임 탈출은 <c>GroundMask</c>(Ground 레이어만)를 썼다.
    ///
    /// 씬의 지면(<c>ENV_Ground_Arena_50m</c>)은 <b>레이어 0(Default)</b> 이고 Ground(8)
    /// 레이어 오브젝트가 <b>씬에 하나도 없다</b>. 그래서 예고 원은 바닥에 묻히고 끼임
    /// 탈출은 조용히 죽어 있었다. 이 스위트가 그 둘을 잠근다.
    /// </summary>
    public sealed class GroundSurfaceTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
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
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 마스크 ---------------------------------------------------------

        /// <summary>
        /// 씬의 지면이 Default 레이어라는 사실이 이 마스크의 존재 이유다.
        /// </summary>
        [Test]
        public void TheSurfaceMaskIncludesTheDefaultLayer()
        {
            Assert.AreNotEqual(0, GameLayers.SurfaceMask & (1 << 0),
                "씬의 지면(ENV_Ground_Arena_50m)이 Default 레이어에 있다.");
            Assert.AreNotEqual(0, GameLayers.SurfaceMask & (1 << GameLayers.Ground),
                "Ground 레이어도 여전히 지면이다.");
        }

        [Test]
        public void TheSurfaceMaskExcludesWhatYouShouldNotStandOn()
        {
            var mask = GameLayers.SurfaceMask;

            Assert.AreEqual(0, mask & (1 << GameLayers.Player), "사람을 밟고 서지 않는다.");
            Assert.AreEqual(0, mask & (1 << GameLayers.PlayerDowned));
            Assert.AreEqual(0, mask & (1 << GameLayers.Interactable), "질의 전용 레이어다.");
            Assert.AreEqual(0, mask & (1 << GameLayers.BeamQuery));
            Assert.AreEqual(0, mask & (1 << GameLayers.Meteor),
                "이미 박힌 운석 위에 예고를 다시 그리지 않는다(§19).");
            Assert.AreEqual(0, mask & (1 << GameLayers.PartCarried),
                "견인 중인 파츠는 곧 자리를 뜬다.");
        }

        // --- 낙하 예고가 지면 위에 온다 --------------------------------------

        /// <summary>
        /// 예전에는 스폰 영역 transform 의 y(=0)를 그대로 썼다. 씬의 바닥은 0.06 이라
        /// 예고 원(띄움 0.03)이 <b>바닥에 묻혔다.</b>
        /// </summary>
        [Test]
        public void TheImpactPointLandsOnTheActualGround()
        {
            MakeGround(height: 0.06f, layer: 0);   // 씬과 같은 조건
            var spawner = MakeSpawner();

            var drop = AdvanceUntilPending(spawner);

            Assert.AreEqual(0.06f, drop.ImpactPoint.y, 0.02f,
                "영역 y(0)가 아니라 지면 높이를 받아야 한다.");
        }

        [Test]
        public void TheWarningDiscSitsAboveTheGround()
        {
            MakeGround(height: 0.06f, layer: 0);
            var spawner = MakeSpawner();

            var drop = AdvanceUntilPending(spawner);
            var discY = drop.ImpactPoint.y + PhysicsScale.MeteorWarningLift;

            Assert.Greater(discY, 0.06f, "원판이 바닥보다 위에 있어야 보인다.");
        }

        /// <summary>지면 높이가 바뀌면 따라와야 한다 — 상수로 맞추지 않는다.</summary>
        [Test]
        public void ItFollowsWhereverTheGroundIs()
        {
            MakeGround(height: 12.5f, layer: 0);
            var spawner = MakeSpawner();

            var drop = AdvanceUntilPending(spawner);

            Assert.AreEqual(12.5f, drop.ImpactPoint.y, 0.05f);
        }

        /// <summary>
        /// 지면을 못 찾으면 받은 값을 그대로 쓴다. 예고를 빼면 §17.4 의 공세 개수 표가
        /// 어긋나고, 운석은 어차피 물리로 떨어져 무엇에든 부딪힌다.
        /// </summary>
        [Test]
        public void WithNoGroundAtAll_TheWaveStillRuns()
        {
            var spawner = MakeSpawner();   // 지면을 안 만든다

            var drop = AdvanceUntilPending(spawner);

            Assert.AreEqual(0f, drop.ImpactPoint.y, 0.01f, "영역 높이로 되돌아간다.");
        }

        [Test]
        public void APlayerDoesNotBecomeTheGround()
        {
            MakeGround(height: 0f, layer: 0);
            MakePlayer(Vector3.zero);
            var spawner = MakeSpawner();

            var drop = AdvanceUntilPending(spawner);

            Assert.Less(drop.ImpactPoint.y, 0.5f,
                "사람 머리 위에 착탄 지점을 잡으면 안 된다.");
        }

        // --- 분화구가 지면 위에 온다 -----------------------------------------

        /// <summary>
        /// <see cref="Meteor.Settle"/> 이 몸통을 지면 아래로 내리므로
        /// <c>transform.position</c> 에 분화구를 그리면 묻힌다.
        /// </summary>
        [Test]
        public void TheCraterUsesTheImpactPointNotTheBuriedBody()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));

            meteor.ApplyImpact(new Vector3(0f, 0.06f, 0f));

            Assert.AreEqual(0.06f, meteor.ImpactPoint.y, 1e-3f);
            Assert.Less(meteor.transform.position.y, meteor.ImpactPoint.y,
                "몸통은 지면 아래로 박힌다(§19).");
        }

        [Test]
        public void TheSpawnerRemembersTheImpactPointForTheScar()
        {
            var spawner = MakeSpawner();
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            Attach(spawner, meteor);

            meteor.ApplyImpact(new Vector3(0f, 0.06f, 0f));
            spawner.Tick(Step);

            Assert.AreEqual(1, spawner.Scars.Count);
            Assert.AreEqual(0.06f, spawner.Scars[0].Point.y, 1e-3f,
                "박힌 몸통 위치를 쓰면 분화구가 묻힌다.");
        }

        // --- 끼임 탈출이 씬 지면에서 동작한다 --------------------------------

        /// <summary>
        /// <b>이것이 조용히 죽어 있었다.</b> <c>GroundMask</c> 는 Ground(8) 레이어만 보는데
        /// 씬에 그 레이어가 하나도 없다.
        /// </summary>
        [Test]
        public void SafeGroundFindsADefaultLayerFloor()
        {
            MakeGround(height: 0f, layer: 0);
            UnityEngine.Physics.SyncTransforms();

            Assert.IsTrue(SafeGround.TryFind(Vector3.zero, out var foot),
                "Default 레이어 바닥에서도 찾아야 한다.");
            Assert.AreEqual(0f, foot.y, 0.05f);
        }

        [Test]
        public void SafeGroundStillFindsAGroundLayerFloor()
        {
            MakeGround(height: 0f, layer: GameLayers.Ground);
            UnityEngine.Physics.SyncTransforms();

            Assert.IsTrue(SafeGround.TryFind(Vector3.zero, out _),
                "예전 검증이 만들던 조건도 그대로 동작해야 한다.");
        }

        /// <summary>
        /// 예전 <c>IsClear</c> 는 <c>~GroundMask</c> 로 겹침을 봤다. 씬의 바닥은 Default 라
        /// 마스크에서 빠지지 않아 <b>바닥이 곧 장애물</b>이 됐다.
        /// </summary>
        [Test]
        public void TheFloorUnderYourFeetIsNotAnObstacle()
        {
            MakeGround(height: 0f, layer: 0);
            UnityEngine.Physics.SyncTransforms();

            Assert.IsTrue(SafeGround.IsClear(Vector3.zero),
                "발이 지면에 닿아 있는 것은 정상이다.");
        }

        [Test]
        public void AnObstacleAtBodyHeightStillBlocks()
        {
            MakeGround(height: 0f, layer: 0);

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "장애물";
            box.layer = GameLayers.Prop;
            box.transform.position = new Vector3(0f, 0.6f, 0f);
            _spawned.Add(box);
            UnityEngine.Physics.SyncTransforms();

            Assert.IsFalse(SafeGround.IsClear(Vector3.zero));
            Assert.IsTrue(SafeGround.IsClear(new Vector3(6f, 0f, 0f)));
        }

        /// <summary>
        /// 끼임 탈출이 실제로 옮겨야 한다 — 씬과 같은 Default 레이어 바닥에서.
        /// </summary>
        [Test]
        public void EscapingWorksOnADefaultLayerFloor()
        {
            MakeGround(height: 0f, layer: 0);
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);

            var before = player.FootPosition;
            var steps = Mathf.CeilToInt((PhysicsScale.StuckEscapeSeconds + 0.05f) / 0.02f);
            for (var i = 0; i < steps; i++) escape.Tick(0.02f, true);

            Assert.AreEqual(1, escape.EscapeCount, "예전에는 영영 0 이었다.");
            Assert.IsFalse(escape.WaitingForRoom);
            Assert.Greater(Vector3.Distance(before, player.FootPosition), 0.5f);
        }

        // --- 리그 -----------------------------------------------------------

        static PendingMeteorDrop AdvanceUntilPending(MeteorSpawner spawner)
        {
            for (var i = 0; i < 200000; i++)
            {
                spawner.Tick(Step);
                if (spawner.Pending.Count > 0) return spawner.Pending[0];
            }

            Assert.Fail("예고가 뜨지 않았다.");
            return default;
        }

        /// <summary>
        /// 스포너의 <c>_live</c> 는 비공개다. 이 스위트가 보는 것은 착탄 뒤이므로
        /// 실제 경로를 돌리지 않고 리플렉션으로 넣는다.
        /// </summary>
        static void Attach(MeteorSpawner spawner, Meteor meteor)
        {
            var field = typeof(MeteorSpawner).GetField(
                "_live", System.Reflection.BindingFlags.Instance
                         | System.Reflection.BindingFlags.NonPublic);

            ((List<Meteor>)field.GetValue(spawner)).Add(meteor);
        }

        void MakeGround(float height, int layer)
        {
            var go = new GameObject("Ground") { layer = layer };
            go.transform.position = new Vector3(0f, height - 0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(400f, 1f, 400f);
            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
        }

        MeteorSpawner MakeSpawner()
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(20260902);
            return spawner;
        }

        Meteor MakeMeteor(Vector3 position)
        {
            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = position;
            _spawned.Add(go);

            go.AddComponent<SphereCollider>().radius = PhysicsScale.MeteorRadius;

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetGroundHeight(0f);

            UnityEngine.Physics.SyncTransforms();
            return meteor;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
