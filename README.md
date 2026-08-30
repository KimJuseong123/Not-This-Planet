# METAGAME

Unity 3D 멀티플레이 게임 프로젝트입니다. 실제 Unity 프로젝트는 저장소 루트가 아닌 **`METAGAME/`** 폴더에 있고, 루트에는 백엔드(`backend/`), 팀 문서(`docs/`), CI·러너 스크립트(`scripts/`)가 함께 있습니다.


## 처음 오셨나요

| 알고 싶은 것 | 어디로 |
| --- | --- |
| **이 팀은 어떻게 일하는가** — 규칙 한 장 | [docs/conventions.md](docs/conventions.md) |
| **이슈 하나가 완료되기까지** — 단계별 흐름과 자동화 | [docs/workflow.md](docs/workflow.md) |
| **지금 프로젝트를 실행해 보고 싶다** | 아래 [빠른 시작](#빠른-시작) |
| **문서 전체 색인** — 지금 하려는 일별로 | [docs/README.md](docs/README.md) |

클론한 뒤 **한 번만** 실행해야 하는 설정이 있습니다(아래 빠른 시작). 하지 않으면 push 가 규칙 검사를 건너뛰고 LFS 가 어긋납니다.

실제 Unity 프로젝트는 저장소 루트가 아닌 `METAGAME/` 폴더에 있습니다. 루트에는 그 밖에 백엔드(`backend/`), 팀 문서(`docs/`), CI·러너 스크립트(`scripts/`)가 함께 있습니다.

**처음 왔다면** [docs/README.md](docs/README.md) 를 먼저 보세요 — 지금 하려는 일별로 무엇을 읽으면 되는지 정리돼 있습니다. 클론 후 `git config core.hooksPath .githooks` 와 `git lfs install --skip-repo` 를 한 번 실행해야 합니다.


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

`git lfs update --force` 는 **실행하지 마세요** — `.githooks/pre-push` 를 지웁니다.

### 프로젝트 열기

Unity Hub 에서 `<S15P21B202>/METAGAME` 을 프로젝트로 추가합니다. 저장소 루트가 아니라 `Assets/`·`Packages/`·`ProjectSettings/` 를 직접 포함하는 **`METAGAME/`** 폴더를 선택해야 합니다.

처음 열면 `Packages/manifest.json`·`packages-lock.json` 기준으로 패키지를 복원하고 각자의 PC 에 `Library/` 를 새로 만듭니다. 그다음:

1. 패키지 복원과 에셋 임포트가 끝날 때까지 기다립니다.
2. Console 에 새 오류가 없는지 확인합니다.
3. `Assets/_Project/Scenes/Surface.unity` 를 엽니다.
4. File → Build Profiles 에서 빌드 씬이 올바르게 등록되어 있는지 확인합니다.

## 개발 규칙 (요약)

전체는 [docs/conventions.md](docs/conventions.md) 한 장에 있습니다. 자주 쓰는 것만:

| 무엇 | 형식 | 예 |
| --- | --- | --- |
| 브랜치 | `<이슈키>-<번호>-<영문-설명>` — Jira 이슈 화면 "브랜치 만들기" 로 생성 | `S15P21B202-12-player-jump` |
| 커밋 | `<type>(<scope>): <요약> (<이슈키>)` | `feat(player): 이단 점프 구현 (S15P21B202-12)` |
| MR | 대상 `develop` · 제목 `[파트] type: 요약 (이슈키)`(CI 가 교정) · **팀장이 머지** | `[Network] feat: 로비 매치메이킹 (S15P21B202-31)` |

- **Jira 우선** — 코드·에셋·문서 어느 것이든 Jira 이슈에서 시작합니다. 이슈 키가 없는 브랜치 이름은 로컬 훅이 push 를 막고, 우회해도 MR 검사가 실패합니다.
- **`master`·`develop` 에 직접 push 하지 않습니다.** 문서·설정 변경도 작업 브랜치 → MR 로 올립니다.
- 상세: 브랜치·커밋·MR은 [docs/git-convention.md](docs/git-convention.md) · 이슈 작성은 [docs/jira-convention.md](docs/jira-convention.md) · **Unity·C# 작성 규칙은 [docs/unity-convention.md](docs/unity-convention.md)**

## 리포 구조

~~~text
.
<<<<<<< README.md
├── .gitignore  .gitattributes
├── .githooks/            push 전 로컬 검사 훅
├── .gitlab/              Merge Request 템플릿
├── .gitlab-ci.yml        MR·스케줄 파이프라인 정의
├── docs/                 팀 문서 (색인: docs/README.md)
├── scripts/              CI 검사·알림(ci/) · 러너 설치(runner/)
├── backend/              서버 코드
=======
├── .gitignore
├── .gitattributes
├── .githooks/
├── .gitlab/
├── .gitlab-ci.yml
├── docs/
├── scripts/
├── backend/
>>>>>>> README.md
├── README.md
└── METAGAME/             Unity 프로젝트
    ├── Assets/  Packages/  ProjectSettings/
    ├── Docs/AI/          프로젝트 구조·현재 상태 기록
    └── .vscode/          공유 디버깅·편집기 설정
~~~

| 경로 | 설명 |
| --- | --- |
<<<<<<< README.md
| `METAGAME/Assets/` | 씬·프리팹·스크립트·아트·오디오. 우리가 만든 것은 `_Project/` 아래 |
| `METAGAME/Packages/` | Package Manager 매니페스트와 잠금 파일 |
| `METAGAME/ProjectSettings/` | Unity 버전·입력·렌더링·빌드 설정 |
| `METAGAME/Docs/AI/` | [Unity 프로젝트 컨텍스트](METAGAME/Docs/AI/UnityProjectContext.md) — 구조와 현재 상태 |
| `.gitattributes` | 대용량 바이너리의 LFS 규칙과 Unity YAML 텍스트 규칙 |
| `.githooks/` | 브랜치명 검사 + `develop` 최신 자동 반영 후 `git lfs pre-push` 실행 |
| `.gitlab-ci.yml` | 어떤 검사가 언제 도는지 — 상세는 [docs/ci-and-notifications.md](docs/ci-and-notifications.md) |
| `scripts/` | CI 검사·알림 스크립트와 그 오프라인 테스트, 러너 설치 스크립트 |
| `docs/` | 워크플로우·컨벤션·자동화 명세·작업 기록 |

**루트에 없어야 하는 것**: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, 생성된 `.csproj`·`.sln`, 빌드 산출물, 개인 편집기 설정(`.vs/`, `.idea/`), OS 잡파일(`.DS_Store`, `Thumbs.db`). 각자의 PC 나 빌드 환경에서 생성되는 것들이라 커밋하지 않으며, 파일 위생 검사가 MR 에서 막습니다. 반대로 Unity 에셋의 `.meta` 는 GUID 보존에 필요하므로 **반드시 함께 커밋**합니다.
=======
| METAGAME/Assets/ | 씬, 프리팹, 스크립트, 아트, 오디오 등 Unity 에셋 |
| METAGAME/Packages/ | Package Manager 매니페스트와 잠금 파일 |
| METAGAME/ProjectSettings/ | Unity 버전, 입력, 렌더링, 빌드 등 프로젝트 설정 |
| METAGAME/Docs/AI/ | 프로젝트 구조와 현재 상태를 기록한 문서 |
| METAGAME/.vscode/ | 공유 가능한 Unity 디버깅 및 편집기 설정 |
| .gitignore | 저장소 전체에 적용되는 Unity/IDE/빌드 산출물 제외 규칙 |
| .gitattributes | 대용량 바이너리 에셋의 Git LFS 및 Unity YAML 텍스트 규칙 |
| .githooks/ | 브랜치명에 Jira 이슈 키가 없으면 push를 막고, 이어서 git-lfs 훅을 실행하는 pre-push 훅 (`git config core.hooksPath .githooks`로 활성화) |
| .gitlab/ | Merge Request 템플릿 (MR 생성 시 자동 적용) |
| .gitlab-ci.yml | MR·스케줄 파이프라인 정의 — 어떤 검사가 언제 도는지 |
| scripts/ | CI 검사·알림 스크립트(`scripts/ci/`, 오프라인 테스트 포함)와 러너 설치 스크립트(`scripts/runner/`) |
| backend/ | 서버 코드 |
| docs/ | 팀 문서 — 워크플로우·컨벤션·자동화 명세·작업 기록. 색인은 [docs/README.md](docs/README.md) |

**루트에 없어야 하는 것**: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, 생성된 `.csproj`·`.sln`, 빌드 산출물, 개인 편집기 설정(`.vs/`, `.idea/`), OS 잡파일(`.DS_Store`, `Thumbs.db`) — 커밋하지 않습니다(파일 위생 검사가 MR 에서 막습니다). 이 파일들은 각자의 PC 또는 빌드 환경에서 생성됩니다. Unity 에셋의 .meta 파일은 GUID 보존에 필요하므로 반드시 함께 커밋합니다.
>>>>>>> README.md

## 현재 프로젝트 기준

- Unity **6000.5.4f1** · 렌더 파이프라인 URP 17.5.0 · 입력 Input System 1.19.0
- 주요 패키지: AI Navigation, uGUI, Timeline, Visual Scripting, Unity Test Framework
- 활성 빌드 씬: `Assets/_Project/Scenes/Surface.unity`
- 어셈블리: `Metagame.Core` → `Gameplay` → `UI` → `Bootstrap` (+ `Editor`)
- 상태: SpaceFlop 프로토타입 코드·에셋이 `Assets/_Project/` 아래에 들어와 있고, 프로젝트 테스트는 아직 없음

자세한 상태와 확인 근거는 [Unity 프로젝트 컨텍스트](METAGAME/Docs/AI/UnityProjectContext.md).

## 문서를 고칠 때

프로젝트 구조나 핵심 규칙이 바뀌면 이 README 와 [Unity 프로젝트 컨텍스트](METAGAME/Docs/AI/UnityProjectContext.md)를 함께 갱신합니다. 협업 규칙의 상세 기준은 [docs/](docs/README.md) 이며, **README 요약과 어긋나면 `docs/` 를 따르고 README 를 고칩니다.**
