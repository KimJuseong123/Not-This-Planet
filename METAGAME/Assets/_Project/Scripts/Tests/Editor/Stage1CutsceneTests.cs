using System.Collections;
using System.Reflection;
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

        static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        SpaceBootstrap TabletBootstrap(out Stage1Exploration exploration)
        {
            // Keep the full world bootstrap dormant; exercise the actual cleanup/state methods.
            var go = new GameObject("tablet bootstrap");
            go.transform.SetParent(_root.transform);
            go.SetActive(false);
            var boot = go.AddComponent<SpaceBootstrap>();
            exploration = _root.AddComponent<Stage1Exploration>();
            exploration.Guide.Enable();
            _session.Exploration = exploration;
            SetField(boot, "<Session>k__BackingField", _session);
            SetField(boot, "<Exploration>k__BackingField", exploration);
            return boot;
        }

        Stage1CutsceneScene TabletScene(SpaceBootstrap boot, bool assault, bool savedOpen)
        {
            var view = boot.gameObject.AddComponent<Stage1CutsceneScene>();
            SetField(view, "_bootstrap", boot);
            SetField(view, "_sequence", _sequence);
            SetField(view, "_assault", assault);
            SetField(view, "_savedTablet", savedOpen);
            return view;
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void CompletedEntryKeepsTabletOpenAfterStopAndRepeatedCleanup(bool skip, bool client)
        {
            var boot = TabletBootstrap(out var exploration);
            _sequence.TryBeginEntry();
            var view = TabletScene(boot, false, false);
            if (!skip) exploration.SetTabletOpen(true); // The final authored shot already opened it.
            int closes = 0;
            exploration.TabletStateChanged += open => { if (!open) closes++; };
            if (client)
            {
                _authority = false;
                _sequence.ApplyReplica(Stage1SequencePhase.Exploration, 18, 2, false);
            }
            else if (skip) Assert.That(_sequence.TrySkip(_sequence.Revision), Is.True);
            else { _now = Stage1Sequence.EntrySeconds; _sequence.Tick(); }
            view.Stop();
            view.Stop();
            Assert.That(exploration.TabletOpen, Is.True);
            Assert.That(exploration.Guide.Visible, Is.True);
            Assert.That(closes, Is.Zero, "Cleanup must not briefly lower the tablet.");
            Assert.That(_session.CursorLocked, Is.False);
        }

        [TestCase(false, false)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(true, true)]
        public void AssaultCompletionAndInterruptedEntryRestorePreviousTablet(bool assault, bool savedOpen)
        {
            var boot = TabletBootstrap(out var exploration);
            _sequence.TryBeginEntry();
            if (assault)
            {
                _sequence.TrySkip(_sequence.Revision);
                _sequence.TryBeginAssault();
                _sequence.TrySkip(_sequence.Revision);
            }
            exploration.SetTabletOpen(!savedOpen);
            TabletScene(boot, assault, savedOpen).Stop();
            Assert.That(exploration.TabletOpen, Is.EqualTo(savedOpen));
        }

        [TestCase(Stage1SequencePhase.Exploration)]
        [TestCase(Stage1SequencePhase.Combat)]
        public void MissingCutsceneViewWaitsForLocalBodyThenOpensTabletOnlyOnce(Stage1SequencePhase phase)
        {
            var boot = TabletBootstrap(out var exploration);
            var flow = boot.gameObject.AddComponent<Stage1CutsceneFlow>();
            SetField(flow, "_bootstrap", boot);
            SetField(flow, "<Sequence>k__BackingField", _sequence);
            var update = typeof(Stage1CutsceneFlow).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            update.Invoke(flow, null);
            Assert.That(exploration.TabletOpen, Is.False, "Waiting is not entry completion.");
            _authority = false;
            _sequence.ApplyReplica(phase, 18, 2, phase == Stage1SequencePhase.Combat);
            update.Invoke(flow, null);
            Assert.That(exploration.TabletOpen, Is.False, "Body initialization still resets the tablet.");
            _session.LocalPlayer = boot.gameObject.AddComponent<PlayerRig>();
            update.Invoke(flow, null);
            Assert.That(exploration.TabletOpen, Is.True);
            Assert.That(_session.CanToggleTablet, Is.True);
            exploration.ToggleTablet();
            update.Invoke(flow, null);
            Assert.That(exploration.TabletOpen, Is.False, "A player's manual close must persist.");
        }

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
            // 탐색 구간에는 가랑비가 흐른다(2026-09-18). 예전에는 여기서
            // SequenceHold 가 True 라 **한 발도 안 떨어졌다** — 그러면 운석이 위협이라는
            // 것을 공세 컷신에서 처음 배우게 된다.
            _now = 300; _sequence.Tick();
            Assert.That(_spawner.SequenceHold, Is.False, "탐색 구간은 막지 않는다.");
            for (var i = 0; i < 400; i++) _spawner.Tick(0.1f);
            Assert.That(_spawner.Pending.Count + _spawner.Live.Count, Is.GreaterThan(0),
                "가랑비라도 떨어져야 한다.");

            // <b>그래도 공세는 시작되지 않았다.</b> 이 검증의 원래 의도가 그것이다 —
            // 공세를 켜는 것은 시계가 아니라 명시적 트리거다.
            Assert.That(_spawner.AssaultEnabled, Is.False,
                "The explicit trigger, not a timer, starts attacks.");
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
            Assert.That(oldInterval, Is.EqualTo(PhysicsScale.MeteorDrizzleIntervalSeconds).Within(0.001f),
                "컷신 전은 가랑비 간격이다.");
            Assert.That(_sequence.TryBeginAssault(), Is.True);
            _now = 22.99; _sequence.Tick();
            Assert.That(_spawner.AssaultEnabled, Is.False);
            _now = 23; _sequence.Tick();
            Assert.That(_spawner.SequenceHold, Is.False);
            Assert.That(_spawner.AssaultEnabled, Is.True);

            // <b>배율이 아니라 단계다</b>(2026-09-18). 예전에는 같은 간격에 0.35 를
            // 곱했는데, 지금은 컷신 뒤 값이 MeteorIntensity.FullBlast 로 정해져 있다 —
            // 둘을 겹치면 5초가 1.75초가 되어 §17.3 의 5초 전 신호가 붙을 자리가 없다.
            Assert.That(_spawner.CurrentInterval,
                Is.EqualTo(PhysicsScale.MeteorFullBlastIntervalSeconds).Within(0.001f));
            Assert.That(_spawner.CurrentInterval, Is.LessThan(oldInterval), "컷신 뒤가 더 잦다.");
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

        [Category("Heavy")] [UnityTest] public IEnumerator ActualWorldSurvivesBothCutscenesAndRestoresCamera()
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
            Assert.That(boot.Exploration.TabletOpen, Is.True, "Entry leaves the tablet ready to read.");
            boot.Exploration.ToggleTablet();
            yield return null;
            Assert.That(boot.Exploration.TabletOpen, Is.False, "The player can still put it away.");
            Assert.That(GameObject.Find("Cutscene meteor shower"), Is.Null, "Cutscene effects must not persist into exploration.");
            var trigger = Object.FindAnyObjectByType<Stage1AssaultTrigger>();
            Assert.That(trigger, Is.Not.Null);
            player.Teleport(trigger.transform.position - Vector3.up, Quaternion.identity);
            deadline = Time.realtimeSinceStartup + 14;
            bool sawVolley = false, sawWideVolley = false;
            while (state.Phase != Stage1SequencePhase.Combat && Time.realtimeSinceStartup < deadline)
            {
                var view = Object.FindAnyObjectByType<Stage1CutsceneScene>();
                if (!sawVolley && view != null && view.IsAssault && state.Age >= 2f)
                {
                    var trails = view.GetComponentsInChildren<LineRenderer>();
                    Assert.That(trails.Length, Is.EqualTo(12), "Assault must show a simultaneous meteor volley.");
                    foreach (var trail in trails)
                    {
                        Assert.That(trail.GetComponentsInChildren<Collider>().Length, Is.Zero);
                        Assert.That(trail.GetComponentsInChildren<Rigidbody>().Length, Is.Zero);
                        Assert.That(trail.GetComponentsInChildren<Meteor>().Length, Is.Zero,
                            "Cinematic meteors must not change gameplay damage or authority.");
                    }
                    sawVolley = true;
                }
                if (!sawWideVolley && view != null && view.IsAssault && state.Age >= 5.5f)
                {
                    int visibleMeteors = 0;
                    foreach (var trail in view.GetComponentsInChildren<LineRenderer>())
                    {
                        Vector3 point = player.Cam.Cam.WorldToViewportPoint(trail.transform.position);
                        if (point.z > 0 && point.x > 0 && point.x < 1 && point.y > 0.1f && point.y < 0.9f)
                            visibleMeteors++;
                    }
                    Assert.That(visibleMeteors, Is.GreaterThanOrEqualTo(3),
                        "The final wide shot must frame multiple meteors inside the cinematic bars.");
                    sawWideVolley = true;
                }
                yield return null;
            }
            Assert.That(sawVolley, Is.True);
            Assert.That(sawWideVolley, Is.True);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(state.Phase, Is.EqualTo(Stage1SequencePhase.Combat));
            Assert.That(player.GetEntityId(), Is.EqualTo(playerId));
            Assert.That(part.GetEntityId(), Is.EqualTo(partId));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Stage1CutsceneFlow.StagePath));
            Assert.That(player.Cam.enabled, Is.True);
            Assert.That(boot.Session.CinematicInputBlocked, Is.False);
            Assert.That(GameObject.Find("Cutscene meteor shower"), Is.Null, "Cutscene effects must not persist into combat.");
            Assert.That(GameObject.Find("Assault meteor visual 1"), Is.Null, "Assault models must be removed after the cutscene.");
            Assert.That(boot.GetComponent<Stage1CutsceneFlow>().PlayEntry(), Is.True, "F10 replay route");
            yield return new ExitPlayMode();
        }
    }
}
