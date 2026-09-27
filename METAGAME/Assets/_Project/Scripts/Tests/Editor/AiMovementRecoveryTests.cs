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
    public sealed class AiMovementRecoveryTests
    {
        static GameObject Box(Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = position; go.transform.localScale = size; return go;
        }

        static PlayerRig Rig(string name, Vector3 point) => PlayerRig.Create(name, point, Quaternion.identity, Color.blue, Color.white, false);

        static IEnumerator Bake(AiNavigationWorld world)
        {
            Physics.SyncTransforms(); world.Rebuild(new Bounds(Vector3.zero, Vector3.one * 100));
            float deadline = Time.realtimeSinceStartup + 20;
            while (!world.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.Ready, Is.True);
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator DisconnectedLandingReturnsToPreviouslySafeGround()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box(Vector3.down * .5f, new Vector3(20,1,20));
            Box(new Vector3(30,-.5f,0), new Vector3(10,1,10));
            var speaker = Rig("speaker", new Vector3(0,.1f,0));
            var self = Rig("AI", new Vector3(0,.1f,5));
            var session = new GameObject("session").AddComponent<GameSession>();
            session.Begin(speaker, null, speaker.FootPosition); session.ScriptedInput = true;
            var world = session.gameObject.AddComponent<AiNavigationWorld>(); yield return Bake(world);
            var recovery = new AiMovementRecovery();
            float until = Time.time + 2;
            while (Time.time < until) { recovery.Tick(self, speaker, false, out _); yield return new WaitForFixedUpdate(); }
            // Occupied fallback must never be used, even though it was safe earlier.
            var blocked = Box(new Vector3(0,1,5), new Vector3(2,2,2));
            self.Teleport(new Vector3(30,3,0), Quaternion.identity);
            bool airborne = false;
            until = Time.time + 7;
            while (Time.time < until)
            {
                bool held = recovery.Tick(self, speaker, true, out var frame);
                airborne |= held && recovery.Status.Contains("착지"); self.SetInput(frame);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(recovery.RestoreCount, Is.Zero, "Never restore inside a new obstacle");
            Object.Destroy(blocked); yield return null;
            until = Time.time + 15;
            while (Time.time < until && recovery.RestoreCount == 0)
            {
                bool held = recovery.Tick(self, speaker, true, out var frame);
                airborne |= held && recovery.Status.Contains("착지"); self.SetInput(frame);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(airborne, Is.True);
            Assert.That(recovery.RestoreCount, Is.EqualTo(1), recovery.Status);
            Assert.That(Mathf.Abs(self.FootPosition.x), Is.LessThan(2));
            Assert.That(self.FootPosition.z, Is.EqualTo(5).Within(1));
            recovery.Reset(); Assert.That(recovery.RestoreCount, Is.Zero);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator PinOrderSurvivesAirborneDisplacementAndCancelPreventsResume()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box(Vector3.down * .5f, new Vector3(40,1,40));
            var partGo = Box(Vector3.up, new Vector3(2,2,2));
            var part = partGo.AddComponent<SpaceshipPart>(); part.Initialize();
            var go = new GameObject("pin") { layer = GameLayers.Interactable };
            go.transform.SetParent(part.transform); go.transform.position = new Vector3(0,.35f,2);
            var shape = go.AddComponent<SphereCollider>(); shape.radius = .3f; shape.isTrigger = true;
            var pin = go.AddComponent<PartClamp>(); part.RefreshClamps();
            var speaker = Rig("speaker", new Vector3(4,.1f,6));
            var self = Rig("AI", new Vector3(0,.1f,8));
            var session = new GameObject("session").AddComponent<GameSession>();
            session.Begin(speaker, null, speaker.FootPosition); session.ScriptedInput = true;
            var world = session.gameObject.AddComponent<AiNavigationWorld>(); yield return Bake(world);
            yield return new WaitForSeconds(1);
            var controller = new AiCompanionController();
            Assert.That(controller.Apply("pull_pin", speaker, self), Is.True);
            self.SetInput(controller.Tick(self, true)); yield return new WaitForFixedUpdate();
            self.Teleport(new Vector3(6,5,6), Quaternion.identity);
            bool airborne = false; float until = Time.time + 40;
            while (!pin.IsReleased && Time.time < until)
            {
                self.SetInput(controller.Tick(self, true)); airborne |= controller.Status.Contains("착지");
                yield return new WaitForFixedUpdate();
            }
            Assert.That(airborne, Is.True);
            Assert.That(pin.IsReleased, Is.True, controller.Status + " " + self.FootPosition);
            controller.Apply("follow", speaker, self);
            self.Teleport(new Vector3(0,5,8), Quaternion.identity);
            self.SetInput(controller.Tick(self, true));
            controller.Apply("cancel", speaker, self);
            yield return new WaitForSeconds(3);
            Assert.That(controller.Tick(self, true).Move, Is.EqualTo(Vector2.zero));
            Assert.That(controller.Status, Is.EqualTo("대기"));
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator StalledMovementTriesPhysicalEscapeBeforeRelocation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box(Vector3.down * .5f, new Vector3(30,1,30));
            var speaker = Rig("speaker", new Vector3(0,.1f,0));
            var self = Rig("AI", new Vector3(0,.1f,5));
            var session = new GameObject("session").AddComponent<GameSession>();
            session.Begin(speaker, null, speaker.FootPosition); session.ScriptedInput = true;
            var world = session.gameObject.AddComponent<AiNavigationWorld>(); yield return Bake(world);
            yield return new WaitForSeconds(1);
            var recovery = new AiMovementRecovery();
            float until = Time.time + 7; bool attempted = false;
            var origin = self.FootPosition;
            while (Time.time < until)
            {
                // Requested movement is blocked: feed no locomotion until recovery takes control.
                recovery.Tick(self, speaker, true, out var frame); self.SetInput(frame);
                attempted |= frame.Move.sqrMagnitude > .01f;
                yield return new WaitForFixedUpdate();
                if (attempted && Vector3.Distance(origin, self.FootPosition) > .3f) break;
            }
            Assert.That(attempted, Is.True, recovery.Status);
            Assert.That(Vector3.Distance(origin, self.FootPosition), Is.GreaterThan(.3f));
            Assert.That(recovery.RestoreCount, Is.Zero);
            yield return new ExitPlayMode();
        }

        /// <summary>
        /// 길찾기 바닥 밖에서 옮겨 주지 못해도(도구를 들었다 · 안전 위치를 아직 모른다) 걸어서 돌아온다 (S15P21B202-749).
        /// 2026-09-26 QA: 방망이를 든 동료가 "이탈 ↔ 복귀 대기" 를 오가며 판이 끝날 때까지 서 있었다.
        /// </summary>
        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator OffNavigationWalksBackWhenItCannotBeMoved()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box(Vector3.down * .5f, new Vector3(20,1,20));
            var speaker = Rig("speaker", new Vector3(0,.1f,0));
            var session = new GameObject("session").AddComponent<GameSession>();
            session.Begin(speaker, null, speaker.FootPosition); session.ScriptedInput = true;
            var world = session.gameObject.AddComponent<AiNavigationWorld>(); yield return Bake(world);
            // 구운 뒤에 붙인 바닥 — 설 수 있지만 길찾기 바닥이 아니다.
            Box(new Vector3(16,-.5f,0), new Vector3(12,1,20));
            var self = Rig("AI", new Vector3(15,.1f,0));
            yield return new WaitForSeconds(1);
            Assert.That(NavMesh.SamplePosition(self.FootPosition, out _, AiMovementRecovery.OffNavigationTolerance, NavMesh.AllAreas),
                Is.False, "시작 자리가 바닥 밖이 아니다");
            // 처음 본 동료라 돌아갈 안전 위치가 없다 — 옮겨 주지 못한다.
            var recovery = new AiMovementRecovery();
            float until = Time.time + 12; bool walked = false; string status = null;
            while (Time.time < until)
            {
                bool held = recovery.Tick(self, speaker, true, out var frame); self.SetInput(frame);
                if (held) status = recovery.Status;
                walked |= frame.Move.sqrMagnitude > .01f;
                yield return new WaitForFixedUpdate();
                if (NavMesh.SamplePosition(self.FootPosition, out _, AiMovementRecovery.OffNavigationTolerance, NavMesh.AllAreas)) break;
            }
            Assert.That(walked, Is.True, "선 채로 멈췄다 · " + status);
            Assert.That(NavMesh.SamplePosition(self.FootPosition, out _, AiMovementRecovery.OffNavigationTolerance, NavMesh.AllAreas),
                Is.True, "바닥으로 돌아오지 못했다 · " + status + " · " + self.FootPosition);
            Assert.That(recovery.RestoreCount, Is.Zero, "걸어 돌아와야 하는데 옮겼다");
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
