using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 원형 접합선의 절단과 재조립을 검증한다(기획 S15P21B202-107 §16.3).
    ///
    /// <c>S15P21B202-206</c> 이 원형 접합선과 좌클릭 용접을 넣었지만 우클릭은 색만
    /// 바뀌었다. 이 규칙이 그 효과다.
    ///
    /// <b>핵심 판단</b>: 절단을 용접 진행분을 깎는 것으로 표현하지 않았다. 기획이 두
    /// 상태를 다르게 다루기 때문이다 — §15.1 은 "잘 올리면 용접 전에도 서 있을 수 있고"
    /// 이므로 용접 0% 는 붙어 있고, §16.3 은 "100% 절단하면 … 분리된다" 다.
    /// 값이 하나면 두 경우가 같아진다.
    /// </summary>
    public sealed class WeldCutTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            WeldSeam.All.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
            WeldSeam.All.Clear();
        }

        // --- 절단 진행 -----------------------------------------------------

        /// <summary>기획 §16.3 — "완전 절단의 최단 시간은 약 6초".</summary>
        [Test]
        public void SweepingTheWholeSeam_SeversInTheShortestTime()
        {
            var (_, _, seam) = WeldedPair();

            var spent = 0f;
            for (var i = 0; i < seam.SegmentCount; i++)
            {
                seam.TryCut(PointAtSegment(seam, i), PhysicsScale.CutSeamSecondsPerSegment);
                spent += PhysicsScale.CutSeamSecondsPerSegment;
            }

            Assert.AreEqual(PhysicsScale.CutSeamSeconds, spent, 0.001f,
                "완전 절단 최단 시간이 기획의 약 6초여야 한다.");
            Assert.LessOrEqual(PhysicsScale.CutSeamSeconds, PhysicsScale.WeldSeamSeconds,
                "부수는 것이 만드는 것보다 빨라야 한다.");
        }

        /// <summary>
        /// 기획 §16.1 의 한 자리 규칙이 절단에도 적용된다.
        /// 조각 구조가 이미 그것을 만든다 — 따로 구현하지 않았다.
        /// </summary>
        [Test]
        public void HoldingOneSpot_StopsAtThatSegment()
        {
            var (_, _, seam) = WeldedPair();
            var point = PointAt(seam, 0f);

            var progressed = 0;
            for (var i = 0; i < 5; i++)
                if (seam.TryCut(point, PhysicsScale.CutSeamSecondsPerSegment))
                    progressed++;

            Assert.AreEqual(1, progressed, "조각이 잘린 뒤에는 진행됐다고 보고하면 안 된다.");
            Assert.AreEqual(1f / PhysicsScale.WeldSeamSegments, seam.CutRatio, 0.001f);
            Assert.IsFalse(seam.IsSevered, "한 자리로는 완전 절단할 수 없다.");
        }

        /// <summary>기획 §16.3 — "절단을 멈춰도 이미 자른 구간은 유지된다".</summary>
        [Test]
        public void StoppingHalfway_KeepsWhatWasCut()
        {
            var (_, _, seam) = WeldedPair();
            var half = PhysicsScale.WeldSeamSegments / 2;

            for (var i = 0; i < half; i++)
                seam.TryCut(PointAtSegment(seam, i), PhysicsScale.CutSeamSecondsPerSegment);

            Assert.AreEqual(0.5f, seam.CutRatio, 0.001f, "절반을 잘랐으면 절반이다.");
            Assert.IsFalse(seam.IsSevered);
        }

        // --- 단계별 거동 (기획 §16.3) ---------------------------------------

        /// <summary>
        /// 기획 §16.3 — <b>"완전히 용접된 접합부는 50%까지 절단해도 즉시 분리되거나
        /// 기능을 잃지 않는다"</b>.
        ///
        /// 단계별 강도를 새 상수로 만들지 않았다. 접합부 강도가 이미 용접 비율로
        /// 보간되므로(8,000 → 200,000), 절단한 만큼 비율이 내려가면 강도가 따라온다.
        /// </summary>
        [Test]
        public void HalfCut_StaysAttachedAndStrong()
        {
            var (part, link, seam) = WeldedPair();

            seam.ForceCut(0.5f);
            link.Tick();

            Assert.IsFalse(seam.IsSevered, "50% 절단으로는 분리되지 않는다.");
            Assert.IsTrue(link.IsIntact, "접합이 남아 있어야 한다.");
            Assert.AreEqual(0.5f, link.WeldRatio, 0.001f);

            var joint = part.GetComponent<FixedJoint>();
            var expected = Mathf.Lerp(
                PhysicsScale.UnweldedBreakForce, PhysicsScale.WeldedBreakForce, 0.5f);

            Assert.AreEqual(expected, joint.breakForce, 1f);
            Assert.Greater(joint.breakForce, PhysicsScale.UnweldedBreakForce * 5f,
                "절반 잘려도 기능을 잃지 않아야 한다.");
        }

        /// <summary>
        /// 기획 §16.3 — "75~99% 절단 시 작은 충격에도 분리될 위험이 크다".
        /// 99% 에서 강도가 미접합 수준까지 내려가야 그 말이 성립한다.
        /// </summary>
        [Test]
        public void NearlyFullCut_IsAsWeakAsUnwelded()
        {
            var (part, link, seam) = WeldedPair();

            seam.ForceCut(0.99f);
            link.Tick();

            var joint = part.GetComponent<FixedJoint>();

            Assert.IsFalse(seam.IsSevered, "99% 는 아직 분리가 아니다.");
            Assert.Less(joint.breakForce, PhysicsScale.UnweldedBreakForce * 1.5f,
                $"작은 충격에도 떨어질 만큼 약해야 한다. 지금 {joint.breakForce:0}");
        }

        // --- 완전 절단과 분리 -----------------------------------------------

        /// <summary>
        /// 기획 §16.3 — "100% 절단하면 해당 접합부 위쪽 파츠들이 하나의 덩어리로
        /// 분리된다".
        /// </summary>
        [Test]
        public void FullCut_SeparatesTheJoint()
        {
            var (part, link, seam) = WeldedPair();

            var severed = 0;
            seam.Severed += _ => severed++;

            for (var i = 0; i < seam.SegmentCount; i++)
                seam.TryCut(PointAtSegment(seam, i), PhysicsScale.CutSeamSecondsPerSegment);

            Assert.AreEqual(1, severed, "분리를 한 번 알려야 한다 — 연출이 붙을 자리다.");
            Assert.IsFalse(link.IsIntact, "접합이 해제되어야 한다.");
            Assert.IsNull(part.GetComponent<FixedJoint>(), "조인트가 사라져야 한다.");
        }

        /// <summary>
        /// 완전 절단 뒤에는 다시 들 수 있어야 한다(기획 §16.3 "절단과 재조립").
        ///
        /// 예전에는 <c>Welded</c> 가 종착 상태여서 완전 절단해도 다시 들 수 없었다.
        /// 그러면 §16.3 의 재배치가 성립하지 않는다.
        /// </summary>
        [Test]
        public void SeveredPart_CanBeCarriedAgain()
        {
            var (part, _, seam) = WeldedPair();

            Assert.AreEqual(PartState.Welded, part.State, "먼저 용접된 상태여야 한다.");
            Assert.IsFalse(part.TryTransitionTo(PartState.Carried),
                "용접된 채로는 들 수 없다 — 이것이 바뀌면 안 된다.");

            for (var i = 0; i < seam.SegmentCount; i++)
                seam.TryCut(PointAtSegment(seam, i), PhysicsScale.CutSeamSecondsPerSegment);

            Assert.AreEqual(PartState.Placed, part.State, "분리되면 다시 놓인 상태다.");
            Assert.IsTrue(part.TryTransitionTo(PartState.Carried), "이제 들 수 있어야 한다.");
        }

        /// <summary>
        /// 분리되면 진행분이 되돌아간다. 다시 쌓아 처음부터 용접해야 한다 —
        /// 잘라 낸 접합이 기억될 이유가 없다.
        /// </summary>
        [Test]
        public void Separation_ForgetsTheWeld()
        {
            var (_, _, seam) = WeldedPair();

            for (var i = 0; i < seam.SegmentCount; i++)
                seam.TryCut(PointAtSegment(seam, i), PhysicsScale.CutSeamSecondsPerSegment);

            Assert.AreEqual(0f, seam.Ratio, 0.001f, "용접 진행분이 남으면 안 된다.");
            Assert.AreEqual(0f, seam.CutRatio, 0.001f, "절단 진행분도 되돌아간다.");
        }

        // --- 재용접 --------------------------------------------------------

        /// <summary>기획 §16.3 — "절단한 구간은 좌클릭으로 다시 용접할 수 있다".</summary>
        [Test]
        public void CutSegment_CanBeWeldedAgain()
        {
            var (part, link, seam) = WeldedPair();

            // 절반만 잘라 분리는 되지 않게 한다. 파츠는 여전히 Welded 다 —
            // 그 상태에서도 재용접이 되어야 §16.3 이 성립한다.
            seam.ForceCut(0.5f);
            link.Tick();
            Assert.AreEqual(0.5f, link.WeldRatio, 0.001f);
            Assert.AreEqual(PartState.Welded, part.State, "아직 용접된 상태다.");

            var before = seam.CutRatio;
            var progressed = seam.TryWeld(PointAt(seam, 0f),
                PhysicsScale.WeldSeamSecondsPerSegment);

            Assert.IsTrue(progressed, "잘린 구간에 용접이 진행돼야 한다.");
            Assert.Less(seam.CutRatio, before, "용접이 잘린 구간을 메워야 한다.");
        }

        // --- 용접할 수 있는 조건 -------------------------------------------

        [Test]
        public void Cut_OnUnassembledPart_DoesNothing()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var free = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));
            var seam = AddSeam(free);

            Assert.IsFalse(seam.CanWork(), "붙지 않은 파츠는 자를 것이 없다.");
            Assert.IsFalse(seam.TryCut(PointAt(seam, 0f), 1f));
        }

        /// <summary>
        /// 완전히 용접한 파츠는 <b>자를 수 있고</b>, 용접을 시도해도 채울 것이 없어
        /// 아무 일도 일어나지 않는다.
        ///
        /// "할 수 있는가"(<c>CanWork</c>)와 "할 것이 있는가"(<c>TryWeld</c> 의 반환값)를
        /// 나눈 이유가 이것이다. 처음에는 용접 조건을 <c>Placed</c> 로 좁혔는데, 그러면
        /// 기획 §16.3 의 "절단한 구간은 좌클릭으로 다시 용접할 수 있다" 가 성립하지 않는다.
        /// </summary>
        [Test]
        public void WeldedPart_CanBeCutAndWeldingDoesNothing()
        {
            var (part, _, seam) = WeldedPair();

            Assert.AreEqual(PartState.Welded, part.State);
            Assert.IsTrue(seam.CanWork(), "용접된 접합도 작업 대상이다 — 자를 수 있다.");
            Assert.IsFalse(seam.TryWeld(PointAt(seam, 0f), 1f),
                "이미 다 채워졌으므로 용접은 진행되지 않는다.");
        }

        // --- 도구 우클릭 ----------------------------------------------------

        [Test]
        public void Tool_CutsTheSeamWithinRange()
        {
            var (_, _, seam) = WeldedPair();
            var tool = MakeTool(PointAt(seam, 0f));

            Assert.IsTrue(tool.CutTick(PhysicsScale.CutSeamSecondsPerSegment, PlasmaBeamMode.Cut),
                "사거리 안이면 절단이 진행돼야 한다.");
            Assert.AreSame(seam, tool.CuttingSeam, "무엇을 자르는지 알려야 한다.");
            Assert.Greater(seam.CutRatio, 0f);
        }

        /// <summary>사거리는 용접과 같아야 한다 — 다르면 이유를 알 수 없다.</summary>
        [Test]
        public void Tool_DoesNotCutBeyondRange()
        {
            var (_, _, seam) = WeldedPair();
            var far = PointAt(seam, 0f) + Vector3.up * (PhysicsScale.WeldSeamRange + 0.5f);
            var tool = MakeTool(far);

            Assert.IsFalse(tool.CutTick(1f, PlasmaBeamMode.Cut));
            Assert.IsNull(tool.CuttingSeam);
            Assert.AreEqual(0f, seam.CutRatio);
        }

        /// <summary>하나의 노즐에서 나가는 상호 배타적 기능이다.</summary>
        [Test]
        public void Tool_DoesNotCutInWeldMode()
        {
            var (_, _, seam) = WeldedPair();
            var tool = MakeTool(PointAt(seam, 0f));

            Assert.IsFalse(tool.CutTick(1f, PlasmaBeamMode.Weld));
            Assert.AreEqual(0f, seam.CutRatio);
        }

        /// <summary>노즐을 접합선 위로 옮겨 다니면 완전 절단된다.</summary>
        [Test]
        public void Tool_SeversWhenWalkedAroundTheSeam()
        {
            var (_, link, seam) = WeldedPair();
            var tool = MakeTool(PointAt(seam, 0f));

            for (var i = 0; i < seam.SegmentCount; i++)
            {
                tool.transform.position = PointAtSegment(seam, i);
                UnityEngine.Physics.SyncTransforms();
                tool.CutTick(PhysicsScale.CutSeamSecondsPerSegment, PlasmaBeamMode.Cut);
            }

            Assert.IsFalse(link.IsIntact, "한 바퀴 돌면 접합이 해제돼야 한다.");
        }

        // --- 헬퍼 ----------------------------------------------------------

        /// <summary>완전히 용접된 접합. §16.3 의 절단은 이 상태를 대상으로 한다.</summary>
        (SpaceshipPart Part, PartAssemblyLink Link, WeldSeam Seam) WeldedPair()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);

            var moving = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

            var seam = AddSeam(moving);
            moving.TryTransitionTo(PartState.Carried);

            var link = PartAssembler.Assemble(PartAssembler.FindMatch(moving));
            Assert.IsNotNull(link, "조립이 되어야 절단 검증을 할 수 있다.");
            link.RefreshSeam();
            link.ForceWeldComplete();
            link.Tick();

            return (moving, link, seam);
        }

        static WeldSeam AddSeam(SpaceshipPart part)
        {
            var go = new GameObject("WeldSeam");
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = Vector3.down * (PhysicsScale.PartHeight * 0.5f);

            var seam = go.AddComponent<WeldSeam>();
            seam.Initialize();
            return seam;
        }

        PlasmaWeldingTool MakeTool(Vector3 nozzle)
        {
            var go = new GameObject("PlasmaWeldingTool");
            go.transform.position = nozzle;
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();
            return go.AddComponent<PlasmaWeldingTool>();
        }

        static Vector3 PointAt(WeldSeam seam, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var local = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * seam.Radius;
            return seam.transform.TransformPoint(local);
        }

        static Vector3 PointAtSegment(WeldSeam seam, int index)
        {
            var span = 360f / seam.SegmentCount;
            return PointAt(seam, index * span + span * 0.5f);
        }
    }
}
