# Stage1 입장 / 공세 전환 컷씬

## 실행

- `METAGAME/Assets/Scenes/Stage1_EntryCutscene.unity`: 입장, 18초.
- `METAGAME/Assets/Scenes/Stage1_AssaultCutscene.unity`: 공세 경고와 전환, 8초.
- 두 씬을 각각 열고 Play하면 실제 `Stage1_Astra`를 가산 로드하여 실행한다.
- Stage1의 `SpaceBootstrap - develop gameplay` / `DevManager`: **F10 입장 재생**, **F9 공세 전환**. Editor / Development Build 전용이며 멀티에서는 호스트가 실행한다. 재생 중 중복 요청은 무시한다.
- 정상 Lobby/Customizer → Stage1 진입에서는 월드와 플레이어를 준비한 뒤 입장 컷씬을 먼저 보여주고 조작을 연다.

## 연출과 실제 월드

입장 샷은 현재 참가자와 커마, 고개를 들어 보는 유성우, 기지 옆 작은 운석 충돌, 소등과 고정못·조립대 강조, 실제 1인칭 태블릿 순서다. 기존 Stage1의 시작 위치를 유지한다. 입장 운석 하나는 안전한 연출용이며 실제 운석 아트와 궤적·충돌 이펙트를 사용한다.

컷씬 씬에는 샷 설정과 프레젠터만 저장한다. 카메라는 실제 로컬 카메라를 사용하고, 별도 대역 캐릭터나 복사된 맵을 만들지 않는다. 런타임 생성 플레이어, 커마, 파츠, 폭풍, 유성우, 물체, 운석은 같은 월드에 남는다. 시간 배율이나 월드 물리를 정지하지 않는다. 종료 시 카메라, HUD, 조명, 태블릿 상태를 복구한다.

## 공세 트리거 / 난이도

아직 위치가 지정되지 않아 **견인 모듈 주차대**에 `Stage1 Assault Trigger - Tractor Dock`을 배치했다. 중심 `(23, 2.33, -19)`, BoxCollider 크기 `(6, 4, 6)`. 위치와 크기는 Inspector에서 수정할 수 있다. 살아 있는 플레이어 누구든 진입하면 서버가 한 번만 공세 전환을 시작한다.

공세 컷씬은 5초 동안 유성우 증가, 경고음, 기지 조명 점멸을 보여준다. 5초 시점부터 기존 `MeteorSpawner`를 강화하고 실제 운석 낙하를 촬영한다. 배율은 `MeteorSpawner` Inspector의 `Assault Wave Interval Multiplier` / `Assault Within Wave Interval Multiplier`에 노출했다. 기본값은 **0.35 / 0.5**로, [기획 §17.8](../planning/chapter-1-detailed-design.md)에 개발 검증용 제안값임을 기록했다. 초기 공세 간격은 30초에서 10.5초로 줄어든다. 물리 낙하속도와 착탄 예고 시간은 바꾸지 않는다.

사용자의 명시적 트리거 요구에 따라 트리거 전에는 공격 운석의 자동 생성을 보류한다. 따라서 기획 §6.2의 ‘45초 후 자동 첫 공격’ 대신 트리거가 공격 시작을 결정한다. 배경 유성우와 폭풍 및 기존 물체는 계속 동작한다. 이미 존재하는 운석이나 흔적을 지우지 않으며 파츠/플레이어를 재생성하지 않는다.

`Stage1Sequence`가 단계·시각·공세 여부를 소유하고 `Stage1SequenceSync`가 기존 WorldSync 신원을 통해 복제한다. 늦참자는 서버의 현재 시점으로 진입한다. 서버 입력도 차단하며 컷씬 중 개인 복구 및 맵 재시작을 막는다. 기존 전원 기절 복구는 컷씬을 다시 실행하지 않는다.

## 검증

리뷰 보완 후 재검증: 관련 테스트 **37/37 통과**. 기존 기본 배율, 사용자 지정 배율, 1배와 하한값, 실제 씬의 트리거 전환, 카메라·입력 복구, 전용 유성우 제거를 포함한다. [테스트 결과](../../artifacts/stage1-cutscenes/review-tests.xml)와 [재현 절차·움직이는 캡처](../../artifacts/stage1-cutscenes/README.md)를 저장했다. 아래는 최초 구현 시 검증 기록이다.

- Unity 6000.5.4f1 격리 프로젝트에서 씬 생성 및 전체 스크립트 컴파일 성공.
- `Stage1CutsceneTests`와 `MeteorSpawnerTests`: **26/26 통과**.
- 실제 Stage1을 로드하는 테스트에서 입장 종료, 주차대 물리 트리거, 공세 종료, 플레이어/파츠 동일성, 활성 Stage1 유지, 카메라/입력 복구, F10 재생 경로 확인.
- 그래픽 Play Mode에서 실제 커마·태블릿과 두 컷씬의 화면을 캡처하여 확인. F9 경로, 종료 후 카메라 복구 및 공세 간격 10.5초 확인.
- 최종 증거: `.local-validation/astra-ver2/cutscene-tests-final.xml`, 같은 프로젝트의 `Validation/Cutscenes/`.
- 두 대 이상의 실제 네트워크 플레이와 배포용 Player 빌드는 미실행. 서버 권위/늦참 시각 적용은 상태 테스트로 검증했다.
- 그래픽 검증 시 기존 `UrpProjectSetupAutoRunner`의 Play Mode 도중 설정 저장 예외, 기존 폭풍 debris 프리팹 참조 경고가 관찰됐다. 컷씬 테스트에서는 새 오류가 없으며 이 작업에서 해당 기존 설정은 수정하지 않았다.

## 편집

각 컷씬 루트의 `Stage1CutsceneScene` / `Shots`에서 시간, 앵커, 시작/끝 오프셋, 시선 및 FOV를 수정한다. 소스 씬이나 커마 데이터를 교체할 필요가 없다. `Stage1CutsceneSetup.Create`는 누락된 씬만 생성하며 이미 편집된 샷을 덮어쓰지 않는다. 두 씬은 Build Settings와 `SpaceBuild` 필수 씬에 포함되어 있다.

## MR 범위 확인

- **562:** 별도 [MR !476](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/merge_requests/476)으로 이미 develop에 병합되었다(`aac8f2f1`). `origin/develop...S15P21B202-587-level` 비교에서 `PartOutline*`, `UrpProjectSetup*`, `UrpRenderValidation*` 변경은 없다. 따라서 이를 다시 제거하거나 중복 MR로 올리지 않는다. 컷씬은 아웃라인 피처를 수정하지 않고 기존 런타임 파츠를 촬영한다.
- **577:** 앞서 요청한 식생·지형 보존 병합(`bd20dd64`)을 유지한다. 이 때문에 develop 대비 diff에는 577 아트 변경이 포함된다. 577 선행 머지 또는 스택 MR 검토가 필요하며, 이를 587의 신규 기능이라고 보지 않는다. `78c02e1f`의 Stage1과 비교하면 컷씬에 필요한 변경은 공세 트리거 오브젝트와 SceneRoots 등록, DevManager F9/F10 활성화뿐이다. 새 씬을 보여주기 위해 원본 월드를 복제하거나 정지하지 않는다.
- **상시 유성우:** 587에서 `authoredStarsOnly`에 추가했던 상시 효과를 제거했다. 컷씬 씬이 전용 유성우를 소유하고 종료 시 제거한다. 기존 월드 유성우의 강도를 일괄 수정하거나 0.4로 덮어쓰지 않는다.
- **밸런스:** Jira 승인값으로 주장하지 않는다. §17.8의 제안값과 Inspector 설정으로 관리하며, 솔로·다인 플레이테스트에서 최종 수치를 확인해야 한다.
