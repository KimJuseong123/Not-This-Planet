# Stage1 자기장 폭풍

기존 컨셉의 보라색 연기, 청록색 자기력선, 부유 파편을 Unity URP에서 사용하는 볼류메트릭 효과로 제작했다.

## 바로 사용하기

- **기존 Stage1_Astra**: 564 worktree의 Unity 프로젝트를 열고 실행하면 기존 폭풍 스케줄에 새 효과가 자동 연결된다. 기존 프로필, 발생 시점, 진행 방향, 서버 시드, 물리 힘과 피해 판정은 유지한다. 동일한 효과를 씬에 또 배치할 필요는 없다.
- **다른 씬에 직접 배치**: Resources/Stage1MagneticStorm/Stage1MagneticStorm.prefab을 Hierarchy로 드래그하고 Play한다. 이 경우 연기·빛·파편의 6초 루프만 자율 재생하며, 씬 전체를 가로지르는 이동이나 피해 판정은 추가하지 않는다.
- **단독 확인**: Preview/MagneticStormPreview.unity를 연 뒤 Play. 메뉴 **Metagame > Art > Open Magnetic Storm Preview**로도 열 수 있다.
- FBX만 직접 배치하면 볼륨이 완성되지 않으므로, 실제 씬에는 제공된 **프리팹**을 사용한다.

## 구성

| 경로 | 내용 |
| --- | --- |
| AstraMagneticStorm.fbx | 연기 볼륨 도메인 1개, 자기력선 7개, 파편 9개 |
| AstraMagneticStorm.fbm/SmokeDensity_128.png | Blender에서 만든 연기 밀도 128³의 XY 슬라이스 아틀라스, 16열 |
| AstraMagneticStorm.fbm/SmokeNoise_64.png | 이음새 없이 반복되는 64³ 난류 데이터 아틀라스, 8열 |
| AstraMagneticStorm.fbm/Flux_Emission.png | 청록색 자기력선 텍스처 |
| AstraMagneticStorm.fbm/StormDebris_BaseColor.png | 파편의 보라색 금속 색상 |
| Shaders/ | URP 볼륨 레이마칭과 자기력선 발광 셰이더 |
| Unity/ | 생성된 Texture3D, 연기·자기력선·파편 재질, 미리보기용 재질·Bloom 프로필 |
| Resources/Stage1MagneticStorm/ | 빌드에 포함되고 런타임에 재사용되는 프리팹 |
| Source~/AstraMagneticStorm.blend | 독립된 Blender 원본 씬. 실제 볼륨 노드와 조형용 밀도 덩어리 포함 |
| Source~/build_magnetic_storm.py | Blender MCP에서 사용한 재현 가능한 제작 스크립트 |
| authoring-manifest.json | 모델 수, 폴리곤 수, 데이터 해상도 |

Source~는 Unity가 자동 임포트하지 않는 원본 보관 폴더다.

## 볼륨 방식과 애니메이션

FBX에는 Blender의 볼륨 셰이더나 유체 시뮬레이션을 직접 저장할 수 없다. 따라서 Blender에서 조형한 밀도를 FBM 데이터 텍스처로 내보내고, Unity에서 3D 텍스처로 변환하여 **연기 내부를 레이마칭**한다. 연기 외형은 불투명 구체나 카메라를 향하는 평면 스프라이트가 아니다.

원본은 70개 조형용 밀도 덩어리로 구성된다. Unity에서는 그 결과를 합친 하나의 128³ 볼륨으로 처리한다. 물리 유체 시뮬레이션은 아니며, 베이크된 밀도와 주기적인 난류를 사용하는 절차적 볼류메트릭 연기다.

- 연기: 6초 주기의 난류 이동과 밀도 변형.
- 자기력선: FBX에 모델링된 곡선 메시의 UV를 따라 빛이 흐르며, 일부 구간만 밝아진다.
- 파편: 닫힌 경로의 부유·회전.
- 단독 프리팹은 게임 시간을, 기존 폭풍에 연결된 인스턴스는 기존 공유 폭풍 시뮬레이션 시간을 사용한다.
- 모델 pivot은 볼륨 중심이며 기본 범위는 로컬 X 24m × Y 5m × Z 10m이다.
- 기존 StormPose.Rotation에 따르면 진행은 **로컬 -X** 방향이다. 이전 컨셉 문서의 +Z 약속 대신, 실제 게임 판정 축에 맞췄다. 바람 힘과 박스 크기는 수정하지 않았다.
- 시각 프리팹에는 새 Collider나 Rigidbody가 없으며, 기존 hazard가 물리 효과를 계속 담당한다.
- 씬의 깊이 텍스처로 불투명 지형과의 교차를 처리하고, 카메라가 볼륨 안에 들어가도 연기를 그린다. 활성화 중 필요한 카메라 깊이 요청은 종료 시 원래 상태로 되돌린다.
- 다른 투명 효과와의 정렬은 일반적인 투명 렌더링 제약을 갖는다.

## 조절

Unity/Smoke.mat:
- Extinction: 연기 밀도.
- Ray steps: 샘플 수. 기본 64, 지원 범위 24~96.
- Violet shadow / Lavender scattering: 내부·빛을 받는 영역의 색.
- Ion scattering: 연기 내부의 청록색 빛.

Unity/Flux.mat:
- Cyan emission: 자기력선 HDR 색과 강도. 씬의 Bloom에 따라 빛 번짐이 달라진다.

MagneticStormVisual은 한 번 캐시한 렌더러와 MaterialPropertyBlock을 사용한다. 기존 AtmosphereDustStorm이 프리팹을 한 번 생성하고 이후 활성 상태를 재사용한다. 별도의 Animator나 네트워크 메시지는 추가하지 않았다.

## 재생성

1. Blender에서 Source~/AstraMagneticStorm.blend를 열어 확인한다.
2. 모델·밀도를 다시 생성할 경우 제공된 Python 스크립트를 Blender에서 실행한다. MCP의 exec 방식이라면 __file__에 스크립트의 실제 경로를 지정한다. 원본 사용자의 씬을 지우지 않고 별도 제작 씬을 만든다.
3. Unity 메뉴 **Metagame > Art > Rebuild Stage1 Magnetic Storm**을 실행한다. 기존 생성 자산의 GUID를 유지하면서 FBX 임포트, FBM → Texture3D 변환, 재질과 프리팹을 갱신한다.
4. 재생성은 이 폴더의 생성 결과를 덮어쓰므로 재질을 직접 조정했다면 먼저 기록한다.

## 검증과 범위

Unity 6000.5.4f1 / URP 17.5.0에서 임포트·C# 컴파일·셰이더 컴파일, 모델 축·크기, 프리팹 참조, 루프와 재활성화를 확인했다. 실제 URP 렌더로 정면·측면·위·볼륨 내부 및 Stage1_Astra 배치 이미지를 확인했다. Stage1 배치 이미지는 저장하지 않은 임시 배치로, 실제 게임에서는 기존 스케줄이 위치를 결정한다.

상세 결과는 저장소의 artifacts/stage1-magnetic-storm/ 및 docs/records/2026-09-16-magnetic-storm.md에 있다.

PC 에디터 기준이다. Windows 플레이어 빌드, 다중 접속, GPU 프레임 비용과 모바일 기기 테스트는 수행하지 않았다. 기존 모바일 비활성화 설정을 유지했다. 밀도·노이즈 원본 GPU 데이터는 약 2.25 MiB이며, 화면을 크게 덮는 레이마칭 비용은 실제 목표 장치에서 별도 측정해야 한다.

