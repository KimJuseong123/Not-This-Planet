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
