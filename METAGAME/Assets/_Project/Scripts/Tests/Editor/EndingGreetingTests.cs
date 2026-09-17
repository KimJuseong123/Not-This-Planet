using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Metagame.Gameplay.Parts;
using Metagame.UI;

namespace Metagame.Gameplay.Tests
{
    public class EndingGreetingTests
    {
        static double clock;
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void EachParticipantGetsATurnBeforeSharedBoarding(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.That(DemoEnding.GreetingActorAt(i * 2f + 0.1f, count), Is.EqualTo(i));
                Assert.That(DemoEnding.GreetingActorAt(i * 2f + 1.9f, count), Is.EqualTo(i));
                Assert.That(DemoEnding.BoardingProgressAt(i * 2f + 1.9f, count), Is.Zero);
            }
            Assert.That(DemoEnding.GreetingActorAt(count * 2f, count), Is.EqualTo(-1));
            Assert.That(DemoEnding.BoardingProgressAt(count * 2f + 3f, count), Is.EqualTo(0.5f));
            float launch = DemoEnding.BoardingDuration(count);
            Assert.That(DemoEnding.BoardingProgressAt(launch, count), Is.EqualTo(1f));
            Assert.That(DemoEnding.StageAt(launch - 0.01f, count), Is.EqualTo(DemoEndingStage.Boarding));
            Assert.That(DemoEnding.StageAt(launch, count), Is.EqualTo(DemoEndingStage.Launching));
            Assert.That(DemoEnding.StageAt(launch + 10f, count), Is.EqualTo(DemoEndingStage.EndTitle));
            Assert.That(DemoEnding.StageAt(launch + 13f, count), Is.EqualTo(DemoEndingStage.Lobby));
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator CameraMovesToEachFaceThenEveryoneBoardsTogether()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            clock = 0;
            var root = new GameObject("greeting test");
            var ending = root.AddComponent<DemoEnding>();
            var players = new DemoEndingPlayer[4];
            for (int i = 0; i < players.Length; i++) players[i] = new DemoEndingPlayer
                { Name = "Actor " + i, Suit = Color.white, Accent = Color.cyan, Appearance = i };
            ending.ConfigurePlayback(new DemoEndingSnapshot
            {
                Players = players,
                Parts = new[] { new DemoEndingPart { Name = PartSpec.All[0].Name,
                    Rotation = Quaternion.identity, Scale = Vector3.one } }
            }, () => clock);
            var view = root.AddComponent<DemoEndingView>();
            view.Configure(ending, () => true);
            view.Prepare();
            var camera = root.GetComponentInChildren<Camera>();
            var stage = root.transform.Find("Demo Ending Stage");
            for (int i = 0; i < 4; i++)
            {
                clock = i * 2f + 0.5f; view.Tick();
                var medium = camera.transform.position;
                clock = i * 2f + 1.8f; view.Tick();
                Assert.That(camera.transform.position.z, Is.GreaterThan(medium.z + 1f));
                var actor = stage.Find("Actor " + i);
                Assert.That(actor.gameObject.activeSelf, Is.True);
                // 해당 얼굴 정면으로 이동한다. 인사 순서에 따라 좌우 초점도 바뀐다.
                Assert.That(camera.transform.position.x - stage.position.x,
                    Is.EqualTo((i - 1.5f) * 1.1f).Within(0.01f));
            }
            clock = 11; view.Tick();
            for (int i = 0; i < 4; i++) Assert.That(stage.Find("Actor " + i).gameObject.activeSelf, Is.True);
            clock = 14; view.Tick();
            for (int i = 0; i < 4; i++) Assert.That(stage.Find("Actor " + i).gameObject.activeSelf, Is.False);
            Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.Launching));
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
