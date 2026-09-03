using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 고정못을 뒤로 이동해 뽑는 조작을 검증한다(기획 S15P21B202-107 §12.1 · §12.2).
    ///
    /// 예전에는 <b>마우스를 위로 16도 들어올려</b> 홀드를 시작하고 1.5초 기다렸다.
    /// 기획은 "좌클릭을 유지한 채 고정못 반대 방향으로 뒤로 이동해 뽑는다" 이고,
    /// 시간도 §12.2 의 약 8초다.
    ///
    /// <c>PartClampTests</c> 는 고정못 자체의 진행분을 본다. 여기서 보는 것은
    /// <b>방향 판정</b> 이다 — 어느 입력이 "뒤로 당기는 것" 인가.
    ///
    /// <c>LogAssert.ignoreFailingMessages</c> 를 켜는 이유는 이 규칙과 무관하다 —
    /// <c>PlayerRig.Create</c> 경로의 <c>Prim.Decor</c> 가 EditMode 에서
    /// <c>Object.Destroy</c> 를 불러 에러 로그를 남긴다(develop 에 이미 있는 문제).
    /// </summary>
    public sealed class ClampPullTests
    {
        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            // 정리 중에도 인형 파괴가 에러 로그를 남긴다. 다시 켜 둔다.
            LogAssert.ignoreFailingMessages = true;
            UnityEngine.Physics.simulationMode = _savedMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
            LowGravityField.All.Clear();

            // 되돌리지 않는다. 인형의 장갑을 만들 때 나는 "Destroy may not be called from
            // edit mode" 에러가 이 테스트가 끝난 뒤 검사에 걸린다. 프레임워크가 테스트마다
            // 이 플래그를 초기화하므로 다른 픽스처로 새지 않는다
            // (TransportDismountTests 도 같은 방식이다).
        }

        // --- 방향 판정 -----------------------------------------------------

        /// <summary>
        /// 기획 §12.1 — "좌클릭을 유지한 채 고정못 반대 방향으로 뒤로 이동해 뽑는다".
        /// </summary>
        [Test]
        public void MovingAwayFromTheClamp_PullsIt()
        {
            var rig = MakeRig();

            Run(rig, PullMode.Away, 1f);

            Assert.IsTrue(rig.Player.ClampGrabbed, "잡고 있어야 한다.");
            Assert.IsTrue(rig.Player.LeftHand.Gripping || rig.Player.RightHand.Gripping,
                "진행 전에 실제 왼손 또는 오른손이 고정쇠에 결합되어야 한다.");
            Assert.AreEqual(1, rig.Clamp.Pullers,
                "한 플레이어의 두 손이 같은 고정쇠에 중복 puller로 계산되면 안 된다.");
            var attached = rig.Player.LeftHand.Gripping ? rig.Player.LeftHand : rig.Player.RightHand;
            Assert.AreSame(rig.Clamp.GripTarget, attached.Held,
                "고정쇠 진행 상태는 실제 손이 연결된 숨김 그립 대상과 함께 있어야 한다.");
            Assert.IsNotNull(attached.GetComponent<FixedJoint>(),
                "고정쇠를 잡으면 손과 파츠 사이의 FixedJoint가 있어야 한다.");
            Assert.Greater(Held(rig).Progress, 0f, "뒤로 이동하면 진행돼야 한다.");
        }

        /// <summary>
        /// <b>제자리에서 버튼만 누르고 있으면 진행되지 않는다.</b>
        ///
        /// 예전 방식은 마우스를 한 번 들어올린 뒤에는 가만히 있어도 시간이 흘렀다.
        /// 이 검증이 그 차이를 잠근다.
        /// </summary>
        [Test]
        public void HoldingWithoutMoving_DoesNotPull()
        {
            var rig = MakeRig();

            Run(rig, PullMode.HoldStill, 2f);

            Assert.IsTrue(rig.Player.ClampGrabbed, "잡기는 되어야 한다.");
            Assert.AreEqual(0f, Held(rig).Progress, 0.001f,
                "제자리에서 버튼만 누르면 진행되지 않는다.");
            Assert.IsFalse(rig.Player.ClampPulling);
        }

        /// <summary>고정못 쪽으로 다가가는 것은 뽑는 것이 아니다.</summary>
        [Test]
        public void MovingTowardTheClamp_DoesNotPull()
        {
            var rig = MakeRig();

            Run(rig, PullMode.Toward, 2f);

            Assert.AreEqual(0f, Held(rig).Progress, 0.001f, "다가가면 진행되지 않는다.");
        }

        /// <summary>옆으로 걷는 것도 뽑는 것이 아니다.</summary>
        [Test]
        public void MovingSideways_DoesNotPull()
        {
            var rig = MakeRig();

            Run(rig, PullMode.Sideways, 2f);

            Assert.AreEqual(0f, Held(rig).Progress, 0.001f, "옆으로 걸으면 진행되지 않는다.");
        }

        // --- 완료와 누적 ---------------------------------------------------

        /// <summary>기획 §12.2 — 1명이 약 8초에 뽑는다.</summary>
        [Test]
        public void PullingSteadily_ReleasesInThePlannedTime()
        {
            var rig = MakeRig();

            var step = 0.05f;
            var spent = 0f;
            for (var i = 0; i < 1000 && !rig.Clamp.IsReleased; i++)
            {
                Run(rig, PullMode.Away, step);
                spent += step;
            }

            Assert.IsTrue(rig.Clamp.IsReleased, $"뽑혀야 한다. 진행 {rig.Clamp.Progress:0.00}");
            Assert.AreEqual(PhysicsScale.ClampPullSeconds, spent, 0.6f,
                $"기획 §12.2 의 약 8초여야 한다. {spent:0.0}초 걸렸다.");
        }

        /// <summary>
        /// 기획 §12.1 — "좌클릭을 놓거나 운석에 맞아도 빠진 정도는 유지된다".
        /// 손을 뗐을 때 <c>PlayerRig</c> 가 진행분을 지우지 않는지 본다.
        /// </summary>
        [Test]
        public void ReleasingTheButton_KeepsTheProgress()
        {
            var rig = MakeRig();

            Run(rig, PullMode.Away, 2f);
            var held = Held(rig);
            var half = held.Progress;
            Assert.Greater(half, 0f, "먼저 얼마간 뽑혀 있어야 한다.");

            Run(rig, PullMode.None, 1f);   // 버튼을 놓는다

            Assert.IsFalse(rig.Player.ClampGrabbed, "손을 떼야 한다.");
            Assert.IsFalse(rig.Player.LeftHand.Gripping || rig.Player.RightHand.Gripping,
                "버튼을 놓으면 고정쇠 FixedJoint도 함께 해제되어야 한다.");
            Assert.AreEqual(half, held.Progress, 0.001f, "빠진 정도가 남아야 한다.");
            Assert.AreEqual(0, held.Pullers, "인원이 남으면 다음 사람이 정원에 막힌다.");
        }

        /// <summary>놓았다 다시 잡으면 이어서 뽑힌다.</summary>
        [Test]
        public void GrabbingAgain_ContinuesFromWhereItStopped()
        {
            var rig = MakeRig();

            Run(rig, PullMode.Away, 2f);
            var held = Held(rig);
            var first = held.Progress;

            Run(rig, PullMode.None, 0.5f);

            Run(rig, PullMode.Away, 2f);

            Assert.Greater(held.Progress, first + 0.1f, "이어서 진행돼야 한다.");
        }

        // --- 마지막 고정못 (기획 §12.2) --------------------------------------

        /// <summary>
        /// 기획 §12.2 — "마지막 고정못이 빠지면 플레이어가 뒤로 넘어질 수 있다".
        ///
        /// 넘어짐 자체는 새로 만들지 않았다. <c>Balance</c> 가 이미 스스로 세운다
        /// (<c>S15P21B202-201</c> 의 사출과 같은 구조).
        /// </summary>
        [Test]
        public void LastClamp_KnocksThePlayerBackAndItGetsUp()
        {
            var rig = MakeRig(clampCount: 1);

            for (var i = 0; i < 1000 && !rig.Clamp.IsReleased; i++)
                Run(rig, PullMode.Away, 0.02f);

            Assert.IsTrue(rig.Clamp.IsReleased, "마지막 고정못이 빠져야 한다.");
            Assert.AreEqual(PartState.Free, rig.Part.State, "파츠가 풀려야 한다.");
            Assert.IsTrue(rig.Player.Ragdolled, "뒤로 넘어져야 한다(기획 §12.2).");

            Run(rig, PullMode.None, 5f);

            Assert.IsFalse(rig.Player.Ragdolled, "5초 안에 스스로 일어나야 한다.");
            Assert.IsFalse(rig.Player.IsDead, "고정못을 뽑다가 죽지는 않는다.");
        }

        /// <summary>
        /// 마지막 고정못 해제 중 Part_1 견인 전환이 상호작용 포커스를 재진입 갱신해도
        /// 현재 Pull 호출이 null 대상에 접근하지 않는지 검증한다.
        /// </summary>
        [Test]
        public void LastClamp_ActivatingPartTractorDoesNotClearPullTargetMidStep()
        {
            var rig = MakeRig(clampCount: 1);
            var activator = rig.Part.gameObject.AddComponent<PartTractorActivator>();

            var controllerObject = new GameObject("TransportController");
            var controller = controllerObject.AddComponent<TractorBeamController>();
            controller.Configure(rig.Player, System.Array.Empty<TractorBeam>(), false);
            _spawned.Add(controllerObject);

            for (var i = 0; i < 1000 && !rig.Clamp.IsReleased; i++)
                Run(rig, PullMode.Away, 0.02f);

            Assert.IsTrue(rig.Clamp.IsReleased, "마지막 고정못이 빠져야 한다.");
            Assert.AreEqual(PartState.Free, rig.Part.State);
            Assert.IsTrue(activator.IsActivated, "마지막 고정못 해제 중 Part_1이 견인 장치로 전환되어야 한다.");
            Assert.IsNotNull(activator.Vehicle);
            Assert.IsNull(rig.Player.ClampGrabTarget, "해제 후 고정못 대상은 정리되어야 한다.");
        }

        /// <summary>마지막이 아닌 고정못은 넘어뜨리지 않는다.</summary>
        [Test]
        public void NonLastClamp_DoesNotKnockThePlayerBack()
        {
            var rig = MakeRig(clampCount: 2);

            // 어느 고정못이 잡히는지는 조준이 정한다. 한 번 잡힌 것을 붙잡아 두고 본다.
            Run(rig, PullMode.Away, 0.5f);
            var held = rig.Player.ClampGrabTarget;
            Assert.IsNotNull(held, "먼저 하나를 잡아야 한다.");

            for (var i = 0; i < 2000 && !held.IsReleased; i++)
                Run(rig, PullMode.Away, 0.02f);

            Assert.IsTrue(held.IsReleased, "하나는 빠져야 한다.");
            Assert.AreEqual(PartState.Locked, rig.Part.State, "아직 남은 고정못이 있다.");
            Assert.IsFalse(rig.Player.Ragdolled, "남았으면 넘어지지 않는다.");
        }

        // --- 헬퍼 ----------------------------------------------------------

        struct Rig
        {
            public PlayerRig Player;
            public SpaceshipPart Part;
            public PartClamp Clamp;
        }

        /// <summary>
        /// 파츠 하나와 고정못, 그 앞에 선 플레이어.
        ///
        /// <b>기본이 고정못 하나다.</b> 처음에는 실제 파츠처럼 네 개를 놓았는데, 조준에
        /// 어느 것이 걸리는지 검증이 통제할 수 없어 엉뚱한 고정못이 잡히고 기준으로 둔
        /// 것은 진행이 0 으로 남았다. 개수가 필요한 검증만 그때 늘린다.
        /// </summary>
        Rig MakeRig(int clampCount = 1)
        {
            // 인형을 만들 때 나는 "Destroy may not be called from edit mode" 를 무시한다.
            // SetUp 에서 켜는 것으로는 이 픽스처에서 걸렸다 — 검증 본문에서 켜는 것이
            // 이 저장소에서 확인된 방식이다(TransportDismountTests 와 같다).
            LogAssert.ignoreFailingMessages = true;

            MakeGround();

            var partGo = new GameObject("Part") { layer = GameLayers.Part };
            partGo.transform.position = new Vector3(0f, PhysicsScale.PartHeight * 0.5f, 0f);
            partGo.AddComponent<BoxCollider>().size = Vector3.one * PhysicsScale.PartHeight;
            partGo.AddComponent<Rigidbody>();
            _spawned.Add(partGo);

            var part = partGo.AddComponent<SpaceshipPart>();

            PartClamp first = null;
            for (var i = 0; i < clampCount; i++)
            {
                // 첫 고정못만 플레이어 손이 닿는 곳에 둔다. 여벌은 멀리 보낸다 —
                // 실제 파츠처럼 둘레에 놓으면 반대편 고정못도 조준(3m) 안에 들어와
                // 대상이 오가고, 진행이 두 고정못에 나뉘어 어느 쪽도 끝나지 않는다.
                // 여벌의 역할은 "아직 남은 고정못이 있다" 는 것뿐이다.
                // 첫 고정못을 파츠의 -Z 쪽에 둔다. S15P21B202-218 이 캐릭터를 항상
                // 카메라 정면으로 돌리게 바꿨고, 이 검증에는 카메라가 없어 월드 +Z 를
                // 본다 — 고정못이 +Z 쪽에 있어야 조준에 들어온다. 예전에는 잡기가
                // 끈적해서(한 번 잡으면 유지) 가려졌고, 놓고 다시 잡는 검증에서 드러났다.
                var offset = i == 0
                    ? Vector3.back * 0.8f
                    : Vector3.right * (6f + i);

                var clampGo = new GameObject($"Clamp_{i}") { layer = GameLayers.Interactable };
                clampGo.transform.SetParent(partGo.transform, false);
                clampGo.transform.localPosition = offset - Vector3.up * (PhysicsScale.PartHeight * 0.5f);
                clampGo.AddComponent<SphereCollider>().isTrigger = true;

                var clamp = clampGo.AddComponent<PartClamp>();
                clamp.Initialize();

                // 해제된 고정못은 SetParent(null) 로 파츠에서 떨어져 나간다. 그러면 파츠를
                // 지워도 남아 다음 픽스처까지 살아 있고, 저중력장 검증이 그것을 센다
                // (실제로 그 4개를 깨뜨렸다). 해제 순간에 정리 목록에 넣는다.
                clamp.Released += released => _spawned.Add(released.gameObject);

                if (i == 0) first = clamp;
            }

            part.Initialize();
            part.RefreshClamps();
            UnityEngine.Physics.SyncTransforms();

            // 플레이어는 첫 고정못 바로 앞에 선다. 조준이 닿아야 잡을 수 있다.
            // 고정못이 -Z 쪽이므로 플레이어는 그보다 더 -Z 에 서고, 월드 +Z 를 보면
            // 고정못이 정면에 온다(위 offset 주석 참고).
            var toward = first.transform.position - partGo.transform.position;
            toward.y = 0f;
            var foot = first.transform.position + toward.normalized * 1.2f;
            foot.y = 0f;

            var player = PlayerRig.Create("테스트", foot,
                Quaternion.LookRotation(-toward.normalized, Vector3.up),
                Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);

            UnityEngine.Physics.SyncTransforms();
            return new Rig { Player = player, Part = part, Clamp = first };
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(60f, 1f, 60f);
            _spawned.Add(go);
        }

        /// <summary>
        /// 매 스텝 <b>지금 잡고 있는 고정못</b> 기준으로 입력을 만들어 돌린다.
        ///
        /// 고정못이 여럿이면 조준에 걸리는 것이 무엇인지 미리 알 수 없다. 처음에
        /// <c>Clamp_0</c> 기준으로 방향을 한 번만 계산했다가, 실제로는 다른 고정못이
        /// 잡혀서 진행이 0 으로 보였다. 실제 플레이도 "지금 잡은 것" 에서 멀어지는
        /// 것이므로 이쪽이 맞다.
        /// </summary>
        void Run(Rig rig, PullMode mode, float seconds)
        {
            // 프로젝트의 Fixed Timestep 은 0.01초다. PlayerRig 내부가 Time.fixedDeltaTime
            // 을 쓰므로 여기서도 같은 간격으로 돌려야 시간 측정이 맞는다 — 0.02 로 돌리면
            // 진행이 정확히 절반으로 보인다(이 검증을 처음 그렇게 짰다가 배웠다).
            var step = Time.fixedDeltaTime;
            for (var s = 0; s < Mathf.Max(1, Mathf.RoundToInt(seconds / step)); s++)
            {
                rig.Player.SetInput(MakeInput(rig, mode));
                rig.Player.Tick();
                UnityEngine.Physics.Simulate(step);
            }
        }

        enum PullMode { None, HoldStill, Away, Toward, Sideways }

        static InputFrame MakeInput(Rig rig, PullMode mode)
        {
            if (mode == PullMode.None) return new InputFrame();
            if (mode == PullMode.HoldStill) return new InputFrame { GrabLeft = true };

            var target = rig.Player.ClampGrabTarget != null
                ? rig.Player.ClampGrabTarget
                : rig.Clamp;

            var away = rig.Player.Doll.Hips.position - target.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            away.Normalize();

            var dir = mode switch
            {
                PullMode.Toward => -away,
                PullMode.Sideways => Vector3.Cross(Vector3.up, away),
                _ => away,
            };

            var fwd = rig.Player.Cam != null ? rig.Player.Cam.PlanarForward : Vector3.forward;
            var right = rig.Player.Cam != null ? rig.Player.Cam.PlanarRight : Vector3.right;

            return new InputFrame
            {
                GrabLeft = true,
                Move = new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, fwd)),
            };
        }

        /// <summary>잡고 있는 고정못. 아직 안 잡았으면 기준으로 둔 첫 고정못.</summary>
        static PartClamp Held(Rig rig) =>
            rig.Player.ClampGrabTarget != null ? rig.Player.ClampGrabTarget : rig.Clamp;
    }
}
