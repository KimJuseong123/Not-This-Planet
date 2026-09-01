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
    /// 1장 샘플 상태로 만드는 데 필요한 에셋·씬 배치만 순서대로 실행한다.
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

            // 런타임 모델 연결 → 퍼즐 물리 구조 → 환경 에셋 → 운석 → 지형
            // → 용접 건 → 최종 외형 순서다.
            ArtLink.BuildPrefabs();
            Stage1PuzzleBuilder.Build();
            Stage1EnvironmentAssets.Build();
            Stage1MeteorAssets.Apply();
            Stage1TerrainBuilder.Build();
            Stage1WeldingPlasmaGunAsset.Build();
            Stage1SkinBuilder.Apply();
            Stage1AtmosphereSetup.Apply();

            Debug.Log("[Stage1Setup] 1장 전체 구성 완료 — 퍼즐·환경·운석·지형·용접 건·스킨·분위기");
        }
    }
}
