# 실험 빌드 규칙 (2026-09-10 · S15P21B202-380)

> "값 하나 바꿔 보고 다시 재는" 빌드를 만들 때의 규칙입니다. 규칙이 없으면 다음 사람이
> 다르게 만들고, **나중에 로그만 보고는 어느 실험인지 못 가립니다.**
>
> 평소 빌드(`Build/Windows`)에는 아무것도 적용되지 않습니다 — 이 문서는 실험용에만
> 해당합니다.

## 왜 규칙이 필요했나

2026-09-09 QA 에서 **누가 호스트였는지 잘못 읽어** 분석을 한 번 되돌렸습니다. 그리고
Unity 플레이어는 늘 같은 경로에 쓰고 **다시 실행하면 덮어씁니다** — 직전 것 하나만
`Player-prev.log` 로 남습니다. 그래서 사람이 매번 손으로 복사해 이름을 붙였고, 그
단계에서 틀렸습니다.

**사람이 이름을 붙이는 단계를 없애는 것**이 이 규칙의 목적입니다.

## 규칙 다섯

| 무엇 | 규칙 | 예 |
|---|---|---|
| **빌드 폴더** | `METAGAME/Build/<이슈번호>-<슬러그>` | `Build/374-rate0` |
| **라벨** (`SPACE_EXPERIMENT`) | `<이슈번호>-<슬러그> \| source=<커밋7자> \| <바꾼 것>=<값>` | `374-rate0 \| source=72f0510 \| PredictionCorrection.Rate=0` |
| **신원** (`SPACE_BUILD_ID`) | 라벨과 같은 `<이슈번호>-<슬러그>` 로 둡니다 — 이벤트·런로그가 이 값으로 빌드를 가립니다(`-597`) | `374-rate0` |
| **로그 파일** | 자동 생성 — `<빌드 폴더>/logs/<yyyyMMdd-HHmmss>.log`. **손으로 이름 붙이지 않습니다** | `logs/20260910-114233.log` |
| **브랜치** | `S15P21B202-<번호>-<슬러그>-experiment` — **머지하지 않습니다** | `S15P21B202-374-rate0-experiment` |
| **넘겨줄 때** | 빌드 폴더의 `logs/` 를 압축합니다 | `logs.zip` |

### 라벨에 **값**까지 적습니다

`374-rate0` 만으로는 무엇을 얼마로 바꿨는지 모릅니다. `PredictionCorrection.Rate=0` 처럼
**바꾼 것과 값**을 적습니다.

다만 라벨은 사람이 적는 것이라 틀릴 수 있습니다. 그래서 **코드가 실제 값을 따로 찍습니다**
(`[PERF] 값 …`). **라벨과 그 줄이 어긋나면 그 줄이 옳습니다.**

### 실험 브랜치는 테스트가 막게 둡니다

`-374` 는 `Rate` 를 0 으로 두었고 `PredictionCorrectionTests` 가 `Rate > 0` 을 단정하고
있어 **EditMode 가 자동으로 막았습니다.** 그것이 이상적인 모양입니다 — 사람의 기억이
아니라 검사가 막습니다.

그런 테스트가 없는 실험이라면 커밋 메시지 첫 줄에 **"이 브랜치는 머지하지 않는다"** 를
적습니다.

## 빌드하는 법

```powershell
$root = "…\S15P21B202\METAGAME"
$env:SPACE_BUILD_DIR  = "$root\Build\374-rate0"
$env:SPACE_EXPERIMENT = "374-rate0 | source=72f0510 | PredictionCorrection.Rate=0"
$env:SPACE_BUILD_ID   = "374-rate0"   # 이벤트·런로그가 이 이름으로 빌드를 가립니다

& "…\Unity.exe" -batchmode -quit -nographics -projectPath $root `
    -executeMethod Metagame.EditorTools.SpaceBuild.BuildWindows -logFile <빌드로그>
```

- **Unity 에디터가 열려 있으면 실패합니다** — 배치모드가 프로젝트 잠금을 잡아야 합니다
- 빌드 뒤 `git status -- METAGAME/` 를 봅니다. 배치모드가 에셋을 재직렬화하므로
  `git checkout -- METAGAME/` 로 되돌립니다(`-351` 이후 삭제는 없지만 수정은 남습니다)

## 로그가 자동으로 남습니다 — QA 하는 사람은 아무것도 안 합니다

실행하면 `<빌드 폴더>/logs/` 에 **실행마다 새 파일**이 생깁니다. 인자도, PowerShell 도
필요 없습니다.

파일 앞쪽이 이렇게 시작합니다.

```
[I] [빌드] 2026-09-10 11:42 · 72f0510 · S15P21B202-374 · +커밋안한변경
[I] [빌드] 실험 374-rate0 | source=72f0510 | PredictionCorrection.Rate=0
[I] [RUN] 이 실행의 로그: …\Build\374-rate0\logs\20260910-114233.log
[I] [PERF] 값 fixedDeltaTime=0.02 vSync=1 targetFrameRate=120 quality=5
[I] [RUN] 역할=참가자 · 비행사(userId=39)
```

**한 파일만 보고 어느 빌드·어느 실험·어느 역할인지 알 수 있어야 합니다.** 그것이 안 되면
이 규칙이 실패한 것이니 이슈로 올려 주세요.

| 머리표 | 뜻 |
|---|---|
| `[I]` `[W]` `[E]` `[X]` | 정보 · 경고 · 오류 · 예외 |
| `[빌드]` | 빌드 스탬프와 실험 라벨 |
| `[RUN]` | 이 실행에 관한 것 — 로그 경로 · 역할 |
| `[PERF]` | 프레임 수치 (5초마다) |
| `[NETDIAG]` | 네트워크 진단 (`-netdiag` 로 켤 때만) |

스택 트레이스는 **오류와 예외에만** 싣습니다. 모든 줄에 싣던 `Player.log` 는 그 때문에
읽기 어렵고 용량이 몇십 배가 됩니다(2026-09-09 실측: 31MB).

파일은 **30개까지** 두고 오래된 것부터 지웁니다.

### 빌드 폴더에 쓸 수 없는 경우

Program Files 같은 곳에 두면 실행 파일 옆에 쓸 수 없습니다. 그때는
`persistentDataPath` 로 떨어지고 **어디에 썼는지 로그가 말합니다** — 조용히 실패하지
않습니다.

## 분석

```
python scripts/ci/perflog.py <로그 파일> [...]
```

인원수별로 평균·최저·95백분위를 묶어 **"몇 명부터 꺾이나"** 를 표로 줍니다. 표본이
없으면 왜 없는지(라벨 없이 빌드했는지) 짚습니다.

## 더 읽기

| 무엇 | 어디 |
|---|---|
| 계측이 어떻게 붙어 있나 | `METAGAME/.../Runtime/Core/PerfLog.cs` · `RunLog.cs` |
| 라벨이 어떻게 박히나 | `.../Editor/BuildStamp.cs` · `.../Runtime/Core/BuildInfo.cs` |
| 네트워크 진단 | `.../Runtime/Network/NetDiagnostics.cs` · `scripts/ci/netdiag.py` |
| 씬·프리팹 변경 규칙 | [git-convention.md](./git-convention.md) |
