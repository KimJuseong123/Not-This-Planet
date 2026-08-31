using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 고정못이 남은 파츠의 부분 부양과, 자유 파츠의 자세 복구를 검증한다(기획 §14.4).
    ///
    /// 예전에는 고정못이 하나라도 남으면 파츠가 Locked 이고 Locked 는 kinematic 이라
    /// 저중력장이 아예 건너뛰었다. 셋을 뽑든 하나를 뽑든 꿈쩍도 하지 않아, 마지막 하나를
    /// 뽑기 전까지 진행 상황이 화면에 전혀 드러나지 않았다.
    /// </summary>
    public sealed class PartialLiftTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
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
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
        }

        /// <summary>고정못 넷이 박힌 파츠. 밑면 네 귀퉁이에 둔다.</summary>
        SpaceshipPart MakeClampedPart(Vector3 center)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Part";
            go.layer = GameLayers.Part;
            go.transform.position = center;
            go.transform.localScale = new Vector3(2f, 2f, 2f);
            spawned.Add(go);

            go.AddComponent<Rigidbody>();

            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector3(i is 0 or 2 ? -0.4f : 0.4f, -0.5f, i < 2 ? -0.4f : 0.4f);

                var clampGo = new GameObject($"Clamp_{i}") { layer = GameLayers.Interactable };
                clampGo.transform.SetParent(go.transform, false);
                clampGo.transform.localPosition = corner;

                var sphere = clampGo.AddComponent<SphereCollider>();
                sphere.radius = 0.12f;
                sphere.isTrigger = true;

                clampGo.AddComponent<PartClamp>();
            }

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            return part;
        }

        LowGravityField MakeField(Vector3 apex)
        {
            var go = new GameObject("Craft");
            go.transform.position = apex;
            spawned.Add(go);

            var field = go.AddComponent<LowGravityField>();
            field.SetActive(true);
            return field;
        }

        void Run(LowGravityField field, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                if (field != null) field.Tick(Step);
                UnityEngine.Physics.Simulate(Step);
            }
        }

        /// <summary>
        /// 고정쇠 하나를 푼다. 상호작용을 거치지 않고 결과 상태만 만든다.
        ///
        /// 풀린 고정못은 그 자리에 물리 소품으로 남는데(PartClamp.Release), 파츠 밑면에
        /// 붙어 있어 그대로 두면 부딪혀 파츠를 돌린다. 여기서 보려는 것은 부양과 세우기이므로
        /// 치운다. 게임에서는 하나씩 뽑는 사이에 떨어져 나가므로 이런 일이 없다.
        /// </summary>
        static void ReleaseClamp(SpaceshipPart part, int index)
        {
            var clamp = part.Clamps[index];
            clamp.Release();

            if (clamp.ReleasedBody != null) Object.DestroyImmediate(clamp.ReleasedBody.gameObject);
        }

        // --- 부분 부양 -------------------------------------------------------

        [Test]
        public void FullyClamped_StaysKinematic()
        {
            var part = MakeClampedPart(new Vector3(0f, 1f, 0f));

            Assert.IsTrue(part.GetComponent<Rigidbody>().isKinematic,
                "하나도 안 풀린 파츠는 예전처럼 꿈쩍하지 않아야 한다.");
            Assert.AreEqual(0, part.ClampConstraintCount, "구속을 걸 이유가 없다.");
        }

        /// <summary>
        /// 이 규칙의 핵심. 하나만 풀려도 파츠가 물리로 움직이기 시작한다.
        /// 예전에는 마지막 하나를 뽑기 전까지 아무 변화가 없었다.
        /// </summary>
        [Test]
        public void PartiallyReleased_BecomesDynamicAndKeepsRemainingClampsPinned()
        {
            var part = MakeClampedPart(new Vector3(0f, 1f, 0f));

            ReleaseClamp(part, 0);

            Assert.IsFalse(part.GetComponent<Rigidbody>().isKinematic,
                "고정못이 하나 풀리면 물리로 움직여야 한다.");
            Assert.AreEqual(PartState.Locked, part.State, "아직 셋이 남았으므로 Free 가 아니다.");
            Assert.AreEqual(3, part.ClampConstraintCount,
                "남은 고정못 수만큼 지면 구속이 있어야 한다.");
        }

        [Test]
        public void ConstraintCount_FollowsRemainingClamps()
        {
            var part = MakeClampedPart(new Vector3(0f, 1f, 0f));

            ReleaseClamp(part, 0);
            Assert.AreEqual(3, part.ClampConstraintCount);

            ReleaseClamp(part, 1);
            Assert.AreEqual(2, part.ClampConstraintCount);

            ReleaseClamp(part, 2);
            Assert.AreEqual(1, part.ClampConstraintCount,
                "하나만 남으면 그 지점을 중심으로 기울 수 있어야 한다.");

            ReleaseClamp(part, 3);
            Assert.AreEqual(PartState.Free, part.State, "모두 풀리면 자유 파츠가 된다.");
            Assert.AreEqual(0, part.ClampConstraintCount, "자유 파츠는 지면에 묶이지 않는다.");
        }

        /// <summary>기획 §14.4 — "고정못을 힘으로 강제 제거할 수는 없다".</summary>
        [Test]
        public void RemainingClamps_DoNotBreakUnderTheField()
        {
            var part = MakeClampedPart(new Vector3(0f, 1f, 0f));
            ReleaseClamp(part, 0);

            var field = MakeField(new Vector3(0f, 9f, 0f));
            Run(field, 3f);

            Assert.AreEqual(3, part.ClampConstraintCount,
                "저중력장이 아무리 당겨도 남은 고정못은 뽑히지 않아야 한다.");
            Assert.Less(part.transform.position.y, 4f,
                "지면에 묶여 있으므로 통째로 떠오르면 안 된다.");
        }

        /// <summary>하나만 남으면 그 지점을 중심으로 크게 기운다(기획 §14.4).</summary>
        [Test]
        public void SingleRemainingClamp_LetsThePartTiltFar()
        {
            var part = MakeClampedPart(new Vector3(0f, 1f, 0f));
            for (var i = 0; i < 3; i++) ReleaseClamp(part, i);

            Assert.AreEqual(1, part.ClampConstraintCount, "이 검증은 하나만 남아야 의미가 있다.");

            var field = MakeField(new Vector3(0f, 9f, 0f));
            Run(field, 3f);

            var tilt = Vector3.Angle(part.transform.up, Vector3.up);
            Assert.Greater(tilt, 10f,
                $"기울기가 {tilt:0.0}도 다. 한 점만 잡혀 있으면 크게 기울어야 한다.");
        }

        // --- 자세 복구 -------------------------------------------------------

        /// <summary>
        /// 기획 §14.4 — "모든 고정못이 제거된 파츠는 견인 시 약 2초에 걸쳐 바닥이 아래를
        /// 향하도록 자세를 보정한다". 바닥에 넘어진 파츠를 다시 일으키는 것도 같은 규칙이다.
        /// </summary>
        [Test]
        public void FreePart_UprightsInsideTheField()
        {
            var part = MakeClampedPart(new Vector3(0f, 5f, 0f));
            for (var i = 0; i < 4; i++) ReleaseClamp(part, i);

            // 옆으로 넘어뜨린다.
            part.transform.rotation = Quaternion.Euler(0f, 0f, 75f);
            UnityEngine.Physics.SyncTransforms();

            var before = Vector3.Angle(part.transform.up, Vector3.up);
            Assert.Greater(before, 60f, "이 검증은 넘어진 상태에서 시작해야 한다.");

            var field = MakeField(new Vector3(0f, 9f, 0f));
            Run(field, 3f);

            var after = Vector3.Angle(part.transform.up, Vector3.up);
            Assert.Less(after, 10f,
                $"{before:0}도 에서 {after:0}도 로만 돌아왔다. 약 2초에 걸쳐 서야 한다.");
        }

        /// <summary>
        /// 요는 건드리지 않는다. S15P21B202-172 가 없앤 것이 "놓을 때 방향까지 맞춰 주는"
        /// 동작이므로, 세우기가 그것을 되살리면 안 된다.
        /// </summary>
        [Test]
        public void Uprighting_DoesNotChangeYaw()
        {
            var part = MakeClampedPart(new Vector3(0f, 5f, 0f));
            for (var i = 0; i < 4; i++) ReleaseClamp(part, i);

            part.transform.rotation = Quaternion.Euler(0f, 40f, 0f);   // 이미 서 있고 요만 돌아 있다
            UnityEngine.Physics.SyncTransforms();

            var field = MakeField(new Vector3(0f, 9f, 0f));
            Run(field, 2f);

            var yaw = part.transform.rotation.eulerAngles.y;
            Assert.AreEqual(40f, Mathf.DeltaAngle(0f, yaw), 8f,
                $"요가 40도 에서 {Mathf.DeltaAngle(0f, yaw):0}도 로 바뀌었다. 방향은 플레이어가 정한다.");
        }

        /// <summary>고정못이 남은 파츠는 세우지 않는다. 기울어 있는 것이 정상이다.</summary>
        [Test]
        public void ClampedPart_IsNotUprighted()
        {
            var part = MakeClampedPart(new Vector3(0f, 1f, 0f));
            for (var i = 0; i < 3; i++) ReleaseClamp(part, i);

            var field = MakeField(new Vector3(0f, 9f, 0f));
            Run(field, 3f);

            Assert.AreEqual(PartState.Locked, part.State, "이 검증은 고정된 파츠여야 의미가 있다.");
            Assert.Greater(Vector3.Angle(part.transform.up, Vector3.up), 5f,
                "고정못이 남은 파츠를 억지로 세우면 구속과 싸워 덜컥거린다.");
        }
    }
}
