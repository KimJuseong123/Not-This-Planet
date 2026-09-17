# Stage1 Astra Terrain conversion

Stage1_Astra의 "Basin - Editable Terrain"이 실제 Unity Terrain 바닥이다.
기존 "Basin · sculpted ground" 메시에서 높이를 샘플링해 지형 형태를 옮겼다.
기존 바닥 MeshRenderer와 MeshCollider만 끄고 원본 메시/위치는 보관한다.
시설, 핵심 파츠, 식생, 스폰, 카메라, 조명은 이동하거나 수정하지 않는다.

## 편집

Hierarchy에서 "Basin - Editable Terrain" 선택 후 Terrain Inspector를 사용한다.

- Paint Terrain > Raise or Lower Terrain / Set Height / Smooth Height: 높이 편집.
- Paint Terrain > Paint Texture: 기본 모래, 외계 지면, 돌 레이어 선택 후 페인팅.
- Edit Terrain Layers > Add/Create Layer: 새 텍스처 레이어 추가.
- 지형 편집 후 새 식생이 뜨거나 묻히면 Tools > Not This Planet > Vegetation >
  Resnap vegetation to ground로 새 식생 그룹만 다시 밀착시킬 수 있다. Undo 지원.

초기 상태는 기존 바닥 텍스처를 전체에 사용한다. 추가 두 레이어의 초기 가중치는 0이다.
기존 전용 셰이더의 보라색 노이즈 효과 대신 Terrain/Lit가 모래 텍스처를 직접
표시하므로 초기 지면 외관은 흙색이다. 레이어 텍스처/색상은 Terrain Inspector에서 수정 가능하다.
TerrainData에는 초기 식생을 굽지 않았다. 기존 식생 프리팹의 Trigger, 흔들림,
감속 및 나무 MeshCollider 동작은 그대로 유지한다.

## 에셋

- Assets/_Project/Terrain/Stage1/TerrainData/TD_Stage1_AstraBasin.asset
  - 112×112m, 높이 범위 16m, 월드 시작점 (-56,-2,-56).
  - Heightmap 2049×2049, Alphamap 512×512, Basemap 1024×1024.
  - 바이너리 TerrainData는 전용 Git LFS 규칙과 -text로 보호한다.
- Assets/_Project/Terrain/Stage1/TerrainLayers/TL_Stage1_AstraSand.terrainlayer
  - 원본 SandTexture, 색상, 텍스처 반복 크기를 연결한 기본 레이어.
  - 기존 TL_Stage1_AlienGround / TL_Stage1_Stone도 페인팅 목록에 연결.
- Assets/_Project/Materials/Environment/Stage1/M_Stage1_AstraTerrain.mat
  - URP Terrain/Lit, GPU instancing.
- Assets/_Project/Scripts/Editor/Stage1AstraTerrainConversion.cs
  - 명시적인 격리 사본 전용 일회성 변환. 기존 TerrainData나 Terrain이 있으면
    실행을 중단하므로 이후 수작업 높이/텍스처 편집을 덮어쓰지 않는다.

## 검증

Unity 6000.5.4f1의 별도 프로젝트 사본에서 컴파일, 변환, 저장 후 재열기,
지면 충돌 및 오프스크린 렌더를 수행했다. 사용자 화면을 조작하지 않는다.

- 원본 메시 꼭짓점과 독립 표본을 합한 24,803개 지점 높이 비교.
- 평균 오차 약 0.42mm, 급경사 경계의 최대 오차 약 45.8mm.
- 식생 721개 위치의 최대 지면 차이 약 4.44mm. 식생 Transform 수정 불필요.
- 높이 브러시 데이터 변경/복원과 텍스처 레이어 칠하기/복원 확인.
- 주요 안내/스폰 34개 지점과 식생 지면 밀착 검사.
- 별도의 빈 물리 씬에서 TerrainCollider에 강체를 떨어뜨린 5개 지점 모두 착지 확인.
- 실제 게임 전체 플레이 및 다중 클라이언트 테스트는 별도로 수행하지 않았다.

최종 보고서: artifacts/stage1-terrain의 terrain-conversion.json,
terrain-reload.json, preservation-audit.json 및 overview.png / grove.png.
별도 물리 씬의 강체 착지 검사 결과도 재열기 보고서에 포함한다.

tools/unity/publish_astra_terrain.py는 저장된 사용자 씬의 동시 변경 여부를 확인하고
기존 바닥의 enabled 플래그 두 개, 지형 부모의 자식 목록, 새 Terrain 블록만 반영한다.
기존 오브젝트의 Transform과 컴포넌트 값은 해당 변경 외에 원본 그대로 보존한다.
