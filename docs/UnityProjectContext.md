# Unity Project Context

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

Stage1_Astra 상호작용 물체의 가려진 윤곽을 로컬 카메라에만 표시한다. OccludedInteractionRendererFeature는 PC/Mobile 렌더러에 등록되어 있고, 전용 Resources 셰이더와 0.5초 갱신 대상 캐시를 사용한다. 도구/파츠/고정못/잡을 수 있는 물체를 포함하며 플레이어와 휴대 중인 도구는 제외한다. 씬/프리팹 수정은 없다. 구현 및 검증 범위는 docs/records/2026-09-17-occluded-interaction-outline.md 참고.