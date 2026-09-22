using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// Stage1_Astra 의 하늘에 별이 <b>전천에</b> 깔리는지 지킨다 (S15P21B202-546).
    ///
    /// <para><b>왜 이 테스트가 있나.</b> <c>-530</c> 에서 별 분포를 고치고 검증 15개를
    /// 통과시켰는데 화면은 그대로였다. 고친 <see cref="AtmosphereBackdrop"/> 이
    /// <b>이 씬에서 한 번도 실행되지 않았기 때문이다</b> —
    /// <see cref="AuthoredMapSettings.preserveSceneLighting"/> 이 참이면 하늘을 만드는 길
    /// 셋이 모두 닫혔다. 실제로 그려지던 것은 씬에 박아 둔 아트 메시 1000개였고, 그 메시는
    /// 면적이 같은 전천 96칸 중 <b>84칸이 비어 있었다.</b></para>
    ///
    /// <para><b>그래서 여기서는 함수가 아니라 씬을 본다.</b> 알고리즘이 옳은지는
    /// <c>StarfieldEvennessTests</c> 가 이미 지킨다. 여기가 지키는 것은
    /// <b>그 알고리즘이 이 씬에서 실제로 돌 수 있는가</b> 이다.</para>
    /// </summary>
    public sealed class Stage1SkyCoverageTests
    {
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string ProfilePath = "Assets/_Project/Data/Atmosphere/Stage1AtmosphereProfile.asset";
        const string GalaxyShaderPath = "Assets/_Project/Shaders/Atmosphere/Galaxy.shader";
        const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";

        /// <summary>고도를 <c>sin</c> 으로 8등분 × 방위를 12등분. 96칸의 넓이가 모두 같다.</summary>
        const int AltitudeBands = 8;
        const int AzimuthSlices = 12;

        /// <summary>
        /// <b>연 씬을 반드시 닫는다.</b> 열어 둔 채 끝나면 뒤이어 도는 검증들이 스테이지의
        /// 콜라이더를 만난다 — 처음에 이것을 빠뜨려 빔·운반체 검증 10개가 줄줄이 무너졌다.
        /// 실패 집합을 develop 과 대조하지 않았으면 못 봤을 자리다.
        ///
        /// <para><b>테스트마다가 아니라 한 번만 닫는다</b> (S15P21B202-602). 지키려는 것은
        /// <i>"이 클래스가 끝난 뒤 스테이지가 열려 있지 않다"</i> 이고, 그건 마지막에 한 번
        /// 닫아도 똑같이 지켜진다. 매번 닫으면 다음 테스트가 <see cref="OpenStage"/> 에서
        /// 씬을 다시 열어 — 실측 4.5초짜리가 4번 돌았다.</para>
        /// </summary>
        [OneTimeTearDown]
        public void CloseTheStage()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>
        /// 스테이지를 <b>한 번만</b> 연다 (S15P21B202-602). 이미 열려 있으면 그대로 쓴다.
        ///
        /// <para><b>공유해도 되는 이유:</b> 이 클래스의 검증은 전부 <b>읽기만</b> 한다 —
        /// 조명·별하늘 설정과 메시 분포를 재고 단정할 뿐, 씬의 무엇도 바꾸지 않는다.
        /// 여기에 씬을 고치는 검증을 새로 넣으려면 그 검증은 <b>자기 씬을 따로 열어야
        /// 한다</b> — 그러지 않으면 같은 클래스의 다른 검증이 그 변경 위에서 돈다.</para>
        /// </summary>
        static UnityEngine.SceneManagement.Scene OpenStage()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.IsValid() && active.path == ScenePath) return active;
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static T FindInScene<T>() where T : Component
        {
            var scene = OpenStage();
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        static AtmosphereProfile ShippedProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(ProfilePath);
            Assert.NotNull(profile, "배포되는 분위기 프로파일이 없다: " + ProfilePath);
            profile.Validate();
            return profile;
        }

        // ── 이 씬에서 별을 만드는 길이 열리는가 ────────────────────────

        [Test]
        public void TheStageKeepsItsAuthoredLighting_WhichIsWhyTheGateHadToBeSplit()
        {
            var authored = FindInScene<AuthoredMapSettings>();
            Assert.NotNull(authored, "Stage1_Astra 에 AuthoredMapSettings 가 없다");
            Assert.IsTrue(authored.preserveSceneLighting,
                "수제 조명 보존이 꺼졌다 — 그렇다면 이 티켓의 전제가 바뀐 것이니 다시 본다");
        }

        [Test]
        public void TheStageAsksForTheCodeStarfield()
        {
            var authored = FindInScene<AuthoredMapSettings>();
            Assert.NotNull(authored);
            Assert.IsTrue(authored.useCodeStarfield,
                "코드 별하늘이 꺼져 있다 — 하늘의 별이 다시 아트 메시 하나로 돌아간다");
        }

        [Test]
        public void TheStageHasAnAtmosphereProfile_OrThereIsNothingToBuildStarsFrom()
        {
            var bootstrap = FindInScene<SpaceBootstrap>();
            Assert.NotNull(bootstrap, "Stage1_Astra 에 SpaceBootstrap 이 없다");

            var field = typeof(SpaceBootstrap).GetField("atmosphereProfile",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field, "atmosphereProfile 필드가 사라졌다");
            Assert.NotNull(field.GetValue(bootstrap),
                "프로파일이 비었다 — 별을 만들 재료가 없어 문지기가 열려도 소용이 없다");
        }

        [Test]
        public void TheShippedStageEnablesTheGalaxyAndShootingStars()
        {
            var profile = ShippedProfile();

            Assert.IsTrue(profile.galaxy.enabled, "은하수 광운이 꺼져 있다");
            Assert.NotNull(profile.galaxy.texture, "은하수 성운 텍스처가 연결되지 않았다");
            Assert.Greater(profile.galaxy.intensity, 0f, "은하수 밝기가 0이다");
            Assert.Greater(profile.galaxy.width, 0.05f, "은하수 띠가 화면에서 식별하기 어려울 만큼 얇다");
            Assert.IsTrue(profile.meteorShower.enabled, "별똥별 효과가 꺼져 있다");
            Assert.Greater(profile.meteorShower.emissionRate, 0f, "별똥별 발생 빈도가 0이다");
            Assert.LessOrEqual(profile.meteorShower.emissionRate, 30f,
                "별똥별 발생량이 연출 기준인 초당 30개를 초과한다");
        }

        [Test]
        public void TheGalaxyShaderIsAlwaysIncludedInPlayerBuilds()
        {
            string shaderGuid = AssetDatabase.AssetPathToGUID(GalaxyShaderPath);
            Assert.IsNotEmpty(shaderGuid, "은하수 셰이더를 찾을 수 없다: " + GalaxyShaderPath);

            string graphicsSettings = File.ReadAllText(GraphicsSettingsPath);
            StringAssert.Contains($"guid: {shaderGuid}", graphicsSettings,
                "은하수 셰이더가 Always Included Shaders 에 없다 — Shader.Find 로만 참조하면 "
                + "플레이어 빌드에서 제거되어 런처의 은하수 띠가 사라진다");
        }

        [Test]
        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-678)
        public void TheBakedArtStarMeshIsOff_SoTheSkyIsNotDrawnTwice()
        {
            var scene = OpenStage();
            var art = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == "Distant star field");

            if (art == null) return;   // 지워졌다면 그것도 맞다

            Assert.IsFalse(art.gameObject.activeSelf,
                "구워 둔 별 메시가 아직 켜져 있다 — 코드 별하늘과 겹쳐 두 겹으로 보인다");
        }

        // ── 배포되는 하늘이 정말 전천을 덮는가 ──────────────────────────

        /// <summary>
        /// 면적이 같은 96칸 중 별이 하나도 없는 칸의 수.
        /// <para>고도를 각도가 아니라 <c>sin</c> 으로 나눈다 — 각도로 나누면 극 근처 칸이
        /// 훨씬 좁아서 "비었다" 가 결함인지 기하인지 구분되지 않는다.</para>
        /// </summary>
        static int EmptyCells(IEnumerable<Vector3> directions, out int fewest)
        {
            var cells = new int[AltitudeBands * AzimuthSlices];
            foreach (var d in directions)
            {
                int band = Mathf.Clamp((int)((d.normalized.y + 1f) * 0.5f * AltitudeBands),
                                       0, AltitudeBands - 1);
                float azimuth = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
                if (azimuth < 0f) azimuth += 360f;
                int slice = Mathf.Clamp((int)(azimuth / (360f / AzimuthSlices)), 0, AzimuthSlices - 1);
                cells[band * AzimuthSlices + slice]++;
            }

            fewest = cells.Min();
            return cells.Count(c => c == 0);
        }

        static Vector3[] ShippedStarDirections()
        {
            var settings = ShippedProfile().stars;
            return AtmosphereBackdrop
                .BuildStarPoints(settings, settings.starCount, settings.bandCount)
                .Select(s => s.Direction)
                .ToArray();
        }

        [Test]
        public void TheShippedSkyLeavesNoPartOfTheSkyEmpty()
        {
            int empty = EmptyCells(ShippedStarDirections(), out int fewest);

            Assert.Zero(empty, $"전천 96칸 중 {empty}칸에 별이 하나도 없다");
            Assert.Greater(fewest, 5, $"가장 빈 칸에 별이 {fewest}개뿐이다");
        }

        [Test]
        public void TheShippedSkyCoversStraightUpAndBelowTheHorizon()
        {
            var directions = ShippedStarDirections();

            int zenith = directions.Count(d => d.normalized.y > 0.87f);   // 위 30도 고깔
            int below = directions.Count(d => d.normalized.y < 0f);

            Assert.Greater(zenith, 50,
                $"똑바로 위를 보면 별이 {zenith}개뿐이다 — 아트 메시가 60도 위를 비워 둔 것이 그 증상이었다");
            Assert.Greater(below, 200,
                $"지평선 아래에 별이 {below}개뿐이다 — 절벽 위에서 내려다보는 시점이 있다");
        }

        [Test]
        public void EveryCompassDirectionGetsAFairShare()
        {
            var directions = ShippedStarDirections();
            var perQuadrant = new int[8];
            foreach (var d in directions)
            {
                float azimuth = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
                if (azimuth < 0f) azimuth += 360f;
                perQuadrant[Mathf.Clamp((int)(azimuth / 45f), 0, 7)]++;
            }

            float average = (float)directions.Length / perQuadrant.Length;
            int worst = perQuadrant.Min();

            // 아트 메시는 이 자리에서 0 이었다(8방향 중 4방향이 0).
            Assert.Greater(worst, average * 0.75f,
                $"방위 8구간 중 가장 적은 곳이 {worst}개다 (평균 {average:F0}) — "
                + $"구간별: {string.Join(" ", perQuadrant)}");
        }

        // ── 아트 메시가 왜 안 되는지도 못박아 둔다 ──────────────────────

        [Test]
        public void TheBakedArtMeshCouldNeverHaveCoveredTheSky()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                "Assets/_Project/Art/Stage1Concept/Meshes/Stars.asset");
            if (mesh == null) return;   // 에셋이 지워졌다면 이 비교는 의미가 없다

            var vertices = mesh.vertices;
            var directions = new List<Vector3>();
            for (int i = 0; i + 3 < vertices.Length; i += 4)
            {
                var centre = (vertices[i] + vertices[i + 1] + vertices[i + 2] + vertices[i + 3]) * 0.25f;
                if (centre.sqrMagnitude > 1e-6f) directions.Add(centre.normalized);
            }

            int empty = EmptyCells(directions, out _);

            // 이 테스트가 없으면 위의 검증들이 "원래 문제가 없었다" 로도 통과한다.
            Assert.Greater(empty, 60,
                $"구워 둔 별 메시가 전천 96칸 중 {empty}칸만 비운다 — 다시 구운 것이라면 "
                + "이 티켓의 근거가 바뀐 것이니 사람이 다시 본다");
        }
    }
}
