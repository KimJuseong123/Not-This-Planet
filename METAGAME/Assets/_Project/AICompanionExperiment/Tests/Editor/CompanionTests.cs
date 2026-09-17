using System;
using System.Collections;
using System.Linq;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Metagame.Core;
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
        [TestCase("move_to_pin", CompanionOrder.MoveToPin)]
        [TestCase("pull_nearby_pins", CompanionOrder.PullNearbyPins)]
        [TestCase("pickup_tool", CompanionOrder.PickUpTool)]
        [TestCase("pickup_bat", CompanionOrder.PickUpBat)]
        [TestCase("pickup_welder", CompanionOrder.PickUpWelder)]
        [TestCase("drop_tool", CompanionOrder.DropTool)]
        [TestCase("hit_target", CompanionOrder.HitTarget)]
        [TestCase("defend_meteors", CompanionOrder.DefendMeteors)]
        public void AcceptsOnlyTheTwelveOrders(string action, CompanionOrder expected)
        {
            Assert.IsTrue(CompanionPolicy.TryParse(action, out var order));
            Assert.AreEqual(expected, order);
        }

        [TestCase("weld")][TestCase("carry_part")]
        [TestCase("clarify")][TestCase("none")][TestCase(null)][TestCase("follow;weld")]
        public void RejectsUnsupportedAndMalformedOrders(string action) => Assert.IsFalse(CompanionPolicy.TryParse(action, out _));

        [TestCase(404, "버전 불일치")]
        [TestCase(409, "다른 요청")]
        [TestCase(0, "연결 실패")]
        [TestCase(503, "모델 처리 실패")]
        [TestCase(500, "HTTP 500")]
        public void ConnectionFailuresHaveDistinctRecoveryHints(long code, string hint)
            => StringAssert.Contains(hint, CompanionPolicy.ServiceError(code));

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

        [Test]
        public void DiagnosticsShowSpeechDecisionTimingAndRejectionWithoutACompanion()
        {
            var go = new GameObject("Diagnostic test");
            try
            {
                var trial = go.AddComponent<AICompanionTrial>();
                Assert.IsFalse(trial.ReceiveResponseJson("{\"text\":\"용접해줘\",\"action\":\"none\",\"model\":\"qwen3:1.7b\",\"prompt_version\":\"companion-v2\",\"llm_wall_ms\":450,\"asr\":{\"asr_ms\":700,\"asr_model\":\"whisper-base\",\"audio_seconds\":2.5}}"));
                StringAssert.Contains("[STT 인식] 용접해줘", trial.DiagnosticText);
                StringAssert.Contains("[LLM 선택] none", trial.DiagnosticText);
                StringAssert.Contains("STT 700ms / LLM 450ms", trial.DiagnosticText);
                StringAssert.Contains("[실행 거절]", trial.DiagnosticText);
                Assert.IsFalse(trial.ReceiveResponseJson("not json"));
                StringAssert.Contains("[응답 오류]", trial.DiagnosticText);
                StringAssert.Contains("기존 행동 유지", trial.DiagnosticText);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TextResponseNeverPretendsSpeechWasRecognized()
        {
            var go = new GameObject("Text-only diagnostics");
            try
            {
                var trial = go.AddComponent<AICompanionTrial>();
                trial.ReceiveResponseJson("{\"text\":\"못 빼줘\",\"action\":\"pull_pin\",\"model\":\"qwen3:1.7b\",\"llm_wall_ms\":600}");
                StringAssert.Contains("STT 생략", trial.DiagnosticText);
                StringAssert.Contains("[입력 문장] 못 빼줘", trial.DiagnosticText);
                StringAssert.DoesNotContain("[STT 인식]", trial.DiagnosticText);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void DiagnosticHistoryIsBoundedAndRetainsLatestRequest()
        {
            var go = new GameObject("Diagnostic capacity test");
            try
            {
                var trial = go.AddComponent<AICompanionTrial>();
                for (int i = 0; i < 65; i++) trial.ReceiveIntent("테스트 " + i, "clarify");
                Assert.AreEqual(120, trial.DiagnosticCount);
                StringAssert.Contains("테스트 64", trial.DiagnosticText);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [UnityTest]
        public IEnumerator RepeatedMisunderstandingsSuggestSpeakingClearlyAndAcceptedOrdersResetIt()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            const string hint = "또렷하고 천천히";
            Assert.IsFalse(trial.ReceiveIntent("그거 좀 해줘", "clarify"));
            StringAssert.DoesNotContain(hint, trial.DiagnosticText, "A single miss must not nag.");
            Assert.IsFalse(trial.ReceiveIntent("", "none"));
            StringAssert.Contains(hint, trial.DiagnosticText, "Repeated misses should suggest speaking clearly.");
            Assert.IsTrue(trial.ReceiveIntent("따라와", "follow"));
            // 미지원 요청은 또렷하게 말해도 달라지지 않으므로 연속 실패로 세지 않는다.
            Assert.IsFalse(trial.ReceiveIntent("용접해줘", "none"));
            Assert.IsFalse(trial.ReceiveIntent("저기 그거", "clarify"));
            var afterReset = trial.DiagnosticText;
            Assert.AreEqual(afterReset.LastIndexOf(hint, System.StringComparison.Ordinal),
                            afterReset.IndexOf(hint, System.StringComparison.Ordinal),
                            "An accepted order resets the streak, so the hint must not fire again yet.");
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator UnrecognizedCommandsShowQuestionWithoutStoppingAndAcceptedOrdersClearIt()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            var bot = trial.Companion;
            Assert.IsTrue(trial.ReceiveIntent("따라와", "follow"));
            Assert.AreEqual(0, bot.EmoteId);
            Assert.IsFalse(trial.ReceiveResponseJson("{\"text\":\"용접해줘\",\"action\":\"none\"}"));
            Assert.AreEqual(1, bot.EmoteId);
            Assert.AreEqual(CompanionOrder.Follow, bot.Order);
            Assert.IsTrue(EmoteState.IsActive(bot.EmoteId, bot.EmoteExpiresAt, bot.EmoteNow));
            StringAssert.Contains("[이모티콘] ?", trial.DiagnosticText);
            Assert.IsFalse(bot.ShowQuestionEmote(), "Repeated requests are cooldown-limited.");
            for (float end = Time.unscaledTime + 3.7f; Time.unscaledTime < end;) yield return null;
            Assert.IsFalse(EmoteState.IsActive(bot.EmoteId, bot.EmoteExpiresAt, bot.EmoteNow));
            Assert.IsFalse(trial.ReceiveIntent("", "none"));
            Assert.IsTrue(EmoteState.IsActive(bot.EmoteId, bot.EmoteExpiresAt, bot.EmoteNow));
            Assert.AreEqual(CompanionOrder.Follow, bot.Order);
            Assert.IsTrue(trial.ReceiveIntent("멈춰", "cancel"));
            Assert.AreEqual(0, bot.EmoteId);
            Assert.AreEqual(CompanionOrder.Cancel, bot.Order);
            Assert.IsFalse(trial.ReceiveResponseJson("broken json"));
            Assert.AreEqual(0, bot.EmoteId, "Malformed server responses are not misunderstood speech.");
            Assert.IsFalse(trial.ReceiveResponseJson("{}"));
            Assert.IsFalse(trial.ReceiveResponseJson("{\"text\":\"못 빼줘\"}"));
            Assert.AreEqual(0, bot.EmoteId, "Missing fields are protocol errors, not misunderstood speech.");
            Assert.AreEqual(CompanionOrder.Cancel, bot.Order);
            for (float end = Time.unscaledTime + 1; Time.unscaledTime < end;) yield return null;
            Assert.IsFalse(trial.ReceiveIntent("그거 좀 해줘", "clarify"));
            Assert.AreEqual(1, bot.EmoteId);
            trial.StopImmediately();
            Assert.AreEqual(0, bot.EmoteId);
            yield return new ExitPlayMode();
        }

        [Test]
        public void NonAuthorityCannotShowAnAIEmote()
        {
            var go = new GameObject("Unspawned observer");
            try
            {
                go.AddComponent<Mirror.NetworkIdentity>();
                Assert.IsFalse(go.AddComponent<NetworkCompanion>().ShowQuestionEmote());
            }
            finally { Object.DestroyImmediate(go); }
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
            StringAssert.Contains("[실행 수락]", trial.DiagnosticText);
            Assert.IsFalse(trial.ReceiveIntent("", "none"));
            Assert.AreEqual(CompanionOrder.Follow, companion.Order, "ASR failure must not impersonate a stop command.");
            StringAssert.Contains("인식 실패", trial.Receipt);
            Assert.IsFalse(trial.ReceiveIntent("용접해줘", "none"));
            Assert.AreEqual(CompanionOrder.Follow, companion.Order);
            Assert.IsTrue(trial.ReceiveIntent("멈춰", "cancel"));
            Assert.AreEqual(CompanionOrder.Cancel, companion.Order);
            StringAssert.Contains("명령 수락", trial.Receipt);
            StringAssert.Contains("[LLM 선택] cancel", trial.DiagnosticText);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator FourPinsSelectNearestWithoutLookingAndRejectOutOfRange()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var session = Object.FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            var pins = Object.FindObjectsByType<PartClamp>().OrderBy(p => p.name).ToArray();
            Assert.AreEqual(4, pins.Length);
            Assert.AreEqual(4, pins.Select(p => p.Part).Distinct().Count(), "Pins must belong to independent cargo, not the original clone source.");
            for (int i = 0; i < pins.Length; ++i)
            {
                var pin = pins[i];
                Assert.AreEqual($"PIN {i + 1:00}", pin.name);
                Assert.Greater(pin.GetComponentsInChildren<Collider>().Length, 0);
                Assert.Greater(pin.GetComponentsInChildren<Renderer>().Length, 0);
                yield return FocusPin(session.LocalPlayer, pin);
                Assert.AreSame(pin, CompanionPinTarget.Resolve(session.LocalPlayer.Hips.transform, out var reason), reason);
                Assert.IsTrue(trial.ReceiveIntent("이거 못 빼줘", "pull_pin"), trial.DiagnosticText);
                Assert.AreSame(pin, trial.Companion.Motor.TargetPin);
                trial.StopImmediately();
            }
            session.LocalPlayer.Teleport(new Vector3(-5, 0.2f, -1), Quaternion.Euler(0, 180, 0));
            session.LocalPlayer.Cam.Yaw = 180;
            session.LocalPlayer.Cam.Pitch = 0;
            for (float end = Time.time + 0.3f; Time.time < end;) yield return null;
            session.LocalPlayer.RefreshInteractionFocus();
            var nearest = CompanionPinTarget.Nearby(session.LocalPlayer.Hips.transform)[0];
            Assert.AreSame(nearest, CompanionPinTarget.Resolve(session.LocalPlayer.Hips.transform, out _));
            Assert.IsTrue(trial.ReceiveIntent("따라와", "follow"));
            Assert.IsTrue(trial.ReceiveIntent("못 빼줘", "pull_pin"));
            Assert.AreSame(nearest, trial.Companion.Motor.TargetPin);
            trial.StopImmediately();
            session.LocalPlayer.Teleport(new Vector3(60, 0.2f, 60), Quaternion.identity);
            Assert.IsNull(CompanionPinTarget.Resolve(session.LocalPlayer.Hips.transform, out _));
            Assert.IsTrue(trial.ReceiveIntent("따라와", "follow"));
            Assert.IsFalse(trial.ReceiveIntent("못 빼줘", "pull_pin"));
            Assert.AreEqual(CompanionOrder.Follow, trial.Companion.Order);
            Assert.IsTrue(pins.All(p => !p.IsReleased && p.Progress == 0));
            yield return new ExitPlayMode();
        }

        static IEnumerator FocusPin(PlayerRig player, PartClamp pin)
        {
            Assert.NotNull(player, "Local player must be ready before aiming.");
            Assert.NotNull(pin, "Requested pin must exist.");
            Assert.NotNull(player.Cam, "Local camera must be ready before aiming.");
            Vector3 center = CompanionPinTarget.Center(pin);
            player.Teleport(new Vector3(center.x, 0.2f, center.z - 0.8f), Quaternion.identity);
            for (float end = Time.time + 0.3f; Time.time < end;)
            {
                var angles = Quaternion.LookRotation(center - player.AimOrigin).eulerAngles;
                player.Cam.Yaw = angles.y;
                player.Cam.Pitch = Mathf.DeltaAngle(0, angles.x);
                yield return null;
            }
            player.RefreshInteractionFocus();
            Assert.AreSame(pin, player.CurrentInteractionTarget.Clamp, "Actual crosshair must point at " + pin.name);
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
            Assert.NotNull(companion, "Companion must be spawned.");
            Assert.NotNull(session.LocalPlayer, "Local player must be spawned.");
            var allPins = Object.FindObjectsByType<PartClamp>();
            Assert.AreEqual(4, allPins.Length);
            var pin = allPins.FirstOrDefault(p => p != null && p.name == "PIN 03");
            Assert.NotNull(pin, "PIN 03 must exist in the authored scene.");
            yield return FocusPin(session.LocalPlayer, pin);
            Assert.IsTrue(trial.ReceiveIntent("못 빼줘", "pull_pin"), trial.Receipt);
            StringAssert.Contains("[선택한 못]", trial.DiagnosticText);
            session.LocalPlayer.Teleport(new Vector3(-1, 0.2f, 2), Quaternion.identity);
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
            yield return FocusPin(session.LocalPlayer, pin);
            Assert.IsTrue(trial.ReceiveIntent("다시 못 빼줘", "pull_pin"));
            session.LocalPlayer.Teleport(new Vector3(-1, 0.2f, 2), Quaternion.identity);
            deadline = Time.time + 20;
            while (Time.time < deadline && !pin.IsReleased && companion.Order == CompanionOrder.PullPin) yield return null;
            Debug.Log($"[AI_PIN_TEST] finish={companion.Status} released={pin.IsReleased} progress={pin.Progress}");
            Assert.IsTrue(pin.IsReleased, companion.Status);
            // Do not capture play-mode objects in a lambda across EnterPlayMode's domain reload.
            foreach (var otherPin in Object.FindObjectsByType<PartClamp>())
                if (otherPin != pin)
                    Assert.IsTrue(!otherPin.IsReleased && otherPin.Progress == 0, "Other pins must remain untouched.");
            Assert.AreEqual(PartState.Free, pin.Part.State);
            for (float end = Time.time + 0.2f; Time.time < end;) yield return null;
            StringAssert.Contains("완료", companion.Status);
            for (float end = Time.time + 0.3f; Time.time < end;) yield return null;
            StringAssert.Contains("작업 완료", trial.DiagnosticText);
            Assert.IsTrue(trial.ReceiveIntent("따라와", "follow"));
            Assert.IsTrue(trial.ReceiveIntent("못 빼줘", "pull_pin"));
            Assert.AreNotSame(pin, companion.Motor.TargetPin, "Released pins must be excluded.");
            trial.StopImmediately();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MoveOnlyDoesNotPullAndBatchCanCancelThenFinishAllFour()
        {
            EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            yield return new EnterPlayMode();
            for (float end = Time.time + 2; Time.time < end;) yield return null;
            var session = Object.FindAnyObjectByType<GameSession>();
            session.ScriptedInput = true;
            session.LocalPlayer.Teleport(new Vector3(-5, .2f, 1), Quaternion.Euler(0, 180, 0));
            var trial = Object.FindAnyObjectByType<AICompanionTrial>();
            var motor = trial.Companion.Motor;
            Assert.IsTrue(trial.ReceiveIntent("못 앞으로 가서 기다려", "move_to_pin"));
            float deadline = Time.time + 30;
            while (Time.time < deadline && motor.Order == CompanionOrder.MoveToPin) yield return null;
            StringAssert.Contains("도착", motor.Status);
            foreach (var pin in PartClamp.All) Assert.AreEqual(0, pin.Progress);
            Assert.IsFalse(trial.Companion.Rig.ClampGrabbed);
            Assert.IsTrue(trial.ReceiveIntent("못들 다 뽑아줘", "pull_nearby_pins"));
            var first = motor.TargetPin;
            Assert.AreEqual(3, motor.PendingPins);
            deadline = Time.time + 30;
            while (Time.time < deadline && first.Progress < .12f) yield return null;
            Assert.GreaterOrEqual(first.Progress, .12f, motor.Status);
            trial.StopImmediately();
            Assert.AreEqual(0, motor.PendingPins);
            for (float end = Time.time + .3f; Time.time < end;) yield return null;
            float progress = first.Progress;
            for (float end = Time.time + .5f; Time.time < end;) yield return null;
            Assert.AreEqual(progress, first.Progress);
            Assert.IsFalse(trial.Companion.Rig.ClampGrabbed);
            Assert.IsTrue(trial.ReceiveIntent("남은 못 전부 뽑아줘", "pull_nearby_pins"));
            // The accepted batch remains fixed even if the speaker moves away.
            session.LocalPlayer.Teleport(new Vector3(0, .2f, -2), Quaternion.identity);
            deadline = Time.time + 130;
            while (Time.time < deadline && motor.Order == CompanionOrder.PullNearbyPins) yield return null;
            foreach (var pin in PartClamp.All) Assert.IsTrue(pin.IsReleased, pin.name + " · " + motor.Status);
            StringAssert.Contains("작업 완료", motor.Status);
            Assert.AreEqual(0, motor.PendingPins);
            yield return new ExitPlayMode();
        }
    }
}
