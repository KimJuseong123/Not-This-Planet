using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 맵 목록이 실제 씬·빌드 설정과 어긋나지 않는지 지킨다 (2맵 준비).
    ///
    /// <para>맵을 더할 때 <see cref="StageCatalog.All"/> 에 한 줄만 더하면 되게 만들었지만,
    /// 그 한 줄이 <b>없는 씬을 가리키거나 빌드 목록에 오르지 않으면</b> 에디터에서는
    /// 멀쩡하고 빌드에서만 검은 화면이 된다. 컴파일로는 잡히지 않으므로 여기서 잡는다.</para>
    /// </summary>
    public sealed class StageCatalogTests
    {
        [Test]
        public void EveryStageSceneExistsAndIsInTheBuildList()
        {
            foreach (var stage in StageCatalog.All)
            {
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(stage.ScenePath), Is.Not.Null,
                    $"맵 \"{stage.Id}\" 의 씬 파일이 없다 → {stage.ScenePath}");
                Assert.That(SceneUtility.GetBuildIndexByScenePath(stage.ScenePath), Is.GreaterThanOrEqualTo(0),
                    $"맵 \"{stage.Id}\" 가 빌드 씬 목록에 없다 → {stage.ScenePath}");
            }
        }

        [Test]
        public void EveryEndingSceneExistsAndIsInTheBuildList()
        {
            foreach (var stage in StageCatalog.All)
            {
                if (string.IsNullOrEmpty(stage.EndingScenePath))
                    continue;

                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(stage.EndingScenePath), Is.Not.Null,
                    $"맵 \"{stage.Id}\" 의 엔딩 씬 파일이 없다 → {stage.EndingScenePath}");
                Assert.That(SceneUtility.GetBuildIndexByScenePath(stage.EndingScenePath), Is.GreaterThanOrEqualTo(0),
                    $"맵 \"{stage.Id}\" 의 엔딩 씬이 빌드 목록에 없다 → {stage.EndingScenePath}");
            }
        }

        [Test]
        public void DefaultStageIsInTheCatalog()
        {
            Assert.That(StageCatalog.TryFind(StageCatalog.DefaultStageId, out var stage), Is.True,
                "기본 맵이 목록에 없다 — 기본값을 바꿨으면 목록도 함께 바꾼다");
            Assert.That(stage.SceneName, Is.EqualTo(StageCatalog.DefaultStageScene));
        }

        /// <summary>
        /// 서버가 우리보다 새 맵을 먼저 아는 경우(클라이언트가 구버전)가 실제로 생긴다.
        /// 그때 빈 씬 이름을 돌려주면 로딩창에서 말없이 멈추므로, 기본 맵으로 떨어뜨린다.
        /// </summary>
        [Test]
        public void UnknownStageFallsBackToTheDefaultMapInsteadOfNothing()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"모르는 stageId"));

            Assert.That(StageCatalog.TryFind("stage-그런건-없다", out _), Is.False);
            Assert.That(StageCatalog.SceneNameOf("stage-그런건-없다"), Is.EqualTo(StageCatalog.DefaultStageScene));
        }

        [Test]
        public void LobbyAndEndingScenesAreNotMistakenForStageScenes()
        {
            Assert.That(StageCatalog.IsStageScene("Lobby"), Is.False);
            Assert.That(StageCatalog.IsStageScene("Customizer"), Is.False);
            Assert.That(StageCatalog.IsStageScene(StageCatalog.Stage1EndingSceneName), Is.False);
            Assert.That(StageCatalog.IsStageScene(StageCatalog.Stage1SceneName), Is.True);
        }

        /// <summary>
        /// 엔딩 씬 경로는 <see cref="DemoEnding"/> 가 이미 알고 있다. <see cref="StageCatalog"/> 는
        /// <c>Metagame.Core</c> 에 있어 게임플레이 쪽을 참조할 수 없으므로(참조 방향을 지킨다)
        /// 값을 한 번 더 적었다 — <b>두 값이 갈라지는 것은 여기서 막는다.</b>
        /// </summary>
        [Test]
        public void CatalogAgreesWithTheEndingSceneTheGameplayCodeUses()
        {
            StageCatalog.TryFind(StageCatalog.DefaultStageId, out var stage);

            Assert.That(stage.EndingScenePath, Is.EqualTo(DemoEnding.ScenePath));
        }
    }
}
