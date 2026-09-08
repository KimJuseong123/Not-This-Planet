using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 참가자 기계가 <b>호스트에게서 받아 반영하는</b> 상태를 검증한다 (S15P21B202-352).
    ///
    /// <para>이 경로는 네트워크 없이 전부 확인할 수 있다 — <c>WorldSync</c> 가 바이트를
    /// 나르고 나면 실제로 하는 일은 여기 있는 네 함수뿐이다. 그래서 Mirror 를 띄우지 않고
    /// EditMode 에서 잡는다.</para>
    ///
    /// <para><b>왜 필요했나.</b> 2026-09-08 에 이 스위트 없이 먼저 구현했더니
    /// <c>ToolBase.SuspendCollisions</c> 가 <c>Body == null</c> 이면 <b>조용히 아무것도
    /// 하지 않는</b> 것을 놓쳤다. 참가자 기계에서는 <c>Equip</c> 이 불리지 않아
    /// <c>Body</c> 가 늘 <c>null</c> 이므로, "휴대 중 충돌을 끈다" 가 그쪽에서만 통째로
    /// 빠져 있었다. 아래 첫 테스트가 그것을 잡는다.</para>
    /// </summary>
    public sealed class NetworkReceivedStateTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            // 그림이 남으면 다음 테스트의 FindObjectsByType 에 섞인다.
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsSortMode.None))
                if (meteor != null)
                    Object.DestroyImmediate(meteor.gameObject);

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
        }

        // --- 도구 휴대 -------------------------------------------------------

        /// <summary>
        /// <b>참가자 화면에서도 휴대 중 충돌이 꺼져야 한다.</b>
        ///
        /// 그 기계의 도구는 서버가 자세를 몰아 주므로 키네마틱이고, 키네마틱 물체는
        /// 사람을 밀어낸다 — 충돌이 켜진 채 남으면 남이 든 방망이가 벽이 된다.
        ///
        /// <c>SuspendCollisions</c> 가 <see cref="ToolBase.Body"/> 만 보던 동안 이 경로는
        /// 조용히 통째로 빠져 있었다(그 값은 원격 경로에서 늘 <c>null</c> 이다).
        /// </summary>
        [Test]
        public void RemoteHeld_SuspendsCollisionsWithoutAnEquipBody()
        {
            var bat = MakeBat(out var body);

            Assert.IsTrue(body.detectCollisions, "시작은 켜져 있다.");
            Assert.IsFalse(bat.IsEquipped, "이 기계에서 Equip 은 불리지 않는다.");

            bat.ApplyRemoteHeld(true);
            Assert.IsFalse(body.detectCollisions,
                "원격이 '들고 있다' 고 알렸으면 충돌이 꺼져야 한다.");

            bat.ApplyRemoteHeld(false);
            Assert.IsTrue(body.detectCollisions, "놓았으면 원래대로 돌아와야 한다.");
        }

        /// <summary>
        /// 뿌리 자세만 나르면 <b>메시가 어긋난다.</b> 방망이는 손에 쥘 때 자식
        /// <c>Visual_ZUpToYUp</c> 을 따로 돌리는데(자산 목록 5-1 의 축 보정 자식),
        /// 그 처리가 <c>OnEquipped</c> 안에 있어 참가자 기계에서는 일어나지 않았다.
        /// </summary>
        [Test]
        public void RemoteHeld_TurnsTheChildVisualIntoTheCarryPose()
        {
            var bat = MakeBat(out _);
            var visual = MakeVisual(bat.transform);
            var dropped = visual.localRotation;

            bat.ApplyRemoteHeld(true);
            Assert.AreNotEqual(Vector3.zero, visual.localPosition,
                "쥔 자세는 원점이 아니다.");
            Assert.AreNotEqual(dropped, visual.localRotation,
                "쥔 자세는 내려놓은 각도와 다르다.");

            var held = visual.localPosition;

            bat.ApplyRemoteHeld(false);
            Assert.AreEqual(dropped, visual.localRotation,
                "놓았으면 내려놓은 각도로 돌아와야 한다.");
            Assert.AreNotEqual(held, visual.localPosition,
                "놓았으면 쥔 자리에 그대로 있어서는 안 된다.");
        }

        /// <summary>
        /// <b>내 손이 우선이다.</b> 자기가 든 도구는 로컬 판정이 이미 옳게 세워 두었고,
        /// 그 위에 원격 값을 덮으면 잡고 놓는 순간마다 한 프레임씩 튄다.
        /// </summary>
        [Test]
        public void RemoteHeld_IsIgnoredWhileThisMachineHoldsIt()
        {
            var bat = MakeBat(out var body);
            var visual = MakeVisual(bat.transform);

            bat.Equip(null, body);
            var held = visual.localPosition;

            bat.ApplyRemoteHeld(false);

            Assert.IsTrue(bat.IsEquipped, "원격 값이 내 장착을 풀어서는 안 된다.");
            Assert.AreEqual(held, visual.localPosition, "쥔 자세가 유지돼야 한다.");
        }

        /// <summary>
        /// 껐던 <b>그 몸</b>으로 되돌려야 한다.
        ///
        /// 원격이 "들고 있다" 고 해서 이 오브젝트의 몸을 껐는데, 그 사이에 이 기계가
        /// 장착하면 그때부터 <c>Body</c> 는 장착 판정이 준 <b>다른 몸</b>일 수 있다
        /// (도구 컴포넌트가 자식에 있으면 부모의 몸이 온다). 되돌릴 때 그 다른 몸을
        /// 보면 껐던 쪽이 **영원히 충돌 없는 상태로 남는다.**
        /// </summary>
        [Test]
        public void RemoteHeld_ThenLocalEquip_RestoresTheBodyItActuallyMuted()
        {
            var bat = MakeBat(out var own);

            var otherGo = new GameObject("장착 판정이 주는 다른 몸");
            _spawned.Add(otherGo);
            var other = otherGo.AddComponent<Rigidbody>();

            bat.ApplyRemoteHeld(true);
            Assert.IsFalse(own.detectCollisions, "원격 휴대가 자기 몸을 껐다.");

            bat.Equip(null, other);
            bat.Unequip();

            Assert.IsTrue(own.detectCollisions, "껐던 그 몸이 되돌아와야 한다.");
        }

        /// <summary>
        /// 도구 컴포넌트가 <b>자식</b>에 있고 몸이 뿌리에 있어도 충돌이 꺼져야 한다.
        ///
        /// <para><c>PlayerRig</c> 가 도구를 <c>GetComponentInChildren&lt;ToolBase&gt;</c> 로도
        /// 찾으므로(<c>PlayerRig.cs:2107</c>) 이 배치가 허용된다. 그런데 원격 경로의 폴백이
        /// <c>GetComponent&lt;Rigidbody&gt;</c> 뿐이면 <b>몸을 못 찾아 조용히 아무것도 안 한다</b> —
        /// 남이 든 도구가 몸을 막는 원래 증상이 그대로 돌아온다(AI 리뷰 지적, 2026-09-08).</para>
        ///
        /// <para>지금 두 프리팹은 뿌리에 함께 있어 <b>이 테스트만이</b> 그 배치를 지킨다.</para>
        /// </summary>
        [Test]
        public void RemoteHeld_FindsTheBodyOnTheParent_WhenTheToolSitsOnAChild()
        {
            var root = new GameObject("도구 뿌리") { layer = GameLayers.Prop };
            _spawned.Add(root);
            root.AddComponent<BoxCollider>();
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;

            var child = new GameObject("Tool");
            child.transform.SetParent(root.transform, false);
            MakeVisual(child.transform);
            var bat = child.AddComponent<MeteorBat>();

            Assert.IsNull(child.GetComponent<Rigidbody>(), "자식에는 몸이 없는 배치다.");
            Assert.IsTrue(body.detectCollisions);

            bat.ApplyRemoteHeld(true);
            Assert.IsFalse(body.detectCollisions, "부모의 몸을 찾아 충돌을 꺼야 한다.");

            bat.ApplyRemoteHeld(false);
            Assert.IsTrue(body.detectCollisions, "놓았으면 그 몸을 되돌려야 한다.");
        }

        /// <summary>
        /// <b>모든 도구가 이 길을 견뎌야 한다.</b>
        ///
        /// <para>원격 경로에서는 <c>OnEquipped</c>·<c>OnUnequipped</c> 가 <c>Owner</c> 와
        /// <c>Body</c> 가 <c>null</c> 인 채로 불린다(<c>ApplyRemoteHeld</c> 의 계약).
        /// 파생 클래스가 그 둘을 무심코 참조하면 <b>참가자 기계에서만</b> NullReference 가
        /// 난다 — 호스트에서는 절대 재현되지 않는 종류다.</para>
        ///
        /// <para><see cref="TypeCache"/> 로 파생을 <b>전수</b> 가져오므로, 도구를 새로
        /// 만들고 이 테스트를 늘리지 않으면 <b>여기서 실패한다.</b> 계약이 주석에만 남지
        /// 않게 하는 장치다(AI 리뷰 지적, 2026-09-08).</para>
        /// </summary>
        [Test]
        public void RemoteHeld_IsSurvivedByEveryToolType()
        {
            var types = TypeCache.GetTypesDerivedFrom<ToolBase>();
            var covered = new List<System.Type> { typeof(MeteorBat), typeof(PlasmaWeldingTool) };

            foreach (var type in types)
            {
                if (type.IsAbstract) continue;

                Assert.Contains(type, covered,
                    $"도구 {type.Name} 이 이 테스트에 없다 — 원격 휴대 경로를 태워 볼 것. "
                    + "그 경로에서는 Owner·Body 가 null 이다(ApplyRemoteHeld 의 계약).");
            }

            foreach (var type in covered)
            {
                var go = new GameObject(type.Name) { layer = GameLayers.Prop };
                _spawned.Add(go);
                go.AddComponent<BoxCollider>();
                go.AddComponent<Rigidbody>().useGravity = false;
                MakeVisual(go.transform);

                var tool = (ToolBase)go.AddComponent(type);

                Assert.DoesNotThrow(() => tool.ApplyRemoteHeld(true),
                    $"{type.Name}: 원격이 들었다고 알렸을 때 터졌다.");
                Assert.DoesNotThrow(() => tool.ApplyRemoteHeld(false),
                    $"{type.Name}: 원격이 놓았다고 알렸을 때 터졌다.");
            }
        }

        // --- 낙하 운석 그림 --------------------------------------------------

        /// <summary>받은 개수만큼 띄우고, 줄면 지운다.</summary>
        [Test]
        public void NetworkMeteors_MatchTheReceivedCount()
        {
            var spawner = MakeSpawner();

            spawner.ApplyNetworkMeteors(Poses(3));
            Assert.AreEqual(3, spawner.Ghosts.Count);

            spawner.ApplyNetworkMeteors(Poses(1));
            Assert.AreEqual(1, spawner.Ghosts.Count);

            spawner.ApplyNetworkMeteors(Poses(0));
            Assert.AreEqual(0, spawner.Ghosts.Count, "0개를 받으면 다 지운다.");
        }

        /// <summary>
        /// <b>그림은 판정에 관여하지 않는다.</b> 이것이 <c>Meteor.cs</c> 를 한 줄도
        /// 고치지 않아도 되는 이유다 — 꺼진 컴포넌트에는 충돌 통보도 틱도 오지 않는다.
        /// </summary>
        [Test]
        public void NetworkMeteors_DoNotTakePartInJudgement()
        {
            var spawner = MakeSpawner();
            spawner.ApplyNetworkMeteors(Poses(1));

            var ghost = spawner.Ghosts[0];
            Assert.IsFalse(ghost.enabled, "Meteor 컴포넌트가 꺼져 있어야 한다.");
            Assert.AreEqual(0, spawner.Live.Count, "권위 쪽 목록에 들어가면 안 된다.");

            foreach (var collider in ghost.GetComponentsInChildren<Collider>(true))
                Assert.IsFalse(collider.enabled, "충돌러가 전부 꺼져 있어야 한다.");

            var body = ghost.GetComponent<Rigidbody>();
            Assert.IsNotNull(body);
            Assert.IsFalse(body.detectCollisions);
            Assert.IsFalse(body.isKinematic, "중력으로 떨어져야 궤적이 이어진다.");
            Assert.IsTrue(body.useGravity);
        }

        /// <summary>
        /// 받은 자리와 <b>속도</b>를 그대로 쓴다. 속도를 버리면 다음 묶음까지 제자리에
        /// 멈춰 있다가 툭툭 끊겨 내려온다.
        /// </summary>
        [Test]
        public void NetworkMeteors_UseTheReceivedPositionAndVelocity()
        {
            var spawner = MakeSpawner();

            var pose = new MeteorSpawner.NetworkMeteorPose
            {
                Position = new Vector3(3f, 12f, -4f),
                Rotation = Quaternion.Euler(10f, 20f, 30f),
                Velocity = new Vector3(1f, -9f, 0.5f),
            };
            spawner.ApplyNetworkMeteors(new List<MeteorSpawner.NetworkMeteorPose> { pose });

            var body = spawner.Ghosts[0].GetComponent<Rigidbody>();
            Assert.AreEqual(pose.Position, body.position);
            Assert.AreEqual(pose.Velocity, body.linearVelocity);
        }

        /// <summary>매치가 끝나면 남기지 않는다 — 충돌러가 꺼져 있어 지면을 뚫고 내려간다.</summary>
        [Test]
        public void ClearNetworkMeteors_LeavesNothingBehind()
        {
            var spawner = MakeSpawner();
            spawner.ApplyNetworkMeteors(Poses(2));

            spawner.ClearNetworkMeteors();

            Assert.AreEqual(0, spawner.Ghosts.Count);
            Assert.AreEqual(0, Object.FindObjectsByType<Meteor>(FindObjectsSortMode.None).Length,
                "씬에도 남아 있으면 안 된다.");
        }

        // --- 받은 예고 -------------------------------------------------------

        /// <summary>
        /// 참가자가 예고의 남은 시간을 <b>스스로 세도 안전한가</b>. 이것이 참이라서
        /// 매 틱 실어 보내지 않고 목록이 달라질 때만 보낸다.
        ///
        /// <c>AdvancePending</c> 가 뺄셈과 목록 정리뿐이고 스폰·난수·스케줄 전진이 그
        /// 밖에 있다는 것을 여기서 지킨다 — 그쪽으로 코드가 옮겨 오면 참가자가 자기
        /// 운석을 만들기 시작해 세계가 갈린다.
        /// </summary>
        [Test]
        public void AgeNetworkPending_OnlyCountsDown_AndNeverSpawns()
        {
            var spawner = MakeSpawner();

            spawner.ApplyNetworkPending(new List<PendingMeteorDrop>
            {
                new PendingMeteorDrop(Vector3.zero, 1f, 3f, 2f),
            });
            Assert.AreEqual(1, spawner.Pending.Count);

            // 남은 1초보다 넉넉히 넘긴다 — 0.02초 × 60 = 1.2초.
            for (var i = 0; i < 60; i++) spawner.AgeNetworkPending(0.02f);

            Assert.AreEqual(0, spawner.Pending.Count, "시간이 다 되면 목록에서 빠진다.");
            Assert.AreEqual(0, spawner.Live.Count, "참가자는 운석을 만들지 않는다.");
        }

        // --- 도우미 ----------------------------------------------------------

        MeteorSpawner MakeSpawner()
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(12345);
            return spawner;
        }

        MeteorBat MakeBat(out Rigidbody body)
        {
            var go = new GameObject("PF_Stage1_MeteorBat") { layer = GameLayers.Prop };
            _spawned.Add(go);

            go.AddComponent<BoxCollider>();
            body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            return go.AddComponent<MeteorBat>();
        }

        /// <summary>[Art] 가 축을 보정해 둔 자식. 휴대 자세는 이것을 돌린다.</summary>
        static Transform MakeVisual(Transform parent)
        {
            var go = new GameObject("Visual_ZUpToYUp");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static List<MeteorSpawner.NetworkMeteorPose> Poses(int count)
        {
            var poses = new List<MeteorSpawner.NetworkMeteorPose>(count);
            for (var i = 0; i < count; i++)
                poses.Add(new MeteorSpawner.NetworkMeteorPose
                {
                    Position = new Vector3(i, 20f + i, 0f),
                    Rotation = Quaternion.identity,
                    Velocity = Vector3.down * 10f,
                });

            return poses;
        }
    }
}
