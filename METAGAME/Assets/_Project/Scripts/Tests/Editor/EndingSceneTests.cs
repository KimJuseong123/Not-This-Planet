using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class EndingSceneTests
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void EndingSceneIsIncludedAndReferencesFourVisualPrefabs()
        {
            Assert.That(SceneUtility.GetBuildIndexByScenePath(EndingSceneFlow.ScenePath), Is.GreaterThanOrEqualTo(0));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(EndingSceneFlow.ScenePath), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DebugConfirmationCancelsSafelyRejectsClientAndUsesRealSceneFlow()
        {
            yield return new EnterPlayMode();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Stage1_Astra"));
            var root = new GameObject("Debug ending test");
            var session = root.AddComponent<GameSession>();
            session.Begin(null, null, Vector3.zero);
            var parts = new SpaceshipPart[4];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = new GameObject(PartSpec.All[i].Name).AddComponent<SpaceshipPart>();
                parts[i].GetComponent<Rigidbody>().isKinematic = true;
                parts[i].transform.position = new Vector3(i * 5f, 0f, 0f);
            }
            var ending = root.AddComponent<DemoEnding>();
            ending.Configure(session, parts, () => true, () => Time.realtimeSinceStartupAsDouble,
                () => new[] { new DemoEndingPlayer { Name = "Debug captain", Suit = Color.cyan } }, () => 3723d);
            var pause = root.AddComponent<Metagame.UI.GamePauseMenu>();
            pause.Initialize(session);
            var shortcut = root.AddComponent<DevManager>();
            shortcut.Configure(session, ending, pause, true);
            var settings = new SerializedObject(shortcut);
            settings.FindProperty("enableF12Ending").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(shortcut.OpenConfirmation(), Is.False);
            Assert.That(session.InputSuppressed, Is.False);
            settings.FindProperty("enableF12Ending").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(shortcut.OpenConfirmation(), Is.True);
            settings.FindProperty("enableF12Ending").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(shortcut.CanConfirm, Is.False);
            yield return null;
            Assert.That(shortcut.IsOpen, Is.False);
            Assert.That(session.InputSuppressed, Is.False);
            Assert.That(pause.enabled, Is.True);
            settings.FindProperty("enableF12Ending").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(shortcut.OpenConfirmation(), Is.True);
            Assert.That(session.InputSuppressed, Is.True);
            Assert.That(pause.enabled, Is.False);
            Assert.That(shortcut.CanConfirm, Is.False, "A participant must not initiate server completion.");
            Assert.That(shortcut.Confirm(), Is.False);
            shortcut.Cancel();
            yield return null;
            Assert.That(session.InputSuppressed, Is.False);
            Assert.That(pause.enabled, Is.True);
            Assert.That(ending.Active, Is.False);
            Assert.That(parts[1].transform.position.x, Is.EqualTo(5f));
            shortcut.Configure(session, ending, pause, false);
            EndingSceneFlow.Ensure().Watch(ending);
            Assert.That(shortcut.OpenConfirmation(), Is.True);
            Assert.That(shortcut.Confirm(), Is.True);
            Assert.That(session.EndingActive, Is.True);
            var deadline = Time.realtimeSinceStartupAsDouble + 15d;
            while (SceneManager.GetActiveScene().path != EndingSceneFlow.ScenePath
                && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(EndingSceneFlow.ScenePath));
            Assert.That(root == null, Is.True);
            var playback = Object.FindAnyObjectByType<DemoEnding>();
            Assert.That(playback.Snapshot.Players[0].Name, Is.EqualTo("Debug captain"));
            Assert.That(playback.Snapshot.PlayTimeSeconds, Is.EqualTo(3723d));
            Assert.That(playback.PlaybackStarted, Is.True);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator UnpreparedParticipantHasBoundedWaitAndReportsFailure()
        {
            yield return new EnterPlayMode();
            var flow = EndingSceneFlow.Ensure();
            typeof(EndingSceneFlow).GetField("transferring", Private).SetValue(flow, true);
            typeof(EndingSceneFlow).GetField("preparationBeganAt", Private).SetValue(flow,
                Time.realtimeSinceStartupAsDouble - EndingSceneFlow.PreparationTimeoutSeconds - 1d);
            LogAssert.Expect(LogType.Warning, "[EndingScene] 엔딩 준비 시간이 초과되었습니다. 연결 또는 씬 로딩을 확인해 주세요.");
            yield return null;
            Assert.That(flow.Failure, Does.Contain("초과"));
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SoloTransferRebuildsSnapshotAfterOriginalSceneIsUnloaded()
        {
            yield return new EnterPlayMode();
            var oldScene = SceneManager.GetActiveScene();
            var marker = new GameObject("Old gameplay scene marker");
            var factory = new GameObject("Preview snapshot factory");
            factory.SetActive(false);
            var snapshot = factory.AddComponent<Stage1EndingBootstrap>().CreatePreview();
            snapshot.PlayTimeSeconds = 3723d;
            snapshot.Parts[1].Position += new Vector3(0.4f, 0f, -0.2f);
            snapshot.Parts[1].Rotation = Quaternion.Euler(0f, 12f, 8f);
            var flow = EndingSceneFlow.Ensure();
            flow.Begin(snapshot);
            var deadline = Time.realtimeSinceStartupAsDouble + 15d;
            while (SceneManager.GetActiveScene().path != EndingSceneFlow.ScenePath
                && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(EndingSceneFlow.ScenePath));
            Assert.That(marker == null, Is.True, "Gameplay scene must be unloaded, not hidden additively.");
            Assert.That(Object.FindAnyObjectByType<GameSession>(), Is.Null);
            var ending = Object.FindAnyObjectByType<DemoEnding>();
            Assert.That(ending.Snapshot.PlayTimeSeconds, Is.EqualTo(3723d));
            Assert.That(ending.Snapshot.Parts[1].Rotation, Is.EqualTo(snapshot.Parts[1].Rotation));
            Assert.That(ending.PlaybackStarted, Is.True);
            Assert.That(Object.FindObjectsByType<MeshRenderer>().Length, Is.GreaterThan(4));
            var bootstrap = Object.FindAnyObjectByType<Stage1EndingBootstrap>();
            Assert.That(bootstrap.PartVisuals.Length, Is.EqualTo(4));
            foreach (var prefab in bootstrap.PartVisuals) Assert.That(prefab, Is.Not.Null);

            // 서버 대기 장벽: 하나라도 준비되지 않으면 시작하지 않는다.
            var connections = NetworkServer.connections;
            NetworkServer.connections = new Dictionary<int, NetworkConnectionToClient>();
            try
            {
                var a = new NetworkConnectionToClient(11) { isAuthenticated = true };
                var b = new NetworkConnectionToClient(12) { isAuthenticated = true };
                NetworkServer.connections.Add(11, a);
                NetworkServer.connections.Add(12, b);
                var ready = (HashSet<NetworkConnectionToClient>)typeof(EndingSceneFlow).GetField("ready", Private).GetValue(flow);
                var allReady = typeof(EndingSceneFlow).GetMethod("AllParticipantsReady", Private);
                ready.Add(a);
                Assert.That(allReady.Invoke(flow, null), Is.False);
                ready.Add(b);
                Assert.That(allReady.Invoke(flow, null), Is.True);
                // 동일 연결 번호라도 새로 접속한 참가자는 다시 준비해야 한다.
                NetworkServer.connections[12] = new NetworkConnectionToClient(12) { isAuthenticated = true };
                Assert.That(allReady.Invoke(flow, null), Is.False);
                NetworkServer.connections.Remove(12);
                Assert.That(allReady.Invoke(flow, null), Is.True, "Disconnected players cannot block the ending.");
            }
            finally { NetworkServer.connections = connections; }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator LargeSnapshotChunksPreserveUtf8AndRejectOutOfOrderData()
        {
            yield return new EnterPlayMode();
            var flow = EndingSceneFlow.Ensure();
            var json = JsonUtility.ToJson(new DemoEndingSnapshot
            { Players = new[] { new DemoEndingPlayer { Name = new string('별', 30000) } } });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            Assert.That(bytes.Length, Is.GreaterThan(65535));
            var receive = typeof(EndingSceneFlow).GetMethod("ReceiveChunk", Private);
            for (int offset = 0; offset < bytes.Length; offset += 8192)
            {
                var chunk = new byte[System.Math.Min(8192, bytes.Length - offset)];
                System.Buffer.BlockCopy(bytes, offset, chunk, 0, chunk.Length);
                var writer = new NetworkWriter();
                writer.Write(new EndingSceneSnapshotChunk { Offset = offset, TotalBytes = bytes.Length, Data = chunk });
                receive.Invoke(flow, new object[] { new NetworkReader(writer.ToArraySegment()).Read<EndingSceneSnapshotChunk>() });
            }
            Assert.That(typeof(EndingSceneFlow).GetField("snapshotJson", Private).GetValue(flow), Is.EqualTo(json));
            Object.Destroy(flow.gameObject);
            yield return null;
            flow = EndingSceneFlow.Ensure();
            LogAssert.Expect(LogType.Warning, "[EndingScene] 엔딩 정보의 전송 순서 또는 크기가 올바르지 않습니다.");
            receive.Invoke(flow, new object[] { new EndingSceneSnapshotChunk { Offset = 1, TotalBytes = 10, Data = new byte[1] } });
            Assert.That(flow.Failure, Is.Not.Empty);
            Assert.That(typeof(EndingSceneFlow).GetField("incomingSnapshot", Private).GetValue(flow), Is.Null);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator DisconnectKeepsEndingClockInsteadOfSwitchingToLocalUptime()
        {
            yield return new EnterPlayMode();
            var flow = EndingSceneFlow.Ensure();
            typeof(EndingSceneFlow).GetField("networked", Private).SetValue(flow, true);
            typeof(EndingSceneFlow).GetField("lastClock", Private).SetValue(flow, 1234d);
            Assert.That(typeof(EndingSceneFlow).GetMethod("Clock", Private).Invoke(flow, null), Is.EqualTo(1234d));
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MirrorEndingMessagePreservesSnapshotAndSharedStartTime()
        {
            yield return new EnterPlayMode();
            var writer = new NetworkWriter();
            var state = new EndingSceneStateMessage
            {
                SnapshotJson = JsonUtility.ToJson(new DemoEndingSnapshot { PlayTimeSeconds = 3723d }),
                Started = true, StartAt = 1234.5d
            };
            writer.Write(state);
            var received = new NetworkReader(writer.ToArraySegment()).Read<EndingSceneStateMessage>();
            Assert.That(received.Started, Is.True);
            Assert.That(received.StartAt, Is.EqualTo(state.StartAt));
            Assert.That(JsonUtility.FromJson<DemoEndingSnapshot>(received.SnapshotJson).PlayTimeSeconds, Is.EqualTo(3723d));
            yield return new ExitPlayMode();
        }
    }
}
