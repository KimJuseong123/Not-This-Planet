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
        /// <summary>
        /// <b>연 씬을 반드시 닫는다</b> (S15P21B202-604). 열어 둔 채 끝나면 뒤이어 도는
        /// 검증들이 이 씬의 콜라이더를 만나 무너진다 — 2026-09-17 에 그렇게
        /// <c>CraftMeteorImpactTests</c> 9건이 한꺼번에 깨졌다(기체가 남의 씬 바닥
        /// 4m 에 걸렸다). <c>test_scene_cleanup.py</c> 가 이 정리를 강제한다.
        /// </summary>
        [OneTimeTearDown]
        public void CloseAnyOpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

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
            // Prefab play-on-awake must work without the editor's explicit Simulate calls.
            var auraPreview = Object.Instantiate(Resources.Load<GameObject>("Cosmetics609/Aura02"), appearance.transform);
            yield return null;
            foreach (var particles in auraPreview.GetComponentsInChildren<ParticleSystem>())
            {
                Assert.That(particles.isPlaying, Is.True);
                Assert.That(particles.particleCount, Is.GreaterThan(0));
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
