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

        /// <summary>한 판을 띄우는 데 쓴 것들. 끝에서 치운다.</summary>
        sealed class Match
        {
            public NetworkSession Lobby;
            public Runtime Runtime;
            public PlayerRig Host;
            public NetworkManager Manager;
            public GameObject Root;
        }

        /// <summary>
        /// 방장 혼자인 방에서 판을 띄운다. AI 수는 <b>방(ROOM_STATE)에서 온 값</b>으로 정한다 —
        /// 실제로도 그 길 하나뿐이다(S15P21B202-713). 실행기는 아직 준비 전(Missing)이다.
        /// </summary>
        static Match StartHost(int aiCount, ushort port)
        {
            TeamSlots.Clear();
            AiHostSettings.BeginRoom();
            var match = new Match { Lobby = AiHostPolicyTests.CreateLobby(aiCount: aiCount), Runtime = new Runtime() };
            Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(aiCount), "방의 AI 수를 받아 두지 않았다");
            AiHostSettings.BindRuntime(match.Runtime);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, -.5f, 0);
            floor.transform.localScale = new Vector3(60, 1, 60);
            match.Host = PlayerRig.Create("Host", Vector3.up * .1f, Quaternion.identity, Color.red, Color.white, false);
            var session = new GameObject("AI seat integration").AddComponent<GameSession>();
            session.Begin(match.Host, null, Vector3.zero);
            session.ScriptedInput = true;
            match.Root = new GameObject("Test network");
            match.Root.SetActive(false);
            var transport = match.Root.AddComponent<TelepathyTransport>();
            transport.port = port;
            match.Manager = match.Root.AddComponent<NetworkManager>();
            match.Manager.transport = transport;
            match.Manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Network/PF_NetPlayer.prefab");
            match.Manager.headlessStartMode = HeadlessStartOptions.DoNothing;
            match.Manager.onlineScene = match.Manager.offlineScene = "";
            match.Root.SetActive(true);
            match.Manager.StartHost();
            return match;
        }

        static void Stop(Match match)
        {
            match.Manager.StopHost();
            Object.Destroy(match.Root);
            Object.Destroy(match.Lobby.gameObject);
        }

        static int SeatedAi() => NetPlayer.AllServerPlayers.Count(p => p != null && p.IsAi && p.HasBody);

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator ReadinessGatesSpawnAndHumanInheritsSameBody()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var match = StartHost(aiCount: 3, port: 27968);
            var manager = match.Manager;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (TeamSeats.Server == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(TeamSeats.Server, Is.Not.Null);
            yield return new WaitForSeconds(.5f);
            Assert.That(NetPlayer.AllServerPlayers.Count(p => p.IsAi), Is.Zero, "Missing pack must not spawn idle substitutes");
            match.Runtime.State = AiRuntimeState.Ready;
            deadline = Time.realtimeSinceStartup + 15f;
            while (SeatedAi() != 3 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SeatedAi(), Is.EqualTo(3));
            var ai = NetPlayer.AllServerPlayers.First(p => p.IsAi && p.HasBody);
            var body = ai.VisualRig;
            int slot = ai.teamSlot;
            Assert.That(ai.TryApplyAiOrder("follow", match.Host), Is.True);
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
            Stop(match);
            yield return new ExitPlayMode();
        }

        /// <summary>
        /// 방이 AI 하나를 고르면 <b>하나만</b> 뜨고, 그 자리는 가장 높은 번호(초록)다 (S15P21B202-713).
        ///
        /// <para>예전에는 빈 자리를 전부 채워 셋이 떴다. 낮은 번호부터 채우면 1번(파랑)에 앉아, 늦게
        /// 붙는 사람(가장 낮은 빈 자리를 받는다)과 부딪친다.</para>
        /// </summary>
        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator OneRequestedAiTakesOnlyTheTopSeat()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var match = StartHost(aiCount: 1, port: 27969);
            match.Runtime.State = AiRuntimeState.Ready;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (SeatedAi() < 1 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SeatedAi(), Is.EqualTo(1));

            // 몇 프레임을 더 보아도 하나 그대로다 — 앉는 사이에 하나 더 띄우지 않는다.
            yield return new WaitForSeconds(1f);
            var ais = NetPlayer.AllServerPlayers.Where(p => p != null && p.IsAi).ToArray();
            Assert.That(ais.Length, Is.EqualTo(1), "고른 수보다 많이 떴다");
            Assert.That(ais[0].teamSlot, Is.EqualTo(TeamSlots.Count - 1), "가장 높은 자리에 앉지 않았다");
            Stop(match);
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
