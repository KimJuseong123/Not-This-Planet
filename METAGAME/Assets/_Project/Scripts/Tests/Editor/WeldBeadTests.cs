using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 우주선 몸체에 쌓이는 용접 비드를 검증한다(S15P21B202-226, 기획 §16.2).
    ///
    /// 지키려는 것은 넷이다.
    /// <list type="number">
    /// <item>덩어리가 자라고, 상한에 이르면 다음 덩어리가 이어져 기둥이 뻗는다.</item>
    /// <item><b>우주선 몸체에만</b> 생긴다 — 지형·운석·소품에는 생기지 않는다.</item>
    /// <item>질량은 늘고 <b>무게중심은 자동으로 따라온다</b>(대입하지 않는다).</item>
    /// <item>강체와 조인트가 늘지 않는다 — 이것이 조인트 사슬을 피한 이유다.</item>
    /// </list>
    /// </summary>
    public sealed class WeldBeadTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            WeldBead.All.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
            WeldBead.All.Clear();
        }

        // --- 자라기 -------------------------------------------------------

        [Test]
        public void HoldingOnABody_StartsABead()
        {
            var part = Body();

            var grew = WeldBead.TryGrowNear(NozzleAbove(part), Step, out var bead);

            Assert.IsTrue(grew, "우주선 몸체에 대고 있으면 비드가 시작돼야 한다.");
            Assert.IsNotNull(bead);
            Assert.AreEqual(part, bead.Owner);
            Assert.AreEqual(1, bead.ChainCount);
            Assert.AreEqual(1, bead.BlobCount);
            Assert.AreEqual(PhysicsScale.WeldBeadMinRadius, bead.TipRadius(0), 1e-4f,
                "첫 덩어리는 보이는 최소 크기로 시작해야 한다 — 진행 바가 없으니 보이는 것이 피드백이다.");
        }

        [Test]
        public void HoldingLonger_GrowsTheBlobToItsCap()
        {
            var part = Body();
            var nozzle = NozzleAbove(part);

            var bead = GrowFor(nozzle, PhysicsScale.WeldBeadBlobSeconds);

            Assert.AreEqual(PhysicsScale.WeldBeadMaxRadius, bead.TipRadius(0), 1e-3f,
                "한 덩어리는 상한 크기에서 멈춰야 한다.");
            Assert.AreEqual(1, bead.BlobCount, "상한에 <b>도달</b>한 것만으로 다음 덩어리가 생기면 안 된다.");
        }

        /// <summary>그림의 "상한선이 되면 다시 동그랗게 점점 커지게" 다.</summary>
        [Test]
        public void PastTheCap_ANewBlobStartsOnTop()
        {
            var part = Body();
            var nozzle = NozzleAbove(part);

            var bead = GrowFor(nozzle, PhysicsScale.WeldBeadBlobSeconds * 2f);

            Assert.AreEqual(1, bead.ChainCount, "같은 자리에 대고 있으면 사슬은 하나여야 한다.");
            Assert.AreEqual(2, bead.BlobCount, "상한을 넘기면 다음 덩어리가 이어져야 한다.");
        }

        [Test]
        public void TheChainGrowsAwayFromTheSurface()
        {
            var part = Body();
            var nozzle = NozzleAbove(part);

            var bead = GrowFor(nozzle, PhysicsScale.WeldBeadBlobSeconds * 2.2f);
            Assert.GreaterOrEqual(bead.BlobsIn(0), 2, "기둥을 보려면 덩어리가 둘 이상이어야 한다.");

            var tip = bead.TipPosition(0);
            var surfaceTop = part.transform.position.y + PhysicsScale.PartHeight * 0.5f;

            Assert.Greater(tip.y, surfaceTop + PhysicsScale.WeldBeadStep * 0.5f,
                "윗면에 쐈으면 기둥은 위로 뻗어야 한다.");
        }

        /// <summary>최대 다리 길이. 이 상한이 없으면 비드로 맵 어디든 이을 수 있다.</summary>
        [Test]
        public void AChainStopsAtItsLengthCap()
        {
            var part = Body();
            var nozzle = NozzleAbove(part);

            // 상한 길이를 채우고도 남을 만큼 오래 댄다.
            var seconds = PhysicsScale.WeldBeadBlobSeconds * (PhysicsScale.WeldBeadBlobsPerChain + 4);
            var bead = GrowFor(nozzle, seconds);

            Assert.AreEqual(PhysicsScale.WeldBeadBlobsPerChain, bead.BlobsIn(0),
                "사슬은 상한 길이에서 멈춰야 한다.");

            var more = WeldBead.TryGrowNear(nozzle, Step, out _);
            Assert.IsFalse(more, "다 찬 사슬에서는 진행됐다고 보고하면 안 된다 — 불꽃이 계속 튄다.");
        }

        [Test]
        public void ADistantSpot_StartsASeparateChain()
        {
            var part = Body();

            WeldBead.TryGrowNear(NozzleAbove(part), Step, out var bead);

            // 첫 기둥 끝에서 사슬 재사용 반경 밖으로 옮긴다.
            var far = NozzleAbove(part) + Vector3.right * (PhysicsScale.WeldBeadChainReuseRadius * 2f);
            WeldBead.TryGrowNear(far, Step, out var second);

            Assert.AreEqual(bead, second, "같은 파츠면 비드 관리자도 하나여야 한다.");
            Assert.AreEqual(2, bead.ChainCount, "떨어진 자리에서는 새 기둥이 시작돼야 한다.");
        }

        [TestCase(1)]
        [TestCase(2)]
        public void SerializedChainLimit_StopsGrowth_AndRaisingItAllowsAnotherBlob(int limit)
        {
            var part = Body();
            SetLimits(part, 48, limit);
            var nozzle = NozzleAbove(part);
            var bead = GrowFor(nozzle, PhysicsScale.WeldBeadBlobSeconds * (limit + 2));
            Assert.AreEqual(limit, bead.BlobCount);
            Assert.IsFalse(WeldBead.TryGrowNear(nozzle, Step, out _));

            SetLimits(part, 48, limit + 1);
            Assert.IsTrue(WeldBead.TryGrowNear(nozzle, Step, out _));
            Assert.AreEqual(limit + 1, bead.BlobCount);
        }

        [Test]
        public void SerializedPartLimit_BlocksPreviewAndNewChains_AndCuttingFreesCapacity()
        {
            var part = Body();
            SetLimits(part, 1, 6);
            var nozzle = NozzleAbove(part);
            var bead = GrowFor(nozzle, PhysicsScale.WeldBeadBlobSeconds * 3);
            Assert.AreEqual(1, bead.BlobCount);
            Assert.That(bead.TipRadius(0), Is.EqualTo(PhysicsScale.WeldBeadMaxRadius).Within(0.001f));

            var far = nozzle + Vector3.right * (PhysicsScale.WeldBeadChainReuseRadius * 2f);
            Assert.IsTrue(UnityEngine.Physics.Raycast(far, Vector3.down, out var hit, PhysicsScale.WeldRange));
            Assert.IsFalse(WeldBead.CanApplyAt(hit, false));
            Assert.IsFalse(WeldBead.TryApplyAt(hit, Step, false, out _));
            Assert.IsFalse(WeldBead.TryGrowNear(far, Step, out _));

            Assert.IsTrue(WeldBead.TryShrinkNear(nozzle, PhysicsScale.WeldBeadBlobSeconds * 2, out _));
            Assert.AreEqual(0, bead.BlobCount);
            Assert.IsTrue(WeldBead.CanApplyAt(hit, false));
            Assert.IsTrue(WeldBead.TryApplyAt(hit, Step, false, out _));
            Assert.AreEqual(1, bead.BlobCount);
        }

        [Test]
        public void LoweringSerializedLimits_PreservesExistingBeads_AndClampsInvalidValues()
        {
            var part = Body();
            var bead = GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds * 2.2f);
            var count = bead.BlobCount;
            Assert.GreaterOrEqual(count, 2);
            SetLimits(part, 0, -1);
            Assert.AreEqual(1, part.WeldBeadsPerPart);
            Assert.AreEqual(1, part.WeldBeadBlobsPerChain);
            GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds * 2);
            Assert.AreEqual(count, bead.BlobCount, "한도를 낮춰도 기존 비드는 제거하지 않는다.");
        }

        static void SetLimits(SpaceshipPart part, int perPart, int perChain)
        {
            var serialized = new UnityEditor.SerializedObject(part);
            serialized.FindProperty("weldBeadsPerPart").intValue = perPart;
            serialized.FindProperty("weldBeadBlobsPerChain").intValue = perChain;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // --- 우주선 몸체에만 -----------------------------------------------

        [Test]
        public void TheGround_TakesNoBead()
        {
            var ground = Slab("Ground", GameLayers.Ground, Vector3.zero);

            var grew = WeldBead.TryGrowNear(TopOf(ground) + Vector3.up * 0.2f, Step, out var bead);

            Assert.IsFalse(grew, "지면에 용접하면 우주선을 땅에 붙여 버릴 수 있다.");
            Assert.IsNull(bead);
        }

        [Test]
        public void AMeteor_TakesNoBead()
        {
            var meteor = Slab("Meteor", GameLayers.Meteor, Vector3.zero);

            var grew = WeldBead.TryGrowNear(TopOf(meteor) + Vector3.up * 0.2f, Step, out _);

            Assert.IsFalse(grew, "운석은 우주선 몸체가 아니다.");
        }

        [Test]
        public void AProp_TakesNoBead()
        {
            var prop = Slab("Prop", GameLayers.Prop, Vector3.zero);

            var grew = WeldBead.TryGrowNear(TopOf(prop) + Vector3.up * 0.2f, Step, out _);

            Assert.IsFalse(grew, "소품에도 붙으면 비드로 아무거나 이을 수 있다.");
        }

        /// <summary>
        /// 파츠 레이어에 있어도 <see cref="SpaceshipPart"/> 가 없으면 몸체가 아니다.
        /// 레이어와 컴포넌트를 둘 다 보는 이유가 이것이다.
        /// </summary>
        [Test]
        public void SomethingOnThePartLayerWithoutAPart_TakesNoBead()
        {
            var decoy = Slab("Decoy", GameLayers.Part, Vector3.zero);

            var grew = WeldBead.TryGrowNear(TopOf(decoy) + Vector3.up * 0.2f, Step, out _);

            Assert.IsFalse(grew, "레이어만 맞는 것에 붙으면 안 된다.");
        }

        [Test]
        public void OutOfRange_TakesNoBead()
        {
            var part = Body();
            var far = NozzleAbove(part) + Vector3.up * (PhysicsScale.WeldRange + 0.5f);

            var grew = WeldBead.TryGrowNear(far, Step, out _);

            Assert.IsFalse(grew, "사거리 밖에서는 생기면 안 된다. 접합선과 같은 0.8m 다.");
        }

        // --- 질량과 무게중심 ------------------------------------------------

        [Test]
        public void GrowingBeads_AddMassToThePart()
        {
            var part = Body();
            var body = part.GetComponent<Rigidbody>();
            var before = body.mass;

            var bead = GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds * 2f);

            Assert.Greater(bead.Mass, 0f);
            Assert.AreEqual(bead.Mass, part.ExtraMass, 1e-3f,
                "비드 질량은 파츠를 거쳐야 한다 — 직접 더하면 상태 전환에 사라진다.");
            Assert.AreEqual(before + bead.Mass, body.mass, 1e-3f);
        }

        /// <summary>
        /// 상태가 바뀌어도 비드 무게가 사라지지 않아야 한다.
        /// <c>ApplyRepresentation</c> 이 질량을 <b>다시 쓰기</b> 때문에 생기는 함정이다.
        /// </summary>
        [Test]
        public void BeadMass_SurvivesAStateChange()
        {
            var part = Body();
            var body = part.GetComponent<Rigidbody>();

            var bead = GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds * 2f);
            var withBeads = body.mass;

            part.TryTransitionTo(PartState.Welded);

            Assert.AreEqual(withBeads, body.mass, 1e-3f, "상태 전환에 비드 무게가 사라지면 안 된다.");
            Assert.AreEqual(bead.Mass, part.ExtraMass, 1e-3f);
        }

        /// <summary>
        /// 무게중심은 <b>대입하지 않는다.</b> 자식 콜라이더가 늘면 Unity 가 다시 계산한다.
        /// 한쪽에만 쌓았을 때 그쪽으로 치우치는지로 자동 계산이 살아 있는지 본다.
        ///
        /// <b>움직이는 폭은 작다 — 그것이 맞다.</b> Unity 의 자동 무게중심은 우리가 정한
        /// <c>Rigidbody.mass</c> 가 아니라 <b>콜라이더 부피</b>로 계산한다(밀도가 균일하다고
        /// 본다). 파츠 상자는 3 x 3.7 x 3 = 33m³ 이고 비드 세 덩어리는 0.01m³ 도 안 되므로
        /// 부피비가 1/4000 쯤이다. 실측 이동량이 0.6mm 인 이유가 이것이고, 실제로도
        /// 3.7m 짜리 파츠의 무게중심이 비드 몇 덩어리로 크게 움직이면 그쪽이 이상하다.
        ///
        /// 그래서 <b>양</b>이 아니라 <b>방향</b>을 잠근다. 자동 계산이 꺼지면(누군가
        /// centerOfMass 를 대입하면) 이 값은 정확히 0 이 되므로 방향만으로도 잡힌다.
        /// </summary>
        [Test]
        public void BeadsShiftTheCenterOfMass_WithoutAssigningIt()
        {
            var part = Body();
            var body = part.GetComponent<Rigidbody>();
            var before = body.centerOfMass;

            // 한쪽 옆면에 쌓는다.
            var side = part.transform.position
                       + Vector3.right * (PhysicsScale.PartWidth * 0.5f + 0.15f);

            GrowFor(side, PhysicsScale.WeldBeadBlobSeconds * 3f);

            var moved = body.centerOfMass.x - before.x;

            Assert.Greater(moved, 1e-4f,
                "오른쪽에만 쌓았으면 무게중심이 오른쪽으로 가야 한다. " +
                "꿈쩍도 하지 않으면 centerOfMass 가 어디선가 대입되어 자동 계산이 꺼진 것이다.");
        }

        /// <summary>
        /// 위 검증의 짝. 비드를 <b>양쪽에 똑같이</b> 쌓으면 무게중심이 제자리여야 한다.
        ///
        /// 방향만 보는 검증은 "무게중심이 아무 쪽으로나 흔들려도" 통과할 수 있다.
        /// 대칭으로 쌓아 제자리인 것까지 봐야 콜라이더 배치를 실제로 따라간다고 할 수 있다.
        /// </summary>
        [Test]
        public void SymmetricBeads_LeaveTheCenterOfMassInPlace()
        {
            var part = Body();
            var body = part.GetComponent<Rigidbody>();
            var before = body.centerOfMass;

            var half = PhysicsScale.PartWidth * 0.5f + 0.15f;
            GrowFor(part.transform.position + Vector3.right * half, PhysicsScale.WeldBeadBlobSeconds);
            GrowFor(part.transform.position + Vector3.left * half, PhysicsScale.WeldBeadBlobSeconds);

            Assert.AreEqual(before.x, body.centerOfMass.x, 1e-4f,
                "양쪽에 똑같이 쌓았으면 무게중심은 가운데 그대로여야 한다.");
        }

        // --- 조인트를 늘리지 않는다 -------------------------------------------

        /// <summary>
        /// 이 검증이 설계의 핵심이다. 덩어리마다 강체를 주고 조인트로 이었다면 여기서
        /// 개수가 늘고, 실제로도 위에 얹힌 파츠 무게에 떨거나 터진다.
        /// </summary>
        [Test]
        public void Beads_AddNoRigidbodiesAndNoJoints()
        {
            var part = Body();

            GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds * 3f);

            var bodies = part.GetComponentsInChildren<Rigidbody>(true);
            var joints = part.GetComponentsInChildren<Joint>(true);

            Assert.AreEqual(1, bodies.Length, "강체는 파츠 하나뿐이어야 한다.");
            Assert.AreEqual(0, joints.Length, "비드는 조인트를 만들지 않는다.");
        }

        [Test]
        public void Beads_AreCollidersUnderThePart()
        {
            var part = Body();

            var bead = GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds * 2f);
            var container = part.transform.Find(WeldBead.ContainerName);

            Assert.IsNotNull(container, "비드는 한 자식 아래에 모여야 씬에서 찾을 수 있다.");
            Assert.AreEqual(bead.BlobCount, container.childCount);

            for (var i = 0; i < container.childCount; i++)
            {
                var blob = container.GetChild(i);
                Assert.IsNotNull(blob.GetComponent<Collider>(), "비드는 실제로 부딪혀야 한다.");
                Assert.AreEqual(part.gameObject.layer, blob.gameObject.layer,
                    "비드는 파츠와 같은 레이어여야 견인빔·운석이 같은 취급을 한다.");
            }
        }

        [Test]
        public void Beads_HidePhysicsSpheresAndShowColliderFreeWeldMetal()
        {
            var part = Body();

            GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds * 2f);
            var container = part.transform.Find(WeldBead.ContainerName);

            for (var i = 0; i < container.childCount; i++)
            {
                var blob = container.GetChild(i);
                var physicsRenderer = blob.GetComponent<Renderer>();
                var visual = blob.Find(WeldBead.VisualName);

                Assert.IsNotNull(physicsRenderer);
                Assert.IsFalse(physicsRenderer.enabled,
                    "물리 구체가 그대로 보이면 용접 자국이 알갱이처럼 보인다.");
                Assert.IsNotNull(visual, "각 물리 조각에는 이어진 용접 금속 외형이 있어야 한다.");
                Assert.IsNotNull(visual.GetComponent<Renderer>());
                Assert.IsNull(visual.GetComponent<Collider>(),
                    "표시 외형이 콜라이더를 하나 더 만들면 접합 물리가 달라진다.");

                if (i == 0)
                {
                    Assert.IsTrue(visual.gameObject.activeSelf,
                        "레이가 맞은 표면에는 용접 자국이 보여야 한다.");
                    Assert.That(visual.localScale.z, Is.LessThan(visual.localScale.x),
                        "브러시 자국은 표면 위로 솟은 구체가 아니라 얇은 표면 표시여야 한다.");
                }
                else
                {
                    Assert.IsFalse(visual.gameObject.activeSelf,
                        "표면 밖으로 자란 물리 구체가 검은 알갱이처럼 보이면 안 된다.");
                }
            }
        }

        [Test]
        public void RestoredBrushStroke_ConnectsPreviousAndCurrentSurfacePoints()
        {
            var part = Body();
            var bead = WeldBead.Attach(part);
            var from = new Vector3(-0.08f, 0.5f, 0f);
            var to = new Vector3(0.08f, 0.5f, 0f);
            bead.RestoreChains(new[]
            {
                new WeldBead.ChainState
                {
                    Origin = from, Direction = Vector3.up,
                    TipRadius = PhysicsScale.WeldBeadMinRadius, BlobCount = 1,
                },
                new WeldBead.ChainState
                {
                    Origin = to, Direction = Vector3.up,
                    HasBrushFrom = true, BrushFrom = from,
                    TipRadius = PhysicsScale.WeldBeadMinRadius, BlobCount = 1,
                },
            }, 0, 2);

            var restored = bead.ReadChain(1);
            var blob = part.transform.Find(WeldBead.ContainerName).GetChild(1);
            var visual = blob.Find(WeldBead.VisualName);

            Assert.IsTrue(restored.HasBrushFrom,
                "연결 정보가 복원되어야 다른 플레이어에게도 같은 브러시 궤적이 보인다.");
            Assert.That((restored.BrushFrom - from).sqrMagnitude, Is.LessThan(1e-8f));
            Assert.IsTrue(visual.gameObject.activeSelf);
            Assert.That(visual.localScale.y, Is.GreaterThan(visual.localScale.x),
                "이동한 거리만큼 이전 점과 현재 점 사이가 이어져야 한다.");
        }

        /// <summary>
        /// 파츠 루트의 스케일이 1 이 아니어도 비드는 구여야 한다.
        /// 검증 리그가 루트를 늘려 쓰므로 이 조건이 실제로 걸린다.
        /// </summary>
        [Test]
        public void Beads_StayRoundUnderAScaledPart()
        {
            var part = Body();
            Assert.AreNotEqual(Vector3.one, part.transform.localScale, "리그가 스케일을 준 파츠여야 의미가 있다.");

            GrowFor(NozzleAbove(part), PhysicsScale.WeldBeadBlobSeconds);

            var blob = part.transform.Find(WeldBead.ContainerName).GetChild(0);
            var scale = blob.lossyScale;

            Assert.AreEqual(PhysicsScale.WeldBeadMaxRadius * 2f, scale.x, 1e-3f);
            Assert.AreEqual(scale.x, scale.y, 1e-3f, "구여야 한다.");
            Assert.AreEqual(scale.x, scale.z, 1e-3f, "구여야 한다.");
        }

        // --- 되돌리기 -----------------------------------------------------

        [Test]
        public void Cutting_ShrinksAndFinallyRemovesTheBead()
        {
            var part = Body();
            var nozzle = NozzleAbove(part);

            var bead = GrowFor(nozzle, PhysicsScale.WeldBeadBlobSeconds * 2f);
            Assert.AreEqual(2, bead.BlobCount);

            // 쌓은 시간의 두 배를 깎으면 남지 않아야 한다.
            var steps = Mathf.CeilToInt(PhysicsScale.WeldBeadBlobSeconds * 4f / Step);
            for (var i = 0; i < steps; i++) WeldBead.TryShrinkNear(nozzle, Step, out _);

            Assert.AreEqual(0, bead.BlobCount, "다 깎으면 비드가 남으면 안 된다.");
            Assert.AreEqual(0f, part.ExtraMass, 1e-3f, "깎은 만큼 무게도 돌아와야 한다.");
        }

        [Test]
        public void CuttingNothing_ReportsNoProgress()
        {
            Body();

            var shrunk = WeldBead.TryShrinkNear(new Vector3(50f, 50f, 50f), Step, out var bead);

            Assert.IsFalse(shrunk);
            Assert.IsNull(bead);
        }

        // --- 도구 연결 -----------------------------------------------------

        [Test]
        public void TheTool_GrowsBeadsOnAWeldHold()
        {
            var part = Body();
            var tool = MakeTool(NozzleAbove(part));

            var progressed = tool.WeldTick(Step, PlasmaBeamMode.Weld);

            Assert.IsTrue(progressed);
            Assert.IsTrue(tool.WeldingProgressed, "몸체에 대고 좌클릭하면 비드가 자라야 한다.");
            Assert.IsNotNull(tool.GrowingBead);
            Assert.AreEqual(part, tool.GrowingBead.Owner);
        }

        [Test]
        public void TheTool_GrowsNothingWhileIdle()
        {
            var part = Body();
            var tool = MakeTool(NozzleAbove(part));

            Assert.IsFalse(tool.WeldTick(Step, PlasmaBeamMode.None));
            Assert.IsFalse(tool.WeldingProgressed);
            Assert.IsNull(tool.GrowingBead);
        }

        [Test]
        public void TheTool_ShrinksBeadsOnACutHold()
        {
            var part = Body();
            var tool = MakeTool(NozzleAbove(part));

            tool.WeldTick(Step, PlasmaBeamMode.Weld);
            UnityEngine.Physics.SyncTransforms();
            var shrunk = tool.CutTick(Step, PlasmaBeamMode.Cut);

            Assert.IsTrue(shrunk, "쌓을 수만 있고 되돌릴 수 없으면 안 된다.");
            Assert.IsTrue(tool.CuttingProgressed);
            Assert.IsNotNull(tool.ShrinkingBead);
        }

        // --- 실제 파츠 -----------------------------------------------------

        /// <summary>
        /// 검증 리그의 상자 하나가 아니라 <b>실제 파츠</b> 위에서도 돌아야 한다.
        ///
        /// 실제 파츠는 <see cref="PartSpec"/> 의 실측 상자 여럿으로 만든 컴파운드
        /// 콜라이더이고(S15P21B202-166), 루트 스케일이 1 이며, 질량이 406~745kg 이다.
        /// 리그와 셋 다 다르므로 여기서 한 번 더 잠근다 — 이 이슈가 손대는 것이
        /// 바로 그 콜라이더 구성이다.
        /// </summary>
        [Test]
        public void BeadsGrowOnARealPart_AndItsCenterOfMassStaysBelowCenter()
        {
            var spec = PartSpec.All[0];
            var part = RealPart(spec);
            var body = part.GetComponent<Rigidbody>();
            var before = body.centerOfMass;

            var nozzle = part.transform.position + Vector3.up * (spec.Height * 0.5f + 0.25f);
            var bead = GrowFor(nozzle, PhysicsScale.WeldBeadBlobSeconds * 2f);

            Assert.AreEqual(2, bead.BlobCount, "실측 형태 위에서도 기둥이 자라야 한다.");
            Assert.AreEqual(spec.Mass + bead.Mass, body.mass, 1e-2f,
                "실측 질량에 비드 무게가 더해져야 한다.");

            // S15P21B202-166 이 세운 조건 — 무게중심이 파츠 한가운데보다 아래.
            // 위에 비드를 쌓았으니 조금 올라가지만 넘어가면 안 된다.
            Assert.Less(body.centerOfMass.y, 0f,
                "위에 비드를 쌓아도 무게중심은 파츠 중심보다 아래여야 한다.");
            Assert.Greater(body.centerOfMass.y, before.y,
                "위에 쌓았으면 무게중심도 위로 가야 한다.");
        }

        /// <summary><see cref="Stage1PuzzleBuilder"/> 와 같은 방식의 컴파운드 콜라이더 파츠.</summary>
        SpaceshipPart RealPart(PartSpec spec)
        {
            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            _spawned.Add(root);

            foreach (var box in spec.Boxes)
            {
                var child = new GameObject("Body") { layer = GameLayers.Part };
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = box.CenterFromPartCenter(spec.Height);
                child.AddComponent<BoxCollider>().size = box.Size;
            }

            root.AddComponent<Rigidbody>();

            var part = root.AddComponent<SpaceshipPart>();
            part.SetPlacedMass(spec.Mass);
            part.Initialize();
            part.TryTransitionTo(PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);

            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        // --- 리그 ---------------------------------------------------------

        /// <summary>물리 고정 스텝. 프로젝트 값은 0.02 가 아니라 0.01 이다.</summary>
        static float Step => Time.fixedDeltaTime;

        /// <summary>비드를 쌓을 수 있는 우주선 몸체 하나.</summary>
        SpaceshipPart Body()
        {
            var part = SocketTestRig.Placed(_spawned, Vector3.zero);
            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        /// <summary>파츠 윗면 바로 위. 표면에서 조금 떨어져 있어야 바깥 방향이 잡힌다.</summary>
        static Vector3 NozzleAbove(SpaceshipPart part) =>
            part.transform.position + Vector3.up * (PhysicsScale.PartHeight * 0.5f + 0.25f);

        static Vector3 TopOf(GameObject go) =>
            go.transform.position + Vector3.up * (go.transform.localScale.y * 0.5f);

        /// <summary><paramref name="seconds"/> 동안 고정 스텝으로 계속 대고 있는다.</summary>
        WeldBead GrowFor(Vector3 nozzle, float seconds)
        {
            WeldBead bead = null;
            var steps = Mathf.CeilToInt(seconds / Step);

            for (var i = 0; i < steps; i++)
            {
                WeldBead.TryGrowNear(nozzle, Step, out var grown);
                if (grown != null) bead = grown;
            }

            UnityEngine.Physics.SyncTransforms();

            Assert.IsNotNull(bead, "비드가 만들어져야 한다.");
            return bead;
        }

        /// <summary>파츠가 아닌 물체 하나. 레이어만 바꿔 가며 재사용한다.</summary>
        GameObject Slab(string name, int layer, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.position = position;
            go.transform.localScale = new Vector3(4f, 1f, 4f);
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();
            return go;
        }

        /// <summary>노즐이 <paramref name="nozzle"/> 에 있는 용접기. 장착 절차를 흉내내지 않는다.</summary>
        PlasmaWeldingTool MakeTool(Vector3 nozzle)
        {
            var go = new GameObject("PlasmaWeldingTool");
            go.transform.position = nozzle;
            go.transform.rotation = Quaternion.LookRotation(Vector3.down);
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();
            return go.AddComponent<PlasmaWeldingTool>();
        }

    }
}
