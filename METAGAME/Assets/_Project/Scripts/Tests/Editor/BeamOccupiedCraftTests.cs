using System.Collections.Generic;
using System.Text.RegularExpressions;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 사람이 탄 견인선을 빔이 집으면 안 된다 (S15P21B202-554).
    ///
    /// <para><b>무엇이 문제였나.</b> 팀원이 자기 빔으로 내가 타고 있는 견인선을 집으면
    /// 파츠가 <c>Free</c> 에서 <c>Carried</c> 로 넘어간다. 그런데 상태 전이표
    /// (<c>SpaceshipPart.IsTransitionAllowed</c>)에 <b><c>Free</c> 로 돌아오는 길이
    /// 없다.</b> 그리고 <c>TransportVehicle.HasPartAttachment()</c> 는
    /// <c>State != Free</c> 를 "붙어 있음" 으로 보므로, 그 뒤로는</para>
    ///
    /// <para>· <c>TickPartFlight</c> 가 부양·추진을 통째로 건너뛰어 <b>탄 사람의 조작이
    /// 죽고</b>,<br/>
    /// · <c>CanBoardFrom</c> 이 거짓이라 <b>아무도 다시 못 탄다</b>. 그 판 내내.</para>
    ///
    /// <para>실측: 방장이 탄 견인선에서 스페이스바가 먹지 않았고, 내린 뒤에는 탑승이
    /// 막혔다.</para>
    ///
    /// <para><b>별도 픽스처에 두는 이유.</b> <c>PartTractorActivatorTests</c> 에 넣었더니
    /// 이름이 알파벳순으로 앞서 그 픽스처의 기존 검증보다 먼저 돌았고, 남긴 상태가
    /// <c>ActivatedPart1_IsRegisteredAsTransportInteractionTarget</c> 을 깨뜨렸다.
    /// 정리 규칙이 다른 검증은 픽스처를 나눈다.</para>
    /// </summary>
    public sealed class BeamOccupiedCraftTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => PlayerRig.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
            PartClamp.All.Clear();
            PartSocket.All.Clear();
            WeldBead.All.Clear();
            WorldEvents.RemoteDecides = false;
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary><b>이 검증이 이 이슈 자체다.</b></summary>
        [Test]
        public void ABeamCannotGrabACraftSomeoneIsRidingOn()
        {
            var vehicle = BoardedCraft(out var part);
            Assert.AreEqual(PartState.Free, part.State, "탑승 시점에는 Free 다.");

            var result = BeamAbove(vehicle).TryGrab(part);

            Assert.AreEqual(BeamGrabResult.Occupied, result,
                "사람이 타고 있으면 집히지 않아야 한다.");
            Assert.AreEqual(PartState.Free, part.State,
                "Free 를 벗어나면 그 판에서는 영영 못 탄다 — 전이표에 돌아오는 길이 없다.");
            Assert.IsTrue(vehicle.IsOccupied, "집기 시도로 사람이 내려지면 안 된다.");
        }

        /// <summary>
        /// <b>빈 기체는 그대로 집힌다.</b> 엔진은 결국 조립해야 하는 파츠다 — 막을 것은
        /// "타고 있는 동안" 뿐이고, 넓게 막으면 조립이 성립하지 않는다.
        /// </summary>
        [Test]
        public void AnEmptyCraftIsStillGrabbable()
        {
            var vehicle = BoardedCraft(out var part);
            vehicle.ReleaseRiderForRespawn();
            Assert.IsFalse(vehicle.IsOccupied);

            Assert.AreEqual(BeamGrabResult.Grabbed, BeamAbove(vehicle).TryGrab(part),
                "빈 견인선까지 막으면 엔진을 조립할 수 없다.");
        }

        /// <summary>
        /// 그물. 빔 말고 다른 경로(용접 · 조립 링크)로 붙어 버려도 <b>갇히지는 않는다</b> —
        /// 조작이 죽은 기체 안에 남기는 대신 내려 준다.
        /// </summary>
        [Test]
        public void AnAttachedCraftPutsItsRiderDownInsteadOfTrappingThem()
        {
            var vehicle = BoardedCraft(out var part);

            // 빔을 거치지 않고 상태만 옮긴다 — 용접·조립이 만드는 것과 같은 결과다.
            Assert.IsTrue(part.TryTransitionTo(PartState.Carried));

            LogAssert.Expect(LogType.Warning, new Regex("S15P21B202-554"));
            Simulate(vehicle, 0.06f);

            Assert.IsFalse(vehicle.IsOccupied,
                "조작이 죽은 기체 안에 사람을 남겨 두면 안 된다.");
        }

        // ── 도구 ────────────────────────────────────────────────

        /// <summary>고정못을 푼 Part_1 에 사람을 태워 돌려준다.</summary>
        TransportVehicle BoardedCraft(out SpaceshipPart part)
        {
            var position = Vector3.up * 3f;
            var go = new GameObject("Part_1") { layer = GameLayers.Part };
            go.transform.position = position;
            _spawned.Add(go);

            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>().size = Vector3.one;
            part = go.AddComponent<SpaceshipPart>();
            go.AddComponent<PartPush>();

            var clamps = new List<PartClamp>(4);
            for (var i = 0; i < 4; i++)
            {
                var clampObject = new GameObject($"Clamp_{i}") { layer = GameLayers.Interactable };
                clampObject.transform.SetParent(go.transform, false);
                clampObject.AddComponent<SphereCollider>().isTrigger = true;
                clamps.Add(clampObject.AddComponent<PartClamp>());
                _spawned.Add(clampObject);
            }
            part.RefreshClamps();

            var activator = go.AddComponent<PartTractorActivator>();
            activator.Initialize();          // EditMode 에서는 OnEnable 이 돌지 않는다

            // 고정못을 멀리 옮긴 뒤 풀어야 Free 로 간다.
            for (var i = 0; i < clamps.Count; i++)
            {
                clamps[i].transform.position = new Vector3(50f + i * 2f, 0f, 0f);
                clamps[i].Release();
            }

            var player = PlayerRig.Create("Pilot", position + Vector3.right * 2f,
                Quaternion.identity, Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            Assert.IsTrue(activator.Vehicle.TryBoard(player), "검증 준비: 탑승이 돼야 한다.");
            return activator.Vehicle;
        }

        /// <summary>
        /// 기체 <b>바로 위에서 아래를 보는</b> 남의 빔.
        ///
        /// <para><see cref="TractorBeam.CanReach"/> 는 사거리뿐 아니라 <b>빔 축에서의
        /// 옆 거리</b>도 본다(<c>CastRadius + 1.25</c>). 옆에 세우면 그 조건에 걸려
        /// <c>NoTarget</c> 이 나오고, 정작 보려는 탑승 가드를 지나지 못한다.</para>
        /// </summary>
        TractorBeam BeamAbove(TransportVehicle vehicle)
        {
            var go = new GameObject("남의 빔");
            go.transform.SetPositionAndRotation(
                vehicle.Body.worldCenterOfMass + Vector3.up * 3f,
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            _spawned.Add(go);

            var beam = go.AddComponent<TractorBeam>();
            beam.Configure(go.transform, PhysicsScale.BeamRange, TransportVehicle.BeamHoldDistance);
            return beam;
        }

        static void Simulate(TransportVehicle vehicle, float seconds)
        {
            var saved = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Physics.SyncTransforms();
                for (var i = 0; i < Mathf.CeilToInt(seconds / 0.02f); i++)
                {
                    vehicle.Tick(0.02f);
                    Physics.Simulate(0.02f);
                }
            }
            finally { Physics.simulationMode = saved; }
        }
    }
}
