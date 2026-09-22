using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Network;
using Mirror;
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

        [UnityEngine.TestTools.UnityTest]
        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-678)
        public System.Collections.IEnumerator MeteorFeedbackSurvivesNetworkSerialization()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            var state = new WorldSync.ToolViewState
            {
                RhythmSequence = 42,
                RhythmGrade = 3,
                RhythmImpactPoint = new Vector3(4f, 5f, 6f),
                RhythmCooldown = 0.2f,
                Owner = 7,
            };
            var writer = new NetworkWriter();
            writer.Write(state);
            var received = new NetworkReader(writer.ToArraySegment()).Read<WorldSync.ToolViewState>();
            Assert.AreEqual(state.RhythmSequence, received.RhythmSequence);
            Assert.AreEqual(state.RhythmGrade, received.RhythmGrade);
            Assert.AreEqual(state.RhythmImpactPoint, received.RhythmImpactPoint);
            Assert.AreEqual(state.RhythmCooldown, received.RhythmCooldown);
            Assert.AreEqual(state.Owner, received.Owner);

            writer = new NetworkWriter();
            var pose = new MeteorSpawner.NetworkMeteorPose
            {
                DeflectionGrade = 3, Harmless = true, Position = Vector3.up * 5f,
                Velocity = Vector3.forward * MeteorBat.SpeedFor(3),
            };
            writer.Write(pose);
            var sizeField = typeof(WorldSync).GetField("MeteorPoseBytes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.AreEqual((int)sizeField.GetRawConstantValue(), writer.Position,
                "Packet capacity must use the actual serialized meteor size.");
            var remote = new NetworkReader(writer.ToArraySegment()).Read<MeteorSpawner.NetworkMeteorPose>();
            Assert.AreEqual(pose.DeflectionGrade, remote.DeflectionGrade);
            Assert.AreEqual(pose.Harmless, remote.Harmless);
            Assert.AreEqual(pose.Position, remote.Position);
            Assert.AreEqual(pose.Velocity, remote.Velocity);
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        [TearDown]
        public void TearDown()
        {
            // 그림이 남으면 다음 테스트의 FindObjectsByType 에 섞인다.
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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

            // **장착하면 그때부터는 장착 몸이 꺼져야 한다**(AI 리뷰 지적, 2026-09-08).
            // 원격이 껐던 표시가 남아 있으면 SuspendCollisions 가 조기 반환해서
            // 정작 손에 든 몸의 충돌이 켜진 채 남는다.
            Assert.IsFalse(other.detectCollisions, "장착 판정이 준 몸이 꺼져야 한다.");
            Assert.IsTrue(own.detectCollisions, "원격이 껐던 몸은 이때 되돌아온다.");

            bat.Unequip();

            Assert.IsTrue(own.detectCollisions, "껐던 그 몸이 되돌아와야 한다.");
            Assert.IsTrue(other.detectCollisions, "장착 몸도 되돌아와야 한다.");
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
                Harmless = true,
                DeflectionGrade = 3,
            };
            spawner.ApplyNetworkMeteors(new List<MeteorSpawner.NetworkMeteorPose> { pose });

            var body = spawner.Ghosts[0].GetComponent<Rigidbody>();
            Assert.AreEqual(pose.Position, body.position);
            Assert.AreEqual(pose.Velocity, body.linearVelocity);
            Assert.AreEqual(3, spawner.Ghosts[0].DeflectionGrade);
            Assert.IsTrue(spawner.Ghosts[0].Harmless);
            Assert.IsFalse(spawner.Ghosts[0].enabled);
            Assert.IsFalse(body.detectCollisions);

            pose.DeflectionGrade = 0;
            pose.Harmless = false;
            spawner.ApplyNetworkMeteors(new[] { pose });
            Assert.AreEqual(0, spawner.Ghosts[0].DeflectionGrade);
            Assert.IsFalse(spawner.Ghosts[0].Harmless);
        }

        /// <summary>매치가 끝나면 남기지 않는다 — 충돌러가 꺼져 있어 지면을 뚫고 내려간다.</summary>
        [Test]
        public void ClearNetworkMeteors_LeavesNothingBehind()
        {
            var spawner = MakeSpawner();
            spawner.ApplyNetworkMeteors(Poses(2));

            spawner.ClearNetworkMeteors();

            Assert.AreEqual(0, spawner.Ghosts.Count);
            Assert.AreEqual(0, Object.FindObjectsByType<Meteor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "씬에도 남아 있으면 안 된다.");
        }

        // --- 받은 흔적 (착지한 운석, S15P21B202-355) -------------------------
        //
        // 참가자는 착탄을 스스로 계산하지 않으므로(MeteorClockBridge 가 조기 반환한다)
        // 착지한 돌이 **아예 없었다.** 그런데 상호작용은 서버가 판정해서 **안 보이는 돌을
        // 집을 수 있었다** — 2026-09-10 QA: "보이지만 않고 상호작용은 돼."

        /// <summary>
        /// <b>번호로 짝을 짓는다.</b> 인덱스로는 안 된다 — <c>Trim</c> 이 상한을 넘으면
        /// <c>RemoveAt(0)</c> 으로 앞에서 지우므로 남은 전부가 한 칸씩 밀리고, 그 프레임에
        /// 돌들이 서로의 자리로 순간이동한다.
        /// </summary>
        [Test]
        public void ScarSet_KeepsGhostsByIdAndDropsTheAbsent()
        {
            var spawner = MakeSpawner();

            spawner.ApplyNetworkScars(new List<MeteorSpawner.NetworkScarState>
            {
                Scar(7, new Vector3(1f, 0f, 2f)),
                Scar(9, new Vector3(5f, 0f, 6f)),
            }, full: true);

            Assert.AreEqual(2, spawner.ScarGhostCount);
            Assert.IsTrue(spawner.TryGetScarGhost(7, out var seven));
            Assert.IsTrue(spawner.TryGetScarGhost(9, out _));

            // 9 가 사라지고 11 이 생긴 목록. **7 은 같은 그림이어야 한다** — 다시 만들면
            // 매 착탄마다 온 화면의 돌이 새로 생긴다.
            spawner.ApplyNetworkScars(new List<MeteorSpawner.NetworkScarState>
            {
                Scar(7, new Vector3(1f, 0f, 2f)),
                Scar(11, new Vector3(9f, 0f, 9f)),
            }, full: true);

            Assert.AreEqual(2, spawner.ScarGhostCount);
            Assert.IsTrue(spawner.TryGetScarGhost(7, out var stillSeven));
            Assert.AreSame(seven, stillSeven, "같은 번호는 같은 그림을 써야 한다.");
            Assert.IsFalse(spawner.TryGetScarGhost(9, out _), "목록에 없는 흔적은 지워야 한다.");
            Assert.IsTrue(spawner.TryGetScarGhost(11, out _));
        }

        /// <summary>
        /// <b>쉬는 돌은 떨어지지 않아야 한다.</b> 낙하 그림은 중력으로 궤적을 흉내내지만
        /// 흔적은 땅에 박혀 있다 — 충돌러가 꺼져 있으므로 중력을 그대로 두면 지면을 뚫고
        /// 영영 내려간다.
        /// </summary>
        [Test]
        public void ScarSet_PlacesTheGhostAndLeavesItAtRest()
        {
            var spawner = MakeSpawner();
            var at = new Vector3(3f, 0.4f, -4f);

            spawner.ApplyNetworkScars(new List<MeteorSpawner.NetworkScarState>
            {
                Scar(1, at),
            }, full: true);

            Assert.IsTrue(spawner.TryGetScarGhost(1, out var ghost));
            var body = ghost.GetComponent<Rigidbody>();
            Assert.AreEqual(at, body.position);
            Assert.AreEqual(Vector3.zero, body.linearVelocity);
            Assert.IsFalse(body.useGravity, "중력을 두면 충돌러가 꺼진 그림이 지면을 뚫고 내려간다.");
        }

        /// <summary>
        /// <b>움직임 묶음은 만들지도 지우지도 않는다.</b> 그 묶음은 신뢰 없는 채널로 오고
        /// 깨어 있는 돌만 실린다 — 그것으로 목록을 판단하면, 사람이 하나를 끌 때
        /// <b>나머지 전부가 사라진다.</b>
        /// </summary>
        [Test]
        public void ScarMoves_MoveOnly()
        {
            var spawner = MakeSpawner();

            spawner.ApplyNetworkScars(new List<MeteorSpawner.NetworkScarState>
            {
                Scar(4, new Vector3(0f, 0f, 0f)),
                Scar(5, new Vector3(2f, 0f, 0f)),
            }, full: true);

            var moved = new Vector3(8f, 1f, 8f);
            spawner.ApplyNetworkScars(new List<MeteorSpawner.NetworkScarState>
            {
                Scar(4, moved),
                Scar(99, new Vector3(-7f, 0f, -7f)),     // 아직 목록에 없는 번호
            }, full: false);

            Assert.AreEqual(2, spawner.ScarGhostCount,
                "움직임 묶음으로는 만들지도 지우지도 않아야 한다.");
            Assert.IsTrue(spawner.TryGetScarGhost(4, out var four));
            Assert.AreEqual(moved, four.GetComponent<Rigidbody>().position);
            Assert.IsTrue(spawner.TryGetScarGhost(5, out _), "실리지 않은 흔적을 지우면 안 된다.");
            Assert.IsFalse(spawner.TryGetScarGhost(99, out _), "움직임 묶음이 새로 만들면 안 된다.");
        }

        /// <summary>접속이 끝나면 남기지 않는다 — 다음 경기의 빈 땅에 옛 돌이 서 있다.</summary>
        [Test]
        public void ClearNetworkScars_LeavesNothingBehind()
        {
            var spawner = MakeSpawner();
            spawner.ApplyNetworkScars(new List<MeteorSpawner.NetworkScarState>
            {
                Scar(1, Vector3.zero),
                Scar(2, Vector3.one),
            }, full: true);

            spawner.ClearNetworkScars();

            Assert.AreEqual(0, spawner.ScarGhostCount);
            Assert.AreEqual(0, Object.FindObjectsByType<Meteor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "씬에도 남아 있으면 안 된다.");
        }

        /// <summary>
        /// <b>두 묶음의 채널이 다르다</b> (S15P21B202-355).
        ///
        /// 전체 목록은 <b>신뢰 채널</b>이어야 한다 — 잃으면 참가자 화면에서 돌이 사라지거나
        /// 유령이 남고 다음 착탄까지 그대로다. 움직임은 매 틱 새 값이 오므로 신뢰가 필요
        /// 없고, 신뢰 채널로 보내면 재전송이 쌓인다.
        ///
        /// 그리고 움직임은 <b>깨어 있는 강체만</b> 실어야 한다. 전부 실으면 30~40개를
        /// 30Hz 로 내보내 사람당 40KB/s 가 된다.
        /// </summary>
        [Test]
        public void ScarChannelsAreChosenOnPurpose()
        {
            var sync = TestSource.Blank(TestSource.Read("Runtime/Network/WorldSync.cs"));

            var full = sync.IndexOf("void RpcScarSet(", System.StringComparison.Ordinal);
            var moves = sync.IndexOf("void RpcScarMoves(", System.StringComparison.Ordinal);
            Assert.Greater(full, 0, "전체 목록 RPC 가 없다");
            Assert.Greater(moves, 0, "움직임 RPC 가 없다");

            // 각 RPC 바로 앞의 특성 줄만 본다.
            var fullAttr = sync.LastIndexOf("[ClientRpc", full, System.StringComparison.Ordinal);
            var movesAttr = sync.LastIndexOf("[ClientRpc", moves, System.StringComparison.Ordinal);
            Assert.That(sync.Substring(fullAttr, full - fullAttr),
                Does.Not.Contain("Unreliable"),
                "전체 목록을 신뢰 없는 채널로 보내면 잃은 뒤 다음 착탄까지 어긋난 채로 남는다");
            Assert.That(sync.Substring(movesAttr, moves - movesAttr),
                Does.Contain("Unreliable"),
                "움직임을 신뢰 채널로 보내면 재전송이 쌓인다 — 매 틱 새 값이 온다");

            var send = TestSource.Method(
                TestSource.Read("Runtime/Network/WorldSync.cs"), "void SendScars()");
            Assert.That(send, Does.Contain("IsScarAwake"),
                "깨어 있는 것만 골라야 한다 — 전부 실으면 사람당 40KB/s 다");

            var awake = TestSource.Method(
                TestSource.Read("Runtime/Network/WorldSync.cs"),
                "static bool IsScarAwake(ScarRecord scar)");
            Assert.That(awake, Does.Contain("IsSleeping"),
                "잠든 강체를 걸러내는 것이 \"끌리는 돌만\" 을 뜻한다");
        }

        /// <summary>
        /// <b>흔적을 훑는 길에서 <c>GetComponent</c> 를 부르지 않는다</b>
        /// (S15P21B202-355, `!321` AI 리뷰).
        ///
        /// 처음에는 <c>IsScarAwake</c>·<c>StateOf</c> 가 각각
        /// <c>GetComponent&lt;Rigidbody&gt;()</c> 를 불렀다. 이 둘은 송신 틱마다 흔적
        /// 전체에 대해 불리므로 <b>상한 200개 × 30Hz 면 프레임당 수백 회</b>가 된다.
        /// <c>-374</c>(여러 명이 같이 뭔가 하면 프레임이 떨어진다)가 열려 있는 동안
        /// 그런 것을 얹으면 어느 쪽이 원인인지 못 가른다.
        ///
        /// <c>Meteor</c> 가 이미 <c>_body</c> 를 캐시하고 있어서, 그것을 내보내는 것으로
        /// 끝났다 — 값을 한 벌 더 들고 있지 않는다.
        /// </summary>
        [Test]
        public void ScarSendingDoesNotSearchComponentsEveryTick()
        {
            var sync = TestSource.Read("Runtime/Network/WorldSync.cs");

            foreach (var name in new[]
                     {
                         "void SendScars()",
                         "static bool IsScarAwake(ScarRecord scar)",
                         "static ScarState StateOf(ScarRecord scar)",
                     })
            {
                var body = TestSource.Method(sync, name);
                Assert.That(body, Does.Not.Contain("GetComponent"),
                    $"{name} 이 매 틱 컴포넌트를 찾는다 — 흔적 200개 × 30Hz 면 "
                    + "프레임당 수백 회다. Meteor.Body 를 쓸 것");
            }

            // 받는 쪽도 같다. 전체 목록은 흔적 전부를 훑는다.
            var apply = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Hazards/MeteorSpawner.cs"),
                "public void ApplyNetworkScars(IReadOnlyList<NetworkScarState> states, bool full)");
            Assert.That(apply, Does.Not.Contain("GetComponent"),
                "받는 쪽도 목록 크기만큼 컴포넌트를 찾는다 — Meteor.Body 를 쓸 것");
        }

        /// <summary>
        /// <b>잡힌 돌도 "움직이는 것" 에 든다</b> (S15P21B202-355, `!321` AI 리뷰 높음).
        ///
        /// <para><c>PlayerRig.BeginCarriedBody</c>(<c>:3110</c>)가 집는 순간
        /// <c>isKinematic = true</c> 로 만들고, 유니티에서 kinematic 강체는
        /// <c>IsSleeping()</c> 이 <b>항상 참</b>이다. <c>IsSleeping()</c> 만 보면
        /// <b>끌고 다니는 내내 이동 묶음이 비어서</b> 참가자 화면에서 돌이 2초
        /// 되보내기마다 툭툭 순간이동한다 — 이 이슈 완료 조건 1번이 무너지는 자리다.</para>
        ///
        /// <para>흔적에게 <c>isKinematic</c> 은 곧 "누가 들고 있다" 다. 착지한 돌은
        /// <c>Settle</c> 이 <c>isKinematic = false</c> 로 두고, 참으로 바꾸는 곳은
        /// 운반뿐이며 놓을 때 되돌린다.</para>
        /// </summary>
        [Test]
        public void CarriedScarsCountAsMoving()
        {
            var awake = TestSource.Method(
                TestSource.Read("Runtime/Network/WorldSync.cs"),
                "static bool IsScarAwake(ScarRecord scar)");

            Assert.That(awake, Does.Contain("IsSleeping"),
                "잠든 돌을 걸러내는 것이 이 채널의 값이다");
            Assert.That(awake, Does.Contain("isKinematic"),
                "잡힌 돌은 kinematic 이라 잠든 것으로 보인다 — 그것만 보면 끌어도 "
                + "이동 묶음이 비고, 완료 조건 1번이 무너진다");
        }

        /// <summary>
        /// <b>두 묶음이 같은 순번 축을 쓴다</b> (S15P21B202-355, `!321` AI 리뷰).
        ///
        /// <para>전체 목록은 <b>신뢰</b> 채널, 이동은 <b>비신뢰</b> 채널이라 서로 순서가
        /// 보장되지 않는다. 순번을 안 보면 <b>늦게 도착한 전체 목록이 최신 이동을
        /// 덮어</b>, 끌고 있는 돌이 2초마다 옛 자리로 되돌아간다.</para>
        /// </summary>
        [Test]
        public void BothScarPacketsShareOneSequenceAxis()
        {
            var sync = TestSource.Read("Runtime/Network/WorldSync.cs");

            foreach (var name in new[]
                     {
                         "void RpcScarSet(uint seq, ScarState[] states)",
                         "void RpcScarMoves(uint seq, ScarState[] states)",
                     })
            {
                var body = TestSource.Method(sync, name);
                Assert.That(body, Does.Contain("lastScarSeq"),
                    $"{name} 이 순번을 보지 않으면 늦게 온 묶음이 최신 자세를 덮는다");
            }

            // **축이 둘이면 뜻이 없다.** 각자 자기 순번만 보면 서로를 거르지 못한다.
            var blanked = TestSource.Blank(sync);
            Assert.That(blanked, Does.Not.Contain("lastScarMoveSeq"),
                "순번 축이 둘로 갈라져 있다 — 두 묶음이 서로를 거르지 못한다");
        }

        static MeteorSpawner.NetworkScarState Scar(int id, Vector3 at) =>
            new MeteorSpawner.NetworkScarState
            {
                Id = id,
                Position = at,
                Rotation = Quaternion.identity,
            };

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
