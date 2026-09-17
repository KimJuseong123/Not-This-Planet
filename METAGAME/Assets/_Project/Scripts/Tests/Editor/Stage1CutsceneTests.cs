using System.Collections;
using Metagame.Bootstrap;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage1CutsceneTests
    {
        GameObject _root;
        GameSession _session;
        Stage1Sequence _sequence;
        MeteorSpawner _spawner;
        double _now;
        bool _authority;

        [SetUp] public void SetUp()
        {
            _root = new GameObject("sequence test");
            _session = _root.AddComponent<GameSession>();
            _session.Begin(null, null, Vector3.zero);
            _spawner = _root.AddComponent<MeteorSpawner>();
            _sequence = _root.AddComponent<Stage1Sequence>();
            _authority = true; _now = 0;
            _sequence.Configure(_session, _spawner, () => _authority, () => _now);
        }

        [TearDown] public void TearDown() { Object.DestroyImmediate(_root); }

        [Test] public void EntryBlocksInputThenExplorationWaitsForTrigger()
        {
            Assert.That(_session.CinematicInputBlocked, Is.True);
            Assert.That(_sequence.TryBeginEntry(), Is.True);
            Assert.That(_sequence.TryBeginEntry(), Is.False);
            _now = 17.99; _sequence.Tick();
            Assert.That(_sequence.IsCinematic, Is.True);
            _now = 18; _sequence.Tick();
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
            Assert.That(_session.CinematicInputBlocked, Is.False);
            _now = 300; _sequence.Tick(); _spawner.Tick(300);
            Assert.That(_spawner.SequenceHold, Is.True);
            Assert.That(_spawner.Live.Count, Is.Zero, "The explicit trigger, not a timer, starts attacks.");
        }

        [Test] public void UnreadyParticipantsCannotHoldAuthorityPastTenSeconds()
        {
            // ReadyForEntry가 한 번도 성공하지 않는 상태에서 권위 시계만 진행한다.
            _now = 9.99; _sequence.Tick();
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Waiting));
            _now = 10; _sequence.Tick();
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Entry));
            _now = 28; _sequence.Tick();
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
            Assert.That(_session.CinematicInputBlocked, Is.False);
        }

        [Test] public void WaitingClientDoesNotStartItsOwnTimelineOnTimeout()
        {
            _authority = false;
            _now = 100; _sequence.Tick();
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Waiting));
            _sequence.ApplyReplica(Stage1SequencePhase.Entry, 98, 1, false);
            Assert.That(_sequence.Age, Is.EqualTo(2));
        }

        [Test] public void EscapeMenuWorksWhileWaitingAndAcrossTimeout()
        {
            var pause = _root.AddComponent<Metagame.UI.GamePauseMenu>();
            pause.Initialize(_session);
            Assert.That(_session.CinematicInputBlocked, Is.True);
            Assert.That(pause.TryToggle(), Is.True);
            Assert.That(pause.IsOpen, Is.True);
            Assert.That(_session.InputSuppressed, Is.True);
            _now = 10; _sequence.Tick();
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Entry));
            Assert.That(pause.TryToggle(), Is.True, "Esc must still close a menu opened before entry.");
            Assert.That(pause.IsOpen, Is.False);
            Assert.That(_session.InputSuppressed, Is.False);
            Assert.That(_session.CinematicInputBlocked, Is.True, "Closing the menu must not unlock movement during entry.");
        }

        [Test] public void AssaultWarnsThenRaisesFrequencyWithoutResettingElapsed()
        {
            _sequence.TryBeginEntry(); _now = 18; _sequence.Tick();
            _spawner.Tick(12f);
            float elapsed = _spawner.Elapsed, oldInterval = _spawner.CurrentInterval;
            Assert.That(_sequence.TryBeginAssault(), Is.True);
            _now = 22.99; _sequence.Tick();
            Assert.That(_spawner.AssaultEnabled, Is.False);
            _now = 23; _sequence.Tick();
            Assert.That(_spawner.SequenceHold, Is.False);
            Assert.That(_spawner.AssaultEnabled, Is.True);
            Assert.That(_spawner.CurrentInterval, Is.EqualTo(oldInterval * 0.35f).Within(0.001f));
            Assert.That(_spawner.Elapsed, Is.EqualTo(elapsed));
            _now = 26; _sequence.Tick();
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Combat));
            Assert.That(_sequence.TryBeginAssault(), Is.False, "Repeated collider contacts cannot restart the cutscene.");
            Assert.That(_session.CinematicInputBlocked, Is.False);
        }

        [Test] public void ClientCannotInitiateAndLateJoinUsesServerTimeline()
        {
            _authority = false;
            Assert.That(_sequence.TryBeginEntry(), Is.False);
            Assert.That(_sequence.TryBeginAssault(true), Is.False);
            _now = 103;
            _sequence.ApplyReplica(Stage1SequencePhase.Assault, 100, 3, false);
            Assert.That(_sequence.Age, Is.EqualTo(3));
            Assert.That(_session.CinematicInputBlocked, Is.True);
            _sequence.ApplyReplica(Stage1SequencePhase.Combat, 108, 4, true);
            Assert.That(_spawner.AssaultEnabled, Is.True);
            Assert.That(_session.CinematicInputBlocked, Is.False);
            _sequence.ApplyReplica(Stage1SequencePhase.Entry, 0, 1, false);
            Assert.That(_sequence.Phase, Is.EqualTo(Stage1SequencePhase.Combat));
        }

        [TestCase(0.6f, 18f)]
        [TestCase(1f, 30f)]
        [TestCase(0f, 1.5f)]
        public void SerializedAssaultMultiplierControlsInterval(float multiplier, float expected)
        {
            var settings = new SerializedObject(_spawner);
            settings.FindProperty("_assaultWaveIntervalMultiplier").floatValue = multiplier;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(_spawner.IntervalAt(0), Is.EqualTo(30f));
            _spawner.BeginAssault();
            Assert.That(_spawner.IntervalAt(0), Is.EqualTo(expected).Within(0.001f));
        }

        [Test] public void BothAuthoredScenesAreEnabledInBuild()
        {
            foreach (var path in new[] { Stage1CutsceneFlow.EntryPath, Stage1CutsceneFlow.AssaultPath })
            {
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null);
                Assert.That(SceneUtility.GetBuildIndexByScenePath(path), Is.GreaterThanOrEqualTo(0));
            }
        }

        [UnityTest] public IEnumerator ActualWorldSurvivesBothCutscenesAndRestoresCamera()
        {
            Object.DestroyImmediate(_root);
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync(Stage1CutsceneFlow.StagePath);
            var boot = Object.FindAnyObjectByType<SpaceBootstrap>();
            Assert.That(boot, Is.Not.Null);
            var state = boot.Session.Sequence;
            yield return null;
            Assert.That(boot.PauseMenu.enabled, Is.True, "The cinematic presenter must leave Esc active.");
            var player = boot.Session.LocalPlayer;
            var part = Object.FindAnyObjectByType<SpaceshipPart>();
            var partId = part.GetEntityId();
            var playerId = player.GetEntityId();
            bool perspective = player.Cam.FirstPerson;
            float deadline = Time.realtimeSinceStartup + 30;
            while (state.Phase != Stage1SequencePhase.Exploration && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(state.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
            Assert.That(player.Cam.enabled, Is.True);
            Assert.That(player.Cam.FirstPerson, Is.EqualTo(perspective));
            Assert.That(boot.Exploration.TabletOpen, Is.False);
            Assert.That(GameObject.Find("Cutscene meteor shower"), Is.Null, "Cutscene effects must not persist into exploration.");
            var trigger = Object.FindAnyObjectByType<Stage1AssaultTrigger>();
            Assert.That(trigger, Is.Not.Null);
            player.Teleport(trigger.transform.position - Vector3.up, Quaternion.identity);
            deadline = Time.realtimeSinceStartup + 14;
            while (state.Phase != Stage1SequencePhase.Combat && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(state.Phase, Is.EqualTo(Stage1SequencePhase.Combat));
            Assert.That(player.GetEntityId(), Is.EqualTo(playerId));
            Assert.That(part.GetEntityId(), Is.EqualTo(partId));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Stage1CutsceneFlow.StagePath));
            Assert.That(player.Cam.enabled, Is.True);
            Assert.That(boot.Session.CinematicInputBlocked, Is.False);
            Assert.That(GameObject.Find("Cutscene meteor shower"), Is.Null, "Cutscene effects must not persist into combat.");
            Assert.That(boot.GetComponent<Stage1CutsceneFlow>().PlayEntry(), Is.True, "F10 replay route");
            yield return new ExitPlayMode();
        }
    }
}
