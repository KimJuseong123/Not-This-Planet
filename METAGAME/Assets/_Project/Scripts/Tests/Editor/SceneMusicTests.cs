using System.Collections;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.Gameplay.Audio;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class SceneMusicTests
    {
        [TestCase(MusicCue.Assault, "Stage 1 Offensive Stage Song")]
        [TestCase(MusicCue.Escape, "Stage 1 Escape BGM")]
        [TestCase(MusicCue.CrashLanding, "Stage 2 Crash Landing BGM")]
        [TestCase(MusicCue.Stage2, "Stage 2 Gameplay BGM")]
        [TestCase(MusicCue.Practice, "practice operation tutorial room")]
        public void BuildResourceReferencesTheExpectedStreamingClip(MusicCue cue, string name)
        {
            var manager = Resources.Load<AudioManager>("Audio/Stage1AudioManager");
            Assert.That(manager, Is.Not.Null);
            var clip = manager.GetMusicClip(cue);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.name, Is.EqualTo(name));
            Assert.That(clip.length, Is.GreaterThan(20f));
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
            Assert.That(importer.forceToMono, Is.False);
        }

        [TestCase("Lobby", false, MusicCue.Default)]
        [TestCase("Main", false, MusicCue.Default)]
        [TestCase("Stage1_Astra", false, MusicCue.Default)]
        [TestCase("Stage1_Astra", true, MusicCue.Practice)]
        [TestCase("PracticeLobby", false, MusicCue.Practice)]
        [TestCase("AIPracticeLobby", false, MusicCue.Practice)]
        [TestCase("AICompanionTest", false, MusicCue.Practice)]
        [TestCase("CheatArena", false, MusicCue.Practice)]
        [TestCase("Stage2_Earth", false, MusicCue.Stage2)]
        public void SceneRoutingPreservesNormalExploration(string scene, bool test, MusicCue expected)
            => Assert.That(SceneMusicController.SceneCue(scene, test), Is.EqualTo(expected));

        [Test]
        public void ExistingLobbyAndExplorationAssetsKeepTheirOriginalTracks()
        {
            Assert.That(Resources.Load<AudioManager>("Audio/LobbyAudioManager").MusicClip.name, Is.EqualTo("Lobby Drift"));
            var stage = Resources.Load<AudioManager>("Audio/Stage1AudioManager");
            Assert.That(stage.MusicClip.name, Is.EqualTo("Weightless Curiosity"));
            Assert.That(stage.MusicRepeatDelaySeconds, Is.EqualTo(5f));
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator LegacyWaitRemainsAndNewGameplayTracksLoopWithoutGaps()
        {
            // 0.1초 음원의 isPlaying을 프레임마다 관찰하면 느린 CI에서 재생 구간을 놓친다.
            // 실제 플레이어가 사용하는 전이 규칙을 명시적인 시각/종료 입력으로 검증한다.
            var repeat = new MusicRepeatState();
            repeat.Reset(10d);
            Assert.That(repeat.Tick(10d, false, .5f), Is.EqualTo(MusicRepeatStep.Start));
            Assert.That(repeat.Tick(10.05d, true, .5f), Is.EqualTo(MusicRepeatStep.Playing));
            Assert.That(repeat.Tick(10.1d, false, .5f), Is.EqualTo(MusicRepeatStep.Waiting));
            Assert.That(repeat.Tick(10.599d, false, .5f), Is.EqualTo(MusicRepeatStep.Waiting));
            Assert.That(repeat.Tick(10.6d, false, .5f), Is.EqualTo(MusicRepeatStep.Start), "기존 곡은 대기 후 재시작한다.");
            yield return new EnterPlayMode();
            var root = new GameObject("BGM repeat test");
            root.AddComponent<AudioListener>();
            var manager = ScriptableObject.CreateInstance<AudioManager>();
            var clip = AudioClip.Create("short music fixture", 4800, 1, 48000, false);
            try
            {
                var config = new SerializedObject(manager);
                foreach (var field in new[] { "musicClip", "stage2MusicClip", "practiceMusicClip" })
                    config.FindProperty(field).objectReferenceValue = clip;
                config.FindProperty("musicRepeatDelaySeconds").floatValue = .5f;
                config.FindProperty("musicFadeInSeconds").floatValue = 0f;
                config.FindProperty("musicFadeOutSeconds").floatValue = 0f;
                config.ApplyModifiedPropertiesWithoutUndo();
                var controller = root.AddComponent<SceneMusicController>();
                foreach (var scene in new[] { "Stage2_Earth", "PracticeLobby" })
                {
                    controller.ConfigureGameplay(manager, null, scene, false);
                    var active = root.GetComponents<AudioSource>().Single(s => s.loop);
                    Assert.That(active.loop, Is.True, "새 상시 곡은 종료/대기 정책 대신 AudioSource 연속 루프를 사용한다.");
                    Assert.That(active.clip, Is.SameAs(clip));
                    controller.Tick();
                    Assert.That(root.GetComponents<AudioSource>().Single(s => s.loop), Is.SameAs(active));
                    Assert.That(root.GetComponents<AudioSource>().Length, Is.EqualTo(2));
                }
            }
            finally { Object.Destroy(root); Object.Destroy(manager); Object.Destroy(clip); }
            yield return null;
            yield return new ExitPlayMode();
        }

        [Test]
        public void LegacyRepeatHandlesSkippedFramesWithoutMovingTheDeadline()
        {
            var repeat = new MusicRepeatState();
            repeat.Reset(0d);
            Assert.That(repeat.Tick(0d, false, .5f), Is.EqualTo(MusicRepeatStep.Start));
            // 0.1초 곡의 재생 중 프레임을 한 번도 관찰하지 못한 경우.
            Assert.That(repeat.Tick(.2d, false, .5f), Is.EqualTo(MusicRepeatStep.Waiting));
            Assert.That(repeat.Tick(.4d, false, .5f), Is.EqualTo(MusicRepeatStep.Waiting));
            Assert.That(repeat.Tick(.6d, false, .5f), Is.EqualTo(MusicRepeatStep.Waiting));
            Assert.That(repeat.Tick(.8d, false, .5f), Is.EqualTo(MusicRepeatStep.Start));
        }

        [Test]
        public void LegacyRepeatResetAndZeroDelayPreserveRestartBehavior()
        {
            var repeat = new MusicRepeatState();
            repeat.Reset(0d);
            Assert.That(repeat.Tick(0d, false, 0f), Is.EqualTo(MusicRepeatStep.Start));
            Assert.That(repeat.Tick(1d, false, 0f), Is.EqualTo(MusicRepeatStep.Waiting));
            Assert.That(repeat.Tick(1d, false, 0f), Is.EqualTo(MusicRepeatStep.Start));
            Assert.That(repeat.Tick(2d, false, 5f), Is.EqualTo(MusicRepeatStep.Waiting));
            repeat.Reset(3d); // 비활성화·재설정은 이전 곡의 대기를 이어받지 않는다.
            Assert.That(repeat.Tick(3d, false, 5f), Is.EqualTo(MusicRepeatStep.Start));
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator ReplicatedAssaultPersistsIntoCombatAndSettingsAndDisableApply()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("BGM replica test");
            var settings = GameSettingsRuntime.Ensure();
            var saved = settings.Current.Clone();
            try
            {
                var session = root.AddComponent<GameSession>();
                session.Begin(null, null, Vector3.zero);
                session.enabled = false;
                double now = 20;
                var sequence = root.AddComponent<Stage1Sequence>();
                sequence.Configure(session, null, () => false, () => now);
                var controller = root.AddComponent<SceneMusicController>();
                var manager = Resources.Load<AudioManager>("Audio/Stage1AudioManager");
                controller.ConfigureGameplay(manager, session, "Stage1_Astra", false);
                var player = root.GetComponent<GameMusicPlayer>();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.Default));
                sequence.ApplyReplica(Stage1SequencePhase.Assault, 17, 1, false);
                controller.Tick();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.Assault));
                var assault = root.GetComponents<AudioSource>().Single(s => s.clip == manager.GetMusicClip(MusicCue.Assault));
                Assert.That(assault.loop, Is.True);
                Assert.That(assault.time, Is.EqualTo(3f).Within(.25f), "늦참은 공세의 경과 위치에서 시작한다.");
                sequence.ApplyReplica(Stage1SequencePhase.Combat, 20, 2, true);
                controller.Tick();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.Assault));
                Assert.That(root.GetComponents<AudioSource>().Length, Is.EqualTo(2));
                var muted = saved.Clone(); muted.audio.musicVolume = 0f;
                settings.Preview(muted);
                yield return null;
                foreach (var source in root.GetComponents<AudioSource>()) Assert.That(source.volume, Is.Zero);
                var audible = saved.Clone(); audible.audio.musicVolume = 1f;
                settings.Preview(audible);
                yield return new WaitForSecondsRealtime(.5f);
                Assert.That(assault.volume, Is.EqualTo(manager.MusicBaseVolume).Within(.001f));
                root.SetActive(false);
                foreach (var source in root.GetComponents<AudioSource>()) Assert.That(source.isPlaying, Is.False);
                root.SetActive(true);
                controller.Tick();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.Assault));
            }
            finally { settings.Preview(saved); Object.Destroy(root); }
            yield return null;
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator EndingWaitsForSharedStartAndLateJoinSeeksCrashCue()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("BGM ending test");
            try
            {
                double now = 0;
                var snapshot = new DemoEndingSnapshot { StartedAt = 10, Players = new DemoEndingPlayer[4] };
                var ending = root.AddComponent<DemoEnding>();
                ending.ConfigurePlayback(snapshot, () => now);
                var controller = root.AddComponent<SceneMusicController>();
                var manager = Resources.Load<AudioManager>("Audio/Stage1AudioManager");
                controller.ConfigureEnding(manager, ending);
                var player = root.GetComponent<GameMusicPlayer>();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.Silence));
                now = 12; controller.Tick();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.Escape));
                now = 10 + ending.ArrivalStartsAt + 9; controller.Tick();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.CrashLanding));
                var crash = root.GetComponents<AudioSource>().Single(s => s.clip == manager.GetMusicClip(MusicCue.CrashLanding));
                Assert.That(crash.loop, Is.False);
                Assert.That(crash.time, Is.EqualTo(9f).Within(.25f));
                now = 10 + ending.ArrivalStartsAt + DemoEnding.ArrivalSeconds; controller.Tick();
                Assert.That(player.Cue, Is.EqualTo(MusicCue.Silence));
                yield return new WaitForSecondsRealtime(.5f);
                foreach (var source in root.GetComponents<AudioSource>()) Assert.That(source.isPlaying, Is.False);
            }
            finally { Object.Destroy(root); }
            yield return null;
            yield return new ExitPlayMode();
        }
    }
}
