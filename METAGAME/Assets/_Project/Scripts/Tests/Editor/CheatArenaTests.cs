using System.Collections.Generic;
using System.IO;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.EditorTools;
using Metagame.Gameplay.Cheats;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 치트 시험장이 기대는 스포너 시험용 진입과, 생성된 씬의 배선을 검증한다.
    ///
    /// 치트가 실제 판과 다른 결과를 내면 시험 자체가 틀린 것이 되므로, 치트용 진입이
    /// 정규 경로(<see cref="MeteorSpawner.Pending"/>·<see cref="MeteorSpawner.Live"/>)에 그대로
    /// 얹히는지를 본다.
    /// </summary>
    public sealed class CheatArenaTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsInactive.Exclude))
                if (meteor != null) Object.DestroyImmediate(meteor.gameObject);

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // ── 스포너 시험용 진입 ────────────────────────────────────────────────

        [Test]
        public void LaunchAt_MakesWarningAndMeteor_AtTheGivenPoint()
        {
            var spawner = CreateSpawner();
            var point = new Vector3(3f, 0f, -4f);

            spawner.LaunchAt(point, MeteorSize.Medium);

            Assert.AreEqual(1, spawner.Pending.Count, "예고가 하나 떠야 한다.");
            Assert.AreEqual(1, spawner.Live.Count, "운석이 하나 떨어지고 있어야 한다.");
            Assert.AreEqual(point, spawner.Pending[0].ImpactPoint, "예고는 지정한 지점에 뜬다.");
            Assert.AreEqual(MeteorSize.Medium, spawner.Live[0].Size, "지정한 크기로 만든다.");
            Assert.AreEqual(0f, spawner.Elapsed, "공세 스케줄의 시계는 건드리지 않는다.");
        }

        [Test]
        public void LaunchAt_DoesNotDisturbTheSchedule()
        {
            var seeded = CreateSpawner(777);
            var cheated = CreateSpawner(777);

            cheated.LaunchAt(new Vector3(1f, 0f, 1f), MeteorSize.Small);

            // 진입 방향에 난수를 한 번 쓰므로 지점까지 같을 수는 없다. 공세가 **시작되는 시각**은
            // 시드·경과 시간만 보므로 같아야 한다 — 치트가 스케줄을 앞당기거나 늦추면 안 된다.
            var seededStart = SecondsUntilFirstWave(seeded);
            var cheatedStart = SecondsUntilFirstWave(cheated);
            Assert.AreEqual(seededStart, cheatedStart, Tuning.FixedStep, "치트가 공세 시각을 바꾸면 안 된다.");
        }

        [Test]
        public void TriggerWave_StartsTheWaveOnTheNextTicks()
        {
            var spawner = CreateSpawner();
            Assert.Greater(spawner.SecondsUntilWave, 10f, "처음에는 공세까지 시간이 남아 있어야 한다.");

            spawner.TriggerWave();
            spawner.Tick(Tuning.FixedStep);   // 신호 + 공세 시작
            spawner.Tick(Tuning.FixedStep);   // 공세의 첫 운석

            Assert.GreaterOrEqual(spawner.Live.Count, 1, "두 틱 안에 첫 운석이 떨어져야 한다.");
        }

        [Test]
        public void SkipTo_MovesTheClock_IntoALaterBand()
        {
            var spawner = CreateSpawner();
            var early = spawner.CurrentInterval;

            spawner.SkipTo(20f * 60f + 1f);

            Assert.AreEqual(20f * 60f + 1f, spawner.Elapsed, 1e-4f);
            Assert.AreEqual(spawner.IntervalAt(spawner.Elapsed), spawner.CurrentInterval, 1e-4f);
            Assert.Less(spawner.CurrentInterval, early, "뒷 시간대는 공세 간격이 짧다(기획 §17.4).");
        }

        [Test]
        public void OverrideCrew_WinsOverSetCrew_UntilCleared()
        {
            var spawner = CreateSpawner();

            spawner.OverrideCrew(4);
            spawner.SetCrew(1);
            Assert.AreEqual(4, spawner.Crew, "강제한 인원이 우선한다.");

            spawner.OverrideCrew(0);
            spawner.SetCrew(2);
            Assert.AreEqual(2, spawner.Crew, "해제하면 SetCrew 를 다시 따른다.");
        }

        // ── 생성된 씬 ────────────────────────────────────────────────────────

        [Test]
        public void CheatArenaScene_HasStage1GameplayAndConsole()
        {
            if (!File.Exists(CheatArenaSceneBuilder.ScenePath))
                Assert.Ignore("CheatArena 씬이 아직 없다. Tools/Not This Planet/치트 시험장 씬 만들기 를 먼저 돌린다.");

            var scene = EditorSceneManager.OpenScene(CheatArenaSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();

                Assert.IsNotNull(Find<SpaceBootstrap>(roots), "SpaceBootstrap 이 있어야 판이 선다.");
                Assert.IsNotNull(Find<DevManager>(roots), "SpaceBootstrap 이 DevManager 를 요구한다.");
                Assert.IsNotNull(Find<CheatArenaConsole>(roots), "치트 패널이 있어야 한다.");

                var bootstrap = Find<SpaceBootstrap>(roots);
                Assert.IsFalse(bootstrap.buildLevel, "SpaceFlop 코스를 세우지 않는다 — 바닥은 씬이 준다.");

                var map = Find<AuthoredMapSettings>(roots);
                Assert.IsNotNull(map, "AuthoredMapSettings 가 스폰·경계를 준다.");
                Assert.IsNotNull(map.playerSpawn, "플레이어 스폰 지점이 있어야 한다.");

                var spawner = Find<MeteorSpawner>(roots);
                Assert.IsNotNull(spawner, "Stage1 의 운석 스포너가 복제돼 있어야 한다.");

                var parts = FindAll<SpaceshipPart>(roots);
                Assert.AreEqual(PartSpec.All.Length, parts.Count, "핵심 파츠 수가 명세와 같아야 한다.");
                foreach (var spec in PartSpec.All)
                    Assert.IsTrue(parts.Exists(p => p.name == spec.Name), $"{spec.Name} 이 있어야 한다.");
                foreach (var part in parts)
                    Assert.Less(Mathf.Abs(BottomOf(part.transform)), 0.05f,
                        $"{part.name} 은 평평한 바닥(y=0)에 내려 앉아 있어야 한다.");

                Assert.IsNotNull(Find<ShipAssembly>(roots), "조립대가 있어야 한다.");
                Assert.GreaterOrEqual(FindAll<ToolBase>(roots).Count, 2, "용접기·방망이가 있어야 한다.");

                var ground = FindByName(roots, "Ground");
                Assert.IsNotNull(ground, "바닥이 있어야 한다.");
                Assert.AreEqual(GameLayers.Ground, ground.layer, "바닥은 Ground 레이어다.");

                var buildScenes = UnityEditor.EditorBuildSettings.scenes;
                Assert.IsFalse(System.Array.Exists(buildScenes, s => s.path == CheatArenaSceneBuilder.ScenePath),
                    "치트 시험장은 빌드 목록에 들어가면 안 된다.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ── 도우미 ───────────────────────────────────────────────────────────

        MeteorSpawner CreateSpawner(int seed = 12345)
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);
            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(seed);
            return spawner;
        }

        static float SecondsUntilFirstWave(MeteorSpawner spawner)
        {
            var elapsed = 0f;
            var guard = 0;
            while (!spawner.WaveActive && guard++ < 200000)
            {
                spawner.Tick(Tuning.FixedStep);
                elapsed += Tuning.FixedStep;
            }
            return elapsed;
        }

        static T Find<T>(GameObject[] roots) where T : Component
        {
            foreach (var root in roots)
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        static List<T> FindAll<T>(GameObject[] roots) where T : Component
        {
            var list = new List<T>();
            foreach (var root in roots) list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        static GameObject FindByName(GameObject[] roots, string name)
        {
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            return null;
        }

        /// <summary>
        /// 충돌 콜라이더의 가장 낮은 월드 y. <c>Collider.bounds</c> 는 에디터에서 물리 장면에
        /// 올라가기 전이면 비어 나오므로 상자 귀퉁이를 직접 옮겨 잰다(빌더와 같은 방식).
        /// </summary>
        static float BottomOf(Transform target)
        {
            var any = false;
            var min = float.MaxValue;
            foreach (var collider in target.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger) continue;
                Bounds local;
                switch (collider)
                {
                    case BoxCollider box: local = new Bounds(box.center, box.size); break;
                    case MeshCollider mesh when mesh.sharedMesh != null: local = mesh.sharedMesh.bounds; break;
                    default: continue;
                }
                for (var i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    min = Mathf.Min(min, collider.transform.TransformPoint(corner).y);
                    any = true;
                }
            }
            return any ? min : 0f;
        }
    }
}
