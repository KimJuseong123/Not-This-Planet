using System.Collections.Generic;
using System.Reflection;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 저중력장(UFO 빔)의 켜짐/꺼짐이 참가자에게 전달되는지 검증한다(S15P21B202-505).
    ///
    /// <para><b>무엇이 문제였나.</b> 빔 그림은 <c>LowGravityField.SetActive</c> 안에서
    /// <see cref="LowGravityField.IsActive"/> 가 참일 때만 붙는다. 그런데 그 값을 바꾸는
    /// 유일한 경로가 <c>TractorBeamController.SetInput</c> 의
    /// <c>if (!Authoritative) return;</c> 뒤에 있어서 <b>참가자 기계에서는 영영
    /// 거짓</b>이었다. 그리고 그 상태를 알려 주는 것도 없었다 — 실측으로
    /// <c>WorldSync</c> 에 <c>Field</c> 라는 글자가 <b>0회</b>였다.</para>
    ///
    /// <para><b>S15P21B202-448 과 같은 모양이다.</b> 입력(<c>InputFrame.FieldToggle</c>)은
    /// 서버로 올라가고 서버가 제대로 실행하는데, <b>실행 결과가 화면에 필요한 값인데
    /// 아무도 돌려보내지 않는다.</b></para>
    /// </summary>
    public sealed class FieldNetworkStateTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            LowGravityField.All.Clear();
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            LowGravityField.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── 받는 쪽 ─────────────────────────────────────────────

        /// <summary><b>이 검증이 이 이슈의 핵심이다.</b></summary>
        [Test]
        public void AParticipantTurnsTheFieldOnFromTheServerState()
        {
            var field = MakeField(physicsAuthority: false);   // 참가자의 장
            Assert.IsFalse(field.IsActive, "참가자는 스스로 켤 길이 없다.");

            field.ApplyNetworkActive(true);

            Assert.IsTrue(field.IsActive, "서버가 켰다고 하면 켜져야 빔이 그려진다.");
        }

        [Test]
        public void AndTurnsItOffAgain()
        {
            var field = MakeField(physicsAuthority: false);
            field.ApplyNetworkActive(true);

            field.ApplyNetworkActive(false);

            Assert.IsFalse(field.IsActive);
        }

        /// <summary>
        /// <b>물리는 따라오지 않는다.</b> 참가자가 켜도 힘을 넣으면 서버와 싸운다 —
        /// 판정을 두 곳에 두면 반드시 갈라진다(<c>-383</c> · <c>-145</c>).
        /// </summary>
        [Test]
        public void TurningItOnDoesNotGiveTheParticipantPhysics()
        {
            var field = MakeField(physicsAuthority: false);
            var body = MakeFloatingBody(field);

            field.ApplyNetworkActive(true);
            field.Tick(0.02f);

            Assert.IsFalse(field.PhysicsAuthority, "참가자에게 물리 권한은 없다.");
            Assert.Zero(field.AffectedCount,
                "켜졌어도 참가자는 아무 몸도 띄우지 않는다 — Tick 이 권한에서 반환한다.");
            Assert.IsNotNull(body);
        }

        [Test]
        public void TheHostStillDrivesPhysicsWhenOn()
        {
            var field = MakeField(physicsAuthority: true);
            MakeFloatingBody(field);

            field.ApplyNetworkActive(true);
            field.Tick(0.02f);

            Assert.IsTrue(field.PhysicsAuthority);
            Assert.Greater(field.AffectedCount, 0, "호스트 경로가 회귀하면 안 된다.");
        }

        // ── 보내는 쪽 (배선) ────────────────────────────────────

        /// <summary>
        /// <b>보내는 자리가 실제로 있는가.</b> 이것이 없으면 위의 받는 쪽 검증이 전부
        /// 통과해도 게임에서는 아무 일이 일어나지 않는다 — <c>-448</c> 과 <c>-438</c> 에서
        /// 두 번 겪은 모양이다("그릴 곳은 있는데 그릴 값이 없다").
        ///
        /// <para><b>파일 전체가 아니라 보내는 메서드 안을 본다.</b> 처음에는 파일에
        /// <c>"RpcApplyFields"</c> 라는 글자가 있는지만 봤는데, <b>호출을 지워도 메서드
        /// 정의에 그 글자가 남아 통과했다</b> — 대조 실험에서 0개가 잡혔다. 글자를 보는
        /// 검증은 <b>범위를 좁혀야</b> 뜻이 산다.</para>
        ///
        /// <para>Mirror 의 <c>[ClientRpc]</c> 는 EditMode 에서 부를 수 있는 자리가 없어
        /// 이 배선만은 글자로 잰다. 대신 <b>보낼지 말지의 판단</b>은 순수 메서드로 빼서
        /// 아래에서 동작으로 잠근다.</para>
        /// </summary>
        [Test]
        public void TheServerActuallySendsIt()
        {
            var sync = TestSource.Read("Runtime/Network/WorldSync.cs");
            var send = TestSource.Method(sync, "void SendFromServer()");

            Assert.That(send, Does.Contain("RpcApplyFields("),
                "보내는 호출이 없으면 참가자에게 아무것도 안 온다.");

            var apply = TestSource.Method(sync, "void RpcApplyFields(uint seq, FieldState[] states)");
            Assert.That(apply, Does.Contain("ApplyNetworkActive"),
                "받은 값을 장에 넣지 않으면 켜지지 않는다.");
            Assert.That(apply, Does.Contain("isServer"),
                "호스트는 자기 물리가 정답이다 — 받은 값으로 덮으면 안 된다.");
        }

        // ── 언제 보내는가 (순수 판단) ───────────────────────────

        [Test]
        public void ItSendsWhenSomethingChanged()
        {
            var now = new[] { Field(0, true) };
            var lastSent = new[] { Field(0, false) };

            Assert.IsTrue(Network.WorldSync.ShouldSendFields(now, lastSent, 0f, 0.5f),
                "켜고 끈 것이 안 가면 참가자 화면이 멈춘다.");
        }

        [Test]
        public void ItStaysQuietWhenNothingChanged()
        {
            var same = new[] { Field(0, true) };

            Assert.IsFalse(Network.WorldSync.ShouldSendFields(same, Copy(same), 0f, 0.5f),
                "안 바뀐 것을 매 틱 보내면 업링크가 비싸다.");
        }

        /// <summary>
        /// 조용해도 가끔은 다시 보낸다 — 신뢰 없는 채널이라 한 번 잃으면 그대로
        /// 어긋난 채 남는다(고정쇠와 같은 이유).
        /// </summary>
        [Test]
        public void ButItResendsOnTheKeepalive()
        {
            var same = new[] { Field(0, true) };

            Assert.IsTrue(Network.WorldSync.ShouldSendFields(same, Copy(same), 0.5f, 0.5f));
        }

        [Test]
        public void ANewVehicleCountCountsAsAChange()
        {
            var now = new[] { Field(0, true), Field(3, false) };
            var lastSent = new[] { Field(0, true) };

            Assert.IsTrue(Network.WorldSync.ShouldSendFields(now, lastSent, 0f, 0.5f));
        }

        [Test]
        public void ADifferentVehicleNumberCountsAsAChange()
        {
            var now = new[] { Field(3, true) };
            var lastSent = new[] { Field(0, true) };

            Assert.IsTrue(Network.WorldSync.ShouldSendFields(now, lastSent, 0f, 0.5f),
                "번호가 달라지면 다른 탈것 이야기다.");
        }

        static Network.WorldSync.FieldState Field(int vehicle, bool active) =>
            new Network.WorldSync.FieldState { Vehicle = vehicle, Active = active };

        static Network.WorldSync.FieldState[] Copy(Network.WorldSync.FieldState[] source) =>
            (Network.WorldSync.FieldState[])source.Clone();

        /// <summary>
        /// <b>탈것 번호는 이미 있는 것을 쓴다.</b> S15P21B202-434 가 "탈것만 따로 줄을
        /// 세우면 -315 가 기록한 사고(개수는 같은데 순서만 어긋남)를 새로 만든다" 고
        /// 적어 두었다.
        ///
        /// <para><b>왕복을 잰다.</b> 처음에는 <c>FieldState.Vehicle</c> 의 존재·타입·
        /// 기본값 0 만 확인했는데, 그것으로는 <b>잘못된 색인을 넣어도 통과한다</b>
        /// (!417 AI 리뷰). 실제로 잠가야 하는 것은 "보내는 번호로 받는 쪽이 같은 탈것을
        /// 되찾는다" 이고, 그것은 공개 API 둘(<c>IndexOfTracked</c> ·
        /// <c>VehicleAt</c>)로 잴 수 있다.</para>
        /// </summary>
        [Test]
        public void TheNumberRoundTripsToTheSameVehicle()
        {
            var sync = MakeSync();
            var vehicles = new[] { MakeVehicle(Vector3.zero), MakeVehicle(new Vector3(8f, 0f, 0f)) };
            Collect(sync);

            foreach (var vehicle in vehicles)
            {
                var index = sync.IndexOfTracked(vehicle.GetComponent<Rigidbody>());

                Assert.GreaterOrEqual(index, 0, "탈것은 짝짓기 목록에 들어가야 한다.");
                Assert.AreSame(vehicle, sync.VehicleAt(index),
                    "보내는 번호로 받는 쪽이 같은 탈것을 되찾아야 한다 — "
                    + "어긋나면 남의 탈것 빔이 켜진다(-315).");
            }
        }

        [Test]
        public void AnOutOfRangeNumberFindsNothing()
        {
            var sync = MakeSync();
            MakeVehicle(Vector3.zero);
            Collect(sync);

            Assert.IsNull(sync.VehicleAt(-1));
            Assert.IsNull(sync.VehicleAt(9999), "못 찾은 번호는 건너뛴다 — 버리지 않는다.");
        }

        // ── 도구 ────────────────────────────────────────────────

        /// <summary>
        /// <c>Collect()</c> 는 비공개다. 짝짓기 목록을 채우는 유일한 경로이므로 여기서만
        /// 리플렉션으로 부른다 — 목록이 비어 있으면 번호 왕복을 잴 수가 없다.
        /// </summary>
        static void Collect(Network.WorldSync sync)
        {
            var collect = typeof(Network.WorldSync).GetMethod(
                "Collect", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(collect, "Collect 가 사라졌다면 이 검증부터 다시 봐야 한다.");
            collect.Invoke(sync, null);
        }

        Network.WorldSync MakeSync()
        {
            var go = new GameObject("WorldSync");
            _spawned.Add(go);

            // NetworkBehaviour 는 NetworkIdentity 를 요구한다. 없으면 컴포넌트를 붙이는
            // 순간 에러 로그가 나고 검증이 빨간불이 된다 — 스폰까지 할 필요는 없다.
            go.AddComponent<Mirror.NetworkIdentity>();

            return go.AddComponent<Network.WorldSync>();
        }

        TransportVehicle MakeVehicle(Vector3 position)
        {
            var go = new GameObject("운반 장치");
            go.transform.position = position;
            _spawned.Add(go);

            go.AddComponent<Rigidbody>().isKinematic = true;
            var beam = go.AddComponent<TractorBeam>();
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            return vehicle;
        }

        LowGravityField MakeField(bool physicsAuthority)
        {
            var go = new GameObject("저중력장");
            go.transform.position = new Vector3(0f, 6f, 0f);
            _spawned.Add(go);

            var field = go.AddComponent<LowGravityField>();
            field.Configure(go.transform, null);
            field.SetPhysicsAuthority(physicsAuthority);
            return field;
        }

        /// <summary>장 안에 놓인 띄울 수 있는 몸 하나.</summary>
        Rigidbody MakeFloatingBody(LowGravityField field)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "파츠";
            go.layer = GameLayers.Part;
            go.transform.position = field.Origin.position + Vector3.down * 2f;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 20f;
            UnityEngine.Physics.SyncTransforms();
            return body;
        }
    }
}
