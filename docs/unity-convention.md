# Unity · C# 작성 규칙

> 코드와 에셋을 어떤 모양으로 두는지에 관한 규칙입니다. 브랜치·커밋·MR 규칙은 [git-convention.md](./git-convention.md), 규칙 전체 요약은 [conventions.md](./conventions.md).
> 실제 게임 코드가 늘어나면서 확정해 가는 문서입니다. 어긋나는 기존 코드를 발견하면 그 파일을 만질 때 함께 정리합니다.

## 1. C# 코드

- 파일명과 그 안의 주요 타입명을 일치시킨다.
- 클래스·메서드·프로퍼티·enum 은 `PascalCase`, private 필드는 `_camelCase`.
- Inspector 에 노출할 런타임 필드는 `public` 대신 **`[SerializeField] private`** 를 쓴다.
- **런타임 코드에서 `UnityEditor` 를 참조하지 않는다.** 에디터 전용 코드는 `Editor/` 폴더에 두고 별도 어셈블리로 분리한다.
- 네임스페이스는 `Metagame.<Feature>` 형태.
- 기능 단위로 나누고, 기능 간 참조가 커지면 `.asmdef` 로 경계를 만든다.
- 테스트 코드는 `Tests/Editor/` 또는 `Tests/Runtime/` 에 분리한다.

### 어셈블리 참조 방향

```
Core ← Gameplay ← UI ← Bootstrap        (Editor 는 에디터 전용)
```

화살표 방향으로만 참조한다. **역방향 참조(Core 가 Gameplay 를 참조하는 등)는 순환이 되므로 만들지 않는다.** 현재 어셈블리: `Metagame.Core` · `Metagame.Gameplay` · `Metagame.UI` · `Metagame.Bootstrap` · `Metagame.Editor`.

## 2. Assets/_Project 구조

기존 Unity 템플릿 에셋은 그대로 두고, **새로 만드는 에셋은 `Assets/_Project/` 아래**에 넣는다.

```
Assets/
├── _Project/
│   ├── Animations/  Audio/  Data/  Materials/
│   ├── Models/               # Characters/, Spaceships/ — FBX 원본
│   ├── Prefabs/
│   │   └── Resources/        # ArtLink 가 만든 FBX 링크 프리팹 (Resources.Load 대상)
│   ├── Scenes/               # Surface.unity
│   ├── Scripts/
│   │   ├── Runtime/Core/         # Metagame.Core
│   │   ├── Runtime/Gameplay/     # Metagame.Gameplay (Player/, World/)
│   │   ├── Runtime/UI/           # Metagame.UI
│   │   ├── Runtime/Bootstrap/    # Metagame.Bootstrap
│   │   ├── Editor/               # Metagame.Editor (에디터 전용)
│   │   └── Tests/                # Editor/, Runtime/
│   ├── Shaders/
│   │   └── Resources/        # Shader.Find 대신 Resources.Load 로 집는 셰이더
│   ├── Textures/  UI/  VFX/
│   └── ThirdParty/           # 외부 에셋 격리 (도입 시 라이선스·버전 기록)
├── Settings/                 # 공용 렌더링·볼륨 설정
├── Scenes/                   # 기존 템플릿 씬
└── TutorialInfo/             # 기존 Unity 템플릿 안내 에셋
```

`_Project/` 는 Unity 가 요구하는 폴더가 아니고 우리 선택이다. 이유: 템플릿·외부 패키지 에셋과 **우리가 만든 것**을 구분하고, 이름 충돌을 줄이고, 리뷰·정리 때 범위를 분명히 하기 위해. `Assets/` 바로 아래에 새 폴더를 만들면 파일 위생 검사가 MR 에서 막는다.

## 3. 에셋 취급

- **`.meta` 파일은 에셋과 반드시 함께 커밋한다.** 빠지면 다른 팀원의 참조가 전부 깨진다 — 검사가 짝이 맞는지 본다.
- **Unity 에디터 밖에서 에셋을 이동·복사·이름 변경하지 않는다.** GUID 가 어긋난다.
- **씬(`.unity`)·프리팹(`.prefab`)은 동시 수정 금지.** 만지기 전에 팀 채널에 선언하고 MR 본문 "씬/프리팹 변경" 에 적는다. 씬은 얇게 두고 프리팹 단위로 작업을 쪼갠다. 병합 도구 설정은 [git-convention §7](./git-convention.md).
- 모델·텍스처·오디오·영상은 **Git LFS** 로 간다. 씬·프리팹·`.meta`·`.asmdef` 는 텍스트로 유지한다(diff 와 병합 확인을 위해). 새 바이너리 확장자는 `.gitattributes` 에 추가한다 — 자세히는 [git-convention §7.4](./git-convention.md).

## 4. 패키지 · 프로젝트 설정

- 패키지는 Package Manager 로만 추가·삭제하고, `Packages/manifest.json` 과 `packages-lock.json` 을 **함께 커밋**한다.
- `Library/PackageCache` 를 직접 수정하지 않는다.
- Unity 에디터 버전이나 핵심 패키지 버전을 올릴 때는 별도 브랜치에서 **이유와 영향 범위를 기록**한다.
- `ProjectSettings/` 를 커밋할 때는 의도하지 않은 플랫폼·입력·렌더링 설정 변경이 섞이지 않았는지 확인한다.
- 새 씬을 추가하면 Build Profiles 에 등록하고 그 변경도 커밋한다.

## 5. push 하기 전에

- [ ] 올바른 Unity 버전으로 열었는가 (루트 [README](../README.md) "현재 프로젝트 기준")
- [ ] 패키지 복원·에셋 임포트가 끝났는가
- [ ] Console 에 새 오류가 없는가
- [ ] 바꾼 씬을 직접 실행해 봤는가
- [ ] 관련 EditMode/PlayMode 테스트를 돌렸는가
- [ ] 새 에셋의 `.meta` 가 포함됐는가
- [ ] `Library/`·`Temp/`·빌드 산출물·개인 설정이 커밋에 섞이지 않았는가
- [ ] 커밋 메시지가 `type(scope): 요약 (이슈키)` 형식인가
