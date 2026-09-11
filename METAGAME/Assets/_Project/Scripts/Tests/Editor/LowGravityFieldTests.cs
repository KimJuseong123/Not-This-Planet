using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 광역 저중력장을 검증한다.
    ///
    /// 견인 도구가 "조준선 앞의 파츠 하나를 끌어오는 선" 에서 "기체 아래 원뿔 안의 것들을
    /// 함께 띄우는 부피" 로 바뀌었다(기획 S15P21B202-107 §14.3). 여기서 지키는 것은
    /// 영역의 모양, 여럿에 동시에 작용하는지, 운석이 빠지는지, 끄면 즉시 중력이 돌아오는지다.
    /// </summary>
    public sealed class LowGravityFieldTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;

        [SetUp]
        public void SetUp()
        {
            savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = savedMode;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        /// <summary>꼭짓점이 원점인 저중력장. 아래로 펼쳐진다.</summary>
        LowGravityField MakeField(Vector3 position)
        {
            var go = new GameObject("Craft");
            go.transform.position = position;
            spawned.Add(go);

            var field = go.AddComponent<LowGravityField>();
            field.SetActive(true);
            return field;
        }

        /// <summary>영역이 붙잡을 강체 하나. 레이어를 지정해 마스크 동작을 확인한다.</summary>
        Rigidbody MakeBody(Vector3 position, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.layer = layer;
            go.transform.position = position;
            spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 500f;

            // 방금 만든 콜라이더는 아직 물리 씬에 옛 좌표로 들어 있다. 맞춰 주지 않으면
            // 첫 질의가 원점의 유령을 보고, 실제 몸은 영역 밖으로 판정된다.
            UnityEngine.Physics.SyncTransforms();
            return body;
        }

        void Run(LowGravityField field, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                field.Tick(Step);
                UnityEngine.Physics.Simulate(Step);
            }
        }

        // --- 영역의 모양 -----------------------------------------------------

        /// <summary>기체 쪽이 좁고 지면 쪽이 넓다 — 기획의 "기체에서 지면으로 갈수록 넓어진다".</summary>
        [Test]
        public void Cone_WidensTowardTheGround()
        {
            var field = MakeField(Vector3.zero);

            var near = field.RadiusAt(0f);
            var far = field.RadiusAt(field.Depth);

            Assert.Less(near, far, "꼭짓점 쪽이 더 좁아야 한다.");
            Assert.AreEqual(field.GroundRadius, far, 0.01f, "가장 깊은 곳의 반경이 기획값과 달라졌다.");
        }

        [Test]
        public void Contains_RejectsAboveTheCraftAndBelowTheDepth()
        {
            var field = MakeField(Vector3.zero);

            Assert.IsFalse(field.Contains(Vector3.up), "기체보다 위는 영역이 아니다.");
            Assert.IsFalse(field.Contains(Vector3.down * (field.Depth + 1f)), "깊이를 넘으면 영역이 아니다.");
            Assert.IsTrue(field.Contains(Vector3.down * (field.Depth * 0.5f)), "축 위 중간 지점은 영역 안이다.");
        }

        /// <summary>같은 깊이라도 옆으로 멀면 밖이다. 원뿔이지 원기둥이 아니다.</summary>
        [Test]
        public void Contains_NarrowsNearTheCraft()
        {
            var field = MakeField(Vector3.zero);

            var shallow = new Vector3(field.GroundRadius * 0.8f, -0.5f, 0f);
            var deep = new Vector3(field.GroundRadius * 0.8f, -field.Depth * 0.95f, 0f);

            Assert.IsFalse(field.Contains(shallow), "기체 바로 아래에서는 좁아야 한다.");
            Assert.IsTrue(field.Contains(deep), "깊은 곳에서는 같은 거리가 영역 안이다.");
        }

        // --- 부양 -----------------------------------------------------------

        /// <summary>
        /// 이 규칙의 핵심. 예전 빔은 한 번에 하나만 잡았다(TractorBeam.Held).
        /// 기획은 "여러 파츠와 플레이어가 동시에 들어오면 모두 영향받는다" 고 정한다.
        /// </summary>
        [Test]
        public void Field_LiftsSeveralBodiesAtOnce()
        {
            var field = MakeField(Vector3.zero);

            var a = MakeBody(new Vector3(-1.2f, -7f, 0f), GameLayers.Part);
            var b = MakeBody(new Vector3(1.2f, -7f, 0f), GameLayers.Part);
            var c = MakeBody(new Vector3(0f, -7f, 1.2f), GameLayers.Prop);

            var startA = a.position.y;
            var startB = b.position.y;
            var startC = c.position.y;

            Run(field, 1.5f);

            Assert.AreEqual(3, field.AffectedCount, "영역 안의 셋 모두에 작용해야 한다.");
            Assert.Greater(a.position.y, startA, "파츠 A 가 떠오르지 않았다.");
            Assert.Greater(b.position.y, startB, "파츠 B 가 떠오르지 않았다.");
            Assert.Greater(c.position.y, startC, "소품 C 가 떠오르지 않았다.");
        }

        [Test]
        public void Field_DeduplicatesSeveralCollidersOnOneBody()
        {
            var field = MakeField(Vector3.zero);
            var body = MakeBody(new Vector3(0f, -6f, 0f), GameLayers.Part);

            var child = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            child.name = "CompoundChild";
            child.layer = GameLayers.Part;
            child.transform.SetParent(body.transform, false);
            child.transform.localPosition = Vector3.right * 0.35f;

            field.Tick(Step);

            Assert.AreEqual(1, field.AffectedCount,
                "compound collider 하나는 Rigidbody 기준으로 한 번만 처리해야 한다.");
        }

        /// <summary>파츠는 기체 아래 5~6m 지점을 유지한다(기획 §14.3).</summary>
        [Test]
        public void Field_HoldsPartAtTargetDepth()
        {
            var field = MakeField(Vector3.zero);
            var part = MakeBody(new Vector3(0f, -9f, 0f), GameLayers.Part);

            Run(field, 6f);

            var depth = -part.position.y;
            Assert.AreEqual(PhysicsScale.FieldHoldDepth, depth, 1.0f,
                $"기체 아래 {depth:0.00}m 에 있다. 기획은 약 5~6m 를 유지하라고 한다.");
        }

        /// <summary>기획 §14.3 — "낙하 중·반사된 운석과 박힌 운석은 저중력장 영향을 받지 않는다".</summary>
        [Test]
        public void Field_IgnoresMeteors()
        {
            var field = MakeField(Vector3.zero);
            var meteor = MakeBody(new Vector3(0f, -6f, 0f), GameLayers.Meteor);

            var start = meteor.position.y;
            Run(field, 1f);

            Assert.AreEqual(0, field.AffectedCount, "운석에는 작용하지 않아야 한다.");
            Assert.Less(meteor.position.y, start, "운석은 그대로 떨어져야 한다.");
        }

        [Test]
        public void Field_IgnoresLockedPart()
        {
            var field = MakeField(Vector3.zero);
            var locked = SocketTestRig.CreatePart(spawned, new Vector3(0f, -6f, 0f));
            var body = locked.GetComponent<Rigidbody>();

            field.Tick(Step);

            Assert.AreEqual(0, field.AffectedCount, "고정 파츠는 저중력장 대상이 아니다.");
            Assert.IsTrue(body.isKinematic, "고정 파츠의 물리 상태를 저중력장이 바꾸면 안 된다.");
        }

        /// <summary>영역 밖은 건드리지 않는다. 옆으로 벗어나면 그냥 떨어진다.</summary>
        [Test]
        public void Field_IgnoresBodiesOutsideTheCone()
        {
            var field = MakeField(Vector3.zero);
            var outside = MakeBody(new Vector3(field.GroundRadius + 3f, -6f, 0f), GameLayers.Part);

            var start = outside.position.y;
            Run(field, 1f);

            Assert.AreEqual(0, field.AffectedCount, "영역 밖에는 작용하지 않아야 한다.");
            Assert.Less(outside.position.y, start, "영역 밖 파츠는 그대로 떨어져야 한다.");
        }

        /// <summary>끄면 일반 중력이 즉시 복구된다(기획 §14.3).</summary>
        [Test]
        public void Field_RestoresGravityWhenTurnedOff()
        {
            var field = MakeField(Vector3.zero);
            var part = MakeBody(new Vector3(0f, -5.5f, 0f), GameLayers.Part);

            Run(field, 1f);

            field.SetActive(false);
            var before = part.position.y;
            Run(field, 1f);

            Assert.AreEqual(0, field.AffectedCount, "꺼져 있으면 아무것에도 작용하지 않아야 한다.");
            Assert.Less(part.position.y, before, "저중력장을 끄면 떨어져야 한다.");
        }

        [Test]
        public void Toggle_FlipsActiveState()
        {
            var field = MakeField(Vector3.zero);
            Assert.IsTrue(field.IsActive);

            field.Toggle();
            Assert.IsFalse(field.IsActive, "F 를 누르면 꺼져야 한다.");

            field.Toggle();
            Assert.IsTrue(field.IsActive, "다시 누르면 켜져야 한다.");
        }

        /// <summary>기체 자신과 그 자식은 제 필드에 뜨지 않는다.</summary>
        [Test]
        public void Field_IgnoresItsOwnCraft()
        {
            var field = MakeField(Vector3.zero);

            // **실제 경로처럼 운반체를 알려 준다.** 자기 기체를 빼는 판정은
            // `LowGravityField` 가 `Configure` 로 받은 운반체 강체를 기준으로 하므로,
            // 그것 없이는 **뺄 대상이 무엇인지 알 방법이 없다** — 자기 부품을 밀어
            // 올리는 것이 당연해진다 (S15P21B202-463). 실제 기체는
            // `TransportVehicle` 이 `Configure(transform, body)` 로 세운다.
            //
            // `MakeField` 헬퍼를 고치지 않은 이유: `Configure` 로 받은 강체는 장치를
            // **따라오는 힘**에도 쓰여서, 헬퍼를 바꾸면 그것을 쓰는 다른 검사들의
            // 측정값까지 같이 움직인다.
            var carrier = field.gameObject.AddComponent<Rigidbody>();
            carrier.useGravity = false;
            carrier.isKinematic = true;
            field.Configure(field.transform, carrier);

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.layer = GameLayers.Prop;
            platform.transform.SetParent(field.transform, false);
            platform.transform.localPosition = Vector3.down * 3f;
            platform.AddComponent<Rigidbody>();
            Physics.SyncTransforms();

            Run(field, 0.5f);

            Assert.AreEqual(0, field.AffectedCount, "기체 자신의 부품에 작용하면 스스로를 밀어 올린다.");
        }

        [Test]
        public void Field_AffectsSiblingPartUnderSameSceneRoot()
        {
            var world = new GameObject("Stage1");
            spawned.Add(world);

            var craft = new GameObject("Transport") { layer = GameLayers.Prop };
            craft.transform.SetParent(world.transform, false);
            craft.transform.position = Vector3.zero;
            var carrier = craft.AddComponent<Rigidbody>();
            carrier.useGravity = false;

            var field = craft.AddComponent<LowGravityField>();
            field.Configure(craft.transform, carrier);
            field.SetActive(true);

            var partObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            partObject.name = "Part_4";
            partObject.layer = GameLayers.Part;
            partObject.transform.SetParent(world.transform, false);
            partObject.transform.localPosition = new Vector3(0f, -6f, 0f);
            var part = partObject.AddComponent<Rigidbody>();
            part.useGravity = false;
            spawned.Add(partObject);
            UnityEngine.Physics.SyncTransforms();

            field.Tick(Step);

            Assert.AreEqual(1, field.AffectedCount,
                "운반체와 같은 Stage1 루트 아래의 조립 파츠도 저중력장 대상이어야 한다.");
        }

        [Test]
        public void Field_UsesCarrierMotionAsInterpolatedForce()
        {
            var field = MakeField(Vector3.zero);
            var carrier = field.gameObject.AddComponent<Rigidbody>();
            carrier.useGravity = false;
            carrier.linearVelocity = Vector3.right * 2f;
            field.Configure(field.transform, carrier);

            var target = MakeBody(new Vector3(0f, -6f, 0f), GameLayers.Prop);
            var before = target.position;

            field.Tick(Step);
            UnityEngine.Physics.Simulate(Step);

            Assert.Greater(target.linearVelocity.x, 0.01f,
                "운반체의 이동은 대상에 속도 대입이 아니라 보간된 힘으로 전달되어야 한다.");
            Assert.Less(target.position.x - before.x, 0.2f,
                "저중력장 대상이 운반체에 순간이동해서는 안 된다.");
        }

        [Test]
        public void Field_DoesNotApplyWithoutPhysicsAuthority()
        {
            var field = MakeField(Vector3.zero);
            var target = MakeBody(new Vector3(0f, -6f, 0f), GameLayers.Part);
            target.useGravity = true;
            field.SetPhysicsAuthority(false);

            field.Tick(Step);

            Assert.AreEqual(0, field.AffectedCount);
            Assert.AreEqual(0f, target.linearVelocity.magnitude, 0.001f,
                "권한 없는 인스턴스는 대상에 물리 힘을 적용하지 않아야 한다.");
        }
    }
}
