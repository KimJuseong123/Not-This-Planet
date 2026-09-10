# METAGAME · Not This Planet

행성 탐사대가 기지에 숨겨진 우주선 파츠를 찾아 운반하고, 쌓고, 용접해 행성을 탈출하는 Unity 3D 협동 멀티플레이 게임입니다. 운석이 떨어지는 동안 플레이어가 운반·용접·방어·구조를 나누어 맡습니다. 1장은 이 과정을 배우는 튜토리얼이며, 상세 기획의 지원 인원은 1~4명입니다.

Unity 프로젝트는 **`METAGAME/`**에 있습니다. 저장소 루트에는 백엔드, 팀 문서, CI·배포 스크립트도 함께 관리합니다.

## 처음 읽는 순서

1. [프로젝트 개요](docs/overview.md): 게임의 목표, 주요 구성 요소, 각 코드의 역할을 확인합니다.
2. 아래 [빠른 시작](#빠른-시작): 저장소를 설정하고 Unity 프로젝트를 실행합니다.
3. [개발 참여 규칙](docs/conventions.md) → [작업 흐름](docs/workflow.md): 이슈를 만들고 MR을 머지하는 과정을 익힙니다.

기획·개발·운영의 상세 문서는 [문서 안내](docs/README.md)에서 찾을 수 있습니다.

## 빠른 시작

### 요구 사항

- Git · Git LFS
- Unity Hub · Unity Editor **6000.5.4f1**
- 패키지 복원을 위한 인터넷 연결
- 선택: VS Code + Unity 디버깅 확장

### 클론과 1회 설정

~~~bash
git lfs install --skip-repo        # --skip-repo 필수 — 리포의 .githooks 를 덮어쓰지 않는다
git clone https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202.git
cd S15P21B202
git config core.hooksPath .githooks # push 전 브랜치명 검사 · develop 최신 자동 반영
~~~

`git lfs update --force`는 **실행하지 마세요** — `.githooks/pre-push`를 지웁니다.

### 프로젝트 열기

Unity Hub에서 `<S15P21B202>/METAGAME`을 프로젝트로 추가합니다. 저장소 루트가 아니라 `Assets/`·`Packages/`·`ProjectSettings/`를 직접 포함하는 **`METAGAME/`** 폴더를 선택해야 합니다.

처음 열면 `Packages/manifest.json`·`packages-lock.json` 기준으로 패키지를 복원하고 각자의 PC에 `Library/`를 새로 만듭니다. 그다음:

1. 패키지 복원과 에셋 임포트가 끝날 때까지 기다립니다.
2. Console에 새 오류가 없는지 확인합니다.
3. `Assets/Scenes/Lobby.unity`를 열어 로비부터 실행합니다. 게임 씬은 `Assets/Scenes/Main.unity`입니다.
4. File → Build Profiles에서 `Lobby.unity` → `Main.unity` 순서로 두 씬이 활성화되어 있는지 확인합니다.

## 개발 규칙 요약

| 작업 | 규칙 | 예시 |
| --- | --- | --- |
| 이슈 | 코드·에셋·문서 작업 모두 Jira 이슈에서 시작 | `[UI] 태블릿 조립 상태 표시` |
| 브랜치 | Jira의 “브랜치 만들기”에서 생성. 이슈 키 뒤에 영문 설명을 붙임 | `S15P21B202-12-player-jump` |
| 커밋 | `<type>(<scope>): <요약> (<이슈키>)` | `feat(player): 이단 점프 구현 (S15P21B202-12)` |
| MR | 기본 대상은 `develop`, 제목은 CI가 보정, 팀장이 리뷰 후 머지 | `[Network] feat: 로비 매치메이킹 (S15P21B202-31)` |

`master`·`develop`에 직접 push하지 않습니다. 문서·설정 변경도 작업 브랜치와 MR을 거칩니다. 이슈 키가 없는 브랜치는 로컬 훅이 push를 막고, 훅을 사용하지 않아도 MR 검사에서 확인합니다.

상세 기준: [Jira](docs/jira-convention.md) · [Git](docs/git-convention.md) · [Unity·C#](docs/unity-convention.md) · [MR 제출과 머지](docs/mr-flow.md).

## 저장소 구조

```text
.
├── METAGAME/             Unity 프로젝트
│   ├── Assets/          씬·스크립트·프리팹·아트·오디오
│   ├── Packages/        패키지 매니페스트·잠금 파일
│   ├── ProjectSettings/ Unity·입력·렌더링·빌드 설정
│   ├── Docs/AI/         Unity 구조·상태 기록
│   └── .vscode/         공유 디버깅·편집기 설정
├── backend/             서버 코드
├── docs/                기획·개발·운영 문서와 작업 기록
├── scripts/             CI·러너·배포·릴레이·문서 생성 스크립트
├── .githooks/           push 전 검사와 Git LFS 연계
├── .gitlab/             MR 템플릿
├── .gitlab-ci.yml        파이프라인 정의
├── .gitattributes        Git LFS·Unity YAML 텍스트 규칙
├── .gitignore           Unity·IDE·빌드 생성물 제외 규칙
└── README.md
```

새로 만드는 Unity 에셋은 `METAGAME/Assets/_Project/`에 둡니다. `.githooks/pre-push`는 브랜치 이름을 검사하고 필요한 경우 최신 `develop`을 반영한 뒤 `git lfs pre-push`를 실행합니다. `scripts/ci/`에는 검사·알림 스크립트와 오프라인 테스트, `scripts/runner/`에는 러너 설치 스크립트가 있습니다. 파이프라인의 실행 조건은 [CI/CD 구조](docs/ci-cd-pipeline.md)를 참고합니다.

`Library/`, `Temp/`, `Logs/`, `UserSettings/`, 생성된 `.csproj`·`.sln`, 빌드 산출물, 개인 IDE 설정(`.vs/`, `.idea/`), OS 생성 파일(`.DS_Store`, `Thumbs.db`)은 커밋하지 않습니다. 각 PC 또는 빌드 환경에서 생성하는 파일이며 MR의 파일 위생 검사 대상입니다. Unity 에셋의 **`.meta`는 GUID 보존을 위해 에셋과 함께 커밋**합니다.

## 현재 프로젝트 기준

- Unity **6000.5.4f1**, URP **17.5.0**, Input System **1.19.0**
- 주요 패키지: AI Navigation, uGUI, Timeline, Visual Scripting, Unity Test Framework
- 활성 빌드 씬: `Assets/Scenes/Lobby.unity` → `Assets/Scenes/Main.unity`
- 어셈블리: `Metagame.Core` · `Gameplay` · `UI` · `Bootstrap` · `Editor`. 참조 방향은 [Unity 작성 규칙](docs/unity-convention.md)을 따릅니다.
- SpaceFlop 프로토타입 코드·에셋과 테스트를 바탕으로 개발 중입니다. 기능별 검증 결과와 미확인 사항은 [작업 기록](docs/records/README.md)에 있습니다.

Unity 버전과 활성 씬은 `ProjectSettings/ProjectVersion.txt`와 `EditorBuildSettings.asset`를 기준으로 확인했습니다. 상세 구조·상태 기록은 [Unity 프로젝트 컨텍스트](METAGAME/Docs/AI/UnityProjectContext.md)에서 확인합니다.

## 문서 갱신

프로젝트 구조나 핵심 규칙이 바뀌면 이 README와 [Unity 프로젝트 컨텍스트](METAGAME/Docs/AI/UnityProjectContext.md)를 함께 갱신합니다. 협업 규칙은 [문서 안내](docs/README.md)에 연결된 상세 문서를 기준으로 합니다. README 요약과 다르면 상세 문서를 확인해 요약을 수정합니다.

초기 README의 `Surface.unity` 안내와 “프로젝트 테스트 없음”은 현재 상태와 달라 [초기 상태 기록](docs/overview.md#초기-readme의-상태-기록)으로 옮겼습니다.
