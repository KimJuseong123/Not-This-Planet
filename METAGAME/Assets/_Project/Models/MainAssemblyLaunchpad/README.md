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
씬 배치·충돌체·조립 판정·고정턱 애니메이션은 아직 연결하지 않았습니다. 기존 런타임 장식과 교체할 때 현재 도킹 높이, 운반 경로와 파츠 크기를 확인해야 합니다.
FBX 재임포트에서 크기, 삼각형 수, 두 UV 채널 및 텍스처 연결을 확인했습니다. Unity Editor에서의 임포트·플레이 검증은 하지 않았습니다.

## 원본과 미리보기
편집 가능한 Blender 파일, 실제 모델 렌더 6장, 검증 보고서는 저장소의 artifacts/stage1-main-assembly-launchpad/에 있습니다.
컨셉은 docs/assets/concepts/stage1-main-assembly-launchpad/를 참고합니다.
tools/blender/build_astra_main_launchpad.py, export_astra_main_launchpad.py, package_astra_main_launchpad.py로 제작 과정을 보관했습니다.
ZIP 압축파일은 생성하지 않았습니다.
