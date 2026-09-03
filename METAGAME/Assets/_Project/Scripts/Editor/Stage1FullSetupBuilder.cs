using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Stage1 제작 도구를 한 번에 실행하는 진입점이다.
    ///
    /// 진단(3D 모델 검사·모델 구조 찍어보기)과 배포(윈도우 빌드)는 작업 성격이
    /// 다르므로 이 통합 구성에는 넣지 않는다. 이 메뉴는 Main 씬을 플레이 가능한
    /// 1장 샘플 상태로 만드는 데 필요한 에셋 생성·씬 배치·참조 연결을 순서대로 실행한다.
    /// </summary>
    public static class Stage1FullSetupBuilder
    {
        [MenuItem("Tools/둥실둥실 우주 대탈출/Main 씬에 1장 전체 구성", false, 1)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Stage1 전체 구성",
                    "Play Mode를 종료한 뒤 실행해 주세요.",
                    "확인");
                return;
            }

            // 기존 도구 중 일부는 Main 씬을 Single 모드로 다시 열기 때문에,
            // 통합 실행 전에 현재 편집 중인 씬을 한 번만 저장 확인한다.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Debug.Log("[Stage1Setup] 1장 전체 구성 시작");

            // 각 도구를 개별 메뉴로 실행해도 되지만, 전체 구성에서는 의존 순서를 고정한다.
            // 태블릿·용접기 프리팹처럼 런타임이 Resources에서 찾는 에셋은 먼저 만들고,
            // 씬 구조를 만든 뒤 그 구조를 참조하는 모델·스킨·분위기를 적용한다.
            ArtLink.BuildPrefabs();

            // 런타임 Resources 에셋
            Stage1TabletAsset.Build();

            // 씬에 배치할 아트와 지형 기반
            Stage1EnvironmentAssets.Build();
            Stage1TerrainBuilder.Build();
            Stage1TileMapBuilder.Build();

            // 퍼즐 구조를 먼저 만든 뒤, 새로 만들어진 MeteorSpawner에 운석 프리팹을 연결한다.
            Stage1PuzzleBuilder.Build();
            Stage1MeteorAssets.Apply();

            // 퍼즐 장비는 Puzzle 그룹이 존재해야 씬에 배치할 수 있다.
            Stage1WeldingPlasmaGunAsset.Build();
            Stage1WeldingPlasmaGunAsset.ApplyProductionModel();

            // 최종 외형과 1장 분위기
            Stage1SkinBuilder.Apply();
            Stage1MeshColliderSetup.Apply();
            Stage1AtmosphereSetup.Apply();

            AssetDatabase.SaveAssets();
            Debug.Log("[Stage1Setup] 1장 전체 구성 완료 — Resources·태블릿·퍼즐·운석·지형·맵 타일·용접 건·스킨·메시 콜라이더·분위기");
        }
    }
}
