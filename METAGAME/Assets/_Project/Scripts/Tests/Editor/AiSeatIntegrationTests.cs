using System.Collections;
using System.Linq;
using Metagame.Network;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiSeatIntegrationTests
    {
        sealed class Runtime : IAiRuntimeStatus
        {
            public AiRuntimeState State { get; set; } = AiRuntimeState.Missing;
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator ReadinessGatesSpawnAndHumanInheritsSameBody()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            TeamSlots.Clear();
            AiHostSettings.BeginRoom();
            var lobby = AiHostPolicyTests.CreateLobby();
            Assert.That(AiHostSettings.TrySetRequested(true), Is.True);
            var runtime = new Runtime();
            AiHostSettings.BindRuntime(runtime);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, -.5f, 0);
            floor.transform.localScale = new Vector3(60, 1, 60);
            var host = PlayerRig.Create("Host", Vector3.up * .1f, Quaternion.identity, Color.red, Color.white, false);
            var session = new GameObject("AI seat integration").AddComponent<GameSession>();
            session.Begin(host, null, Vector3.zero);
            session.ScriptedInput = true;
            var root = new GameObject("Test network");
            root.SetActive(false);
            var transport = root.AddComponent<TelepathyTransport>();
            transport.port = 27968;
            var manager = root.AddComponent<NetworkManager>();
            manager.transport = transport;
            manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Network/PF_NetPlayer.prefab");
            manager.headlessStartMode = HeadlessStartOptions.DoNothing;
            manager.onlineScene = manager.offlineScene = "";
            root.SetActive(true);
            manager.StartHost();
            float deadline = Time.realtimeSinceStartup + 15f;
            while (TeamSeats.Server == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(TeamSeats.Server, Is.Not.Null);
            yield return new WaitForSeconds(.5f);
            Assert.That(NetPlayer.AllServerPlayers.Count(p => p.IsAi), Is.Zero, "Missing pack must not spawn idle substitutes");
            runtime.State = AiRuntimeState.Ready;
            deadline = Time.realtimeSinceStartup + 15f;
            while (NetPlayer.AllServerPlayers.Count(p => p.IsAi && p.HasBody) != 3
                && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(NetPlayer.AllServerPlayers.Count(p => p.IsAi && p.HasBody), Is.EqualTo(3));
            var ai = NetPlayer.AllServerPlayers.First(p => p.IsAi && p.HasBody);
            var body = ai.VisualRig;
            int slot = ai.teamSlot;
            Assert.That(ai.TryApplyAiOrder("follow", host), Is.True);
            var humanObject = Object.Instantiate(manager.playerPrefab);
            var human = humanObject.GetComponent<NetPlayer>();
            // Exercise the same atomic seat handoff used by BuildServerBody, without
            // pretending this in-process test is a two-machine reconnect test.
            var inherited = TeamSeats.Server.ClaimBody(slot, human, true, "Returning human");
            Assert.That(inherited, Is.SameAs(body));
            Assert.That(TeamSeats.Server.DriverOf(slot), Is.SameAs(human));
            yield return null;
            Assert.That(body, Is.Not.Null, "Destroying the AI driver must preserve its pooled body");
            Assert.That(NetPlayer.AllServerPlayers.Count(p => p.IsAi), Is.EqualTo(2));
            Assert.That(TeamSeats.Server.NeedsAi(slot), Is.False);
            Object.Destroy(humanObject);
            manager.StopHost();
            Object.Destroy(root);
            Object.Destroy(lobby.gameObject);
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (NetworkManager.singleton != null && NetworkServer.active) NetworkManager.singleton.StopHost();
            if (Application.isPlaying) yield return new ExitPlayMode();
            TeamSlots.Clear();
            AiHostSettings.BeginRoom();
            AiHostSettings.BindRuntime(null);
        }
    }
}
