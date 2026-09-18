using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using Metagame.UI;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 엔딩 인사 구간의 개인 스포트라이트를 지킨다 (S15P21B202-639).
    ///
    /// <para><b>이것도 <c>Light</c> 컴포넌트가 아니라 그림이다</b> — 이유는
    /// <see cref="StageSpotlight"/> 에 적어 뒀다. 다만 커마와 달리 엔딩에서는 광원이
    /// 실제로 표면에 기여한다(키 라이트를 끄면 화소가 바뀐다 — 평균 0.074 · 최대 2.25).
    /// 그래도 공기 중의 빛줄기는 광원이 그려 주지 않으므로 직접 그린다.</para>
    /// </summary>
    public class EndingSpotlightTests
    {
        static double clock;

        // --- 시각 계산은 씬 없이 잰다 ------------------------------------------------

        [Test]
        public void ItStaysDarkUntilTheCameraHasArrived()
        {
            // 카메라는 다음 사람으로 0.3초에 걸쳐 섞인다(DemoEndingView.ShowBoarding).
            // 그 전에 켜면 이미 켜진 것을 향해 다가갈 뿐이라 **켜지는 순간이 없다**.
            Assert.That(DemoEnding.SpotlightIntensityAt(0f), Is.Zero);
            Assert.That(DemoEnding.SpotlightIntensityAt(DemoEnding.SpotlightStartsAt - 0.01f), Is.Zero);
            Assert.That(DemoEnding.SpotlightIntensityAt(DemoEnding.SpotlightStartsAt + 0.12f),
                Is.GreaterThan(0.5f), "카메라가 도착했는데도 아직 어둡다.");
        }

        [Test]
        public void ItPopsPastFullBeforeSettling()
        {
            float peak = 0f;
            for (float t = 0f; t < DemoEnding.GreetingSeconds; t += 0.01f)
                peak = Mathf.Max(peak, DemoEnding.SpotlightIntensityAt(t));
            // "뿅" 은 한 번 넘겼다 가라앉는 것이다. 1 에서 멈추면 그냥 켜지는 것이다.
            Assert.That(peak, Is.GreaterThan(1.1f), $"넘기지 않는다(최대 {peak:0.000}).");
            Assert.That(peak, Is.LessThan(1.5f), $"너무 많이 넘긴다(최대 {peak:0.000}).");
            Assert.That(DemoEnding.SpotlightIntensityAt(1.0f), Is.EqualTo(1f).Within(0.001f),
                "손 흔드는 동안에는 가만히 있어야 한다.");
        }

        [Test]
        public void ItIsAlreadyOffBeforeTheCameraLeaves()
        {
            Assert.That(DemoEnding.SpotlightDarkAt, Is.LessThan(DemoEnding.GreetingSeconds));
            Assert.That(DemoEnding.SpotlightIntensityAt(DemoEnding.SpotlightDarkAt), Is.Zero);
            Assert.That(DemoEnding.SpotlightIntensityAt(DemoEnding.GreetingSeconds - 0.001f), Is.Zero);
            // 꺼지는 것도 보여야 한다 — 한 프레임에 사라지면 안 된다.
            Assert.That(DemoEnding.SpotlightIntensityAt(DemoEnding.SpotlightDarkAt - 0.15f),
                Is.InRange(0.05f, 0.95f), "사그라드는 구간이 없다.");
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void OnlyOnePersonIsEverLit(int count)
        {
            for (float elapsed = 0f; elapsed < count * DemoEnding.GreetingSeconds + 1f; elapsed += 0.02f)
            {
                int lit = 0;
                int greeting = DemoEnding.GreetingActorAt(elapsed, count);
                for (int i = 0; i < count; i++)
                {
                    float value = greeting == i
                        ? DemoEnding.SpotlightIntensityAt(elapsed - i * DemoEnding.GreetingSeconds) : 0f;
                    if (value > 0f) lit++;
                }
                Assert.That(lit, Is.LessThanOrEqualTo(1), $"{elapsed:0.00}초에 {lit} 명이 켜져 있다.");
            }
        }

        // --- 무대에 실제로 서는지는 세워 보고 잰다 ------------------------------------

        static GameObject BuildStage(out DemoEndingView view, out Transform stage, int count)
        {
            var root = new GameObject("ending spotlight test");
            var ending = root.AddComponent<DemoEnding>();
            var players = new DemoEndingPlayer[count];
            for (int i = 0; i < count; i++) players[i] = new DemoEndingPlayer
                { Name = "Actor " + i, Suit = Color.white, Accent = Color.cyan, Appearance = i };
            ending.ConfigurePlayback(new DemoEndingSnapshot
            {
                Players = players,
                Parts = new[] { new DemoEndingPart { Name = PartSpec.All[0].Name,
                    Rotation = Quaternion.identity, Scale = Vector3.one } }
            }, () => clock);
            view = root.AddComponent<DemoEndingView>();
            view.Configure(ending, () => true);
            view.Prepare();
            stage = root.transform.Find("Demo Ending Stage");
            return root;
        }

        static Transform Spot(Transform stage, int index) => stage.Find("Greeting Spot " + index);

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator EachGreeterGetsALightOnTheirOwnAxis()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            clock = 0;
            var root = BuildStage(out var view, out var stage, 4);

            for (int i = 0; i < 4; i++)
            {
                var spot = Spot(stage, i);
                Assert.That(spot, Is.Not.Null, $"{i} 번 자리에 조명이 없다.");
                var beam = spot.Find("Greeting Spot " + i + " Beam");
                var pool = spot.Find("Greeting Spot " + i + " Pool");
                var lamp = spot.Find("Greeting Spot " + i + " Lamp");
                Assert.That(beam, Is.Not.Null); Assert.That(pool, Is.Not.Null); Assert.That(lamp, Is.Not.Null);

                // **레이어 31 이 아니면 아무것도 안 보인다** — 엔딩 카메라가 그것만 본다.
                foreach (var t in spot.GetComponentsInChildren<Transform>(true))
                    Assert.That(t.gameObject.layer, Is.EqualTo(31), t.name + " 이 엔딩 카메라에 안 보인다.");

                // 축이 맞아야 한다. 비스듬하면 빛기둥이 한쪽으로 쏠려 보인다(631 에서 겪었다).
                Assert.That(lamp.position.x, Is.EqualTo(spot.position.x).Within(0.001f));
                Assert.That(lamp.position.z, Is.EqualTo(spot.position.z).Within(0.001f));
                Assert.That(pool.position.x, Is.EqualTo(lamp.position.x).Within(0.001f));
                Assert.That(pool.position.z, Is.EqualTo(lamp.position.z).Within(0.001f));
                Assert.That(lamp.position.y - spot.position.y, Is.GreaterThan(1.3f),
                    "광원이 캐릭터 키(1.147m)보다 낮다 — 정수리 위여야 한다.");

                // 빛은 더해지는 것이다. 불투명이면 캐릭터를 가린다.
                foreach (var go in new[] { beam, pool, lamp })
                {
                    var material = go.GetComponent<MeshRenderer>().sharedMaterial;
                    Assert.That(material.shader.name, Is.EqualTo("NotThisPlanet/SpotlightGlow"));
                    Assert.That(material.GetTag("Queue", false, "Geometry"), Is.Not.EqualTo("Geometry"),
                        go.name + " 가 불투명 큐에 있다.");
                }
            }

            // 옆 사람까지 밝히면 "그 사람만 비춘다" 가 깨진다. 인사 자리 간격은 1.1m 다.
            float gap = Mathf.Abs(Spot(stage, 1).position.x - Spot(stage, 0).position.x);
            var bounds = Spot(stage, 0).Find("Greeting Spot 0 Pool").GetComponent<MeshRenderer>().bounds;
            Assert.That(bounds.size.x, Is.LessThan(gap),
                $"웅덩이 폭 {bounds.size.x:0.00}m 가 자리 간격 {gap:0.00}m 보다 넓다.");

            Object.DestroyImmediate(root);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator TheLightFollowsWhoIsGreetingAndLeavesWithThem()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            clock = 0;
            var root = BuildStage(out var view, out var stage, 4);

            for (int i = 0; i < 4; i++)
            {
                // 카메라가 막 도착한 순간에는 아직 어둡다.
                clock = i * DemoEnding.GreetingSeconds + DemoEnding.SpotlightStartsAt - 0.05f;
                view.Tick();
                Assert.That(Spot(stage, i).gameObject.activeSelf, Is.False,
                    $"{i} 번이 카메라가 도착하기도 전에 켜졌다.");

                // 한가운데에서는 그 사람만 켜져 있다.
                clock = i * DemoEnding.GreetingSeconds + 1f;
                view.Tick();
                for (int j = 0; j < 4; j++)
                    Assert.That(Spot(stage, j).gameObject.activeSelf, Is.EqualTo(i == j),
                        $"{i} 번 차례에 {j} 번 조명 상태가 틀렸다.");

                // 화면이 넘어가기 전에 꺼진다.
                clock = i * DemoEnding.GreetingSeconds + DemoEnding.SpotlightDarkAt;
                view.Tick();
                Assert.That(Spot(stage, i).gameObject.activeSelf, Is.False,
                    $"{i} 번이 화면이 넘어갈 때까지 켜져 있다.");
            }

            // 인사가 끝나고 다 같이 타러 가면 하나도 남지 않는다.
            clock = 4 * DemoEnding.GreetingSeconds + 1f;
            view.Tick();
            for (int j = 0; j < 4; j++)
                Assert.That(Spot(stage, j).gameObject.activeSelf, Is.False, $"{j} 번이 탑승 중에도 켜져 있다.");

            // 발사까지 가도 마찬가지다.
            clock = DemoEnding.BoardingDuration(4) + 1f;
            view.Tick();
            for (int j = 0; j < 4; j++)
                Assert.That(Spot(stage, j).gameObject.activeSelf, Is.False, $"{j} 번이 발사 중에도 켜져 있다.");

            Object.DestroyImmediate(root);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator FoldingTheStageTakesTheLightsWithIt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            clock = 1f;
            var root = BuildStage(out var view, out var stage, 2);
            var before = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Count(r => r.name.StartsWith("Greeting Spot"));
            Assert.That(before, Is.EqualTo(6), "조명 한 벌은 세 조각이다 — 2 명이면 6 개.");

            Object.DestroyImmediate(root);
            yield return null;
            var after = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Count(r => r.name.StartsWith("Greeting Spot"));
            Assert.That(after, Is.Zero, "무대를 접었는데 조명이 남았다.");

            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
