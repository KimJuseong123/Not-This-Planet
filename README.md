# METAGAME

Unity 3D 게임 프로젝트입니다.

현재 저장소는 초기 프로젝트 상태이며, 실제 Unity 프로젝트는 저장소 루트가 아닌 METAGAME/ 폴더에 있습니다.

## 빠른 시작

### 요구 사항

- Git
- Git LFS
- Unity Hub
- Unity Editor 6000.5.4f1
- 패키지 복원을 위한 인터넷 연결
- 선택 사항: VS Code와 Unity 디버깅 확장

### clone 및 프로젝트 열기

~~~bash
git lfs install
git clone https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202.git
cd S15P21B202
~~~

Unity Hub에서 다음 경로를 프로젝트로 추가합니다.

~~~text
<S15P21B202>/METAGAME
~~~

저장소 루트인 <S15P21B202>가 아니라 Assets/, Packages/, ProjectSettings/를 직접 포함하는 METAGAME/ 폴더를 선택해야 합니다.

Unity 6000.5.4f1이 설치되어 있지 않다면 해당 버전을 먼저 설치합니다. 프로젝트를 처음 열면 Packages/manifest.json과 Packages/packages-lock.json을 기준으로 패키지를 복원하고, 각 개발자의 로컬 환경에 Library/ 폴더를 새로 생성합니다.

### 첫 실행 확인

1. 패키지 복원과 에셋 임포트가 완료될 때까지 기다립니다.
2. Console에 새 오류가 없는지 확인합니다.
3. Assets/Scenes/SampleScene.unity를 엽니다.
4. File > Build Profiles에서 빌드 씬이 올바르게 등록되어 있는지 확인합니다.

## 프로젝트 구성

~~~text
.
├── .gitignore
├── .gitattributes
├── .githooks/
├── .gitlab/
├── docs/
├── README.md
└── METAGAME/
    ├── Assets/
    ├── Packages/
    ├── ProjectSettings/
    ├── Docs/AI/
    └── .vscode/
~~~

| 경로 | 설명 |
| --- | --- |
| METAGAME/Assets/ | 씬, 프리팹, 스크립트, 아트, 오디오 등 Unity 에셋 |
| METAGAME/Packages/ | Package Manager 매니페스트와 잠금 파일 |
| METAGAME/ProjectSettings/ | Unity 버전, 입력, 렌더링, 빌드 등 프로젝트 설정 |
| METAGAME/Docs/AI/ | 프로젝트 구조와 현재 상태를 기록한 문서 |
| METAGAME/.vscode/ | 공유 가능한 Unity 디버깅 및 편집기 설정 |
| .gitignore | 저장소 전체에 적용되는 Unity/IDE/빌드 산출물 제외 규칙 |
| .gitattributes | 대용량 바이너리 에셋의 Git LFS 및 Unity YAML 텍스트 규칙 |
| .githooks/ | 브랜치명에 Jira 이슈 키가 없으면 push를 막는 pre-push 훅 (`git config core.hooksPath .githooks`로 활성화) |
| .gitlab/ | Merge Request 템플릿 |
| docs/ | 팀 컨벤션 문서 — 브랜치·Jira·자동화·파트 구분 ([docs/README.md](docs/README.md)) |

Library/, Temp/, Logs/, UserSettings/, 생성된 .csproj·.sln 파일과 빌드 결과물은 커밋하지 않습니다. 이 파일들은 각자의 PC 또는 빌드 환경에서 생성됩니다. Unity 에셋의 .meta 파일은 GUID 보존에 필요하므로 반드시 함께 커밋합니다.

## 현재 프로젝트 기준

- Unity: 6000.5.4f1
- 렌더 파이프라인: URP 17.5.0
- 입력: Input System 1.19.0
- 주요 패키지: AI Navigation, uGUI, Timeline, Visual Scripting, Unity Test Framework
- 현재 활성 빌드 씬: Assets/Scenes/SampleScene.unity
- 초기 폴더 스캐폴드: Assets/_Project/
- 초기 어셈블리 경계: Metagame.Core, Metagame.Gameplay, Metagame.UI
- 현재 상태: 게임플레이 코드와 프로젝트 테스트는 아직 없음

자세한 상태와 확인 근거는 [Unity 프로젝트 컨텍스트](METAGAME/Docs/AI/UnityProjectContext.md)를 참고합니다.

## 브랜치 규칙

브랜치 전략과 상세 규칙은 [docs/git-convention.md](docs/git-convention.md)를 기준으로 합니다. 요약:

| 브랜치 | 용도 |
| --- | --- |
| master | 항상 릴리즈 가능한 안정 버전. 마일스톤마다 버전 태그 |
| develop | 기능을 통합하는 기본 개발 브랜치. 항상 에디터에서 실행 가능해야 함 |
| `<JIRA키>-<번호>-<영문설명>` | Jira 이슈 단위 작업 브랜치 — **유일한 형식.** Jira 이슈 화면 "브랜치 만들기"로 생성 (설명부는 영문) |
| `hotfix/<JIRA키>-<번호>-<설명>` | master 긴급 수정 (접두어 필수, master에서 분기) |

~~~text
Jira 이슈 생성 → 이슈 화면 "브랜치 만들기" (소스 develop) → 작업 → Merge Request → develop
master ← 릴리즈 검증이 끝난 develop
~~~

- **Jira 우선:** 브랜치는 Jira 이슈에서 만듭니다. 이슈 키가 없는 이름은 GitLab 보호 브랜치 규칙이 **생성 자체를 거부**하고, 로컬 `.githooks/pre-push` 훅도 push 전에 막습니다. `feature/` 같은 접두어는 쓰지 않습니다.
  클론 후 한 번 `git config core.hooksPath .githooks`를 실행해 훅을 켭니다.
- master에 직접 작업하거나 강제 push하지 않습니다. 문서·설정 변경도 작업 브랜치 → develop MR로 올립니다.
- 작업 브랜치는 최신 develop에서 만들고, 1~3일 안에 작게 머지합니다.
- MR은 `.gitlab/merge_request_templates/Default.md` 템플릿(자동 적용)을 채우고, **팀장(Maintainer)이 승인·머지**합니다. develop 머지 권한은 Maintainer에게만 있습니다.

## 커밋 메시지 규칙

Conventional Commits 형식에 Jira 이슈 키를 덧붙입니다. 상세는 [docs/git-convention.md](docs/git-convention.md)를 참고합니다.

~~~text
<type>(<scope>): <summary> (<JIRA키>-<번호>)
~~~

- `<scope>`는 선택 사항입니다 (예: player, ui, network).
- 이슈 키는 브랜치 작업의 커밋에 붙입니다. GitLab-Jira 연동 시 커밋이 이슈에 링크됩니다.

| Type | 용도 |
| --- | --- |
| feat | 새 기능 |
| fix | 버그 수정 |
| asset | 아트·사운드·씬 등 에셋 추가/수정 (코드 변경 없음) |
| refactor | 동작 변경 없는 구조 개선 |
| docs | 문서 변경 |
| test | 테스트 추가 또는 수정 |
| perf | 성능 개선 |
| build | 빌드·패키지·플랫폼 설정 |
| chore | 기타 유지보수 |

예시:

~~~text
feat(player): add double jump (S15P21B202-12)
asset(lobby): 로비 배경 3D 모델 추가 (S15P21B202-45)
docs: add Unity project setup guide (S15P21B202-3)
chore: update gitignore
~~~

커밋은 하나의 논리적 변경 단위로 작게 유지합니다. 코드, 씬, 프리팹, 설정을 함께 바꿔야 기능이 완성되는 경우에는 하나의 커밋에 포함해도 되지만, 관련 없는 변경은 분리합니다.

## Unity 및 C# 작성 규칙

현재는 템플릿 코드만 있어 일부 규칙은 권장안입니다. 실제 게임 코드가 추가되면 팀 합의에 따라 확정합니다.

- 파일명과 주요 타입명은 일치시킵니다.
- 클래스, 메서드, 프로퍼티, enum은 PascalCase를 사용합니다.
- private 필드는 _camelCase를 사용합니다.
- Inspector에 노출할 런타임 필드는 가능한 한 public 대신 SerializeField private을 사용합니다.
- 런타임 코드에서 UnityEditor를 참조하지 않습니다. Editor 전용 코드는 Editor/ 폴더에 둡니다.
- 첫 번째 사용자 정의 네임스페이스는 Metagame.<Feature> 형태를 권장합니다.
- 기능 단위로 코드를 분리하고, 기능 간 참조가 커지면 .asmdef로 경계를 명확히 합니다.
- 테스트 코드는 Tests/Editor/ 또는 Tests/Runtime/에 분리합니다.
- Unity Editor 밖에서 파일을 이동·복사·이름 변경하지 말고, 에셋 변경 시 .meta 파일의 GUID가 유지되는지 확인합니다.

### Assets/_Project 구조

기존 Unity 템플릿 에셋은 유지하고, 새로 제작하는 프로젝트 에셋은 Assets/_Project/ 아래에 추가합니다.

Unity는 Assets/ 아래 어느 위치의 폴더도 정상적으로 인식하므로 _Project/는 필수 폴더가 아닙니다. 이 프로젝트에서는 다음 이유로 자체 제작 에셋을 별도로 묶습니다.

- Unity 템플릿인 Scenes/, Settings/, TutorialInfo/와 팀이 직접 만든 에셋을 구분합니다.
- Asset Store나 외부 패키지의 폴더와 이름이 충돌하는 것을 줄입니다.
- 코드 리뷰, 에셋 정리, 담당 영역 구분 시 프로젝트 소유 파일의 범위를 명확하게 합니다.
- 향후 ThirdParty/와 프로젝트 자체 에셋을 분리해 패키지 업데이트 영향을 줄입니다.

작은 프로젝트에서 외부 패키지를 거의 사용하지 않는 경우에는 Assets/Scripts/나 Assets/Prefabs/처럼 Assets/ 바로 아래에 폴더를 두어도 기능상 차이는 없습니다. 현재 저장소에서는 장기적인 팀 개발과 확장성을 고려해 _Project/ 방식을 기준으로 합니다.

~~~text
Assets/
├── _Project/
│   ├── Animations/
│   ├── Audio/
│   ├── Data/
│   ├── Materials/
│   ├── Models/
│   ├── Prefabs/
│   ├── Scenes/
│   ├── Scripts/
│   │   ├── Runtime/Core/
│   │   ├── Runtime/Gameplay/
│   │   ├── Runtime/UI/
│   │   ├── Editor/
│   │   └── Tests/
│   ├── Shaders/
│   ├── Textures/
│   ├── UI/
│   ├── VFX/
│   └── ThirdParty/
├── Settings/
├── Scenes/        # 기존 템플릿 씬
└── TutorialInfo/  # 기존 Unity 템플릿 안내 에셋
~~~

Settings/에는 공용 렌더링·볼륨 설정을 유지하고, 외부 에셋은 _Project/ThirdParty/ 아래에 격리합니다. 외부 에셋을 도입할 때는 라이선스와 버전을 기록합니다.

## 패키지 및 설정 관리

- 패키지는 Unity Package Manager를 통해 추가·삭제합니다.
- Packages/manifest.json과 Packages/packages-lock.json은 함께 커밋합니다.
- Library/PackageCache를 직접 수정하지 않습니다.
- Assets/_Project/Scripts/Runtime/ 아래의 기능 코드는 Core, Gameplay, UI 어셈블리 경계를 존중합니다.
- Unity Editor 버전이나 핵심 패키지 버전을 올릴 때는 별도 브랜치에서 변경 이유와 영향 범위를 기록합니다.
- ProjectSettings/ 변경은 의도하지 않은 플랫폼·입력·렌더링 설정 변경이 없는지 확인합니다.
- 새로운 씬을 추가하면 Build Profiles에 필요한 씬을 등록하고 변경 사항을 커밋합니다.

## Git LFS 및 바이너리 에셋

대용량 모델, 텍스처, 오디오, 영상은 .gitattributes에 따라 Git LFS로 관리합니다.

~~~bash
git lfs install
git lfs pull
~~~

현재 Unity 프로젝트는 텍스트 직렬화를 사용하므로 .unity, .prefab, .asset, .mat, .anim, .controller, .meta, .asmdef, .json은 일반 Git 텍스트 파일로 유지합니다. 이 파일들을 LFS로 보내면 씬·프리팹 diff와 병합 확인이 어려워질 수 있습니다. 새 바이너리 확장자를 도입하면 .gitattributes에 추가하고 팀 전체가 동일한 규칙을 사용합니다.

## 변경 전 검증 체크리스트

- [ ] 올바른 Unity 버전 6000.5.4f1로 프로젝트를 열었는가?
- [ ] 패키지 복원과 에셋 임포트가 완료되었는가?
- [ ] Console에 새 오류가 없는가?
- [ ] 변경한 씬을 직접 실행해 보았는가?
- [ ] 관련 EditMode/PlayMode 테스트를 실행했는가?
- [ ] 새 에셋의 .meta 파일이 포함되었는가?
- [ ] Library/, Temp/, 빌드 파일, 개인 설정이 커밋 대상에 포함되지 않았는가?
- [ ] 커밋 메시지가 규칙에 맞는가?

## 문서 관리

프로젝트 구조나 핵심 규칙이 바뀌면 이 README와 [Unity 프로젝트 컨텍스트](METAGAME/Docs/AI/UnityProjectContext.md)를 함께 갱신합니다. 브랜치·Jira·자동화 등 협업 규칙의 상세 기준은 [docs/](docs/README.md)이며, README의 요약과 어긋나면 docs/를 따르고 README를 고칩니다.
