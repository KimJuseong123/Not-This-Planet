# 핵심 파츠 원래 위치의 beacon 플라즈마 안내 VFX

## 적용

- `AssemblyGuidanceLight.Start`가 같은 장면의 기존 `Module beacon · visual guide` 세 개와 `02 Tractor sky beacon` 한 개를 찾아 `PartGuidanceVfx`를 연결한다. Stage1_Astra에서 Inspector 작업은 없다. 대상은 파츠 머리 위가 아니라 파츠가 원래 있던 장소의 beacon이다.
- 기존 beacon MeshRenderer의 전용 HDR 재질, 완만하게 맥동하는 링, 부드러운 플라즈마 광채와 최대 24개의 상승 입자를 표시한다. 광채와 입자는 용접 건의 `Resources/WeldingParticle` 셰이더를 그대로 사용한다.
- 기존 beacon의 월드 bounds로 광선의 시작점과 끝점을 정한다. 기존 위치·전체 높이·색상을 유지한다. 파츠 위치/상태를 읽지 않으므로 파츠가 이동·도킹·용접·파괴되어도 beacon은 원래 장소에서 계속 표시된다.
- 추가 효과는 안내 오브젝트 아래에 생성한다. 기존 beacon에는 재질 인스턴스만 적용하고 컴포넌트 비활성화/파괴 시 원래 재질로 복원한다. beacon 자체가 비활성화/파괴되면 추가 효과도 숨긴다.
- 파츠의 Collider/Renderer 계층, 질량, 레이어, 관절, 네트워크 상태, 용접 자원과 입력을 변경하지 않는다. 월드 깊이 판정을 유지한다.

## 기존 표시가 약했던 이유

- 경로 재질은 URP Unlit 기본 색상이며 HDR 발광 설정이 없다.
- 고정못 연결선의 `GuidanceNoFog`는 `_MaxBrightness` 기본값이 1이고 셰이더가 출력 밝기를 제한한다. `ConceptVolume`의 Bloom 임계값은 1.1, 강도는 0.25다. 이 밝기 제한은 강한 Bloom에 불리하다.
- beacon 전용 `GuidanceNoFog` 재질 인스턴스의 밝기 상한을 3으로 올렸다. 용접 건·기존 경로의 공유 재질과 전역 Volume 설정을 바꾸지 않았다. 기존 기둥과 additive 광채는 Bloom이 약해도 위치 단서로 남는다.
- Emission은 표면의 출력 밝기이고 Bloom은 밝은 픽셀 주변의 번짐이다. 카메라 HDR/후처리와 유효 Volume이 함께 필요하다. 현재 `AuthoredMapSettings.ConfigureCamera`가 HDR/후처리를 켜고 PC/Mobile RPAsset 모두 HDR을 지원한다.

## 검증

- 기준: 커밋 `796af968`, 시작 시 작업 트리 깨끗함. Unity `6000.5.4f1`, URP `17.5.0`.
- 원본을 열어 저장하지 않고 `outputs/tractor-tests` 격리 복사본에서 검증했다.
- EditMode 116/116 통과: PartGuidanceVfxTests, PlasmaWeldingToolTests, WeldBeadTests, WeldBeadJoinTests, WeldBeadRestoreTests, TractorBeamTests, AssemblyFloorStateTests, PartStateTests.
- 신규 검사는 기존 beacon 전체 높이 사용, 파츠 이동/도킹/파괴 시 고정 위치와 표시 유지, 공유 재질 불변, 물리 값 불변, beacon 파괴/비활성화, 중복 구성 방지와 재질 복원을 다룬다. EditMode 생명주기 검사는 reflection으로 콜백을 명시 호출한다.
- 결과: `.local-validation/part-guidance.xml`, 컴파일/검사 로그: `.local-validation/part-guidance-retry.log`.
- 최신 Play Mode 시각 검사에서 기존 beacon 네 개의 재질 교체 및 파츠를 이동해도 광선 위치/표시 유지 검사가 통과했다. 접근 시점 네 장과 전체 장면을 렌더링하고 시각적으로 확인했다: `.local-validation/beacon-guidance-visual/`. 로그는 `.local-validation/part-guidance-visual.log`의 `BEACON_SMOKE_PASS`.

## 한계와 별도 관찰

- 실제 두 클라이언트 동시 플레이, 타깃 플레이어 빌드, GPU 성능 측정은 실행하지 않았다. 자동 검사 통과가 모든 멀티플레이 충돌의 부재를 보장하지는 않는다.
- 최초 시각 검사에서는 에디터 초기화 직후 Play Mode 진입과 `UrpProjectSetupAutoRunner`의 장면 저장 API가 충돌했다. 수정한 하네스는 설정 적용을 EditMode에서 먼저 완료한다. Unity Search 초기 인덱싱 예외와 기존 에디터/자산 경고는 별도 관찰 대상이다.
- 장면 로드에서 기존 debris prefab 참조 타입 경고 및 일부 convex mesh 256폴리곤 제한 경고가 관찰됐다. 이번 변경은 해당 자산을 수정하지 않았다.
- 초기 신규 검사 실패는 정확한 float 비교 및 EditMode 콜백 호출 방식이었다. 허용 오차 및 프로젝트의 reflection 검사 방식으로 수정한 뒤 통과했다. 기존 112개 검사는 첫 실행부터 통과했다.

## 파일

- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/AssemblyGuidanceLight.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartGuidanceVfx.cs`와 `.meta`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/PartGuidanceVfxTests.cs`와 `.meta`

참고: [Unity URP Bloom](https://docs.unity3d.com/kr/Packages/com.unity.render-pipelines.universal%4014.0/manual/post-processing-bloom.html).
