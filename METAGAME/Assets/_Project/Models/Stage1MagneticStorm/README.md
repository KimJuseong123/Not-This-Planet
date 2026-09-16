# Stage1 자기장 폭풍

기존 컨셉의 보라색 연기, 청록색 자기력선, 부유 파편을 Unity URP에서 사용하는 볼류메트릭 효과로 제작했다.

## 바로 사용하기

- **기존 Stage1_Astra**: 564 worktree의 Unity 프로젝트를 열고 실행하면 기존 폭풍 스케줄에 새 효과가 자동 연결된다. 공유 서버 시드를 사용하며, 넓은 수평 스윕과 주기적 단발 충격 설정을 적용한다. 동일한 효과를 씬에 또 배치할 필요는 없다.
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
- StormPose.Rotation의 진행 방향은 **로컬 +Z**이며 긴 X축을 진행 방향에 수직으로 배치한다. Stage1은 깊이를 절반으로 줄여 너비 24m × 높이 5m × 두께 5m로 사용한다.
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


## Stage1_Astra 씬 적용 (2026-09-16)

씬의 `Stage1 Magnetic Storm`에 프리팹을 배치하고 SpaceBootstrap의 Storm Presentation에 연결했다. Play하면 기존 공유 시드/시간에 따라 맵 외곽에서 진입하며, 24×5×5m 범위가 초당 6m로 이동한다. 내부 연기·자기장·파편은 6초 루프를 사용한다. 바람 물리와 표시 효과는 같은 이동 스케줄을 따른다.

자기장 사용 시 기존 모래/장식 파편 ParticleSystem은 생성하지 않는다. Resources 프리팹이 없는 이전 콘텐츠의 모래 폴백은 유지한다. 이동 오브젝트를 공용 시스템과 분리해 카메라·HUD·물리 파편의 부모가 함께 움직이지 않도록 했다.

씬 재연결 메뉴: **Metagame > Art > Apply Magnetic Storm to Stage1 Astra**. 플레이 검증 메뉴: **Metagame > Art > Validate Stage1 Magnetic Storm in Play Mode** (저장된 Stage1_Astra에서 실행하며 검사 후 Play Mode를 종료한다).

실제 Stage1 플레이에서 단일 인스턴스, 모래 제거, 공용 시스템 위치 유지, 물리 스케줄과 위치 일치, 1.5초 동안 수평 9m 이동, 휴지기 숨김 및 재사용을 확인했다. 이번 추가 회귀 테스트는 Unity 창 제어 제한으로 실행하지 못했다. 증거: `artifacts/stage1-magnetic-storm/scene-integration/`.

## 뭉게구름 움직임 강화

연기의 밀도 좌표에 이동하는 회전 흐름과 국소 팽창을 적용했다. 덩어리마다 서로 다른 위상으로 부풀고 말려 올라가며, 반복 노이즈가 두 차례 흐르고 위로 상승하면서 가장자리를 흩뜨린다. 자기장 선과 파편의 흔들림도 확대했다. 폭풍의 이동 경로·물리 범위와 6초 주기는 유지한다.

Smoke 재질의 **Rolling cloud strength**(기본 1)로 덩어리 변형, **Breaking wisps**(기본 0.22)로 가장자리 분해 정도를 조절할 수 있다. 레이마칭 단계와 3D 텍스처 샘플 수는 유지했지만 삼각함수 연산이 늘었으므로 GPU 성능 개선을 의미하지 않는다.

Unity URP로 49개 프레임을 렌더링했으며 볼륨/자기장 셰이더 오류가 없었다. 0초와 6초 프레임의 픽셀 차이는 0이다. `artifacts/stage1-magnetic-storm/billowing/magnetic-storm-billowing.gif`에서 내부 움직임을 확인할 수 있다. 미리보기에서는 형태 변화를 보기 쉽게 폭풍 위치를 고정했다. 재생성 메뉴는 **Metagame > Art > Render Magnetic Storm Billow Loop**이다.

## 넓은 수평 스윕과 단발 충격

현재 게임 설정은 너비 24m(X), 높이 5m(Y), 진행 방향 두께 5m(Z)다. 긴 X축을 진행 방향에 수직으로 놓고 +Z 방향으로 이동한다. 중심 높이는 맵의 바닥 기준 + 반높이 + Ground Clearance로 고정되며, 지형·벽·물체를 만나도 이동 방향이나 높이를 바꾸지 않는다. 과거의 지형 따라가기와 지속 바람 힘은 사용하지 않는다.

- 씬의 **Stage1 Magnetic Storm > Stage1MagneticStorm > Magnetic Storm Visual > Smoke Opacity**: 기본 0.38. 0은 연기 투명, 1은 원래 불투명도. 자기장 선은 유지된다. 프리팹에서도 같은 필드를 조절할 수 있다.
- **Assets/_Project/Data/Atmosphere/Stage1AtmosphereProfile.asset > Dust Storm > Gameplay**에서 타격을 설정한다. Impulse Interval은 기본 1초, Impulse Speed는 전진 순간 속도 증가량 18m/s, Impulse Lift는 위쪽 증가량 4m/s다. Lift를 0으로 하면 수평 충격만 준다. Player/Object Impulse Multiplier는 각각 기본 1이다. 프로필 설정은 Play 전에 조절한다.
- 같은 대상은 진입 시 한 번 맞고, 계속 범위 안에 있으면 간격마다 다시 맞는다. 이탈·재진입해도 간격이 지나기 전에는 중복 타격하지 않는다. 여러 콜라이더가 있는 물체와 플레이어 팔다리는 중복 타격을 막는다.
- 폭풍 자체는 중력 없는 kinematic Rigidbody + Trigger BoxCollider이며 충돌로 물체를 막거나 방향을 바꾸지 않는다. 상호작용 레이를 가리지 않는 Ignore Raycast 레이어를 사용한다. 기존의 보이지 않는 충돌 파편도 생성하지 않는다.
- 힘은 권한을 가진 서버/로컬 시뮬레이션에서만 적용한다. 동적인 Grabbable/InteractableBase 물체와 플레이어가 대상이며, 고정·kinematic 물체와 장식용 Rigidbody는 제외한다. 질량에 따른 약화를 없앤 속도 충격으로 무거운 운반 부품도 밀 수 있다. 플레이어는 기존 KnockDown을 사용해 걷기 제어가 충격을 지우지 않게 한다.

기존 windForce·fragment 계열 프로필 값은 이전 에셋 호환을 위해 남아 있지만 자기장 타격에는 사용하지 않는다. 이동·타격 변경 검증 결과는 `artifacts/stage1-magnetic-storm/sweep/tests.xml`에 기록한다. 실제 다중 접속 검증은 별도다.

검증 완료: 기존 스케줄/표시 17개 + 신규 스윕/투명도/실제 물리 타격 3개, 총 20개 모두 통과. 500kg 상호작용 물체, 플레이어 팔다리 동일 충격 및 넘어짐, 1초 재타격과 0.5초 중복 방지, 비상호작용/kinematic/비권한 제외, Trigger 상태를 확인했다. 수정된 투명도에서도 0초와 6초 렌더 픽셀 차이는 0이다.
