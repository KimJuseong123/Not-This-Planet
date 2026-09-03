using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 빔과 용접 판정이 <b>보이는 노즐 끝</b>에서 나가는지 검증한다(S15P21B202-277).
    ///
    /// <b>연출 문제가 아니었다.</b> 용접 건 프리팹의 <c>AttachPoints/Muzzle</c> 은
    /// 코드로 만든 임시 총(<c>PlaceholderVisual</c>, 끝이 z 0.79)에 맞춰 z 0.84 에
    /// 놓은 값인데, 장착하면 <c>ApplyHeldVisualPose</c> 가 <c>ReplacementModel</c> 아래의
    /// 실제 모델만 (0.7, −0.9, 0.25) 로 옮기고 90도 돌린다. Muzzle 은 그 모델의
    /// <b>형제</b>라 제자리에 남는다.
    ///
    /// <code>
    /// 두 점의 거리          1.34m
    /// Tuning.ToolBeamRange  0.80m
    /// </code>
    ///
    /// <b>사거리가 어긋난 거리보다 짧다.</b> 보이는 총이 닿는 곳과 빔이 닿는 곳이 겹칠
    /// 수가 없었고, <c>WeldBead.TryGrowNear(Muzzle.position, …)</c> 도 같은 좌표를 쓰므로
    /// 용접·절단 판정이 총에서 1.34m 떨어진 허공에서 이뤄졌다. 기획 §16 의 조립이
    /// 여기에 걸려 있다.
    /// </summary>
    public sealed class NozzleTipTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        // --- 어긋남의 크기 (이 이슈가 존재하는 이유) --------------------------

        /// <summary>
        /// <b>사거리가 어긋난 거리보다 짧다는 것</b>이 이 이슈의 핵심이다. 이 검증은
        /// 그 산술을 문서로 남긴다 — 휴대 자세를 누가 조정하면 함께 다시 봐야 한다.
        /// </summary>
        [Test]
        public void TheStaleAttachPointIsFartherThanTheBeamCanReach()
        {
            // 프리팹에 적힌 값이다.
            var staleMuzzle = new Vector3(0f, 0.08f, 0.84f);
            var heldVisual = new Vector3(0.7f, -0.9f, 0.25f);

            var gap = (staleMuzzle - heldVisual).magnitude;

            Assert.Greater(gap, Tuning.ToolBeamRange,
                $"어긋남 {gap:0.00}m 가 사거리 {Tuning.ToolBeamRange:0.00}m 보다 짧으면 "
                + "보이는 총이 닿는 곳과 빔이 닿는 곳이 겹칠 수 있다 — 이 이슈의 전제가 사라진다.");
            Assert.AreEqual(1.34f, gap, 0.01f);
        }

        // --- 끝점을 실제로 옮긴다 ---------------------------------------------

        [Test]
        public void TheNozzleMovesOntoTheVisibleModel()
        {
            var (tool, model) = MakeWelder();

            Assert.IsTrue(tool.ResolveNozzleTip(Vector3.forward));

            var toModel = tool.Muzzle.position - model.position;

            Assert.Less(toModel.magnitude, 1f,
                "기준점이 보이는 총 위에 있어야 한다.");
        }

        [Test]
        public void WithNoVisibleModel_NothingChanges()
        {
            var go = new GameObject("빈 용접 건");
            _spawned.Add(go);
            var tool = go.AddComponent<PlasmaWeldingTool>();

            Assert.IsFalse(tool.ResolveNozzleTip(Vector3.forward),
                "모델이 없으면 손대지 않는다 — 기존 동작이 유지되어야 한다.");
        }

        [Test]
        public void TheNozzleFollowsTheHeldPose()
        {
            var (tool, model) = MakeWelder();

            tool.ResolveNozzleTip(Vector3.forward);
            var before = tool.Muzzle.position;

            // 장착 자세가 하는 일이다 — 모델만 옮기고 돌린다.
            model.localPosition = new Vector3(0.7f, -0.9f, 0.25f);
            model.localRotation = Quaternion.Euler(-90f, -80f, 10f);
            UnityEngine.Physics.SyncTransforms();

            tool.ResolveNozzleTip(Vector3.forward);

            Assert.AreNotEqual(before, tool.Muzzle.position,
                "모델이 움직였는데 기준점이 남아 있으면 이 이슈가 되돌아온다.");

            var toModel = tool.Muzzle.position - model.position;
            Assert.Less(toModel.magnitude, 1f, "옮겨진 총 위에 붙어 있어야 한다.");
        }

        [Test]
        public void ResolvingTwice_DoesNotMakeASecondNozzle()
        {
            var (tool, _) = MakeWelder();

            tool.ResolveNozzleTip(Vector3.forward);
            var first = tool.Muzzle;

            tool.ResolveNozzleTip(Vector3.right);

            Assert.AreSame(first, tool.Muzzle);
        }

        // --- 기하: 조준 방향에서 본 끝 -----------------------------------------

        [Test]
        public void TheTipIsAheadOfTheCenter()
        {
            var space = MakeSpace(Vector3.zero, Quaternion.identity);
            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1.6f));

            var tip = NozzleGeometry.TipAlong(space, bounds, Vector3.forward);

            Assert.AreEqual(0.8f, tip.z, 0.001f, "+Z 로 보면 앞쪽 끝이어야 한다.");
        }

        /// <summary>
        /// <b>모서리가 아니라 지지면의 중심이다.</b> 모서리를 쓰면 총 옆으로 비켜난
        /// 자리에서 빔이 나간다.
        /// </summary>
        [Test]
        public void TheTipIsNotACorner()
        {
            var space = MakeSpace(Vector3.zero, Quaternion.identity);
            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1.6f));

            var tip = NozzleGeometry.TipAlong(space, bounds, Vector3.forward);

            Assert.AreEqual(0f, tip.x, 0.001f);
            Assert.AreEqual(0f, tip.y, 0.001f);
        }

        /// <summary>
        /// 총의 어느 축이 총구인지 <b>추측하지 않는다.</b> FBX 마다 다르고 휴대 자세가
        /// 90도 돌리므로, 축을 정해 두면 모델이 바뀔 때마다 어긋난다.
        /// </summary>
        [Test]
        public void TheTipFollowsTheAimDirection()
        {
            var space = MakeSpace(Vector3.zero, Quaternion.identity);
            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1.6f));

            var ahead = NozzleGeometry.TipAlong(space, bounds, Vector3.forward);
            var behind = NozzleGeometry.TipAlong(space, bounds, Vector3.back);

            Assert.Greater(ahead.z, 0f);
            Assert.Less(behind.z, 0f);
        }

        [Test]
        public void TheTipRotatesWithTheModel()
        {
            var space = MakeSpace(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1.6f));

            // 모델이 +X 를 보게 돌았다. 세계 +X 로 조준하면 긴 축이 그 방향이다.
            var tip = NozzleGeometry.TipAlong(space, bounds, Vector3.right);

            Assert.AreEqual(0.8f, tip.x, 0.001f);
        }

        [Test]
        public void TheTipMovesWithTheModel()
        {
            var space = MakeSpace(new Vector3(3f, 1f, -2f), Quaternion.identity);
            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1.6f));

            var tip = NozzleGeometry.TipAlong(space, bounds, Vector3.forward);

            Assert.AreEqual(new Vector3(3f, 1f, -1.2f), tip);
        }

        [Test]
        public void ScaleIsRespected()
        {
            var space = MakeSpace(Vector3.zero, Quaternion.identity);
            space.localScale = new Vector3(1f, 1f, 2f);
            UnityEngine.Physics.SyncTransforms();

            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1.6f));

            var tip = NozzleGeometry.TipAlong(space, bounds, Vector3.forward);

            Assert.AreEqual(1.6f, tip.z, 0.01f, "두 배로 늘린 총은 끝도 두 배 멀다.");
        }

        [Test]
        public void AZeroDirection_FallsBackToTheModelForward()
        {
            var space = MakeSpace(Vector3.zero, Quaternion.identity);
            var bounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1.6f));

            var tip = NozzleGeometry.TipAlong(space, bounds, Vector3.zero);

            Assert.AreEqual(0.8f, tip.z, 0.001f, "0 을 넣어도 정의된 답이 있어야 한다.");
        }

        [Test]
        public void ANullSpace_DoesNotThrow()
        {
            var bounds = new Bounds(new Vector3(1f, 2f, 3f), Vector3.one);

            Assert.AreEqual(bounds.center, NozzleGeometry.TipAlong(null, bounds, Vector3.forward));
        }

        // --- 경계 상자 --------------------------------------------------------

        [Test]
        public void TheBoundsUnionEveryRenderer()
        {
            var root = MakeSpace(Vector3.zero, Quaternion.identity);
            AddBox(root, new Vector3(0f, 0f, 0f), Vector3.one);
            AddBox(root, new Vector3(0f, 0f, 2f), Vector3.one);

            Assert.IsTrue(NozzleGeometry.TryLocalBounds(root, out var bounds));

            Assert.Greater(bounds.extents.z, 1f, "두 조각을 모두 감싸야 한다.");
        }

        /// <summary>
        /// <b>꼬리와 파티클은 세지 않는다.</b> 크기가 매 프레임 변해 끝점을 흔든다 —
        /// 빔이 나가는 자리가 떨리면 조준할 수 없다.
        /// </summary>
        [Test]
        public void TrailsAreIgnored()
        {
            var root = MakeSpace(Vector3.zero, Quaternion.identity);
            AddBox(root, Vector3.zero, Vector3.one);

            Assert.IsTrue(NozzleGeometry.TryLocalBounds(root, out var withoutTrail));

            var trail = new GameObject("꼬리");
            trail.transform.SetParent(root, false);
            trail.transform.localPosition = new Vector3(0f, 0f, 20f);
            trail.AddComponent<TrailRenderer>();
            UnityEngine.Physics.SyncTransforms();

            Assert.IsTrue(NozzleGeometry.TryLocalBounds(root, out var withTrail));

            Assert.AreEqual(withoutTrail.extents.z, withTrail.extents.z, 0.01f);
        }

        [Test]
        public void WithNoRenderer_ItFails()
        {
            var root = MakeSpace(Vector3.zero, Quaternion.identity);

            Assert.IsFalse(NozzleGeometry.TryLocalBounds(root, out _));
        }

        [Test]
        public void ANullRoot_Fails()
        {
            Assert.IsFalse(NozzleGeometry.TryLocalBounds(null, out _));
        }

        // --- 리그 -------------------------------------------------------------

        Transform MakeSpace(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("모델 기준");
            _spawned.Add(go);

            go.transform.SetPositionAndRotation(position, rotation);
            UnityEngine.Physics.SyncTransforms();
            return go.transform;
        }

        static void AddBox(Transform parent, Vector3 localPosition, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;

            Object.DestroyImmediate(go.GetComponent<Collider>());
            UnityEngine.Physics.SyncTransforms();
        }

        /// <summary>
        /// 프리팹과 같은 모양을 세운다 — <c>ResolveHeldVisual</c> 이 찾는 경로다.
        /// </summary>
        (PlasmaWeldingTool Tool, Transform Model) MakeWelder()
        {
            var root = new GameObject("용접 건");
            _spawned.Add(root);

            var attach = new GameObject("AttachPoints").transform;
            attach.SetParent(root.transform, false);

            // 임시 총에 맞춰 놓은 옛 기준점. 이것이 이 이슈의 원인이다.
            var stale = new GameObject("Muzzle").transform;
            stale.SetParent(attach, false);
            stale.localPosition = new Vector3(0f, 0.08f, 0.84f);

            var replacement = new GameObject("ReplacementModel").transform;
            replacement.SetParent(attach, false);

            var model = new GameObject("SF_Stage1_PlasmaWeldingGun").transform;
            model.SetParent(replacement, false);
            AddBox(model, Vector3.zero, new Vector3(0.15f, 0.15f, 0.9f));

            UnityEngine.Physics.SyncTransforms();

            return (root.AddComponent<PlasmaWeldingTool>(), model);
        }
    }
}
