# Astra 메인 조립·발사대

Blender MCP로 기존 컨셉아트를 참고하여 직접 모델링한 시각 에셋입니다.

## 구성
- SM_Astra_MainAssemblyLaunchpad.fbx
- SM_Astra_MainAssemblyLaunchpad.fbm/ : BaseColor, Normal, Metallic, Roughness, Emission, MS 6종 PNG
- M_Astra_MainAssemblyLaunchpad.mat : 프로젝트 기존 URP Lit 재질 규칙 및 텍스처 연결
- asset-info.json : 크기, 원점, 메시 수와 검증 정보

## 구조
낮은 원형 받침, 청록 유도 링 두 줄, 분리된 고정턱 네 개, 후방 기둥 두 개, 냉각구와 점검문, 앞쪽 경사로, 오른쪽 조작판으로 구성했습니다.
표면 색과 미세 질감은 기존 AstraHangar의 2048px 팔레트 텍스처를 재사용했습니다. UV0는 팔레트 셀을 공유하고 UV1은 메시별 라이트맵용 펼침입니다.
14개 메시, 47,488 삼각형. 고정턱 4개는 개별 원점을 가지며 다른 구조와 분리되어 있습니다.

## 크기와 배치
너비 약 7.73m, 깊이 8.29m, 높이 3.33m입니다.
원점은 중앙 평면의 조립 기준점이며, 받침은 원점 아래 약 0.53m까지 내려갑니다. Blender 앞쪽은 -Y입니다.
현재 22m Central Assembly Dock 위에 얹는 안쪽 조립 장치이며, 바깥 도크는 모델에 포함하지 않았습니다.
Stage1_Astra의 AssemblyPoint 아래 `PF_Stage1_MainAssemblyLaunchpad` 프리팹으로 배치했습니다. 조립 기준점은 (0, 1.44, 0), 회전은 Y 45°이며 진입 경사로가 남서쪽 엔진 운반 경로를 향합니다. 하부 받침은 기존 도크에 약 8cm 묻히고 경사로 아래 끝은 기존 바닥에 닿습니다.
기존 ShipAssembly·LaunchTest·ShipHatch 참조와 AssemblyDock의 포획/권한 판정은 유지합니다. AssemblyDockVisual은 네 고정턱의 0.25m 이동과 대기 적색/고정 청록 상태등만 담당합니다. 모델이 없는 다른 씬에는 기존 절차 생성 장식을 유지합니다.
충돌체는 World 레이어의 고정 충돌체 8개입니다: 중앙 받침 MeshCollider, 경사로 MeshCollider, 기둥 받침/기둥 BoxCollider 4개, 후방 정비 장치 및 조작판 BoxCollider 2개. 고정턱과 발광 장식은 충돌체가 없으며 중앙 로켓 적층 공간은 비워 둡니다.
`Integration/`에는 저밀도 받침 충돌 메시, 상태등 면을 분리한 고정턱 메시 4개, 상태 재질 2개를 보관합니다. 원본 FBX/FBM은 유지합니다. 에디터 메뉴 `Metagame > Stage1 > Apply Main Launchpad`로 저장된 Stage1 씬에서 재생성할 수 있습니다.

## 원본과 미리보기
편집 가능한 Blender 파일, 실제 모델 렌더 6장, 검증 보고서는 저장소의 artifacts/stage1-main-assembly-launchpad/에 있습니다.
컨셉은 docs/assets/concepts/stage1-main-assembly-launchpad/를 참고합니다.
tools/blender/build_astra_main_launchpad.py, export_astra_main_launchpad.py, package_astra_main_launchpad.py로 제작 과정을 보관했습니다.
ZIP 압축파일은 생성하지 않았습니다.
