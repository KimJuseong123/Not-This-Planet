using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Metagame.Gameplay;
using Metagame.UI;

namespace Metagame.Gameplay.Tests
{
    public class CustomizerRuntimeTests
    {
        [Category("Heavy")]
        [UnityTest]
        public IEnumerator PreviewLoadsWithoutGameplayAndCancelReturnsToLobby()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Customizer.unity");
            yield return new EnterPlayMode();
            yield return null;
            var appearance = Object.FindAnyObjectByType<PlayerAppearance>();
            Assert.That(appearance, Is.Not.Null);
            Assert.That(appearance.Applied, Is.EqualTo(CustomizationData.Load().Packed));
            Assert.That(Object.FindAnyObjectByType<GameSession>(), Is.Null);
            foreach (var body in appearance.GetComponentsInChildren<Rigidbody>())
            {
                Assert.That(body.isKinematic, Is.True);
                Assert.That(body.detectCollisions, Is.False);
            }
            Assert.That(LobbyTransition.Ensure().BeginTitle(), Is.True);
            float deadline = Time.realtimeSinceStartup + 15;
            while (SceneManager.GetActiveScene().name != "Lobby" && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Lobby"));
            Assert.That(Object.FindAnyObjectByType<PlayerAppearance>(), Is.Null);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
