using System;
using System.Collections;
using System.IO;
using System.Linq;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiRuntimeTests
    {
        [TestCase("{}", false)]
        [TestCase("{\"service\":\"other\",\"protocol\":\"companion-v4\",\"state\":\"ready\"}", false)]
        [TestCase("{\"service\":\"ntp-ai-worker\",\"protocol\":\"companion-v3\",\"state\":\"ready\"}", false)]
        [TestCase("{\"service\":\"ntp-ai-worker\",\"protocol\":\"companion-v4\",\"state\":\"loading\"}", false)]
        [TestCase("{\"service\":\"ntp-ai-worker\",\"protocol\":\"companion-v4\",\"state\":\"ready\"}", false)]
        [TestCase("{\"service\":\"ntp-ai-worker\",\"protocol\":\"companion-v4\",\"state\":\"ready\",\"command_schema\":\"social-v1\"}", false)]
        [TestCase("{\"service\":\"ntp-ai-worker\",\"protocol\":\"companion-v4\",\"state\":\"ready\",\"command_schema\":\"work-v1\"}", false)]
        [TestCase("{\"service\":\"ntp-ai-worker\",\"protocol\":\"companion-v4\",\"state\":\"ready\",\"command_schema\":\"work-v1\",\"voice_plan\":true}", true)]
        public void ReadyRequiresMatchingWorkerAndProtocol(string json, bool expected)
            => Assert.That(AiLocalRuntime.ValidHealth(json), Is.EqualTo(expected));

        [Test] public void MissingPackDoesNotQualify() => Assert.That(AiLocalRuntime.HasPack(""), Is.False);

        /// <summary>
        /// 팩 인식은 <b>모델 이름에 걸려 있으면 안 된다</b> (S15P21B202-726).
        ///
        /// <para>전에는 <c>whisper-small-ko-zeroth</c> 를 글자 그대로 찾았다. 모델을
        /// <c>whisper-small</c> 로 바꾸자 <b>설치를 마친 팩이 "없는 팩" 이 됐고</b>, 그 사실은
        /// 1.9GB 를 받아 설치까지 끝낸 뒤에야 드러났다. 여기서 이름을 다시 박으면 같은 일이
        /// 다음 모델 교체 때 또 난다 — 그래서 이 시험은 <b>일부러 없는 이름</b>을 쓴다.</para>
        /// </summary>
        [TestCase("whisper-small", true)]
        [TestCase("whisper-small-ko-zeroth", true)]
        [TestCase("whatever-we-pick-next", true)]
        [TestCase(null, false)]
        public void PackIsRecognisedWhateverTheModelIsCalled(string model, bool expected)
        {
            string root = Path.Combine(Path.GetTempPath(), "ntp-pack-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "runtime/ntp-ai-worker"));
                File.WriteAllText(Path.Combine(root, "runtime/ntp-ai-worker/ntp-ai-worker.exe"), "");
                Directory.CreateDirectory(Path.Combine(root, "models/ollama/manifests/registry.ollama.ai/library/qwen3"));
                File.WriteAllText(Path.Combine(root, "models/ollama/manifests/registry.ollama.ai/library/qwen3/1.7b"), "");
                if (model != null)
                {
                    Directory.CreateDirectory(Path.Combine(root, "models", model));
                    File.WriteAllText(Path.Combine(root, "models", model, "model.bin"), "");
                }
                Assert.That(AiLocalRuntime.HasPack(root), Is.EqualTo(expected));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test] public void DecisionsRequireKnownActionsTargetsAndApproach()
        {
            Assert.That(AiLocalRuntime.ValidDecision(new AiLocalRuntime.Decision {action="dance",recipients="all",approach=true}),Is.True);
            Assert.That(AiLocalRuntime.ValidDecision(new AiLocalRuntime.Decision {action="dance"}),Is.False);
            Assert.That(AiLocalRuntime.ValidDecision(new AiLocalRuntime.Decision {action="weld",recipients="all"}),Is.True);
            Assert.That(AiLocalRuntime.ValidDecision(new AiLocalRuntime.Decision {action="teleport",recipients="all"}),Is.False);
            Assert.That(AiLocalRuntime.ValidDecision(new AiLocalRuntime.Decision {action="cancel",recipients="all",approach=true}),Is.False);
        }

        [TestCase(1f, 0f, 0f)] [TestCase(35f, 0f, 0f)] [TestCase(6f, 2f, 0f)] [TestCase(6f, 0f, 1f)]
        public void FollowHasStopDistanceHeightAndRangeLimits(float distance, float height, float move)
        {
            var frame = AiFollowController.Direction(Vector3.zero, new Vector3(0, height, distance), 2.3f);
            Assert.That(frame.Move.y, Is.EqualTo(move));
            Assert.That(frame.GrabLeft || frame.GrabRight || frame.Jump || frame.Run || frame.ToolAction, Is.False);
        }

        [Test] public void UnknownActionDoesNotBecomeMovement()
        {
            var driver = new AiFollowController();
            Assert.That(driver.Apply("pull_pin", null), Is.False);
            Assert.That(driver.Apply("follow", null), Is.False);
            Assert.That(driver.Apply("cancel", null), Is.True);
            Assert.That(driver.Status, Is.EqualTo("대기"));
        }

        [UnityTest, Category("LocalAiPack")]
        public IEnumerator LocalPackTextControlsActualServerAi()
        {
            if (Environment.GetEnvironmentVariable("NTP_TEST_LOCAL_AI") != "1")
                Assert.Ignore("Opt-in local models integration; no model downloads.");
            Assert.That(AiLocalRuntime.HasPack(AiLocalRuntime.DefaultPackRoot()), Is.True);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            TeamSlots.Clear();
            var lobby = AiHostPolicyTests.CreateLobby();
            AiHostSettings.BeginRoom();
            Assert.That(AiHostSettings.TrySetRequested(true), Is.True);
            var runtime = new GameObject("Test local worker").AddComponent<AiLocalRuntime>();
            float deadline = Time.realtimeSinceStartup + 150f;
            while (runtime.State != AiRuntimeState.Ready && runtime.State != AiRuntimeState.Failed && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(runtime.State, Is.EqualTo(AiRuntimeState.Ready), runtime.Message);
            runtime.enabled = false;
            Assert.That(runtime.State, Is.EqualTo(AiRuntimeState.Missing));
            // OS 자식 종료에 시간을 주고 재활성화한다. 점유된 서비스를 강제로 정리하지 않는다.
            yield return new WaitForSecondsRealtime(2f);
            runtime.enabled = true;
            deadline = Time.realtimeSinceStartup + 150f;
            while (runtime.State != AiRuntimeState.Ready && runtime.State != AiRuntimeState.Failed && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(runtime.State, Is.EqualTo(AiRuntimeState.Ready), runtime.Message);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, -.5f, 0);
            floor.transform.localScale = new Vector3(60, 1, 60);
            var session = new GameObject("AI test session").AddComponent<GameSession>();
            var host = PlayerRig.Create("Host", Vector3.up * .1f, Quaternion.identity, Color.red, Color.white, false);
            session.Begin(host, null, Vector3.zero); session.ScriptedInput = true;
            var root = new GameObject("AI test network"); root.SetActive(false);
            var transport = root.AddComponent<TelepathyTransport>(); transport.port = 27964;
            var manager = root.AddComponent<NetworkManager>();
            manager.transport = transport;
            manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Network/PF_NetPlayer.prefab");
            manager.headlessStartMode = HeadlessStartOptions.DoNothing; manager.onlineScene = ""; manager.offlineScene = "";
            root.SetActive(true); manager.StartHost();
            deadline = Time.realtimeSinceStartup + 15f;
            while (NetPlayer.AllServerPlayers.Count(p => p != null && p.IsAi && p.HasBody) != 3 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(NetPlayer.AllServerPlayers.Count(p => p != null && p.IsAi && p.HasBody), Is.EqualTo(3));
            var ai = NetPlayer.AllServerPlayers.First(p => p.IsAi && p.HasBody);
            ai.VisualRig.Teleport(new Vector3(0,.1f,8), Quaternion.identity);
            yield return new WaitForSeconds(.5f);
            float before = Vector3.Distance(ai.VisualRig.FootPosition, host.FootPosition);
            Assert.That(runtime.SendText("나 따라와", ai), Is.True);
            yield return WaitForReply(runtime);
            Assert.That(runtime.Message, Does.StartWith("follow →"));
            deadline = Time.realtimeSinceStartup + 8f;
            while (Vector3.Distance(ai.VisualRig.FootPosition, host.FootPosition) > before - 1f && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Vector3.Distance(ai.VisualRig.FootPosition, host.FootPosition), Is.LessThan(before - 1f), ai.AiActivity);
            Assert.That(runtime.SendText("여기서 기다려", ai), Is.True);
            yield return WaitForReply(runtime);
            Assert.That(runtime.Message, Does.StartWith("wait →"));
            yield return new WaitForFixedUpdate();
            Assert.That(ai.VisualRig.CurrentInput.Move, Is.EqualTo(Vector2.zero));
            Assert.That(runtime.SendText("나 따라와", ai), Is.True);
            runtime.StopOrders();
            yield return new WaitForSeconds(2f);
            Assert.That(runtime.Busy, Is.False);
            Assert.That(ai.VisualRig.CurrentInput.Move, Is.EqualTo(Vector2.zero), "Late reply must not undo stop");
            Assert.That(runtime.SendText("용접해줘", ai), Is.True);
            yield return WaitForReply(runtime);
            Assert.That(ai.VisualRig.CurrentInput.Move, Is.EqualTo(Vector2.zero));
            Assert.That(runtime.Message, Does.Contain("지원하지 않는"));
            Assert.That(runtime.SendText("못 뽑아줘", ai), Is.True);
            yield return WaitForReply(runtime);
            Assert.That(runtime.Message, Does.StartWith("pull_pin →"));
            yield return new WaitForFixedUpdate();
            Assert.That(ai.AiActivity, Does.Contain("고정못"), "Classified pin order must reach the game task");
            runtime.StopOrders();
            // Batch server omits meshes; explicitly attach one for real LLM -> dance validation.
            if(ai.VisualRig.Doll.Suit==null)
                ai.VisualRig.Doll.Suit=SpaceSuit.Attach(ai.VisualRig.Doll,ai.VisualRig.Doll.Hips.transform.parent);
            ai.VisualRig.Doll.Suit.InitializeLocomotion();
            Assert.That(runtime.SendText("춤춰봐",ai),Is.True);
            yield return WaitForReply(runtime);
            Assert.That(runtime.Message,Does.StartWith("dance → 1/1명"));
            deadline=Time.realtimeSinceStartup+4;
            while(!ai.VisualRig.DanceHolding && ai.AiBusy && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(ai.VisualRig.DanceHolding,Is.True,ai.AiActivity);
            runtime.StopOrders();
            manager.StopHost();
            Object.Destroy(runtime.gameObject);
            deadline = Time.realtimeSinceStartup + 3f;
            while (AiLocalRuntime.Current != null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(AiLocalRuntime.Current == null, Is.True);
            Assert.That(AiHostSettings.RuntimeState, Is.EqualTo(AiRuntimeState.Missing));
            Object.Destroy(root); Object.Destroy(session.gameObject); Object.Destroy(host.gameObject);
            Object.Destroy(floor); Object.Destroy(lobby.gameObject);
        }

        static IEnumerator WaitForReply(AiLocalRuntime runtime)
        {
            float deadline = Time.realtimeSinceStartup + 35f;
            while (runtime.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(runtime.Busy, Is.False, runtime.Message);
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (NetworkManager.singleton != null && NetworkServer.active) NetworkManager.singleton.StopHost();
            if (AiLocalRuntime.Current != null) Object.Destroy(AiLocalRuntime.Current.gameObject);
            if (Application.isPlaying) yield return new ExitPlayMode();
            TeamSlots.Clear(); AiHostSettings.BeginRoom(); AiHostSettings.BindRuntime(null);
        }
    }
}
