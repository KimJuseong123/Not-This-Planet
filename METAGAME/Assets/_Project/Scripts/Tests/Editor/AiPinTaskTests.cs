using System.Collections;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiPinTaskTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static PartClamp Pin(SpaceshipPart part, Vector3 position)
        {
            var go = new GameObject("Test pin") { layer = GameLayers.Interactable };
            go.transform.SetParent(part.transform); go.transform.position = position;
            var collider = go.AddComponent<SphereCollider>(); collider.radius = .3f; collider.isTrigger = true;
            var pin = go.AddComponent<PartClamp>();
            part.RefreshClamps(); return pin;
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator RealGrabPullCanCancelResumeAndReleaseOnlyOnePin()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(30,1,30);
            var partGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            partGo.transform.position = Vector3.up; partGo.transform.localScale = new Vector3(2,2,2);
            var part = partGo.AddComponent<SpaceshipPart>(); part.Initialize();
            var near = Pin(part, new Vector3(0,.35f,2));
            var far = Pin(part, new Vector3(0,.35f,-2));
            var requester = PlayerRig.Create("speaker", new Vector3(4,.1f,6), Quaternion.identity, Color.red, Color.white, false);
            var self = PlayerRig.Create("AI", new Vector3(0,.1f,6), Quaternion.identity, Color.blue, Color.white, false);
            var session = new GameObject("session").AddComponent<GameSession>();
            session.Begin(requester, null, requester.FootPosition); session.ScriptedInput = true;
            var world = session.gameObject.AddComponent<AiNavigationWorld>();
            Physics.SyncTransforms(); world.Rebuild(new Bounds(Vector3.zero, Vector3.one * 80));
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!world.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.Ready, Is.True);
            var task = new AiPinTask(); task.Begin(self, requester);
            self.SetInput(task.Tick(true));
            Assert.That(task.Target, Is.SameAs(near), "Select relative to speaker, not whichever pin AI sees");
            var other = PlayerRig.Create("other AI", new Vector3(5,.1f,0), Quaternion.identity, Color.green, Color.white, false);
            var otherTask = new AiPinTask(); otherTask.Begin(other, requester); otherTask.Tick(true);
            Assert.That(otherTask.Target, Is.SameAs(far), "Reserved nearest pin must not be selected twice");
            otherTask.Cancel(); Object.Destroy(other.gameObject);
            deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline && near.Progress < .2f && task.Active)
            {
                self.SetInput(task.Tick(true));
                yield return new WaitForFixedUpdate();
            }
            Assert.That(near.Progress, Is.GreaterThanOrEqualTo(.2f), task.Status + " " + self.FootPosition);
            Assert.That(self.ClampGrabTarget, Is.SameAs(near));
            float partial = near.Progress;
            task.Cancel();
            Assert.That(self.ClampGrabbed, Is.False);
            Assert.That(near.Pullers, Is.EqualTo(0));
            yield return new WaitForSeconds(.3f);
            Assert.That(near.Progress, Is.EqualTo(partial).Within(.001f));
            task.Begin(self, requester);
            deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline && !near.IsReleased && task.Active)
            {
                self.SetInput(task.Tick(true));
                yield return new WaitForFixedUpdate();
            }
            Assert.That(near.IsReleased, Is.True, task.Status + " " + self.FootPosition);
            self.SetInput(task.Tick(true));
            Assert.That(task.Active, Is.False);
            Assert.That(task.Succeeded, Is.True);
            typeof(AiPinTask).GetProperty(nameof(AiPinTask.Status)).GetSetMethod(true)
                .Invoke(task, new object[] { "Pin released" });
            Assert.That(task.Succeeded, Is.True, "Localized presentation does not change success");
            Assert.That(far.IsReleased, Is.False);
            Assert.That(far.Progress, Is.Zero);
            Assert.That(self.CurrentInput.Move, Is.EqualTo(Vector2.zero));
            task.Cancel();
            Assert.That(task.Succeeded, Is.False, "Cancellation clears previous success");
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator ActualStagePinCanBeReachedAndPulledUsingPlayerInputs()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return null;
            var session = GameSession.Instance;
            Assert.That(session, Is.Not.Null);
            session.ScriptedInput = true;
            var requester = session.LocalPlayer;
            Assert.That(requester, Is.Not.Null);
            var world = session.gameObject.AddComponent<AiNavigationWorld>();
            Physics.SyncTransforms(); world.Rebuild(new Bounds(Vector3.zero, new Vector3(512,128,512)));
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!world.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.Ready, Is.True);
            Vector3 spawn = default; bool found = false;
            for (int i = 0; i < 12; i++)
            {
                var candidate = requester.FootPosition + Quaternion.Euler(0,i*30,0) * Vector3.forward * 2f;
                if (!NavMesh.SamplePosition(candidate, out var hit, .6f, NavMesh.AllAreas)) continue;
                if (Physics.CheckCapsule(hit.position + Vector3.up * .5f, hit.position + Vector3.up * 1.3f,
                    .4f, GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                spawn = hit.position + Vector3.up * .1f; found = true; break;
            }
            Assert.That(found, Is.True, "No valid initial AI spawn near actual player");
            var self = PlayerRig.Create("AI actual stage", spawn, Quaternion.identity, Color.blue, Color.white, false);
            var task = new AiPinTask(); task.Begin(self, requester);
            PartClamp chosen = null;
            deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline && task.Active)
            {
                self.SetInput(task.Tick(true));
                if (task.Target != null) chosen = task.Target;
                yield return new WaitForFixedUpdate();
            }
            var status = task.Status;
            task.Cancel();
            Assert.That(chosen, Is.Not.Null, status);
            Assert.That(chosen.IsReleased, Is.True, status + " AI=" + self.FootPosition + " pin=" + chosen.transform.position);
            yield return new ExitPlayMode();
        }

        [Test]
        public void CompletionWordInCancellationIsNotSuccess()
        {
            var task = new AiPinTask();
            task.Cancel("완료되지 않음");
            Assert.That(task.Succeeded, Is.False);
            task.Begin(null, null);
            task.Tick(true);
            Assert.That(task.Active, Is.False);
            Assert.That(task.Succeeded, Is.False);
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator QueuedPlanUsesTaskOutcomeNotDisplayText()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var speaker = PlayerRig.Create("speaker", Vector3.zero, Quaternion.identity, Color.red, Color.white, false);
            var self = PlayerRig.Create("AI", Vector3.right * 3, Quaternion.identity, Color.blue, Color.white, false);
            foreach (bool success in new[] { false, true })
            {
                var controller = new AiCompanionController();
                Assert.That(controller.Apply("pull_pin", speaker, self), Is.True);
                controller.QueueAfterCurrent(new[] {
                    new AiCommandStep { action = "pull_pin" },
                    new AiCommandStep { action = "follow" }
                }, speaker);
                var task = (AiPinTask)typeof(AiCompanionController).GetField("pin",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                task.Cancel(success ? "Pin released" : "완료되지 않음");
                // Inject the executor boundary outcome; physical completion is covered above.
                typeof(AiPinTask).GetProperty(nameof(AiPinTask.Succeeded)).GetSetMethod(true)
                    .Invoke(task, new object[] { success });
                controller.Tick(self, true);
                controller.Tick(self, true);
                Assert.That(controller.Busy, Is.EqualTo(success), "Only success may start the queued follow");
                controller.Stop();
                Assert.That(controller.Busy, Is.False);
            }
            yield return new ExitPlayMode();
        }

        [Test]
        public void NoSpeakerDoesNotStartPinActionAndUnknownKeepsState()
        {
            var controller = new AiCompanionController();
            Assert.That(controller.Apply("pull_pin", null, null), Is.False);
            Assert.That(controller.Apply("unsupported", null, null), Is.False);
            Assert.That(controller.Apply("cancel", null, null), Is.True);
            Assert.That(controller.Status, Is.EqualTo("대기"));
        }
    }
}
