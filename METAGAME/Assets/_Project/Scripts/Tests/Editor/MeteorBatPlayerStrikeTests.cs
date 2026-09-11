using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 방망이로 동료를 때리는 것을 검증한다(기획 §18.3, S15P21B202-257).
    ///
    /// §18.3 이 허용과 금지를 함께 정한다.
    ///
    /// <code>
    /// 허용  방망이로 다른 플레이어를 직접 때릴 수 있다
    ///       약하게 스치면 휘청이고 정면으로 맞으면 넘어진다
    /// 금지  기절하지는 않는다
    /// 금지  장비를 떨어뜨리게 하지는 않는다
    /// </code>
    ///
    /// <b>인게임에서 확인된 적이 없다.</b> 솔로 플레이에서는 확인할 수 없다 —
    /// <c>FindPlayer</c> 가 <see cref="PlayerRig.All"/> 을 훑고 자기 자신을 뺀다.
    /// 그래서 EditMode 로 잠근다.
    ///
    /// 금지 쪽이 특히 중요하다. S15P21B202-247 이 <see cref="PlayerRig.DropEquipment"/> 를
    /// 공개했고 그것이 <see cref="PlayerRig.KnockDown"/> 바로 옆에 있다 — 나중에 딸려
    /// 들어가기 쉬운 자리다.
    /// </summary>
    public sealed class MeteorBatPlayerStrikeTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
            PlayerRig.All.Clear();

            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            Puff.Enabled = _puffWasEnabled;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 허용 (§18.3) --------------------------------------------------

        [Test]
        public void ATeammateInFront_GetsKnockedOver()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));
            var bat = MakeBat();

            var hit = bat.Swing(Vector3.zero, Vector3.right);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsTrue(hit);
            Assert.AreSame(target, bat.LastStruck);
            Assert.IsTrue(target.Ragdolled, "정면으로 맞으면 넘어진다.");
        }

        [Test]
        public void ATeammateBehindYou_IsNotStruck()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(-1f, 0f, 0f));
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);

            Assert.IsNull(bat.LastStruck);
            Assert.IsFalse(target.Ragdolled);
        }

        [Test]
        public void ATeammateOutOfReach_IsNotStruck()
        {
            MakeGround();
            MakePlayer(new Vector3(PhysicsScale.BatStrikeReach + 1.5f, 0f, 0f));
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right));
            Assert.IsNull(bat.LastStruck);
        }

        // --- 금지 (§18.3) --------------------------------------------------

        /// <summary>§18.3 "넘어지지만 <b>기절하지는 않는다</b>".</summary>
        [Test]
        public void BeingStruck_DoesNotKnockYouOut()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(target.Downed, "방망이는 기절시키지 않는다.");
            Assert.Greater(target.Health, 0f);
        }

        /// <summary>
        /// §18.3 "<b>장비를 떨어뜨리게</b> 하거나 … 하지는 않는다".
        ///
        /// <see cref="PlayerRig.DropEquipment"/> 가 <see cref="PlayerRig.KnockDown"/> 바로
        /// 옆에 있으므로, 타격 경로에서 실수로 불리는 것을 여기서 막는다.
        /// </summary>
        [Test]
        public void BeingStruck_DoesNotMakeYouDropYourTool()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));
            var tool = MakeTool(new Vector3(1.1f, 0.6f, 0f));

            var bat = MakeBat();
            bat.Swing(Vector3.zero, Vector3.right);
            UnityEngine.Physics.Simulate(Step);

            Assert.AreSame(target, bat.LastStruck, "맞았어야 의미가 있다.");
            Assert.IsTrue(tool != null, "도구가 사라지지 않는다.");
            Assert.IsFalse(target.Downed);
        }

        /// <summary>맞은 사람이 반복해서 맞아도 기절로 넘어가지 않는다.</summary>
        [Test]
        public void BeingStruckRepeatedly_StillDoesNotKnockYouOut()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));

            for (var i = 0; i < 10; i++)
            {
                var bat = MakeBat();
                bat.Swing(Vector3.zero, Vector3.right);
                UnityEngine.Physics.Simulate(Step);
            }

            Assert.IsFalse(target.Downed, "§18.3 은 기절을 금지한다.");
        }

        [Test]
        public void YouCannotStrikeYourself()
        {
            MakeGround();
            var self = MakePlayer(Vector3.zero);
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);

            Assert.AreNotSame(self, bat.LastStruck);
        }

        // --- 우선순위 ------------------------------------------------------

        [Test]
        public void AMeteorWinsOverATeammate()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));
            var meteor = MakeMeteorAtWindow();
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);

            Assert.AreSame(meteor, bat.LastDeflected);
            Assert.IsNull(bat.LastStruck);
            Assert.IsFalse(target.Ragdolled);
        }

        [Test]
        public void ATeammateWinsOverAProp()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));

            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.name = "화물 상자";
            prop.layer = GameLayers.Prop;
            prop.transform.position = new Vector3(1.2f, 0.5f, 0f);
            _spawned.Add(prop);
            var body = prop.AddComponent<Rigidbody>();
            body.mass = 20f;
            body.useGravity = false;
            UnityEngine.Physics.SyncTransforms();

            var bat = MakeBat();
            bat.Swing(Vector3.zero, Vector3.right);

            Assert.AreSame(target, bat.LastStruck);
            Assert.IsNull(bat.LastPushed);
        }

        // --- 리그 ---------------------------------------------------------

        MeteorBat MakeBat()
        {
            var go = new GameObject("MeteorBat") { layer = GameLayers.Prop };
            _spawned.Add(go);
            return go.AddComponent<MeteorBat>();
        }

        GameObject MakeTool(Vector3 position)
        {
            var go = new GameObject("PF_Stage1_WeldingPlasmaGun") { layer = GameLayers.Prop };
            go.transform.position = position;
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = 4f;
            body.useGravity = false;
            go.AddComponent<PlasmaWeldingTool>();
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();
            return go;
        }

        Meteor MakeMeteorAtWindow()
        {
            var g = -UnityEngine.Physics.gravity.y;
            var t = PhysicsScale.BatSwingWindowSeconds * 0.5f;

            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = new Vector3(0f, 0.5f * g * t * t, 0f);
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetGroundHeight(0f);

            UnityEngine.Physics.SyncTransforms();
            return meteor;
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(60f, 1f, 60f);
            _spawned.Add(go);
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
