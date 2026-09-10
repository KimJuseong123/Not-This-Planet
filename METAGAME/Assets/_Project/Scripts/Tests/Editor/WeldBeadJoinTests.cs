using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 비드가 다른 우주선 몸체에 닿으면 접합이 생기는 것을 검증한다
    /// (S15P21B202-228, 기획 §16.2 · §16.3).
    ///
    /// 지키려는 것은 넷이다.
    /// <list type="number">
    /// <item>닿으면 접합이 생기고, 조인트는 두 몸체 사이 <b>하나</b>다.</item>
    /// <item>강도는 <b>닿아 있는 비드의 부피</b>다 — 기둥 길이가 아니다.</item>
    /// <item>여러 사슬이 같은 쌍을 이으면 더해진다.</item>
    /// <item>다 깎으면 접합이 풀린다.</item>
    /// </list>
    ///
    /// S15P21B202-226 의 조건(우주선 몸체에만)은 <see cref="WeldBeadTests"/> 가 지키므로
    /// 여기서는 반복하지 않고, 접합이 그 조건을 넘어서지 않는 것만 확인한다.
    /// </summary>
    public sealed class WeldBeadJoinTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            WeldBead.All.Clear();
            PartAssemblyLink.All.Clear();
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
            PartAssemblyLink.All.Clear();
        }

        // --- 닿으면 접합 ---------------------------------------------------

        [Test]
        public void BridgingChainChangesColor_WithoutRecoloringUnusedBeads()
        {
            var (lower, upper) = SeparatedPair();
            var nozzle = NozzleForBridge(lower);
            var bead = GrowAt(nozzle, Step);
            var container = bead.transform.Find(WeldBead.ContainerName);
            var seed = container.GetChild(0).GetComponent<Renderer>();
            var unusedMaterial = seed.sharedMaterial;
            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper));

            BridgeUp(lower);
            Assert.Greater(bead.BlobsIn(0), 1, "다리 중간 덩어리도 검증한다.");
            Assert.AreNotEqual(unusedMaterial.color, seed.sharedMaterial.color);
            foreach (var renderer in container.GetComponentsInChildren<Renderer>())
                Assert.AreSame(seed.sharedMaterial, renderer.sharedMaterial);

            // 같은 파츠의 아래쪽에 만든 비드는 위 파츠와의 접합에 기여하지 않는다.
            var bottomNozzle = lower.transform.position - Vector3.up *
                (PhysicsScale.PartHeight * 0.5f + NozzleStandoff);
            GrowAt(bottomNozzle, Step);
            Assert.AreSame(unusedMaterial,
                container.GetChild(container.childCount - 1).GetComponent<Renderer>().sharedMaterial);
            Assert.AreNotSame(unusedMaterial, seed.sharedMaterial);
        }

        [Test]
        public void BrokenBridgeReturnsToUnusedColor_WithoutRecreatingJoint()
        {
            var (lower, upper) = SeparatedPair();
            var bead = GrowAt(NozzleForBridge(lower), Step);
            var seed = bead.transform.Find(WeldBead.ContainerName).GetChild(0).GetComponent<Renderer>();
            var unusedMaterial = seed.sharedMaterial;
            BridgeUp(lower);
            Assert.AreNotSame(unusedMaterial, seed.sharedMaterial);

            PartAssemblyLink.FindBetween(lower, upper).Release();
            // EditMode에서는 Unity 메시지 디스패치를 거치지 않고 갱신 본문만 실행한다.
            var lateUpdate = typeof(WeldBead).GetMethod("LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(lateUpdate);
            lateUpdate.Invoke(bead, null);

            Assert.AreSame(unusedMaterial, seed.sharedMaterial);
            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper));
        }

        [Test]
        public void RestoredBridgeRecoversItsHelpfulColor()
        {
            var (lower, upper) = SeparatedPair();
            var bead = BridgeUp(lower);
            var helpfulMaterial = bead.transform.Find(WeldBead.ContainerName)
                .GetChild(0).GetComponent<Renderer>().sharedMaterial;
            var snapshot = new[] { bead.ReadChain(0) };

            bead.Clear();
            bead.RestoreChains(snapshot, 0, snapshot.Length);

            Assert.IsNotNull(PartAssemblyLink.FindBetween(lower, upper));
            foreach (var renderer in bead.transform.Find(WeldBead.ContainerName)
                         .GetComponentsInChildren<Renderer>())
                Assert.AreSame(helpfulMaterial, renderer.sharedMaterial);
        }

        /// <summary>그림의 "아래 물체 한 지점에 계속 가했더니 위의 물체와 만나 하나가 된다".</summary>
        [Test]
        public void ABeadReachingAnotherBody_CreatesOneJoint()
        {
            var (lower, upper) = SeparatedPair();

            var bead = BridgeUp(lower);

            Assert.IsTrue(bead.BridgedParts.Contains(upper), "닿았으면 그 파츠를 잇고 있어야 한다.");

            var link = PartAssemblyLink.FindBetween(lower, upper);
            Assert.IsNotNull(link, "닿으면 접합이 생겨야 한다.");
            Assert.IsTrue(link.IsIntact);
            Assert.IsFalse(link.HasSockets, "비드로 이은 접합에는 소켓이 없다.");
            Assert.AreEqual(1, JointCount(lower) + JointCount(upper), "조인트는 하나여야 한다.");
        }

        [Test]
        public void GrowingMoreBeads_DoesNotAddMoreJoints()
        {
            var (lower, upper) = SeparatedPair();

            BridgeUp(lower);
            var before = JointCount(lower) + JointCount(upper);

            // 같은 자리에 계속 쏜다.
            GrowAt(NozzleForBridge(lower), PhysicsScale.WeldBeadBlobSeconds * 4f);

            Assert.AreEqual(before, JointCount(lower) + JointCount(upper),
                "비드를 더 쌓아도 조인트가 늘면 안 된다 — 조인트 사슬을 피한 이유가 사라진다.");
        }

        [Test]
        public void TouchingNothing_CreatesNoJoint()
        {
            var lower = Body("Lower", Vector3.zero);

            // 위에 아무것도 없는 상태에서 기둥을 끝까지 쌓는다.
            GrowAt(NozzleForBridge(lower), FullChainSeconds);

            Assert.IsNull(lower.GetComponent<PartAssemblyLink>(), "닿을 것이 없으면 접합이 없어야 한다.");
            Assert.AreEqual(0, JointCount(lower));
        }

        // --- 강도는 접촉 부피 ------------------------------------------------

        [Test]
        public void ContactStrength_RaisesTheJointBreakForce()
        {
            var (lower, upper) = SeparatedPair();

            var bead = BridgeUp(lower);
            var link = PartAssemblyLink.FindBetween(lower, upper);

            Assert.Greater(bead.StrengthTo(upper), 0f, "닿았으면 강도가 있어야 한다.");
            Assert.AreEqual(bead.StrengthTo(upper), link.BeadWeldRatio, 1e-3f,
                "비드가 잰 강도가 그대로 접합에 들어가야 한다.");
            Assert.Greater(JointOf(lower).breakForce, PhysicsScale.UnweldedBreakForce,
                "이은 비드가 있으면 미접합보다 튼튼해야 한다.");
        }

        /// <summary>
        /// <b>이 검증이 강도 규칙의 핵심이다.</b> 얇고 긴 다리가 짧은 다리보다 튼튼하면
        /// 물리적으로 거꾸로다. 긴 기둥은 끝만 닿으므로 한 덩어리 몫이어야 한다.
        /// </summary>
        [Test]
        public void ALongPillar_IsNotStrongerThanAShortOne()
        {
            // 짧은 다리 — 두 덩어리쯤에서 닿는다.
            var (nearLower, nearUpper) = SeparatedPair(Clearance);
            var nearBead = BridgeUp(nearLower);
            var nearStrength = nearBead.StrengthTo(nearUpper);
            var nearBlobs = nearBead.BlobsIn(0);

            TearDown();
            SetUp();

            // 긴 다리 — 같은 사슬을 훨씬 멀리 뻗어 끝만 닿는다.
            var (farLower, farUpper) = SeparatedPair(PhysicsScale.WeldBeadStep * 5f);
            var farBead = BridgeUp(farLower);
            var farStrength = farBead.StrengthTo(farUpper);

            Assert.Greater(farBead.BlobsIn(0), nearBlobs, "긴 다리가 실제로 더 길어야 의미가 있다.");
            Assert.Greater(farStrength, 0f, "긴 다리도 닿았으면 강도가 있어야 한다.");
            Assert.LessOrEqual(farStrength, nearStrength + 1e-3f,
                $"긴 다리({farStrength:0.00})가 짧은 다리({nearStrength:0.00})보다 튼튼하면 안 된다.");
        }

        /// <summary>
        /// 기획 §16.1 의 "원형 접합부 전체를 따라간다" 를 비드 방식으로 되살린 규칙.
        /// 한 자리에 몰아 쏘는 것보다 여러 군데를 잇는 편이 튼튼해야 한다.
        /// </summary>
        [Test]
        public void SeveralChainsBridgingTheSamePair_AddUp()
        {
            var (lower, upper) = SeparatedPair();

            var bead = BridgeUp(lower);
            var one = bead.StrengthTo(upper);

            // 사슬 재사용 반경 밖의 다른 자리에서 두 번째 기둥을 잇는다.
            var apart = Vector3.right * (PhysicsScale.WeldBeadChainReuseRadius * 2f);
            GrowAt(NozzleForBridge(lower) + apart, FullChainSeconds);

            Assert.Greater(bead.ChainCount, 1, "두 번째 기둥이 생겨야 의미가 있다.");
            Assert.Greater(bead.StrengthTo(upper), one + 1e-3f,
                "여러 군데를 이으면 강도가 더해져야 한다.");
        }

        [Test]
        public void EnoughContact_ReachesFullWeldStrength()
        {
            var (lower, upper) = SeparatedPair();

            var bead = BridgeUp(lower);
            // 상한(WeldBeadFullStrengthBlobs)을 채울 만큼 여러 군데를 잇는다.
            foreach (var offset in LateralSpots)
                GrowAt(NozzleForBridge(lower) + offset, FullChainSeconds);

            var link = PartAssemblyLink.FindBetween(lower, upper);

            Assert.AreEqual(1f, bead.StrengthTo(upper), 1e-3f, "충분히 이으면 완전 강도여야 한다.");
            Assert.IsTrue(link.IsFullyWelded);
            Assert.AreEqual(PhysicsScale.WeldedBreakForce, JointOf(lower).breakForce, 1f);
        }

        /// <summary>
        /// 완전 강도에 이르면 <b>둘 다</b> Welded 가 된다. 옆으로 이었으면 어느 쪽이
        /// "위" 인지가 없고, 하나로 움직이므로 한쪽만 들 수 있으면 안 된다.
        /// </summary>
        [Test]
        public void FullBeadWeld_LocksBothParts()
        {
            var (lower, upper) = SeparatedPair();
            var bead = BridgeUp(lower);

            foreach (var offset in LateralSpots)
                GrowAt(NozzleForBridge(lower) + offset, FullChainSeconds);

            Assert.AreEqual(1f, bead.StrengthTo(upper), 1e-3f);
            Assert.AreEqual(PartState.Welded, lower.State);
            Assert.AreEqual(PartState.Welded, upper.State);
        }

        // --- 이미 있는 접합 --------------------------------------------------

        /// <summary>
        /// 소켓으로 맞물린 쌍에 비드를 쌓으면 <b>조인트를 새로 만들지 않고</b> 강도에
        /// 더해진다. 새로 만들면 두 파츠 사이에 조인트가 둘이 된다.
        /// </summary>
        [Test]
        public void BeadsOnAnAssembledPair_ReinforceInsteadOfCreatingASecondJoint()
        {
            var (lower, upper, link) = AssembledPair();
            var before = JointCount(lower) + JointCount(upper);
            Assert.AreEqual(1, before, "소켓 조립이 조인트 하나를 만들어야 한다.");

            // 아래 파츠의 옆면에 쌓아 위 파츠에 닿게 한다.
            var bead = GrowAt(SideNozzleAt(lower, upper), FullChainSeconds);

            Assert.AreEqual(before, JointCount(lower) + JointCount(upper),
                "이미 이어진 쌍에 조인트를 하나 더 만들면 안 된다.");
            Assert.AreSame(link, PartAssemblyLink.FindBetween(lower, upper));
            Assert.Greater(link.BeadWeldRatio, 0f, "보강한 만큼 강도가 올라야 한다.");
            Assert.Greater(bead.StrengthTo(upper), 0f);
        }

        /// <summary>
        /// 한 사람이 채우다 멈춘 자리를 <b>다른 사람이 이어서</b> 채울 수 있다
        /// (기획 §16.1 "용접을 멈춰도 이미 용접한 구간은 유지되고 다른 플레이어가
        /// 이어서 작업할 수 있다").
        ///
        /// 예전에는 원형 접합선이 이 성질을 검증했다. 접합선이 사라졌으므로
        /// S15P21B202-230 에서 여기로 옮겼다 — 진행분이 <b>도구가 아니라 대상</b>에 있어야
        /// 성립하는 성질이고, 비드에서는 파츠에 남은 덩어리가 그것이다.
        /// </summary>
        [Test]
        public void AnotherPlayer_ContinuesWhereTheFirstStopped()
        {
            var (lower, upper) = SeparatedPair();

            // 첫 번째 사람이 한 자리를 이었다.
            var bead = BridgeUp(lower);
            var first = bead.StrengthTo(upper);
            Assert.Greater(first, 0f);

            // 도구를 놓았다고 보고, 새 용접기로 다른 자리를 잇는다.
            var tool = MakeTool(NozzleForBridge(lower) + LateralSpots[0]);
            for (var i = 0; i < Mathf.CeilToInt(FullChainSeconds / Step); i++)
                tool.WeldTick(Step, PlasmaBeamMode.Weld);

            Assert.Greater(bead.StrengthTo(upper), first + 1e-3f,
                "앞사람이 채운 만큼은 남고 그 위에 더해져야 한다.");
        }

        // --- 절단 ---------------------------------------------------------

        [Test]
        public void CuttingAllTheBeads_ReleasesTheBeadJoint()
        {
            var (lower, upper) = SeparatedPair();

            BridgeUp(lower);
            Assert.IsNotNull(PartAssemblyLink.FindBetween(lower, upper));

            var nozzle = NozzleForBridge(lower);
            var steps = Mathf.CeilToInt(FullChainSeconds * 3f / Step);
            for (var i = 0; i < steps; i++) WeldBead.TryShrinkNear(nozzle, Step, out _);

            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper),
                "잇는 비드가 사라지면 접합이 풀려야 한다(기획 §16.3).");
            Assert.AreEqual(0, JointCount(lower) + JointCount(upper));
        }

        /// <summary>
        /// 소켓으로도 맞물린 접합은 비드를 다 깎아도 <b>풀리지 않는다</b>.
        /// 비드는 보강이었을 뿐이고, 풀어 버리면 용접하지 않은 탑이 갑자기 분해된다.
        /// </summary>
        [Test]
        public void CuttingBeadsOffAnAssembledPair_KeepsTheSocketJoint()
        {
            var (lower, upper, link) = AssembledPair();
            var nozzle = SideNozzleAt(lower, upper);

            GrowAt(nozzle, FullChainSeconds);
            Assert.Greater(link.BeadWeldRatio, 0f);

            var steps = Mathf.CeilToInt(FullChainSeconds * 3f / Step);
            for (var i = 0; i < steps; i++) WeldBead.TryShrinkNear(nozzle, Step, out _);

            Assert.IsTrue(link.IsIntact, "소켓 접합은 남아야 한다.");
            Assert.AreEqual(0f, link.BeadWeldRatio, 1e-3f, "보강분만 사라져야 한다.");
            Assert.AreSame(link, PartAssemblyLink.FindBetween(lower, upper));
        }

        [Test]
        public void ClearingTheBeads_ReleasesTheBeadJoint()
        {
            var (lower, upper) = SeparatedPair();
            var bead = BridgeUp(lower);

            bead.Clear();

            Assert.AreEqual(0f, bead.StrengthTo(upper), 1e-3f);
            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper));
        }

        // --- 이 이슈의 경계 --------------------------------------------------

        /// <summary>
        /// 파츠 하나가 <b>여러 쌍</b>과 이어질 수 있다.
        ///
        /// 이 검증은 원래 반대를 확인했다 — S15P21B202-228 시점에는
        /// <see cref="PartAssemblyLink"/> 가 <c>DisallowMultipleComponent</c> 여서 이미
        /// 이어진 파츠에서는 비드가 닿아 있어도 새 접합을 만들지 못했고, 그 경계를
        /// <b>기록</b>하는 것이 이 검증의 일이었다.
        ///
        /// S15P21B202-229 가 그 경계를 풀었다. 지우지 않고 <b>뒤집어</b> 둔다 — 경계가
        /// 사라진 것 자체가 잠글 값이 있는 사실이고, 지워 버리면 나중에 누가 제약을
        /// 되살려도 아무것도 알려 주지 않는다.
        /// </summary>
        [Test]
        public void APartAlreadyLinked_TakesASecondBeadJoint()
        {
            // 가운데 파츠에서 양옆으로 비드를 뻗는다.
            var middle = Body("Middle", Vector3.zero);
            var right = Body("Right", Vector3.right * (PhysicsScale.PartWidth + Clearance));
            var left = Body("Left", Vector3.left * (PhysicsScale.PartWidth + Clearance));

            GrowAt(SideNozzle(middle, Vector3.right), FullChainSeconds);
            Assert.IsNotNull(PartAssemblyLink.FindBetween(middle, right),
                "링크가 없던 파츠는 이어져야 한다.");

            // 이제 가운데 파츠는 링크를 가졌다. 반대쪽으로도 이어져야 한다.
            var bead = GrowAt(SideNozzle(middle, Vector3.left), FullChainSeconds);

            Assert.IsTrue(bead.BridgedParts.Contains(left), "비드가 왼쪽 파츠에 닿아야 한다.");
            Assert.IsNotNull(PartAssemblyLink.FindBetween(middle, left),
                "파츠 하나가 여러 쌍과 이어질 수 있다(S15P21B202-229).");
            Assert.AreEqual(2, JointCount(middle), "쌍마다 조인트 하나씩이어야 한다.");
        }

        // --- 리그 ---------------------------------------------------------

        static float Step => Time.fixedDeltaTime;

        /// <summary>
        /// 윗면 안쪽의 서로 떨어진 네 자리. 여기서 각각 쏘면 사슬이 넷 생긴다.
        ///
        /// 파츠 폭(3m)의 절반 안에 두어야 표면에서 벗어나지 않고, 서로는 사슬 재사용
        /// 반경(0.35m)보다 멀어야 각자 새 기둥이 된다.
        /// </summary>
        static readonly Vector3[] LateralSpots =
        {
            new Vector3(0.9f, 0f, 0f),
            new Vector3(-0.9f, 0f, 0f),
            new Vector3(0f, 0f, 0.9f),
            new Vector3(0f, 0f, -0.9f),
        };

        /// <summary>사슬 하나를 끝까지 쌓고도 남는 시간.</summary>
        static float FullChainSeconds =>
            PhysicsScale.WeldBeadBlobSeconds * (PhysicsScale.WeldBeadBlobsPerChain + 2);

        SpaceshipPart Body(string name, Vector3 position)
        {
            var part = SocketTestRig.Placed(_spawned, position);
            part.name = name;
            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        /// <summary>
        /// 서로 <b>닿지 않은</b> 두 파츠. 위 파츠를 비드로 이을 만큼만 띄운다.
        /// 소켓 조립 허용치(0.2m)보다 훨씬 멀어 저절로 붙지 않는다.
        /// </summary>
        (SpaceshipPart Lower, SpaceshipPart Upper) SeparatedPair() => SeparatedPair(Clearance);

        (SpaceshipPart Lower, SpaceshipPart Upper) SeparatedPair(float clearance)
        {
            var lower = Body("Lower", Vector3.zero);
            var upper = Body("Upper", Vector3.up * (PhysicsScale.PartHeight + clearance));

            Assert.Greater(clearance, PhysicsScale.WeldGapTolerance,
                "소켓으로 저절로 붙지 않을 만큼 떨어져 있어야 한다.");

            UnityEngine.Physics.SyncTransforms();
            return (lower, upper);
        }

        /// <summary>
        /// 파츠 사이의 틈(m). 소켓 접합 허용치(0.2m)보다 넓어 저절로 붙지 않고,
        /// 사슬 하나(약 0.75m)로는 이을 수 있는 거리다.
        /// </summary>
        static float Clearance => PhysicsScale.WeldGapTolerance * 1.25f;

        /// <summary>파츠 옆면 바로 밖. 기둥이 그 방향으로 뻗는다.</summary>
        static Vector3 SideNozzle(SpaceshipPart part, Vector3 direction) =>
            part.transform.position + direction * (PhysicsScale.PartWidth * 0.5f + NozzleStandoff);

        /// <summary>아래 파츠 윗면 바로 위. 여기서 쏘면 기둥이 위로 뻗는다.</summary>
        static Vector3 NozzleForBridge(SpaceshipPart lower) =>
            lower.transform.position + Vector3.up * (PhysicsScale.PartHeight * 0.5f + NozzleStandoff);

        /// <summary>
        /// 노즐을 표면에서 띄우는 거리(m).
        ///
        /// <b>작아야 한다.</b> 비드는 노즐에서 가장 가까운 몸체에 씨앗을 내리므로,
        /// 위 파츠 바닥보다 아래 파츠 윗면에 뚜렷하게 가까워야 아래에서 위로 뻗는다.
        /// 처음에 0.2m 로 뒀다가 간격 0.3m 인 쌍에서 <b>위 파츠에</b> 씨앗이 내려
        /// 기둥이 반대로 자랐다.
        /// </summary>
        const float NozzleStandoff = 0.08f;

        /// <summary>
        /// 아래 파츠의 옆면에서 위 파츠 쪽으로. 소켓으로 맞물린 쌍을 보강할 때 쓴다 —
        /// 윗면은 위 파츠가 덮고 있어 노즐을 둘 자리가 없다.
        /// </summary>
        static Vector3 SideNozzleAt(SpaceshipPart lower, SpaceshipPart upper)
        {
            // 아래 파츠 윗면 <b>바로 위</b>를 옆에서 겨눈다.
            //
            // 틈 가운데를 겨누면 안 된다. 두 파츠 표면까지의 거리가 같아져 어느 쪽에
            // 씨앗이 내릴지 갈리지 않는다 — 실제로 정확히 동점이 나서 검증이 흔들렸다.
            // 위로 살짝 올리면 아래 파츠가 확실히 가까워지고, 윗면 모서리에 내린 덩어리가
            // 굵어지면서 위 파츠 밑면까지 닿는다. 접합선을 따라 덧대는 실제 용접 자리다.
            var top = lower.transform.position.y + PhysicsScale.PartHeight * 0.5f;
            var bottom = upper.transform.position.y - PhysicsScale.PartHeight * 0.5f;
            Assert.Less(top, bottom, "위 파츠가 아래 파츠보다 위에 있어야 한다.");

            return new Vector3(
                PhysicsScale.PartWidth * 0.5f + NozzleStandoff, top + StraddleLift, 0f);
        }

        /// <summary>옆에서 겨눌 때 윗면보다 얼마나 위인지(m). 아래 파츠가 확실히 가까워야 한다.</summary>
        const float StraddleLift = 0.02f;

        /// <summary>기둥이 위 파츠에 닿을 때까지 쌓는다.</summary>
        WeldBead BridgeUp(SpaceshipPart lower) => GrowAt(NozzleForBridge(lower), FullChainSeconds);

        WeldBead GrowAt(Vector3 nozzle, float seconds)
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

        /// <summary>노즐이 <paramref name="nozzle"/> 에 있는 용접기. 장착 절차를 흉내내지 않는다.</summary>
        PlasmaWeldingTool MakeTool(Vector3 nozzle)
        {
            var go = new GameObject("PlasmaWeldingTool");
            go.transform.position = nozzle;
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();
            return go.AddComponent<PlasmaWeldingTool>();
        }

        static int JointCount(SpaceshipPart part) => part.GetComponents<Joint>().Length;

        static Joint JointOf(SpaceshipPart part)
        {
            var joints = part.GetComponents<Joint>();
            Assert.AreEqual(1, joints.Length, "조인트가 하나여야 한다.");
            return joints[0];
        }

        /// <summary>소켓으로 맞물린 파츠 한 쌍과 그 접합. 보강 검증에 쓴다.</summary>
        (SpaceshipPart Lower, SpaceshipPart Upper, PartAssemblyLink Link) AssembledPair()
        {
            var lower = Body("Lower", Vector3.zero);

            var upper = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));
            upper.name = "Upper";

            upper.TryTransitionTo(PartState.Carried);

            var link = PartAssembler.Assemble(PartAssembler.FindMatch(upper));
            Assert.IsNotNull(link, "조립이 되어야 보강 검증을 할 수 있다.");

            UnityEngine.Physics.SyncTransforms();
            return (lower, upper, link);
        }
    }
}
