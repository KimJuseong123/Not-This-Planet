using System;
using System.Collections;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Metagame.AIExperiment.Tests
{
    public sealed class CompanionTests
    {
        [TestCase("follow", CompanionOrder.Follow)]
        [TestCase("wait", CompanionOrder.Wait)]
        [TestCase("cancel", CompanionOrder.Cancel)]
        [TestCase("pull_pin", CompanionOrder.PullPin)]
        public void AcceptsOnlyTheFourOrders(string action, CompanionOrder expected)
        {
            Assert.IsTrue(CompanionPolicy.TryParse(action, out var order));
            Assert.AreEqual(expected, order);
        }

        [TestCase("weld")][TestCase("carry_part")]
        [TestCase("clarify")][TestCase("none")][TestCase(null)][TestCase("follow;weld")]
        public void RejectsUnsupportedAndMalformedOrders(string action) => Assert.IsFalse(CompanionPolicy.TryParse(action, out _));

        [Test]
        public void WaveHeaderAndClippingAreCorrect()
        {
            var bytes = CompanionPolicy.EncodeWave(new float[] { -2, 0, 2 }, 1, 16000);
            Assert.AreEqual(50, bytes.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.AreEqual(16000, BitConverter.ToInt32(bytes, 24));
            Assert.AreEqual(6, BitConverter.ToInt32(bytes, 40));
            Assert.AreEqual(-32767, BitConverter.ToInt16(bytes, 44));
            Assert.AreEqual(32767, BitConverter.ToInt16(bytes, 48));
        }

        [UnityTest]
        public IEnumerator RealLobbyCompanionFollowsStopsAndRejectsToolOrders()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var session = Object.FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            var companion = Object.FindAnyObjectByType<NetworkCompanion>();
            Assert.NotNull(companion);
            Assert.NotNull(companion.Motor);
            Assert.AreEqual(1, Object.FindObjectsByType<NetworkCompanion>(FindObjectsSortMode.None).Length);
            session.LocalPlayer.Teleport(new Vector3(-2f, 0.2f, -2.5f), Quaternion.identity);
            Vector3 target = session.LocalPlayer.Hips.position;
            float before = Vector3.Distance(companion.Rig.Hips.position, target);
            Assert.IsTrue(companion.Apply("follow", session.LocalPlayer.Hips.transform));
            for (float end = Time.time + 6; Time.time < end;) yield return null;
            float after = Vector3.Distance(companion.Rig.Hips.position, session.LocalPlayer.Hips.position);
            Debug.Log($"[AIExperimentTest] follow distance {before:F2} -> {after:F2}; {companion.Status}");
            Assert.That(after, Is.LessThan(before - 0.7f));
            Assert.IsTrue(companion.Apply("wait", null));
            for (float end = Time.time + 0.5f; Time.time < end;) yield return null;
            Vector3 stopped = companion.Rig.Hips.position;
            for (float end = Time.time + 1; Time.time < end;) yield return null;
            Assert.That(Vector3.Distance(stopped, companion.Rig.Hips.position), Is.LessThan(0.2f));
            Assert.IsTrue(companion.Apply("cancel", null));
            Assert.IsFalse(companion.Apply("weld", session.LocalPlayer.Hips.transform));
            Assert.AreEqual(CompanionOrder.Cancel, companion.Order);
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            Assert.IsTrue(trial.ReceiveIntent("따라와", "follow"));
            Assert.IsFalse(trial.ReceiveIntent("", "none"));
            Assert.AreEqual(CompanionOrder.Follow, companion.Order, "ASR failure must not impersonate a stop command.");
            StringAssert.Contains("인식 실패", trial.Receipt);
            Assert.IsFalse(trial.ReceiveIntent("용접해줘", "none"));
            Assert.AreEqual(CompanionOrder.Follow, companion.Order);
            Assert.IsTrue(trial.ReceiveIntent("멈춰", "cancel"));
            Assert.AreEqual(CompanionOrder.Cancel, companion.Order);
            StringAssert.Contains("명령 수락", trial.Receipt);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator RealPinGripCancelAndResumeReleasesTheAuthoredPin()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var session = Object.FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            var companion = trial.Companion;
            var pin = Object.FindAnyObjectByType<PartClamp>();
            Assert.IsTrue(trial.ReceiveIntent("못 빼줘", "pull_pin"), trial.Receipt);
            float deadline = Time.time + 25;
            while (Time.time < deadline && pin.Progress < 0.12f && companion.Order == CompanionOrder.PullPin)
                yield return null;
            Debug.Log($"[AI_PIN_TEST] first phase={companion.Status}, position={companion.Rig.Hips.position}, target={CompanionPinTarget.Center(pin)}, progress={pin.Progress}, grip={companion.Rig.ClampGrabbed}");
            Assert.IsTrue(companion.Rig.ClampGrabbed, "The real PlayerRig hand must grip the pin before progress.");
            Assert.That(pin.Progress, Is.GreaterThanOrEqualTo(0.12f), companion.Status);
            Assert.That(pin.Pullers, Is.EqualTo(1));
            Assert.IsTrue(trial.ReceiveIntent("멈춰", "cancel"));
            for (float end = Time.time + 0.2f; Time.time < end;) yield return null;
            float preserved = pin.Progress;
            for (float end = Time.time + 1; Time.time < end;) yield return null;
            Assert.IsFalse(companion.Rig.ClampGrabbed);
            Assert.AreEqual(0, pin.Pullers);
            Assert.That(pin.Progress, Is.EqualTo(preserved).Within(0.002f));
            Assert.IsTrue(trial.ReceiveIntent("다시 못 빼줘", "pull_pin"));
            deadline = Time.time + 20;
            while (Time.time < deadline && !pin.IsReleased && companion.Order == CompanionOrder.PullPin) yield return null;
            Debug.Log($"[AI_PIN_TEST] finish={companion.Status} released={pin.IsReleased} progress={pin.Progress}");
            Assert.IsTrue(pin.IsReleased, companion.Status);
            Assert.AreEqual(PartState.Free, pin.Part.State);
            for (float end = Time.time + 0.2f; Time.time < end;) yield return null;
            StringAssert.Contains("완료", companion.Status);
            Assert.IsTrue(trial.ReceiveIntent("따라와", "follow"));
            Assert.IsFalse(trial.ReceiveIntent("못 빼줘", "pull_pin"));
            Assert.AreEqual(CompanionOrder.Follow, companion.Order, "No eligible pin must not cancel previous action.");
            yield return new ExitPlayMode();
        }
    }
}
