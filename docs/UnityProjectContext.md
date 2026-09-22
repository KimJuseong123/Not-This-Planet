# Unity Project Context

## 2026-09-22 팀 생존 HUD (682)

`GameHud.TeamStatus`는 왼쪽 위에 현재 참가자(자신 포함)의 원형 실제 모델 초상화, 팀 색 닉네임 및 생존/쓰러짐 심장을 표시한다. `TeamStatusRoster`는 기존 NetPlayer의 동기화된 신원·체력·downed를 읽고, `TeamPortrait`는 게임 카메라와 원본 렌더러를 바꾸지 않고 이미지를 캐시한다. `Stage2Hud`는 공통 HUD 높이를 환산해 상단 안내를 아래로 옮긴다. 씬/프리팹 설정은 없다. 관련 47개 및 최종 구도 재검사 8개 통과; 다인 플레이는 미검증. [작업 기록](records/2026-09-22-682-team-status-hud.md).

## 2026-09-21 컷씬 스킵 (666)

`EndingSceneFlow`가 소유하는 `CutsceneSkipFlow`는 Stage1 입장/공세 및 엔딩 각 구간에 오른쪽 아래 SKIP 투표 UI를 제공한다. 서버가 현재 인증 연결의 만장일치를 판정하며, `CutsceneSkipVotes`는 중복/구간이 지난 표를 거부한다. **1장 완료·플레이 타임을 표시하는 EndTitle은 스킵 불가 지점**이며 기존 3초를 유지한다. 씬/프리팹 연결 변경은 없다. 구현과 검증/기존 테스트 실패 구분은 [작업 기록](records/2026-09-21-666-cutscene-skip.md)을 참고한다.

<!-- unity-onboarding:generated:start -->

## 기준

- 2026-09-14, develop 74f7116. Unity 프로젝트는 저장소의 `METAGAME/`.
- Unity 6000.5.4f1, URP 17.5.0, Unity Test Framework 1.7.0. Windows 클라이언트.
- Input System 패키지가 있으나 실제 이동 입력은 Core/Controls.cs의 UnityEngine.Input을 사용한다.
- Mirror는 Assets/_Project/ThirdParty에 있으며 게임 플레이 상태 동기화에 실제 사용된다.

## 구조와 실행

- Runtime/Core → Gameplay → UI → Bootstrap 방향의 의존성을 유지한다. Network는 별도 어셈블리이며 Bootstrap에서 조합한다.
- SpaceBootstrap이 GameSession·PlayerRig·카메라·HUD·환경 및 서버 어댑터를 구성한다.
- LobbyBootstrap은 Spring 방 관리 후 GAME_STARTED에서 Mirror 게임 서버에 접속시킨다. 기존 로비는 조작 가능한 대기 공간이 아니다.
- 기존 빌드 씬: Lobby, Main, Stage1_Astra, Stage1_Ending, Customizer. SpaceBuild와 StageCatalog가 빌드 경로를 보장한다.
- PracticeLobby도 Stage1과 동일한 SpaceBootstrap으로 실행한다. 손 설정 및 도구/고정못 객체 구성은 Stage1_Astra에서 가져오며, 실내 중력만 -9.81이다. PracticeLobbyBootstrap은 연습 안내와 출발 연결만 담당한다. 다인 매치 진입 경로에는 아직 연결하지 않는다.
- 시각 에셋은 Prefabs/Props, Prefabs/Spaceships 및 Resources 참조를 사용한다. 본 게임 도구는 ToolBase를 통해 PlayerRig 입력을 받는다.

## 규칙과 검증

- 2026-09-17: `Stage1_Astra`는 `Stage1CutsceneFlow`로 실제 월드 위에 `Assets/Scenes/Stage1_EntryCutscene.unity` / `Stage1_AssaultCutscene.unity`를 가산 로드한다. 입장 후 주차대의 `Stage1AssaultTrigger`가 공세를 시작한다. `Stage1Sequence` 상태는 기존 WorldSync에 추가한 `Stage1SequenceSync`로 복제한다. DevManager F10/F9는 호스트/솔로 테스트 경로다. 상세와 검증은 `docs/records/2026-09-17-stage1-entry-assault-cutscenes.md`.

- docs/unity-convention.md, jira-convention.md, git-convention.md를 따른다. Jira Task당 브랜치/MR 하나. 새 씬은 합의한 Assets/Scenes에 추가한다.
- 기존 씬·프리팹을 덮어쓰지 않는다. .meta 포함, 바이너리는 LFS, Editor 코드는 Editor 어셈블리.
- Tests/Editor/Metagame.Gameplay.Tests.Editor.asmdef에서 EditMode 및 EnterPlayMode 기반 검사를 사용한다.
- Unity MCP 도구는 제공되지만 검사 시 연결 인스턴스는 0개였다. 새 폴더에 MCP 패키지를 임의 설치하지 않는다.
- 이 작업의 에디터 검사 대안은 Unity 배치모드이다. 런타임 및 빌드 검증 범위는 작업 기록에 별도로 남긴다.

## 근거

ProjectVersion.txt, Packages/manifest.json, EditorBuildSettings.asset, 어셈블리 정의, SpaceBootstrap.cs, LobbyBootstrap.cs, LobbyTransition.cs, GameSession.cs, Controls.cs, MeteorBat.cs, PlasmaWeldingTool.cs, SpaceshipPart.cs, PartClamp.cs, SpaceBuild.cs.

<!-- unity-onboarding:generated:end -->


## 2026-09-17 Stage1 가림 테두리

Stage1_Astra 상호작용 물체의 가려진 윤곽을 로컬 카메라에만 표시한다. OccludedInteractionRendererFeature는 PC/Mobile 렌더러에 등록되어 있고, 전용 Resources 셰이더와 0.5초 갱신 대상 캐시를 사용한다. S15P21B202-633에서 대상을 방망이(MeteorBat), 용접 건(PlasmaWeldingTool), PartSpec에 등록된 핵심 파츠 1~4로 제한했다. 고정못(파츠 아래 자식 포함), 식생, 환경 및 일반 잡기 오브젝트는 제외하며 플레이어와 휴대 중인 도구는 제외한다. 씬/프리팹 수정은 없다. 구현 및 검증 범위는 docs/records/2026-09-17-occluded-interaction-outline.md 참고.

## 2026-09-17 Stage2_Earth 초안 (`603`, 기준 10bdab36)

- `Stage2_Earth`는 같은 SpaceBootstrap/PlayerRig/GameSession을 사용한다. Stage1 전용 용접·운석·태블릿·DemoEnding은 Stage2 목표에 사용하지 않는다.
- `Stage2Yard`가 크레인·로켓·사다리·승강기·오염의 권한 있는 상태를 소유하고, Network 어셈블리의 `Stage2Bridge`가 인증 연결 입력과 전체 스냅샷을 전달한다. Gameplay에서 Mirror를 참조하지 않는다.
- Stage1 WorldSync는 SpaceshipPart 전용이므로 Stage2에서는 설치 대기를 생략한다. 참가자 몸은 기존 NetPlayer, Stage2 장비는 별도 Stage2 스냅샷으로 동기화한다.
- `GameSession.StageInputFilter`로 Stage2 입력을 기존 조작 읽기 경로에서 소비한다. 원형 체결 동안 시점 회전을 막는다.
- Stage1 엔딩 종료 시 호스트가 같은 연결로 Stage2를 연다. StageCatalog와 EditorBuildSettings에 `stage2`가 등록된다.
- 도구는 초안용 장비대 장착 방식이며 기존 ToolBase의 운반·투척에 아직 통합하지 않았다. 수치·미정 채택·검증 근거는 [작업 기록](records/2026-09-17-stage2-earth-draft.md)에 남긴다.
- 현재 제공된 Unity MCP 호출은 없다. 지정 버전 Unity 배치모드로 씬 생성·컴파일·테스트·로컬 검증 빌드를 실행한다.

## 2026-09-21 Stage1 독립 운석 (661)

공세의 실제 게임 트리거는 주차대 진입에서 첫 고정못 완전 해제로 바뀌었다. Stage1Sequence가 서버/솔로의 해제 상태를 확인하고 기존 컷신 시계를 유지한다. Stage1 운석은 탐색 1개/공세 8개의 독립 1~5초 타이머를 한 MeteorSpawner에서 관리하며, 공세의 플레이어 주변 낙하는 기본 5m 원판 내 무작위 지점이다. 기존 시험장 프리셋과 WorldSync 복제 경로는 유지한다. 설정·검증 범위: [661 작업 기록](records/2026-09-21-661-independent-meteors.md).
