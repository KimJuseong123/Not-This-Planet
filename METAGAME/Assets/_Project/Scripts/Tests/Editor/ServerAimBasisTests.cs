using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 카메라 없이 조작될 때 "앞" 이 어느 쪽인지 검증한다(S15P21B202-147).
    ///
    /// 물리는 서버가 돌리는데 카메라는 각자 PC 에만 있다. 그래서 클라이언트가
    /// 지금 보고 있는 각도를 입력에 실어 보내고(<see cref="InputFrame.AimYaw"/>),
    /// 서버는 그것으로 이동 기준을 세운다. 이 값이 무시되면 W 를 눌러도 언제나
    /// 월드 +Z 로만 걸어가 — 실제로 처음 붙였을 때 그렇게 됐다.
    ///
    /// <c>LogAssert.ignoreFailingMessages</c> 는 ClampPullTests 와 같은 이유다 —
    /// <c>PlayerRig.Create</c> 가 EditMode 에서 남기는 에러 로그(develop 기존 문제).
    /// </summary>
    public sealed class ServerAimBasisTests
    {
        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _savedMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            MakeGround();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _savedMode;
            LogAssert.ignoreFailingMessages = false;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(120f, 1f, 120f);
            _spawned.Add(go);
        }

        /// <summary>카메라 없이 선 몸 하나. 서버에 있는 몸과 같은 모양이다.</summary>
        PlayerRig SpawnWithoutCamera()
        {
            var rig = PlayerRig.Create("서버몸", Vector3.zero, Quaternion.identity,
                                       Color.white, Color.gray, useModel: false);
            _spawned.Add(rig.gameObject);
            Physics.SyncTransforms();

            Assert.IsNull(rig.Cam, "이 검증은 카메라가 없는 몸을 전제로 한다.");
            return rig;
        }

        /// <summary>앞으로 걷게 하고 실제로 간 방향(수평)을 돌려준다.</summary>
        Vector3 WalkForward(PlayerRig rig, float aimYaw, float seconds)
        {
            var frame = new InputFrame { Move = new Vector2(0f, 1f), AimYaw = aimYaw };

            // 발이 땅에 닿아 안정될 때까지 잠깐 세워 둔다.
            Step(rig, new InputFrame { AimYaw = aimYaw }, 1.0f);

            Vector3 from = rig.Hips.position;
            Step(rig, frame, 2.5f);
            Vector3 moved = rig.Hips.position - from;

            moved.y = 0f;
            return moved;
        }

        static void Step(PlayerRig rig, InputFrame frame, float seconds)
        {
            var step = Time.fixedDeltaTime;
            for (var s = 0; s < Mathf.Max(1, Mathf.RoundToInt(seconds / step)); s++)
            {
                rig.SetInput(frame);
                rig.Tick();
                Physics.Simulate(step);
            }
        }

        [Test]
        public void 보내온각도가0이면_월드앞으로걷는다()
        {
            var moved = WalkForward(SpawnWithoutCamera(), 0f, 2.5f);

            Assert.Greater(moved.magnitude, 1.0f, "걷지 않았다.");
            Assert.Greater(moved.z, moved.magnitude * 0.8f,
                $"yaw 0 이면 +Z 로 가야 한다 (실제 {moved}).");
        }

        [Test]
        public void 보내온각도가90도면_그쪽으로걷는다()
        {
            // yaw 90 = 오른쪽(+X). 카메라가 있었다면 화면의 "앞" 이 그쪽이다.
            var moved = WalkForward(SpawnWithoutCamera(), 90f, 2.5f);

            Assert.Greater(moved.magnitude, 1.0f, "걷지 않았다.");
            Assert.Greater(moved.x, moved.magnitude * 0.8f,
                $"yaw 90 이면 +X 로 가야 한다 (실제 {moved}). " +
                "보내온 조준 각도가 무시되면 여기서 +Z 로 간다.");
        }

        [Test]
        public void 보내온각도가180도면_반대로걷는다()
        {
            var moved = WalkForward(SpawnWithoutCamera(), 180f, 2.5f);

            Assert.Greater(moved.magnitude, 1.0f, "걷지 않았다.");
            Assert.Less(moved.z, -moved.magnitude * 0.8f,
                $"yaw 180 이면 -Z 로 가야 한다 (실제 {moved}).");
        }
    }
}
