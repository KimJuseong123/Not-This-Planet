using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 용접 조인트 표시를 언제 다시 짓는가 (S15P21B202-722).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 이 판정이 느슨해지면 <b>멀티에서 용접건을 드는
    /// 순간 프레임이 떨어진다.</b> 다시 짓는 일이 파츠 수에 대해 O(n²) 이고 메시까지
    /// 새로 만드는데, 참가자 화면의 파츠는 <c>WorldSync</c> 가 목표를 향해 <c>Lerp</c> 로
    /// 미는 키네마틱 강체라 <b>눈에 멈춰 있어도 행렬이 매 프레임 조금씩 달라진다.</b>
    /// 행렬을 그대로 견주면 그 미세한 차이가 "움직였다" 로 읽혀 스로틀이 통째로
    /// 무시된다 — 싱글에서 안 보이던 이유는 방장 쪽 파츠가 멈추면 잠들어서다.</para>
    /// </summary>
    public sealed class WeldingJointGuideTests
    {
        [Test]
        public void DenseContactBuildIsBoundedAndStaleGeometryDisappearsImmediately()
        {
            var objects = new List<GameObject>();
            var guide = new WeldingJointGuide(Color.yellow);
            try
            {
                var a = SocketTestRig.Placed(objects, Vector3.zero);
                var b = SocketTestRig.Placed(objects, Vector3.right);
                Physics.SyncTransforms();
                guide.Show(Vector3.zero, 4f, 0.012f);
                Assert.IsTrue(guide.IsBuilding, "Dense geometry must not finish in one blocking frame.");
                for (int i = 0; i < 2048 && guide.IsBuilding; i++)
                {
                    guide.Show(Vector3.zero, 4f, 0.012f);
                    Assert.LessOrEqual(guide.LastWorkCount, WeldingJointGuide.WorkPerFrame);
                }
                Assert.IsFalse(guide.IsBuilding, "Stable contact must eventually finish.");
                Assert.IsTrue(guide.Visible);
                b.transform.position += Vector3.right * 10f;
                Physics.SyncTransforms();
                guide.Show(Vector3.zero, 4f, 0.012f);
                Assert.IsFalse(guide.Visible, "Never show a recommendation at a departed part.");
                guide.Hide();
                Assert.IsFalse(guide.IsBuilding);
            }
            finally
            {
                guide.Dispose();
                foreach (var go in objects) if (go != null) Object.DestroyImmediate(go);
            }
        }

        static Matrix4x4 At(Vector3 position) =>
            Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);

        // --- 안 움직인 것 ------------------------------------------------------

        [Test]
        public void TheSamePoseIsNotMovement()
        {
            var pose = At(new Vector3(1f, 2f, 3f));

            Assert.IsFalse(WeldingJointGuide.Moved(pose, pose));
        }

        /// <summary>
        /// <b>이것이 이 검증의 본체다.</b> Lerp 가 목표에 정확히 닿지 않아 생기는
        /// 떨림은 움직임이 아니다.
        /// </summary>
        [Test]
        public void NetworkJitterIsNotMovement()
        {
            var before = At(new Vector3(1f, 2f, 3f));
            var after = At(new Vector3(1f + 0.0001f, 2f - 0.0001f, 3f + 0.0001f));

            Assert.IsFalse(WeldingJointGuide.Moved(after, before),
                "보간 떨림을 움직임으로 읽으면 매 프레임 다시 짓는다.");
        }

        // --- 움직인 것 --------------------------------------------------------

        /// <summary>사람이 파츠를 옮기는 것은 반드시 잡아야 한다 — 표시가 옛 자리에 남는다.</summary>
        [Test]
        public void ARealMoveIsMovement()
        {
            var before = At(Vector3.zero);

            Assert.IsTrue(WeldingJointGuide.Moved(At(new Vector3(0.05f, 0f, 0f)), before));
            Assert.IsTrue(WeldingJointGuide.Moved(At(new Vector3(0f, 1f, 0f)), before));
        }

        /// <summary>
        /// 제자리에서 도는 파츠도 잡는다. 위치만 보면 표시가 <b>옛 면</b>에 남는다.
        /// </summary>
        [Test]
        public void RotatingInPlaceIsMovement()
        {
            var before = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            var after = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 30f, 0f), Vector3.one);

            Assert.IsTrue(WeldingJointGuide.Moved(after, before));
        }

        /// <summary>크기가 바뀌어도 닿는 면이 달라진다.</summary>
        [Test]
        public void ScalingIsMovement()
        {
            var before = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            var after = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 1.5f);

            Assert.IsTrue(WeldingJointGuide.Moved(after, before));
        }

        // --- 경계 --------------------------------------------------------------

        /// <summary>
        /// 문턱은 비드 접촉 판정보다 훨씬 작아야 한다. 이보다 커지면 <b>표시가
        /// 실제 파츠를 따라가지 못한다</b> — 성능을 고치려다 표시를 틀리게 만든다.
        /// </summary>
        [Test]
        public void TheThresholdStaysFarBelowWeldContact()
        {
            Assert.Less(WeldingJointGuide.MoveEpsilon, PhysicsScale.WeldBeadContactSkin * 0.25f);
            Assert.Greater(WeldingJointGuide.MoveEpsilon, 0f);
        }

        [Test]
        public void JustOverTheThresholdCounts()
        {
            var before = At(Vector3.zero);
            var after = At(new Vector3(WeldingJointGuide.MoveEpsilon * 2f, 0f, 0f));

            Assert.IsTrue(WeldingJointGuide.Moved(after, before));
        }
    }
}
