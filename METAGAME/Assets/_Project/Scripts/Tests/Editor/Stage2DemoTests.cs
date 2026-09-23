using System.Collections;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.Gameplay.Stage2;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using Metagame.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage2DemoTests
    {
        static double clock;
        Stage2Yard yard;
        GameSession session;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Stage2_Earth.unity");
            yield return new EnterPlayMode();
            yield return null;
            session = GameSession.Instance;
            yard = Object.FindAnyObjectByType<Stage2Yard>();
            Object.FindAnyObjectByType<Stage2Bridge>().enabled = false;
            session.ScriptedInput = true;
            clock = 100;
            yard.SetClock(() => clock);
            yard.State.DemoStart = clock;
            yard.State.DemoStarted = true;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [UnityTest]
        public IEnumerator ThreeSecondsThenCameraAssemblyAndTerminalCardWithoutClearing()
        {
            Assert.That(yard.DemoMode, Is.True);
            var movement = session.StageInputFilter(new InputFrame
            { Move = Vector2.up, Jump = true, ToolPrimary = true, GrabLeft = true, TransportAction = true });
            Assert.That(movement.Move, Is.EqualTo(Vector2.up));
            Assert.That(movement.Jump, Is.True);
            Assert.That(movement.ToolPrimary || movement.GrabLeft || movement.TransportAction, Is.False);
            clock = 102.99;
            Assert.That(session.CinematicInputBlocked, Is.False);
            clock = 103;
            Assert.That(session.CinematicInputBlocked, Is.True);
            Assert.That(session.StageInputFilter(movement).Move, Is.EqualTo(Vector2.zero));
            var view = Object.FindAnyObjectByType<Stage2DemoView>();
            view.Tick();
            Assert.That(view.Presenting, Is.True);
            Assert.That(view.ActorCount, Is.GreaterThan(0));
            Assert.That(session.LocalPlayer.Cam.enabled, Is.False);
            Capture(session.LocalPlayer.Cam.Cam, "01-overview");
            var before = session.LocalPlayer.Cam.transform.position;
            clock = 108;
            view.Tick();
            Assert.That(Vector3.Distance(before, session.LocalPlayer.Cam.transform.position), Is.GreaterThan(1));
            Capture(session.LocalPlayer.Cam.Cam, "02-yard");
            clock = 114.8;
            yield return null;
            Assert.That(yard.DemoBoltProgress(0), Is.EqualTo(1));
            var firstModule = yard.transform.Find("09 Rocket module 1");
            Assert.That(Vector3.Distance(firstModule.position, yard.DemoSocket(0)), Is.LessThan(.01f),
                "Presentation transforms must not lag behind Rigidbody interpolation.");
            Debug.Log($"[DemoVisual] module={firstModule.position} socket={yard.DemoSocket(0)}");
            foreach (var renderer in yard.GetComponentsInChildren<Renderer>())
                if (renderer.name.Contains("UpperPlatform")) Debug.Log($"[DemoVisual] platform={renderer.bounds}");
            var crew = GameObject.Find("Stage2 demo crew - presentation only");
            Assert.That(crew.GetComponentsInChildren<Mirror.NetworkIdentity>(), Is.Empty);
            Assert.That(crew.GetComponentsInChildren<Collider>().All(c => !c.enabled), Is.True);
            Capture(session.LocalPlayer.Cam.Cam, "03-assembly");
            clock = 100 + yard.DemoEndAt;
            yield return null;
            Assert.That(yard.DemoFinished, Is.True);
            Assert.That(session.CinematicInputBlocked, Is.True);
            Assert.That(session.Cleared || yard.State.Launched, Is.False);
            Assert.That(yard.State.Parts.Any(part => part.Secured), Is.False);
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                ScreenCapture.CaptureScreenshot("../artifacts/677-demo/04-demo-complete.png");
                yield return null; yield return null;
            }
            var pause = Object.FindAnyObjectByType<GamePauseMenu>();
            session.ScriptedInput = false;
            Assert.That(pause.TryToggle(), Is.True, "Final card must retain an exit through the existing Esc menu.");
            Assert.That(pause.IsOpen, Is.True);
            Assert.That(pause.TryToggle(), Is.True);
            Assert.That(session.CinematicInputBlocked, Is.True, "Closing a menu must not unlock demo gameplay.");
            view.enabled = false;
            Assert.That(session.LocalPlayer.Cam.enabled, Is.True);
            foreach (var renderer in session.LocalPlayer.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.forceRenderingOff, Is.False);
        }

        static void Capture(Camera camera, string name)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            Directory.CreateDirectory("../artifacts/677-demo");
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            bool async = UnityEditor.ShaderUtil.allowAsyncCompilation;
            try
            {
                UnityEditor.ShaderUtil.allowAsyncCompilation = false;
                target.Create(); camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes("../artifacts/677-demo/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previous;
                UnityEditor.ShaderUtil.allowAsyncCompilation = async;
                Object.Destroy(image); target.Release(); Object.Destroy(target);
            }
        }

        [UnityTest]
        public IEnumerator RejectsPuzzleCommandsAndReplicaStartAndSeeksLateSnapshot()
        {
            yard.Register(1, session.LocalPlayer);
            var target = yard.Targets.First(t => t.Kind == YardTargetKind.Container);
            session.LocalPlayer.Teleport(target.transform.position + Vector3.back, Quaternion.identity);
            Physics.SyncTransforms();
            yard.Submit(1, new YardCommand { Action = YardAction.Interact, Target = target.Id });
            Assert.That(yard.State.Parts[target.Index].Open, Is.False);
            Assert.That(yard.TryStartDemo(), Is.False, "Duplicate start cannot restart the clock.");
            var snapshot = JsonUtility.FromJson<Stage2State>(JsonUtility.ToJson(yard.Capture()));
            snapshot.DemoPlayers = new[] { new DemoEndingPlayer { Name = "Crew at start", Suit = Color.white, Accent = Color.blue, Appearance = 0 } };
            snapshot = JsonUtility.FromJson<Stage2State>(JsonUtility.ToJson(snapshot));
            yard.SetAuthority(false);
            yard.ApplyReplica(snapshot);
            Time.timeScale = 0;
            clock = snapshot.DemoStart + 17;
            yield return null;
            Assert.That(yard.DemoCinematic, Is.True);
            Assert.That(yard.DemoElapsed, Is.EqualTo(17));
            Assert.That(yard.State.DemoPlayers.Single().Name, Is.EqualTo("Crew at start"));
            snapshot.DemoStarted = false;
            yard.ApplyReplica(snapshot);
            Assert.That(yard.TryStartDemo(), Is.False, "A client cannot originate the demo.");
            Assert.That(yard.DemoBlocksInput, Is.True, "Wait for a server start before allowing the initial walk.");
        }

        [UnityTest]
        public IEnumerator FullSnapshotFitsKcpAndReachesReadyConnections()
        {
            yard.State.DemoPlayers = new[]
            {
                new DemoEndingPlayer { Name = "Host", Suit = Color.red, Accent = Color.white },
                new DemoEndingPlayer { Name = "Guest", Suit = Color.blue, Accent = Color.white }
            };
            var previousTransport = Mirror.Transport.active;
            var root = new GameObject("Stage2 snapshot transport regression");
            var transport = root.AddComponent<kcp2k.KcpTransport>();
            transport.port = 0;
            var bridge = root.AddComponent<Stage2Bridge>();
            bridge.enabled = false;
            bool sent = false;
            int channel = -1, bytes = 0;
            void OnSend(Mirror.NetworkDiagnostics.MessageInfo info)
            {
                if (!(info.message is Stage2SnapshotMessage)) return;
                sent = true;
                channel = info.channel;
                bytes = info.bytes;
            }
            try
            {
                Mirror.Transport.active = transport;
                Mirror.NetworkServer.Listen(4);
                Mirror.NetworkServer.connections.Add(1, new Mirror.NetworkConnectionToClient(1)
                    { isAuthenticated = true, isReady = true });
                bridge.Configure(yard);
                Mirror.NetworkDiagnostics.OutMessageEvent += OnSend;
                bridge.SendMessage("Update");
                Assert.That(sent, Is.True, "The complete snapshot must not be dropped before transport send.");
                Assert.That(bytes, Is.GreaterThan(Mirror.NetworkMessages.MaxMessageSize(Mirror.Channels.Unreliable)),
                    "Exercise the real full snapshot that exceeded the old KCP channel limit.");
                Assert.That(channel, Is.EqualTo(Mirror.Channels.Reliable));
                Assert.That(bytes, Is.LessThanOrEqualTo(Mirror.NetworkMessages.MaxMessageSize(channel)));
                Debug.Log($"[Stage2Snapshot] {bytes} bytes; KCP unreliable limit {Mirror.NetworkMessages.MaxMessageSize(Mirror.Channels.Unreliable)}; channel {channel}");
            }
            finally
            {
                Mirror.NetworkDiagnostics.OutMessageEvent -= OnSend;
                Object.DestroyImmediate(bridge);
                Mirror.NetworkServer.connections.Clear();
                Mirror.NetworkServer.Shutdown();
                Object.DestroyImmediate(root);
                Mirror.Transport.active = previousTransport;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator YardOptOutRetainsUnderlyingGameplayAndSessionInput()
        {
            yard.SetDemoMode(false);
            yield return null;
            Assert.That(session.CinematicInputBlocked, Is.False);
            Assert.That(yard.DemoCinematic, Is.False);
            Assert.That(Object.FindAnyObjectByType<Stage2DemoView>().Presenting, Is.False);
            Assert.That(session.LocalPlayer.Cam.enabled, Is.True);
            Assert.That(Object.FindAnyObjectByType<Stage2Hud>().enabled, Is.True);
        }
    }
}
