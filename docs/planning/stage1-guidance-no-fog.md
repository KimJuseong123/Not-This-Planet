# Stage1 Astra 유도등 안개 제외

- 셰이더: `METAGAME/Assets/_Project/Shaders/Resources/GuidanceNoFog.shader`
- 이름: `Metagame/Guidance/NoFog`
- 전용 재질: `METAGAME/Assets/_Project/Materials/Rendering/M_Guidance_NoFog_*.mat`
- 색상: Ion cyan, Route amber, Route lime, Signal orchid의 기존 Base Color 유지
- Emission Color는 각 Base Color와 동일하며 Emission Intensity는 1.25이며 Emission 체크박스는 켜져 있다.
  셰이더는 기본색 + 발광색 × 강도를 계산한 뒤 RGB 전체를 같은 비율로 줄여 최대 채널 밝기를 1로 제한한다.
- 적용: `Stage1_Astra.unity`의 `Module beacon · visual guide` 3개와
  `02 Tractor sky beacon` 1개. 원본 공용 재질은 변경하지 않는다.

URP 무조명 셰이더로 일반 Fog 계산을 하지 않는다. 깊이 판정과 깊이 쓰기는
유지하므로 가려진 유도등이 벽을 뚫고 보이는 방식은 아니다. 기본 불투명
오브젝트 다음에 렌더링하고, 기존 안개 제외 규약인 스텐실 비트 64를 쓴다.

화면 전체 Haze·Ground Fog도 제외하려면 스텐실 검사가 실제 카메라 깊이 버퍼를
사용해야 한다. `AtmosphereHazeRendererFeature`는 색상 복사본을 읽고 원래 색상과
깊이·스텐실 버퍼에 연결한 Raster Pass로 안개를 합성한다. 제외 픽셀의 원래
색상을 보존하며, 다른 불투명 오브젝트에는 기존 안개 처리가 적용된다.

## Emission과 Bloom

Astra에 이미 연결된 `ConceptVolume.asset`의 Bloom을 사용한다
(Threshold 1.1, Intensity 0.25, Scatter 0.65, 흰색 Tint).
흰색 Tint는 모든 유도등에 같은 색을 덧씌우지 않고 각 HDR 발광색을 유지한다.
PC URP의 HDR와 런타임 카메라의 HDR·후처리 활성화 경로를 그대로 사용한다.
재질 Inspector에서 Emission Color와 Emission Intensity를 조절할 수 있다.

### 색상 보존 수정

기존 기본색 + 3배 발광은 최대 밝기가 4까지 올라가 톤매핑 뒤 유도등 중심이
하얗거나 노랗게 보였다. 최대 밝기를 1로 제한한다.
채널별 clamp 대신 RGB 전체에 같은 배율을 적용해 원래 채널 비율을 보존한다.
글로벌 Bloom 임계값을 0.45로 낮추고 강도를 1.2로 높인 시도는 유도등 외의
밝은 표면에도 번짐을 적용했다. 전체 화면이 뿌옇다는 사용자 보고 후 글로벌
Bloom을 원래 값(임계값 1.1, 강도 0.25, Scatter 0.65)으로 되돌렸다.
따라서 아래 별도 유도등 샘플의 강한 Bloom 결과를 실제 씬 전체에 대한 검증으로
해석하면 안 된다. 유도등만 선별한 강한 번짐은 아직 별도 구현이 필요하다.

별도 Unity 6000.5.4f1 / URP 17.5.0 검증 장면에 실제 재질 4종과 Astra Volume을
연결해 렌더링했다. 청록·주황·연두·보라 중심색이 구분되고 주변 Bloom이 남는 것을
이미지로 확인했다. 결과: `.local-validation/guide-color/color-bloom.png`.
실제 Astra 전체 씬 화면 검증은 수행하지 않았다.

이전 Emission 3 설정 검증에서는 별도 Windows 빌드와 Fog 제외를 확인했지만, 과도한 발광으로 인한 색상 구분 저하는 검출하지 못했다. 아래는 당시 기록이다.
Astra의 실제 ConceptVolume과 URP 후처리 리소스를 연결한 빌드 플레이어에서
청록·주황·연두·보라 빛 번짐을 렌더링 이미지로 확인했다.
강한 안개 전후 유도등 중심 색상 차이는 0이었다.
이번 결과는 `check-bloom-initialized.log`, `player-bloom-initialized.log`와
`Build/before.png`, `Build/after.png`에 기록했다.

## 빌드 보호

### 실제 플레이 카메라 연결 수정

`OrbitCamera.Create`는 Camera만 생성하므로 URP 추가 컴포넌트가 없는 상태로
`AuthoredMapSettings.ConfigureCamera`에 들어올 수 있다. 기존 코드는 컴포넌트가
있을 때만 후처리를 켜서 실제 플레이에서 Bloom이 빠질 수 있었다.
이제 없는 컴포넌트는 생성하고 HDR와 Post Processing을 명시적으로 켠다.
반복 호출 시에도 URP 컴포넌트를 중복 생성하지 않는다.
`AuthoredMapSettingsTests.RuntimeCameraGetsPostProcessingAndHdrWithoutDuplicateComponents`에
회귀 테스트를 추가했다. 기존 별도 렌더링 검증은 카메라에 URP 컴포넌트를 직접
추가했기 때문에 이 통합 누락을 검출하지 못했다.

- 씬 → 재질 → 셰이더를 GUID로 직접 연결했다.
- 셰이더를 Resources 경로와 GraphicsSettings의 Always Included Shaders에 등록했다.
- `SpaceBuild`, `UrpProjectSetup`, `UrpRenderValidation`의 보호·검증 목록에도 등록했다.
- Built-in 셰이더 폴백에 의존하지 않는다. URP Forward 및 DepthOnly 패스를 제공한다.

## 검증 범위

실제 게임 전체의 빌드·화면 검증과 별도 셰이더 검증 프로젝트의 결과는 구분한다.
검증 프로젝트 및 로그는 `METAGAME/Temp/GuidanceShaderCheck/`에 저장한다.

- Unity 6000.5.4f1 / URP 17.5.0 / Windows x64 / Direct3D 11 검증 빌드 성공:
  `result.txt`의 `Succeeded errors=0 materials=4`.
- 실제 빌드 플레이어에서 같은 셰이더·재질·Haze Renderer Feature를 사용해
  GPU 렌더링했다. 강한 Haze·Ground Fog 전후 유도등 중심 픽셀의 최대 색상 차이는
  0, 배경 색상 변화는 0.4980392였으며 네 재질 모두 표시되고 셰이더가 지원되었다.
- `Build/render-result.txt`: `guideMaxColorDelta=0 visible=True backgroundDelta=0.4980392 pass=True`.
- `Build/before.png`, `Build/after.png` 및 `player-render.log`로 결과를 기록했다.
  렌더링 이미지를 확인했으며 네 재질에 핑크 오류가 없었다.
- 전체 게임 런타임 참조 어셈블리 컴파일 오류 0. 기존 경고 49개.
- 실제 Astra 전체 씬의 플레이·완성 게임 전체 빌드는 이번 검증에 포함하지 않았다.
  Android·다른 그래픽 API도 미검증이다.
