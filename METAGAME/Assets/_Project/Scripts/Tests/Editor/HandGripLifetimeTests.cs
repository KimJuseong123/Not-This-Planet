using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 손을 놓을 때 조인트가 실제로 사라지는지 검증한다(S15P21B202-213).
    ///
    /// <c>Hand.Release()</c> 가 <c>Destroy(Grip)</c> 를 썼다. <b>Destroy 는 에디터에서
    /// 동작하지 않는다</b> — 에러 로그만 남고 조인트가 그대로 남는다. 그래서 EditMode
    /// 검증에서는 손을 놓아도 몸이 물체에 계속 묶여 있었고, 그 상태를 전제로 통과하던
    /// 검증이 있었다.
    ///
    /// 이 저장소는 같은 함정을 이미 여러 곳에서 고쳤다 — <c>Prim.Decor</c>,
    /// <c>PartAssemblyLink.DestroyObject</c>, <c>PlayerRig.DisconnectTransportJoint</c>.
    /// <c>Hand.Release</c> 만 빠져 있었다.
    ///
    /// 이 규칙이 그 경로를 직접 잠근다. 지금까지 손 놓기의 조인트 정리를 보는 검증이
    /// 하나도 없었다.
    /// </summary>
    public sealed class HandGripLifetimeTests
    {
        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            UnityEngine.Physics.simulationMode = _savedMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        /// <summary>
        /// <b>이 이슈의 회귀 검증이다.</b> 놓은 뒤 손에 조인트가 남아 있으면 안 된다.
        /// </summary>
        [Test]
        public void Release_ActuallyDestroysTheGripJoint()
        {
            var rig = MakeRig();

            Assert.IsTrue(rig.Hand.GripTo(rig.Target, null), "먼저 짚어야 한다.");
            Assert.IsNotNull(rig.Hand.GetComponent<Joint>(),
                "짚으면 조인트가 생겨야 이 검증이 의미가 있다.");

            rig.Hand.Release();

            Assert.IsNull(rig.Hand.GetComponent<Joint>(),
                "놓으면 조인트가 실제로 사라져야 한다. EditMode 에서 Destroy 는 동작하지 않는다.");
            Assert.IsNull(rig.Hand.Grip, "Grip 참조도 비어야 한다.");
            Assert.IsFalse(rig.Hand.Gripping, "짚고 있지 않아야 한다.");
        }

        /// <summary>
        /// 놓은 뒤 몸이 실제로 자유로워지는지 본다. 조인트가 남아 있으면 손이 짚은
        /// 자리에 계속 묶여 움직이지 못한다 — EditMode 에서 정확히 그랬다.
        /// </summary>
        [Test]
        public void Release_FreesTheBodyToMove()
        {
            var rig = MakeRig();

            rig.Hand.GripTo(rig.Target, null);
            rig.Hand.Release();

            var before = rig.Body.position;
            rig.Body.linearVelocity = Vector3.right * 3f;

            for (var s = 0; s < 50; s++)
                UnityEngine.Physics.Simulate(0.02f);

            var moved = Vector3.Distance(rig.Body.position, before);
            Assert.Greater(moved, 1f,
                $"놓았으면 자유롭게 움직여야 한다. {moved:0.00}m 만 움직였다.");
        }

        /// <summary>여러 번 놓아도 안전해야 한다.</summary>
        [Test]
        public void Release_IsIdempotent()
        {
            var rig = MakeRig();

            rig.Hand.GripTo(rig.Target, null);
            rig.Hand.Release();
            rig.Hand.Release();

            Assert.IsNull(rig.Hand.Grip);
            Assert.IsNull(rig.Hand.GetComponent<Joint>());
        }

        /// <summary>다시 짚을 수 있어야 한다. 조인트가 남으면 새 그립이 막힌다.</summary>
        [Test]
        public void Release_AllowsGrippingAgain()
        {
            var rig = MakeRig();

            rig.Hand.GripTo(rig.Target, null);
            rig.Hand.Release();

            Assert.IsTrue(rig.Hand.GripTo(rig.Target, null), "놓았으면 다시 짚을 수 있어야 한다.");
            Assert.IsNotNull(rig.Hand.Grip);
        }

        [Test]
        public void GripToFixedAtPoint_UsesTheRequestedWorldAnchor()
        {
            var rig = MakeRig();
            var targetBody = rig.Target.gameObject.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;

            var worldPoint = new Vector3(0.2f, 0.12f, 0.03f);
            Assert.IsTrue(rig.Hand.GripToFixedAtPoint(rig.Target, targetBody, worldPoint),
                "고정쇠는 실제 손과 대상 지점을 연결할 수 있어야 한다.");

            var joint = rig.Hand.GetComponent<FixedJoint>();
            Assert.IsNotNull(joint);
            Assert.AreSame(targetBody, joint.connectedBody);
            Assert.Less(Vector3.Distance(rig.Hand.transform.TransformPoint(joint.anchor), worldPoint),
                        0.001f);
            Assert.Less(Vector3.Distance(targetBody.transform.TransformPoint(joint.connectedAnchor), worldPoint),
                        0.001f);
            Assert.IsTrue(rig.Hand.Gripping);
            Assert.AreSame(rig.Target, rig.Hand.Held);

            rig.Hand.Release();

            Assert.IsFalse(rig.Hand.Gripping, "버튼을 놓으면 실제 손 결합도 풀려야 한다.");
            Assert.IsNull(rig.Hand.GetComponent<FixedJoint>());
        }

        // --- 헬퍼 ----------------------------------------------------------

        struct Rig
        {
            public Hand Hand;
            public Rigidbody Body;
            public Grabbable Target;
        }

        /// <summary>
        /// 손 하나와 짚을 고정물.
        ///
        /// <c>PlayerRig</c> 를 만들지 않는다. 이 규칙이 보는 것은 손의 조인트 수명이고,
        /// 인형 전체를 세우면 <c>Prim.Decor</c> 의 로그와 균형 물리가 섞인다.
        /// </summary>
        Rig MakeRig()
        {
            var handGo = new GameObject("손");
            handGo.transform.position = Vector3.zero;
            _spawned.Add(handGo);

            var body = handGo.AddComponent<Rigidbody>();
            body.useGravity = false;

            // Hand 는 MonoBehaviour 이고 조인트를 자기 오브젝트에 만든다.
            var hand = handGo.AddComponent<Hand>();
            hand.Body = body;

            // 짚을 대상은 세계 고정물(HeldBody == null)로 둔다. 그 경로가 FixedJoint 를
            // 만드는 경로다 — 가벼운 동적 물체는 조인트를 만들지 않는다.
            var targetGo = new GameObject("손잡이");
            targetGo.transform.position = new Vector3(0.2f, 0f, 0f);
            targetGo.AddComponent<BoxCollider>().size = Vector3.one * 0.3f;
            _spawned.Add(targetGo);

            var target = Grabbable.Mark(targetGo, "손잡이");

            UnityEngine.Physics.SyncTransforms();
            return new Rig { Hand = hand, Body = body, Target = target };
        }
    }
}
