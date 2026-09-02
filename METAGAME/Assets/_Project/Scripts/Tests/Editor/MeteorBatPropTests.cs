using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 방망이로 소품을 밀치는 것을 검증한다(S15P21B202-245).
    ///
    /// <b>기획에 없는 기능이다.</b> §18.3 이 정한 것은 플레이어 타격이고 "굴러다니는
    /// 소품을 밀친다" 는 언급이 없다. 넣기로 정한 것이므로 <b>무엇을 빼는지</b>가 규칙의
    /// 전부이고, 이 스위트는 그 배제 목록을 잠근다.
    ///
    /// <b>빼는 목록은 기획이 금지한 것뿐이다</b>(S15P21B202-257). §18.3 의 마지막 줄이
    /// 금지의 전부다 — "장비를 떨어뜨리게 하거나 파츠와 용접부에 피해를 주지는 않는다".
    /// 245 에서 그 문장을 "내려놓은 도구를 못 때린다" 로 넓게 읽은 것이 틀렸고,
    /// 그 결과 인게임에서 <b>아무것도 안 맞았다</b> — 씬의 Prop 레이어 Rigidbody 가
    /// 용접기 · 방망이 자신 · 견인 운반체뿐이고 셋 다 제외 대상이었다.
    /// </summary>
    public sealed class MeteorBatPropTests
    {
        /// <summary>물리 한 걸음. 프로젝트 고정 시간 간격과 같다.</summary>
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            // 충격량은 <b>다음 물리 걸음</b>에 속도가 된다 — 대입이 아니다. 그래서
            // 직접 돌려야 밀린 것을 볼 수 있다(운석은 속도를 대입하므로 필요 없었다).
            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;

            Meteor.All.Clear();
            PlayerRig.All.Clear();
            PartSocket.All.Clear();
            WeldBead.All.Clear();
            PartAssemblyLink.All.Clear();

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
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
            PartSocket.All.Clear();
            WeldBead.All.Clear();
            PartAssemblyLink.All.Clear();
        }

        // --- 밀친다 -------------------------------------------------------

        [Test]
        public void APropInFront_IsPushed()
        {
            var prop = MakeProp(new Vector3(1f, 0.5f, 0f), mass: 20f);
            var bat = MakeBat();

            var hit = bat.Swing(Vector3.zero, Vector3.right);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsTrue(hit, "사거리 안 정면의 소품은 밀려야 한다.");
            Assert.AreSame(prop, bat.LastPushed);
            Assert.Greater(prop.linearVelocity.magnitude, 0.1f);
        }

        [Test]
        public void ThePushGoesTheSameWayAsADeflection()
        {
            var prop = MakeProp(new Vector3(1f, 0.5f, 0f), mass: 20f);
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);
            UnityEngine.Physics.Simulate(Step);

            var velocity = prop.linearVelocity;

            Assert.Greater(velocity.x, 0f, "조준한 쪽으로 가야 한다.");
            Assert.Greater(velocity.y, 0f, "운석과 같이 약간 위로 들려야 한다(기획 §18.2).");
            Assert.AreEqual(0f, velocity.z, 0.05f);
        }

        /// <summary>
        /// 속도가 아니라 <b>충격량</b>이다. 소품은 질량이 제각각인데 속도를 대입하면
        /// 무거운 상자와 가벼운 조각이 똑같이 날아간다.
        /// </summary>
        [Test]
        public void AHeavyProp_MovesLessThanALightOne()
        {
            // 한 장면에서 양쪽으로 한 번씩. 같은 물리 걸음을 받으므로 비교가 공정하다.
            var light = MakeProp(new Vector3(1f, 0.5f, 0f), mass: 5f);
            var heavy = MakeProp(new Vector3(-1f, 0.5f, 0f), mass: 80f);

            MakeBat().Swing(Vector3.zero, Vector3.right);
            MakeBat().Swing(Vector3.zero, Vector3.left);
            UnityEngine.Physics.Simulate(Step);

            var lightSpeed = light.linearVelocity.magnitude;
            var heavySpeed = heavy.linearVelocity.magnitude;

            Assert.Greater(heavySpeed, 0f, "무거운 것도 조금은 밀려야 한다.");
            Assert.Greater(lightSpeed, heavySpeed * 2f,
                $"가벼운 것({lightSpeed:0.0})이 무거운 것({heavySpeed:0.0})보다 훨씬 빨라야 한다.");
        }

        [Test]
        public void APropBehindYou_IsNotPushed()
        {
            var prop = MakeProp(new Vector3(-1f, 0.5f, 0f), mass: 20f);
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right));
            UnityEngine.Physics.Simulate(Step);
            Assert.AreEqual(0f, prop.linearVelocity.magnitude, 0.01f);
        }

        [Test]
        public void APropOutOfReach_IsNotPushed()
        {
            MakeProp(new Vector3(PhysicsScale.BatStrikeReach + 1f, 0.5f, 0f), mass: 20f);
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right));
        }

        [Test]
        public void AKinematicProp_IsNotPushed()
        {
            var prop = MakeProp(new Vector3(1f, 0.5f, 0f), mass: 20f);
            prop.isKinematic = true;
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right),
                "밀릴 수 없는 것을 맞혔다고 하면 안 된다.");
        }

        // --- 빼는 목록 -----------------------------------------------------

        /// <summary>기획 §18.3 "파츠와 용접부에 피해를 주지는 않는다".</summary>
        [Test]
        public void ASpaceshipPart_IsNotPushed()
        {
            var part = MakePart(new Vector3(1f, 0.5f, 0f));
            var body = part.GetComponent<Rigidbody>();
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right), "파츠는 대상이 아니다.");
            UnityEngine.Physics.Simulate(Step);
            Assert.IsNull(bat.LastPushed);
            Assert.AreEqual(0f, body.linearVelocity.magnitude, 0.01f);
        }

        /// <summary>
        /// 비드는 파츠의 <b>자식 콜라이더</b>다(S15P21B202-226). 부모에서 함께 걸려야
        /// 하고, 안 걸리면 용접부를 때리는 것이 된다 — §18.3 이 금지한다.
        /// </summary>
        [Test]
        public void AWeldBeadOnAPart_IsNotPushed()
        {
            var part = MakePart(new Vector3(1f, 0.5f, 0f));
            var bead = WeldBead.Attach(part);

            // 파츠 표면에 비드 하나를 쌓는다.
            var nozzle = part.transform.position + Vector3.up * 0.8f;
            for (var i = 0; i < 10; i++) WeldBead.TryGrowNear(nozzle, Time.fixedDeltaTime, out _);
            Assert.Greater(bead.BlobCount, 0, "비드가 있어야 의미가 있다.");

            UnityEngine.Physics.SyncTransforms();

            var bat = MakeBat();
            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right), "용접부는 대상이 아니다.");
        }

        /// <summary>
        /// 견인 조종 모듈은 §14.5 가 "마지막 우주선 파츠이기도 하다" 고 정하고,
        /// <b>사람이 타고 있을 수도 있다.</b> Prop 레이어에 있으므로 레이어로만
        /// 판정하면 이것이 날아간다.
        /// </summary>
        [Test]
        public void TheTransportVehicle_IsNotPushed()
        {
            var body = MakeVehicle(new Vector3(1f, 0.5f, 0f));
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right),
                "견인 조종 모듈은 파츠이며 사람이 타고 있을 수도 있다.");
            UnityEngine.Physics.Simulate(Step);
            Assert.AreEqual(0f, body.linearVelocity.magnitude, 0.01f);
        }

        /// <summary>
        /// <b>245 에서 이 검증이 반대였다.</b> §18.3 의 "장비를 떨어뜨리게" 는 맞은
        /// 플레이어가 들고 있는 것에 대한 규정이고, 바닥에 굴러다니는 용접기와는 무관하다.
        /// </summary>
        [Test]
        public void ADroppedTool_IsPushed()
        {
            var go = new GameObject("PF_Stage1_WeldingPlasmaGun") { layer = GameLayers.Prop };
            go.transform.position = new Vector3(1f, 0.5f, 0f);
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = 4f;
            body.useGravity = false;   // 떨어지는 것과 밀리는 것을 섞지 않는다
            go.AddComponent<PlasmaWeldingTool>();
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();

            var bat = MakeBat();
            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.right),
                "내려놓은 도구를 못 때릴 근거가 기획에 없다.");
            UnityEngine.Physics.Simulate(Step);

            Assert.AreSame(body, bat.LastPushed);
            Assert.Greater(body.linearVelocity.magnitude, 0.1f);
        }

        /// <summary>내려놓은 다른 방망이도 같다.</summary>
        [Test]
        public void ADroppedBat_IsPushed()
        {
            var go = new GameObject("PF_Stage1_MeteorBat") { layer = GameLayers.Prop };
            go.transform.position = new Vector3(1f, 0.5f, 0f);
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = 3f;
            body.useGravity = false;
            go.AddComponent<MeteorBat>();
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();

            var swinging = MakeBat();
            Assert.IsTrue(swinging.Swing(Vector3.zero, Vector3.right));
            Assert.AreSame(body, swinging.LastPushed);
        }

        /// <summary>
        /// 자기를 때리는 것은 어느 경로로도 일어나면 안 된다. 휴대 중에는 충돌이 꺼져
        /// 있지만(S15P21B202-237) 규칙으로도 막아 둔다.
        /// </summary>
        [Test]
        public void TheSwingingBatDoesNotHitItself()
        {
            var go = new GameObject("MeteorBat") { layer = GameLayers.Prop };
            go.transform.position = Vector3.zero;
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = 3f;
            body.useGravity = false;
            var bat = go.AddComponent<MeteorBat>();
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();

            bat.Swing(Vector3.zero, Vector3.right);

            Assert.AreNotSame(body, bat.LastPushed);
        }

        /// <summary>
        /// Prop 레이어 밖에 있는 움직이는 물체도 대상이다. 예전에는
        /// <c>PropMask</c> 하나만 봐서 찾지 못했다.
        /// </summary>
        [Test]
        public void AMovableObjectOutsideThePropLayer_IsPushed()
        {
            var prop = MakeProp(new Vector3(1f, 0.5f, 0f), mass: 20f);
            prop.gameObject.layer = 0;   // Default
            UnityEngine.Physics.SyncTransforms();

            var bat = MakeBat();

            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.right));
            Assert.AreSame(prop, bat.LastPushed);
        }

        /// <summary>
        /// Rigidbody 가 없는 자연물·지면은 밀릴 수 없다. 마스크에 걸려도 헛스윙이 되는
        /// 것이 맞다 — §25.10 이 자연물을 정적 배경으로 둔다.
        /// </summary>
        [Test]
        public void StaticSceneryIsAMiss()
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "자연물";
            rock.layer = 0;
            rock.transform.position = new Vector3(1f, 0.5f, 0f);
            _spawned.Add(rock);   // Rigidbody 를 붙이지 않는다
            UnityEngine.Physics.SyncTransforms();

            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right));
            Assert.IsNull(bat.LastPushed);
            Assert.AreEqual(PhysicsScale.BatMissCooldownSeconds, bat.Cooldown, 1e-3f);
        }

        // --- 마스크 -------------------------------------------------------

        [Test]
        public void TheStrikeMaskExcludesOnlyWhatItShould()
        {
            var mask = GameLayers.BatStrikeMask;

            Assert.AreEqual(0, mask & (1 << GameLayers.Part),
                "§18.3 파츠와 용접부.");
            Assert.AreEqual(0, mask & (1 << GameLayers.PartCarried));
            Assert.AreEqual(0, mask & (1 << GameLayers.Player), "동료 타격은 별 경로다.");
            Assert.AreEqual(0, mask & (1 << GameLayers.PlayerDowned));
            Assert.AreEqual(0, mask & (1 << GameLayers.Meteor), "운석 반사도 별 경로다.");
            Assert.AreEqual(0, mask & (1 << GameLayers.Interactable));
            Assert.AreEqual(0, mask & (1 << GameLayers.BeamQuery));

            Assert.AreNotEqual(0, mask & (1 << GameLayers.Prop), "소품은 대상이다.");
            Assert.AreNotEqual(0, mask & (1 << 0), "Default 의 움직이는 물체도 대상이다.");
        }

        /// <summary>
        /// 해제된 고정못은 <b>밀린다.</b> 도구도 파츠도 아니고, 뽑아 던져 둔 못이 굴러가는
        /// 것은 기획이 금지한 것도 아니다 — 이 이슈에서 정한 결정이다.
        /// </summary>
        [Test]
        public void AReleasedClamp_IsPushed()
        {
            var go = new GameObject("Clamp") { layer = GameLayers.Prop };
            go.transform.position = new Vector3(1f, 0.5f, 0f);
            go.AddComponent<SphereCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = 6f;
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();

            var bat = MakeBat();
            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.right),
                "뽑아 둔 고정못은 소품으로 본다.");
            Assert.AreSame(body, bat.LastPushed);
        }

        // --- 우선순위 -----------------------------------------------------

        /// <summary>운석이 방망이의 목적이다(§18.1). 발밑 상자 때문에 놓치면 안 된다.</summary>
        [Test]
        public void AMeteorWins_OverAProp()
        {
            var prop = MakeProp(new Vector3(1f, 0.5f, 0f), mass: 20f);
            var meteor = MakeMeteorAtWindow();
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);

            UnityEngine.Physics.Simulate(Step);

            Assert.AreSame(meteor, bat.LastDeflected);
            Assert.IsNull(bat.LastPushed);
            Assert.AreEqual(0f, prop.linearVelocity.magnitude, 0.01f);
        }

        /// <summary>동료 타격은 기획에 있는 기능이다(§18.3). 소품보다 앞이다.</summary>
        [Test]
        public void ATeammateWins_OverAProp()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));
            var prop = MakeProp(new Vector3(1.2f, 0.5f, 0f), mass: 20f);
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);

            Assert.AreSame(target, bat.LastStruck);
            Assert.IsNull(bat.LastPushed);
            Assert.AreEqual(0f, prop.linearVelocity.magnitude, 0.01f);
        }

        [Test]
        public void AMissLocksTheBat_EvenWithNoPropAround()
        {
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right));
            Assert.AreEqual(PhysicsScale.BatMissCooldownSeconds, bat.Cooldown, 1e-3f);
        }

        // --- 리그 ---------------------------------------------------------

        MeteorBat MakeBat()
        {
            var go = new GameObject("MeteorBat") { layer = GameLayers.Prop };
            _spawned.Add(go);
            return go.AddComponent<MeteorBat>();
        }

        Rigidbody MakeProp(Vector3 position, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "화물 상자";
            go.layer = GameLayers.Prop;
            go.transform.position = position;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = false;   // 검증은 밀치는 것만 본다

            UnityEngine.Physics.SyncTransforms();
            return body;
        }

        SpaceshipPart MakePart(Vector3 position)
        {
            var go = new GameObject("Part_1") { layer = GameLayers.Part };
            go.transform.position = position;
            go.AddComponent<BoxCollider>();
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            part.TryTransitionTo(PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);

            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        /// <summary>
        /// 견인 조종 모듈. <b>Prop 레이어에 있다</b> — 그것이 이 검증의 요점이다.
        /// </summary>
        Rigidbody MakeVehicle(Vector3 position)
        {
            var go = new GameObject("TractorBeam_1") { layer = GameLayers.Prop };
            go.transform.position = position;
            go.AddComponent<BoxCollider>();
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            go.AddComponent<TransportVehicle>();

            UnityEngine.Physics.SyncTransforms();
            return body;
        }

        Meteor MakeMeteorAtWindow()
        {
            var g = -UnityEngine.Physics.gravity.y;
            var t = PhysicsScale.BatSwingWindowSeconds * 0.5f;
            var height = 0.5f * g * t * t;

            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = new Vector3(0f, height, 0f);
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
            LogAssert.ignoreFailingMessages = true;
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
