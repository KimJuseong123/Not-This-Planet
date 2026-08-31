using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 원형 용접선을 검증한다(기획 S15P21B202-107 §16.1 · §16.2).
    ///
    /// 예전 <c>WeldPointTests</c> 를 옮겨 온 것이다. 용접점 4개가 원형 접합선 하나로
    /// 바뀌었지만 지켜야 하는 것은 그대로다 — 진행분이 누적되고, 중간에 놓아도 채운
    /// 만큼 남고, 다른 플레이어가 이어서 채울 수 있고, 완료 비율만큼 조인트가 튼튼해진다.
    ///
    /// 더해서 원형이 되어 생긴 규칙을 잠근다. 가장 중요한 것은 <b>같은 자리에 계속 대고
    /// 있어도 더 진행되지 않는다</b>(§16.1) 다 — 이것이 없으면 점 용접과 같아진다.
    /// </summary>
    public sealed class WeldSeamTests
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

        // --- 진행분 누적 ---------------------------------------------------

        [Test]
        public void FreshSeam_StartsEmpty()
        {
            var (_, link, seam) = AssembledPair();

            Assert.AreEqual(0f, seam.Ratio);
            Assert.IsFalse(seam.IsComplete);
            Assert.AreEqual(0f, link.WeldRatio);
            Assert.AreEqual(PhysicsScale.WeldSeamSegments, seam.SegmentCount);
        }

        /// <summary>기획 §16.1 — 빠짐없이 훑으면 최단 시간에 끝난다.</summary>
        [Test]
        public void SweepingTheWholeSeam_CompletesInTheShortestTime()
        {
            var (_, link, seam) = AssembledPair();

            var spent = SweepOnce(seam, PhysicsScale.WeldSeamSecondsPerSegment);

            Assert.IsTrue(seam.IsComplete, $"한 바퀴 훑으면 끝나야 한다. 비율 {seam.Ratio:0.00}");
            Assert.AreEqual(PhysicsScale.WeldSeamSeconds, spent, 0.001f,
                "최단 시간이 기획의 약 10초여야 한다.");
            Assert.AreEqual(1f, link.WeldRatio, 0.001f);
        }

        /// <summary>
        /// 기획 §16.1 — <b>"같은 위치에 계속 대고 있어도 추가 진행되지 않는다"</b>.
        ///
        /// 이 규칙이 원형 용접의 핵심이다. 없으면 한 점에 대고 있는 것으로 끝나
        /// 예전 점 용접과 같아진다.
        /// </summary>
        [Test]
        public void HoldingOneSpot_StopsAtThatSegment()
        {
            var (_, _, seam) = AssembledPair();
            var point = PointAt(seam, 0f);

            // 한 조각을 채울 시간의 다섯 배를 한 자리에 대고 있는다.
            var progressed = 0;
            for (var i = 0; i < 5; i++)
                if (seam.TryWeld(point, PhysicsScale.WeldSeamSecondsPerSegment))
                    progressed++;

            Assert.AreEqual(1, progressed, "조각이 찬 뒤에는 진행됐다고 보고하면 안 된다.");
            Assert.AreEqual(1f / PhysicsScale.WeldSeamSegments, seam.Ratio, 0.001f,
                "한 자리에 대고 있으면 조각 하나만 찬다.");
            Assert.IsFalse(seam.IsComplete, "한 자리로는 완성할 수 없다.");
        }

        /// <summary>기획 §16.1 — "용접을 멈춰도 이미 용접한 구간은 유지된다".</summary>
        [Test]
        public void StoppingHalfway_KeepsWhatWasWelded()
        {
            var (_, _, seam) = AssembledPair();
            var half = PhysicsScale.WeldSeamSegments / 2;

            for (var i = 0; i < half; i++)
                seam.TryWeld(PointAtSegment(seam, i), PhysicsScale.WeldSeamSecondsPerSegment);

            Assert.AreEqual(0.5f, seam.Ratio, 0.001f, "절반을 훑었으면 절반이다.");

            // "멈춤" 은 아무것도 하지 않는 것이다. 진행분이 사라지면 안 된다.
            Assert.AreEqual(0.5f, seam.Ratio, 0.001f);
        }

        /// <summary>
        /// 기획 §16.1 — "다른 플레이어가 이어서 작업할 수 있다".
        /// 진행분이 접합선에 있으므로 누가 채웠는지는 기록하지 않는다.
        /// </summary>
        [Test]
        public void SecondPlayer_CanFinishWhatFirstPlayerStarted()
        {
            var (_, _, seam) = AssembledPair();
            var half = PhysicsScale.WeldSeamSegments / 2;

            for (var i = 0; i < half; i++)
                seam.TryWeld(PointAtSegment(seam, i), PhysicsScale.WeldSeamSecondsPerSegment);

            for (var i = half; i < PhysicsScale.WeldSeamSegments; i++)
                seam.TryWeld(PointAtSegment(seam, i), PhysicsScale.WeldSeamSecondsPerSegment);

            Assert.IsTrue(seam.IsComplete, "이어받아 완성할 수 있어야 한다.");
        }

        /// <summary>부분 진행분도 남는다. 조각 하나를 절반만 채우고 놓아도 된다.</summary>
        [Test]
        public void PartialSegment_KeepsItsProgress()
        {
            var (_, _, seam) = AssembledPair();
            var point = PointAt(seam, 0f);
            var index = seam.SegmentAt(point);

            seam.TryWeld(point, PhysicsScale.WeldSeamSecondsPerSegment * 0.5f);
            Assert.AreEqual(0.5f, seam.SegmentProgress(index), 0.01f);

            seam.TryWeld(point, PhysicsScale.WeldSeamSecondsPerSegment * 0.5f);
            Assert.AreEqual(1f, seam.SegmentProgress(index), 0.01f, "이어서 채우면 찬다.");
        }

        [Test]
        public void Completion_RaisesTheEventOnce()
        {
            var (_, _, seam) = AssembledPair();

            var fired = 0;
            seam.Completed += _ => fired++;

            SweepOnce(seam, PhysicsScale.WeldSeamSecondsPerSegment);
            SweepOnce(seam, PhysicsScale.WeldSeamSecondsPerSegment);

            Assert.AreEqual(1, fired, "완성은 한 번만 알려야 한다.");
        }

        // --- 용접할 수 있는 조건 -------------------------------------------

        [Test]
        public void Seam_OnUnassembledPart_CannotBeWelded()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var free = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));
            var seam = AddSeam(free);

            Assert.IsFalse(seam.CanWork(), "붙지 않은 파츠는 용접할 것이 없다.");
            Assert.IsFalse(seam.TryWeld(PointAt(seam, 0f), 1f));
        }

        /// <summary>
        /// 기획 §16.1 — "파츠가 움직여 접합면 간격이 벌어지면 작업이 중단된다".
        /// 기준은 조립 판정이 쓰는 <c>WeldGapTolerance</c> 를 그대로 쓴다.
        /// </summary>
        [Test]
        public void WideningGap_StopsTheWork()
        {
            var (part, link, seam) = AssembledPair();

            Assert.IsTrue(seam.CanWork(), "먼저 용접할 수 있어야 한다.");

            part.transform.position += Vector3.up * (PhysicsScale.WeldGapTolerance + 0.3f);
            UnityEngine.Physics.SyncTransforms();

            Assert.Greater(link.Gap, PhysicsScale.WeldGapTolerance, "간격이 벌어져야 한다.");
            Assert.IsFalse(seam.CanWork(), "간격이 벌어지면 중단된다.");
            Assert.IsFalse(seam.TryWeld(PointAt(seam, 0f), 1f));
        }

        // --- 조인트 강도 (기획 §16.2) ---------------------------------------

        [Test]
        public void UnweldedJoint_UsesWeakBreakForce()
        {
            var (part, link, _) = AssembledPair();
            link.Tick();

            var joint = part.GetComponent<FixedJoint>();
            Assert.AreEqual(PhysicsScale.UnweldedBreakForce, joint.breakForce, 1f);
        }

        /// <summary>
        /// 기획 §16.2 — "처음 조립하는 접합부의 강도는 <b>실제로 용접한 길이에 비례</b>한다".
        /// 조각 길이가 모두 같으므로 절반을 훑으면 강도도 가운데다.
        /// </summary>
        [Test]
        public void HalfWeldedJoint_UsesInterpolatedBreakForce()
        {
            var (part, link, seam) = AssembledPair();
            var half = PhysicsScale.WeldSeamSegments / 2;

            for (var i = 0; i < half; i++)
                seam.TryWeld(PointAtSegment(seam, i), PhysicsScale.WeldSeamSecondsPerSegment);

            link.Tick();

            var expected = Mathf.Lerp(
                PhysicsScale.UnweldedBreakForce, PhysicsScale.WeldedBreakForce, 0.5f);
            var joint = part.GetComponent<FixedJoint>();

            Assert.AreEqual(0.5f, link.WeldRatio, 0.001f);
            Assert.AreEqual(expected, joint.breakForce, 1f);
        }

        [Test]
        public void FullyWeldedJoint_PromotesPartAndUsesStrongBreakForce()
        {
            var (part, link, _) = AssembledPair();
            link.ForceWeldComplete();
            link.Tick();

            var joint = part.GetComponent<FixedJoint>();

            Assert.AreEqual(PartState.Welded, part.State, "전부 용접하면 Welded 로 간다.");
            Assert.AreEqual(PhysicsScale.WeldedBreakForce, joint.breakForce, 1f);
            Assert.IsTrue(link.IsFullyWelded);
        }

        [Test]
        public void WeldedPart_CannotBeCarriedAgain()
        {
            var (part, link, _) = AssembledPair();
            link.ForceWeldComplete();
            link.Tick();

            Assert.IsFalse(part.TryTransitionTo(PartState.Carried),
                "용접한 파츠를 다시 들 수 있으면 안 된다.");
        }

        [Test]
        public void JointBreak_ResetsWeldProgress()
        {
            var (part, link, seam) = AssembledPair();
            var half = PhysicsScale.WeldSeamSegments / 2;

            for (var i = 0; i < half; i++)
                seam.TryWeld(PointAtSegment(seam, i), PhysicsScale.WeldSeamSecondsPerSegment);

            Assert.AreEqual(0.5f, seam.Ratio, 0.001f);

            Object.DestroyImmediate(part.GetComponent<FixedJoint>());
            link.Tick();

            Assert.AreEqual(0f, seam.Ratio, "접합이 풀리면 용접도 없던 일이 된다.");
        }

        [Test]
        public void WeldRatio_IsZero_WhenJointHasNoSeam()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

            moving.TryTransitionTo(PartState.Carried);
            var link = PartAssembler.Assemble(PartAssembler.FindMatch(moving));
            Assert.IsNotNull(link);
            link.RefreshSeam();

            Assert.AreEqual(0f, link.WeldRatio);
            Assert.IsFalse(link.IsFullyWelded,
                "접합선이 없는 접합이 저절로 완성되면 안 된다.");
        }

        // --- 플라즈마 용접기 연결 -------------------------------------------

        /// <summary>
        /// 용접기는 S15P21B202-168([Art])이 만들었고 <b>효과가 없었다</b> — 좌클릭하면
        /// 색만 바뀌었다. 노즐이 사거리 안에 있으면 접합선을 채워야 한다.
        /// </summary>
        [Test]
        public void Tool_WeldsTheSeamWithinRange()
        {
            var (_, link, seam) = AssembledPair();
            var tool = MakeTool(PointAt(seam, 0f));

            Assert.IsTrue(tool.WeldTick(PhysicsScale.WeldSeamSecondsPerSegment, PlasmaBeamMode.Weld),
                "사거리 안이면 용접이 진행돼야 한다.");
            Assert.AreSame(seam, tool.WeldingSeam, "무엇을 용접하는지 알려야 한다.");
            Assert.Greater(link.WeldRatio, 0f);
        }

        /// <summary>기획 §16.1 — "용접기 최대 사거리는 약 0.8m".</summary>
        [Test]
        public void Tool_DoesNothingBeyondRange()
        {
            var (_, link, seam) = AssembledPair();
            var far = PointAt(seam, 0f) + Vector3.up * (PhysicsScale.WeldSeamRange + 0.5f);
            var tool = MakeTool(far);

            Assert.IsFalse(tool.WeldTick(1f, PlasmaBeamMode.Weld), "사거리 밖이면 진행되지 않는다.");
            Assert.IsNull(tool.WeldingSeam);
            Assert.AreEqual(0f, link.WeldRatio);
        }

        /// <summary>
        /// 절단은 후속 이슈(§16.3)지만, 절단 모드에서 용접이 진행되면 안 된다 —
        /// 하나의 노즐에서 나가는 상호 배타적 기능이다.
        /// </summary>
        [Test]
        public void Tool_DoesNotWeldInCutMode()
        {
            var (_, link, seam) = AssembledPair();
            var tool = MakeTool(PointAt(seam, 0f));

            Assert.IsFalse(tool.WeldTick(1f, PlasmaBeamMode.Cut), "절단 모드에서는 용접하지 않는다.");
            Assert.AreEqual(0f, link.WeldRatio);
        }

        /// <summary>
        /// 용접기로도 한 자리에서는 완성할 수 없다. §16.1 의 규칙이 도구를 거쳐도
        /// 그대로여야 한다 — 여기가 실제로 플레이어가 겪는 경로다.
        /// </summary>
        [Test]
        public void Tool_CannotFinishFromOneSpot()
        {
            var (_, _, seam) = AssembledPair();
            var tool = MakeTool(PointAt(seam, 0f));

            for (var i = 0; i < 200; i++)   // 넉넉히 4초
                tool.WeldTick(0.02f, PlasmaBeamMode.Weld);

            Assert.IsFalse(seam.IsComplete, "한 자리에 대고 있으면 완성되지 않는다.");
            Assert.AreEqual(1f / PhysicsScale.WeldSeamSegments, seam.Ratio, 0.001f,
                "조각 하나만 찬다.");
        }

        /// <summary>노즐을 접합선 위로 옮겨 다니면 완성된다.</summary>
        [Test]
        public void Tool_CompletesWhenWalkedAroundTheSeam()
        {
            var (_, _, seam) = AssembledPair();
            var tool = MakeTool(PointAt(seam, 0f));

            for (var i = 0; i < seam.SegmentCount; i++)
            {
                tool.transform.position = PointAtSegment(seam, i);
                UnityEngine.Physics.SyncTransforms();
                tool.WeldTick(PhysicsScale.WeldSeamSecondsPerSegment, PlasmaBeamMode.Weld);
            }

            Assert.IsTrue(seam.IsComplete, "한 바퀴 돌면 완성돼야 한다.");
        }

        // --- 헬퍼 ----------------------------------------------------------

        /// <summary>아래 파츠 위에 조립된 위 파츠와 그 링크, 접합선을 만든다.</summary>
        (SpaceshipPart Part, PartAssemblyLink Link, WeldSeam Seam) AssembledPair()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);

            var moving = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

            var seam = AddSeam(moving);
            moving.TryTransitionTo(PartState.Carried);

            var link = PartAssembler.Assemble(PartAssembler.FindMatch(moving));
            Assert.IsNotNull(link, "조립이 되어야 용접 검증을 할 수 있다.");
            link.RefreshSeam();

            return (moving, link, seam);
        }

        /// <summary>
        /// 노즐이 <paramref name="nozzle"/> 에 있는 용접기.
        ///
        /// <c>Muzzle</c> 은 자식이 없으면 자기 Transform 이므로 위치만 두면 된다.
        /// 장착 절차를 흉내내지 않는 이유는 <c>WeldTick</c> 이 모드를 인수로 받기
        /// 때문이다 — 보고 싶은 것은 기하 판정이다.
        /// </summary>
        PlasmaWeldingTool MakeTool(Vector3 nozzle)
        {
            var go = new GameObject("PlasmaWeldingTool");
            go.transform.position = nozzle;
            _spawned.Add(go);

            UnityEngine.Physics.SyncTransforms();
            return go.AddComponent<PlasmaWeldingTool>();
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

        /// <summary>접합선 위 <paramref name="degrees"/> 각도의 지점.</summary>
        static Vector3 PointAt(WeldSeam seam, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var local = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * seam.Radius;
            return seam.transform.TransformPoint(local);
        }

        /// <summary><paramref name="index"/> 번째 조각의 가운데 지점.</summary>
        static Vector3 PointAtSegment(WeldSeam seam, int index)
        {
            var span = 360f / seam.SegmentCount;
            return PointAt(seam, index * span + span * 0.5f);
        }

        /// <summary>
        /// 조각마다 <paramref name="secondsPerSegment"/> 씩 대며 한 바퀴 돈다.
        /// 쓴 시간을 돌려준다.
        /// </summary>
        static float SweepOnce(WeldSeam seam, float secondsPerSegment)
        {
            var spent = 0f;
            for (var i = 0; i < seam.SegmentCount; i++)
            {
                seam.TryWeld(PointAtSegment(seam, i), secondsPerSegment);
                spent += secondsPerSegment;
            }

            return spent;
        }
    }
}
