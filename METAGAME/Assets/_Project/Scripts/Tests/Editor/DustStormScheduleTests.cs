using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class DustStormScheduleTests
    {
        AtmosphereProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<AtmosphereProfile>();
            profile.dustStorm.enabled = true;
            profile.dustStorm.seed = 12345;
            profile.dustStorm.speed = 6f;
            profile.Validate();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null) Object.DestroyImmediate(profile);
        }

        [Test]
        public void SameServerTimeAndSeedProduceTheSameStormPose()
        {
            var bounds = new Bounds(new Vector3(25f, 3f, 0f), new Vector3(150f, 6f, 100f));
            var first = new DustStormSchedule(profile.dustStorm).Evaluate(13.25d, bounds);
            var second = new DustStormSchedule(profile.dustStorm).Evaluate(13.25d, bounds);

            Assert.IsTrue(first.IsActive);
            Assert.That(first.CycleIndex, Is.EqualTo(second.CycleIndex));
            Assert.That(first.Center, Is.EqualTo(second.Center));
            Assert.That(first.Direction, Is.EqualTo(second.Direction));
            Assert.That(first.HalfExtents, Is.EqualTo(second.HalfExtents));
        }

        [Test]
        public void ScheduleKeepsOneStormInactiveDuringTheConfiguredGap()
        {
            var bounds = new Bounds(new Vector3(25f, 3f, 0f), new Vector3(150f, 6f, 100f));
            var schedule = new DustStormSchedule(profile.dustStorm);

            var beforeFirstStorm = schedule.Evaluate(10.99d, bounds);
            var duringFirstStorm = schedule.Evaluate(12.0d, bounds);

            Assert.IsFalse(beforeFirstStorm.IsActive);
            Assert.IsTrue(duringFirstStorm.IsActive);
        }

        [Test]
        public void ActiveStormUsesGroundBaseInsteadOfBoundsCenterHeight()
        {
            profile.dustStorm.groundClearance = 0.25f;
            var bounds = new Bounds(new Vector3(5025f, 30f, 5000f), new Vector3(150f, 60f, 100f));
            var pose = new DustStormSchedule(profile.dustStorm).Evaluate(12.0d, bounds);

            Assert.IsTrue(pose.IsActive);
            Assert.That(pose.Center.y,
                Is.EqualTo(pose.HalfExtents.y + profile.dustStorm.groundClearance).Within(0.001f));
        }

        [Test]
        public void ValidateCapsStormBudgetsAndNormalizesVolumeSize()
        {
            profile.dustStorm.halfExtents = new Vector3(-1f, 0f, -2f);
            profile.dustStorm.maximumInterval = 1f;
            profile.dustStorm.minimumInterval = 12f;
            profile.dustStorm.maxPhysicalFragments = 99;
            profile.dustStorm.maxVisualFragments = 99;
            profile.dustStorm.maxDustParticles = 999;
            profile.dustStorm.debugBoundaryWidth = 0f;

            profile.Validate();

            Assert.That(profile.dustStorm.halfExtents.x, Is.EqualTo(1f));
            Assert.That(profile.dustStorm.halfExtents.y, Is.EqualTo(0.1f));
            Assert.That(profile.dustStorm.halfExtents.z, Is.EqualTo(2f));
            Assert.That(profile.dustStorm.groundClearance, Is.GreaterThanOrEqualTo(0f));
            Assert.That(profile.dustStorm.maximumInterval,
                Is.GreaterThanOrEqualTo(profile.dustStorm.minimumInterval));
            Assert.That(profile.dustStorm.maxPhysicalFragments, Is.EqualTo(3));
            Assert.That(profile.dustStorm.maxVisualFragments, Is.EqualTo(12));
            Assert.That(profile.dustStorm.maxDustParticles, Is.EqualTo(128));
            Assert.That(profile.dustStorm.debugBoundaryWidth, Is.GreaterThan(0f));
            Assert.That(profile.dustStorm.debugSkyMarkerHeight, Is.GreaterThanOrEqualTo(10f));
        }

        [Test]
        public void InactiveStormDoesNotContainAnyPoint()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(100f, 6f, 80f));
            profile.dustStorm.enabled = false;

            var pose = new DustStormSchedule(profile.dustStorm).Evaluate(13d, bounds);

            Assert.IsFalse(pose.IsActive);
            Assert.IsFalse(pose.Contains(Vector3.zero));
        }

        [Test]
        public void BoundaryReacquiresRequiredRendererBeforeApplyingStyle()
        {
            var go = new GameObject("Dust Storm Debug Boundary Test");
            try
            {
                var boundary = go.AddComponent<DustStormBoundaryRenderer>();
                boundary.SetStyle(Color.red, 0.04f);
                boundary.SetPose(new StormPose(
                    true,
                    Vector3.zero,
                    Vector3.forward,
                    new Vector3(12f, 2.5f, 5f),
                    0));

                var filter = go.GetComponentInChildren<MeshFilter>(true);
                var renderer = go.GetComponentInChildren<MeshRenderer>(true);
                Assert.IsNotNull(filter);
                Assert.IsNotNull(renderer);
                Assert.IsNotNull(filter.sharedMesh);
                Assert.IsNotNull(renderer.sharedMaterial);
                Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(104));
                Assert.That(filter.sharedMesh.GetIndexCount(0), Is.EqualTo(468));
                Assert.IsTrue(renderer.enabled);
                Assert.IsNull(go.GetComponentInChildren<Collider>(true));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DestroyedDebrisReference_DoesNotAbortVisualSetup()
        {
            var root = new GameObject("Dust Storm Destroyed Debris Test");
            var debris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            profile.dustStorm.enabled = true;
            profile.dustStorm.largeDebrisPrefab = debris;
            Object.DestroyImmediate(debris);

            try
            {
                var storm = root.AddComponent<AtmosphereDustStorm>();
                Assert.DoesNotThrow(() => storm.Apply(profile));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DebrisSource_IsResolvedFromATemporaryInstance()
        {
            var root = new GameObject("Dust Storm Debris Source Test");
            var debris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            profile.dustStorm.enabled = true;
            profile.dustStorm.largeDebrisPrefab = debris;

            try
            {
                var storm = root.AddComponent<AtmosphereDustStorm>();
                storm.Apply(profile);

                var renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0));
                Assert.IsNotNull(renderers[0].mesh,
                    "유효한 debris 원본은 임시 인스턴스에서 Mesh를 추출해야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(debris);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BuiltInDebrisMaterial_FallsBackToProjectSurface()
        {
            var root = new GameObject("Dust Storm Built-in Material Test");
            var debris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var builtInShader = Shader.Find("Standard");
            Assume.That(builtInShader, Is.Not.Null,
                "이 테스트 환경에는 Built-in Standard 셰이더가 없습니다.");
            var builtInMaterial = new Material(builtInShader);
            debris.GetComponent<Renderer>().sharedMaterial = builtInMaterial;
            profile.dustStorm.largeDebrisPrefab = debris;

            try
            {
                var storm = root.AddComponent<AtmosphereDustStorm>();
                storm.Apply(profile);

                var debrisRenderer = root.transform.Find("Dust Storm Debris Large")
                    .GetComponent<ParticleSystemRenderer>();
                Assert.IsNotNull(debrisRenderer.sharedMaterial);
                Assert.AreNotEqual("Standard", debrisRenderer.sharedMaterial.shader.name);
                Assert.AreNotEqual("Hidden/InternalErrorShader", debrisRenderer.sharedMaterial.shader.name);
            }
            finally
            {
                Object.DestroyImmediate(builtInMaterial);
                Object.DestroyImmediate(debris);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeAtmosphereParticleSystem_UsesItsExistingRenderer()
        {
            var root = new GameObject("Atmosphere Particle Renderer Test");
            profile.dustGust.enabled = true;
            profile.meteorShower.enabled = true;

            try
            {
                root.AddComponent<AtmosphereDustGust>().Apply(profile);
                root.AddComponent<AtmosphereMeteorShower>().Apply(profile);

                var renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
                Assert.That(renderers.Length, Is.EqualTo(2));
                for (int i = 0; i < renderers.Length; i++)
                    Assert.That(renderers[i].GetComponents<ParticleSystemRenderer>().Length, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
