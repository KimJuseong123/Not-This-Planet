# Git 컨벤션

브랜치 생성, 커밋, MR, 릴리스에 적용하는 규칙입니다. 일반 작업은 `develop`에서 시작하며, 다른 작업에 의존하는 스택 MR과 릴리스·핫픽스는 별도 절차를 따릅니다.

[문서 안내](README.md) · 선행: [Jira 이슈 작성](jira-convention.md) · 다음: [MR 제출과 머지](mr-flow.md)

> 관련 문서: [워크플로우 구현 계획](./workflow-plan.md) · [Jira 컨벤션](./jira-convention.md) · [파트 구분](./parts.md) · [Jira-GitLab 자동화](./jira-gitlab-automation.md)
> 기준 버전: workflow-plan v1.0 (2026-08-26)

**이 문서의 내용**

- [1. 브랜치 전략](#section-1)
- [2. 브랜치 생성 — Jira 이슈 화면에서](#section-2)
- [3. 브랜치 네이밍](#section-3)
- [4. 커밋 메시지](#section-4)
- [5. MR(Merge Request) 규칙](#section-5)
- [6. 규칙 검사와 권한 제한](#section-6)
- [7. Unity 특화 규칙](#section-7)
- [8. 릴리스 — `release/` 브랜치 → master (2026-09-07 결정, S15P21B202-325)](#section-8)

<a id="section-1"></a>

## 1. 브랜치 전략

### 결정: `master` – `develop` – 작업 브랜치 (3단 구조)

```
master   ─────●───────────────●──────    ← 항상 릴리즈(빌드 배포) 가능. 마일스톤마다 태그
              ↑               ↑
develop  ──●──●──●──●──●──●───●──────    ← 통합 개발 브랜치. 항상 에디터에서 실행 가능해야 함
           ↑     ↑     ↑
S15P21B202-12-…  ●──●  ●─●               ← Jira 이슈 단위 작업 브랜치 (수명 1~3일)
```

- 작업 브랜치는 항상 `develop`에서 분기하고 `develop`으로 MR을 올린다.
- `master`에는 직접 커밋/MR하지 않는다. 릴리스는 **develop에서 `release/<이슈키>-vX.Y.Z` 브랜치를 따서 master로 MR**한다(§8). 머지되면 CI가 태그 `vX.Y.Z`와 GitLab Release(빌드 zip)를 만든다 — 사람이 태그를 달거나 빌드를 커밋하지 않는다.
- `master` 긴급 수정은 `hotfix/<이슈키>-…` 브랜치를 `master`에서 분기 → `master`와 `develop` 양쪽에 머지.
- **문서(docs/), 설정 파일도 예외 없이** 작업 브랜치 → develop MR로 올린다.

### 왜 이 구조인가 (웹에서 쓰던 trunk 방식과의 차이)

| 관점 | 웹 (master 직결) | 게임/Unity (develop 경유) |
| --- | --- | --- |
| 미완성 기능 숨기기 | feature flag로 쉬움 | 씬·프리팹·에셋은 flag로 숨기기 어려움 → master가 바로 오염 |
| 배포 단위 | 커밋 단위 상시 배포 | 빌드 산출물 단위. 마일스톤마다 "이 시점의 게임"을 버전으로 박제 |
| 충돌 | 텍스트 머지로 해결 | 씬/프리팹은 사실상 머지 불가 → 통합 지점(develop) 하나로 모아 충돌을 빨리 드러냄 |
| 롤백 | revert로 간단 | 에셋이 얽혀 revert 위험 → master 보수적으로 |

단, **작업 브랜치는 작게(스토리 포인트 ≤ 5), 1~3일 안에 develop으로 머지**한다 — "구조는 git-flow 축소판, 속도는 trunk처럼".

### 운영 규칙

1. `develop`은 항상 **컴파일 에러 없이 에디터에서 실행 가능한 상태**를 유지한다.
2. **브랜치는 Jira 이슈에서 만든다.** 이슈 1개당 브랜치 1개. 이슈 키 없는 이름은 로컬 훅이 push를 막고, 올라가더라도 CI `mr-lint`가 MR을 실패 표시로 만든다(§6).
3. `develop` 머지는 **팀장(Maintainer)만** 할 수 있다 — 팀장의 머지가 승인이다(§5).
4. **push 하면 훅이 알아서 `develop` 최신을 얹는다**(S15P21B202-137). `git push` 하면 pre-push 훅이 `origin/develop`을 가져와 뒤처졌으면 **자동으로 리베이스한 뒤 그 push를 중단**하고, 다시 push 할 명령을 그대로 알려 준다(이미 올라간 브랜치면 `git push --force-with-lease`). 리베이스가 충돌하면 **원래 상태로 되돌리고 경고만** 남기므로 작업이 사라지지 않는다 — 그때는 씬/프리팹 충돌을 한 번에 보는 `git merge origin/develop`로 정리한다. 씬·프리팹을 많이 만지는 작업은 `METAGAME_AUTOREBASE_MODE=merge`로 두면 처음부터 merge로 얹는다(merge는 커밋 해시가 그대로라 강제 push도 필요 없다). 끄려면 `METAGAME_AUTOREBASE=0`.
5. `master`, `develop`은 보호 브랜치 (직접 push 불가).

<a id="section-2"></a>

## 2. 브랜치 생성 — Jira 이슈 화면에서

```
① Jira 이슈 생성 ([파트] 제목, Epic, 담당자, 기한 …)            상태: 해야 할 일
        ↓
② 이슈 화면 → 개발(Development) 패널 → "브랜치 만들기"
        ↓
③ GitLab 생성 화면: 프로젝트 S15P21B202 · 소스 브랜치 develop
   이름은 Jira 제안값(KEY-번호-…) — 설명부가 비면 영문으로 채움      → 자동: 진행 중
        ↓
④ git fetch && git checkout <브랜치명> → 작업
```

- 이것이 이슈를 **진행 중**으로 만드는 유일한 길이다. 보드에서 카드를 끌어도 브랜치가 없으면 되돌려진다.
- 로컬에서 `git checkout -b S15P21B202-12-player-jump develop && git push -u origin …`로 만들어도 동작은 같다(이름 규칙만 지키면 Jira가 감지한다).
- 브랜치가 필요 없는 작업은 없다. 문서·회의·리서치도 브랜치에 기록 1장을 남긴다([Jira 컨벤션 §6](./jira-convention.md)).

<a id="section-3"></a>

## 3. 브랜치 네이밍

```
<JIRA키>-<번호>-<짧은-영문-설명>          ← 작업 브랜치 (기본 형식)
hotfix/<JIRA키>-<번호>-<짧은-영문-설명>   ← master 긴급 수정만
release/<JIRA키>-<번호>-vX.Y.Z           ← 릴리스 — develop 스냅샷을 master 로 (§8)
```

| 예시 | 설명 |
| --- | --- |
| `S15P21B202-12-player-jump` | 기본형 |
| `S15P21B202-45-lobby-bgm` | 에셋·문서 작업도 동일 |
| `S15P21B202-31-network` | 한글 제목이라 설명부가 비었을 때 최소한 파트명 |
| `hotfix/S15P21B202-40-crash-on-join` | master 긴급 수정 (Maintainer만 생성) |
| `release/S15P21B202-318-v0.1.0` | 릴리스 브랜치 — develop에서 딴 스냅샷, master 로만 MR. 버전이 이름에 있어야 CI가 태그를 만든다 |

- `feature/`, `fix/` 같은 접두어는 **쓰지 않는다** — 훅과 `mr-lint`가 위 세 형식만 받는다.
- 설명부는 영문 kebab-case. 자동화·훅·서버 규칙은 **이슈 키만** 본다.

> Jira 프로젝트 키는 실제 값으로 — 이 문서는 `S15P21B202`로 가정한다.

<a id="section-4"></a>

## 4. 커밋 메시지

Conventional Commits 형식에 Jira 이슈 키를 덧붙인다 (루트 README의 규칙과 동일).

```
<type>(<scope>): <summary> (<JIRA키>-<번호>)
```

- `<scope>`는 선택 (예: player, ui, network, lobby). summary는 한글 허용.
- 이슈 키를 넣으면 커밋이 Jira 이슈의 개발 패널에 링크된다.

| type | 용도 |
| --- | --- |
| `feat` | 기능 추가 |
| `fix` | 버그 수정 |
| `asset` | 아트/사운드/씬 등 에셋 추가·수정 (코드 변경 없음) |
| `refactor` | 동작 변경 없는 구조 개선 |
| `docs` | 문서 |
| `test` | 테스트 |
| `perf` | 성능 개선 |
| `build` | 빌드·패키지·플랫폼 설정 |
| `chore` | 기타 유지보수 |

```
feat(player): 이단 점프 구현 (S15P21B202-12)
asset(lobby): 로비 배경 3D 모델 추가 (S15P21B202-45)
docs: 로비 BGM 레퍼런스 조사 기록 (S15P21B202-27)
```

<a id="section-5"></a>

## 5. MR(Merge Request) 규칙

| 항목 | 규칙 |
| --- | --- |
| 제목 | `[파트] type: 요약 (JIRA키-번호)` — 예: `[Network] feat: 로비 매치메이킹 구현 (S15P21B202-31)` |
| 본문 | `.gitlab/merge_request_templates/Default.md`가 자동으로 채워진다. 섹션 제목은 지우지 않는다(CI가 검사). **`이슈: S15P21B202-번호` 줄은 절대 지우지 말 것** — GitLab for Jira 앱은 MR을 **제목·본문의 Jira 키로만** 이슈에 연결한다(브랜치명은 "브랜치" 항목만 연결). 키가 없으면 개발 패널에 MR이 안 뜨고 머지해도 **자동 완료(R6)가 안 된다**(08-27 MR !3 실측) |
| 대상 | 항상 `develop` (릴리즈 MR — `release/<키>-vX.Y.Z` 또는 `develop` — 만 `master`, §8) |
| Draft | **만들지 않는다.** MR 생성 = 리뷰 요청. 진행 상황 공유는 Jira 코멘트로 |
| 승인·머지 | **팀장(Maintainer)이 승인 버튼을 누르고 바로 머지**한다. GitLab Free에서는 "승인 필수"를 강제할 수 없으므로 develop **머지 권한을 Maintainer로 제한**한 것이 실제 관문이다 |
| 머지 방식 | Merge commit + **squash** (이슈 1개 = develop 커밋 1개). 릴리즈 MR만 squash 해제 — CI(`mr-title`)가 자동으로 끄고 `mr-lint`가 확인한다(§8) |
| 머지 조건 | 모든 스레드 해결 · 파이프라인 성공. 초기 문서의 “러너 확보 후” 조건은 해소됨([CI/CD 설정](ci-cd-pipeline.md) §3) |
| 대리 승인 | 팀장이 24시간 내 불가하면 해당 Epic 담당 파트원이 (필요 시 임시 Maintainer로) 머지 |
| 머지 후 | **브랜치 이름의 Jira 이슈만** 자동 완료(R6). 본문에 다른 이슈 키를 적으면 Jira 개발 패널에는 연결되지만 상태는 바뀌지 않는다(2026-08-27 보강 — 그 전엔 언급된 Epic·Task까지 완료되는 사고 있었음). 작업 브랜치는 머지와 함께 자동 삭제(2026-08-27~) |

팀장의 체크리스트(범위 · 완료 조건 · 씬/프리팹 · 구조 · 기록)는 MR 템플릿 하단 "리뷰어 확인"에 있다.

<a id="6-규칙-강제--4겹"></a>

<a id="section-6"></a>

## 6. 규칙 검사와 권한 제한

| 겹 | 장치 | 막는 것 | 비고 |
| --- | --- | --- | --- |
| **서버** | GitLab 보호 브랜치: `develop`·`master` = push No one / merge Maintainers, `hotfix/S15P21B202-*` = Maintainers. **작업 브랜치는 보호하지 않음**(2026-08-27 해제 — 그 전엔 `*` No one + `S15P21B202-*` 허용으로 이름을 서버에서 거부했으나, 머지 시 자동 삭제 불가·개발자 삭제 불가 부작용) | develop·master 직접 push, 팀장 외 머지 | 이름 규칙은 아래 3겹이 담당. 머지 시 **소스 브랜치 자동 삭제**(프로젝트 기본값 ON) |
| **스택 MR** | 작업 브랜치 → 작업 브랜치 MR은 **허용**(2026-08-28). 초기에는 부모 작업 브랜치로 먼저 머지한 뒤 수동 완료하는 방식도 허용했다. 이후 `stack-order`가 부모부터 머지하도록 강제한다. 현재 절차는 부모가 develop에 머지된 뒤 자식 MR의 대상을 develop으로 전환하고 다시 검사·머지하는 것이다([MR 흐름](mr-flow.md) §3) | — |
| **로컬** | `.githooks/pre-push` | 형식 안 맞는 push를 서버까지 가기 전에 차단 (친절한 안내 메시지) → **뒤처진 브랜치에 `develop` 최신을 자동으로 얹고 다시 push 하도록 안내**(-137) → 이어서 `git lfs pre-push` 실행(LFS 업로드 유지) | 클론 후 1회 `git config core.hooksPath .githooks` |
| **Jira** | 자동화 되돌림 (R5·R9) | 브랜치 없이 진행 중, 머지 없이 완료 | 키가 실존하지 않는 브랜치는 Jira가 무시 → 보드에 아무 일이 안 생겨 드러남 |
| **CI** (러너 후) | `mr-lint` — 제목·브랜치 형식, **Jira 키 실존 확인**, 템플릿 섹션·체크박스 | 규칙 위반 MR의 머지 | "Pipelines must succeed" |

- "Jira 버튼으로만 브랜치 생성"을 문자 그대로 강제하는 것은 불가능하다(서버는 버튼 출처를 구분할 수 없다). 위 4겹이 실질적 동치다.
- 초기에는 서버의 `*` 규칙만 제거하고 나머지 검사를 유지하는 대안을 두었다. 해당 보호 규칙은 2026-08-27에 해제했다.

<a id="section-7"></a>

## 7. Unity 특화 규칙

1. **`.meta` 파일은 에셋과 반드시 함께 커밋한다.** 빠지면 다른 팀원의 참조가 전부 깨진다.
2. **씬(.unity)·프리팹(.prefab)은 동시 수정 금지.** 씬을 만지기 전에 팀 채널에 선언하고, MR 템플릿의 "씬/프리팹 변경"에 적는다. 씬은 얇게, 프리팹 단위로 작업을 쪼갠다.
3. **UnityYAMLMerge(Smart Merge).** 로컬 설정:
   ```
   git config merge.unityyamlmerge.name "Unity SmartMerge"
   git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/<버전>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
   ```
   실제로 동작하려면 `.gitattributes`에도 드라이버 지정이 필요하다 (현재 `*.unity text`만 있음). 팀 결정 후 일괄 적용:
   ```
   *.unity  merge=unityyamlmerge eol=lf
   *.prefab merge=unityyamlmerge eol=lf
   *.asset  merge=unityyamlmerge eol=lf
   ```
4. **Git LFS는 이미 적용되어 있다.** 루트 `.gitattributes`가 모델·텍스처·오디오·영상·네이티브 라이브러리·압축 파일을 LFS로 보낸다.
   - 클론 **전에** `git lfs install --skip-repo` 1회 (README "빠른 시작"). `--skip-repo`를 빼면 `core.hooksPath` 때문에 `Hook already exists: pre-push` 경고가 나며, 이때 `git lfs update --force`를 실행하면 `.githooks/pre-push`(브랜치명 검사 + LFS 연계)가 덮어써지므로 금지.
   - Unity YAML(씬/프리팹/meta)은 텍스트로 유지 — diff/머지 확인을 위해 LFS로 보내지 않는다.
   - 새 바이너리 확장자는 `.gitattributes`에 추가. lab.ssafy.com의 LFS 용량 제한은 아트 에셋 대량 커밋 전에 확인.

<a id="section-8"></a>

## 8. 릴리스 — `release/` 브랜치 → master (2026-09-07 결정, S15P21B202-325)

팀장이 하는 일은 둘이다. ① Jira 릴리스 이슈에서 **브랜치 만들기**, 이름 앞에 `release/`를 붙인다 — `release/<이슈키>-vX.Y.Z` ② master로 MR을 올려 **머지**. 그 뒤는 CI가 한다(-326): 태그 `vX.Y.Z` → Windows 빌드 → GitLab Release(zip 내려받기) → MM 🟢 카드 → Jira 릴리스에 fixVersion(-327).

| 규칙 | 왜 |
| --- | --- |
| 릴리스 브랜치는 **develop 에서** 딴다 | master 로 갈 내용은 develop 에 들어올 때 이미 MR 단위로 검사·리뷰됐다. 그래서 릴리즈 MR 은 위생·템플릿·AI 리뷰(와 그 게이트)를 건너뛰고 릴리스 규칙만 본다 |
| **릴리스 브랜치에는 커밋하지 않는다** | develop 의 스냅샷일 뿐이다. 자기 커밋이 있으면 그 변경이 develop 에 없어 다음 릴리스에서 사라지고, develop 재머지라는 절차가 생긴다. 고칠 게 나오면 **develop 에서 고치고 브랜치를 다시 딴다**(두 클릭). `mr-lint` 가 develop 밖 커밋을 세어 막는다 |
| 버전은 **`vMAJOR.MINOR.PATCH`**, 브랜치 이름에 넣는다 | 첫 릴리스는 2026-09-11 에 **`v1.0.0`** 으로 냈다(계획은 `v0.1.0` 이었다). 마일스톤(스프린트 끝·시연)마다 MINOR, 그 사이 고침은 PATCH, MAJOR 는 팀이 정할 때. 태그는 사람이 달지 않고 **CI 가 머지 커밋에 단다** — 손으로 만든 태그는 `v` 가 빠지기 쉽고, 그러면 CI 가 다음 버전을 잘못 세어 **번호를 건너뛴다**(`-446`). zip 이름은 `-269` 규칙대로 날짜·시각도 넣는다 |
| master 대상 MR 은 **squash 를 끈다** | 켜진 채 머지하면 develop 수백 커밋이 squash 1개로 들어가 두 브랜치가 역사를 공유하지 않고, 머지 베이스가 초기 커밋에 고정돼 **다음 릴리스부터 겹치는 파일이 전부 충돌**한다. `mr-title` 이 자동으로 끄고 `mr-lint` 가 확인한다 — 팀장이 체크를 풀 필요 없다 |
| **빌드 산출물은 git 에 넣지 않는다** | 2026-09-07 !240: 218MB · 경로 232자 폴더가 커밋되어 클론·CI 가 영구히 느려지고 윈도우 체크아웃이 깨질 상황이었다(08-31 MAX_PATH 사고와 같은 원인). `.gitignore` 가 `METAGAME/Release/`·`Build/` 를 막고 `repo-hygiene` 가 `.exe`·긴 경로를 막는다. 빌드는 GitLab Release 에 |
| 릴리스 뒤 develop 재머지 **없음** | 브랜치에 자기 커밋이 없으니 develop 에 넣을 것이 없다 |
| hotfix 는 그대로 | `hotfix/<키>-…` 를 master 에서 분기 → master·develop 양쪽 머지. 태그·Release 는 -326 에서 정한다 |
| 태그는 **항상 `vX.Y.Z`** 로 만든다 | CI 가 붙이는 이름이 그 형식이다. 읽을 때는 `v` 없는 것도 버전으로 인정한다 — 저장소에 `1.0.1` 이 하나 있고 **그것을 지우지 않는다**(릴리스 `1.0.1` 이 참조하고 있어 지우면 그 릴리스가 깨진다). 그래서 핫픽스가 이전 태그를 찾을 때 그것도 후보가 된다 |
| **실제 릴리스는 `master` push 하나뿐이다** | Run pipeline 으로 돌리는 것은 언제나 리허설이고, 변수로 그것을 끌 수 없다(`publish_release.dry_run`). 다른 경로로 릴리스가 나가면 이 절의 절차가 뜻을 잃는다 |

### 배포 순서 — 백엔드가 먼저다

릴리스는 **두 가지가 따로 나갑니다.** 트리거가 다릅니다.

| 나가는 것 | 트리거 | 시점 |
| --- | --- | --- |
| Spring 백엔드 | `develop` push + 버튼 | 팀장이 누를 때 |
| Windows 클라이언트 | `master` push 자동 | 릴리스 브랜치를 머지할 때 |

**백엔드를 먼저 배포합니다.** 클라이언트가 먼저 나가면 새 클라이언트가 아직 없는 API 를 부를 수 있습니다. 반대 순서는 안전합니다 — 백엔드는 옛 클라이언트를 계속 받아 주게 만들기 때문입니다(버전 게이트는 `클라 버전 < 최소 허용 버전` 일 때만 막습니다).

### 릴리스가 실패하면

`master` 파이프라인 실패는 **🔴 릴리스가 실패했다** 카드로 옵니다(2026-09-11 부터). 그 카드가 없으면 실패를 모르는 것이 아니라 **알림 배선이 깨진 것**이므로 그것부터 확인합니다.

| 증상 | 원인 | 할 일 |
| --- | --- | --- |
| "버전을 정할 수 없다" | 머지 커밋에 `release/<키>-vX.Y.Z` 가 없다 | 브랜치 이름에 버전을 넣어 다시 딴다 |
| "Unity 라이선스가 마운트되지 않았다" | 러너 설정 | Infra |
| EditMode 테스트 실패 | 머지 결과 트리가 깨졌다 | develop 에서 고치고 릴리스 브랜치를 다시 딴다 |
| 카드가 아예 안 온다 | 잡이 못 떴다(이미지·러너·타임아웃) | 파이프라인 화면에서 직접 확인 |

**릴리스가 실패해도 master 코드는 그대로 머지된 상태입니다.** 태그·패키지·Release 가 만들어지지 않았을 뿐이므로, 원인을 고친 뒤 그 파이프라인을 재실행하면 됩니다.

### 되돌리기

| 무엇 | 방법 |
| --- | --- |
| 백엔드 | **자동입니다.** 헬스체크가 실패하면 `deploy.sh` 가 직전 jar(최근 3개 보관)로 되돌리고 🔴 카드를 보냅니다. 사람이 할 일은 없고, 롤백 뒤에도 실패하면 카드가 "사람이 봐야 한다" 로 옵니다 |
| 클라이언트 | 나쁜 릴리스를 **되돌리는 절차는 아직 없습니다.** 사용자가 이미 받은 것을 회수할 수 없기 때문이고, 런처(`-406`)가 생기면 서버의 최신 표시를 되돌리는 것이 곧 롤백이 됩니다. 그때까지는 **다음 PATCH 릴리스로 고칩니다** |
| master 커밋 | revert 하지 않습니다 — 에셋이 얽혀 위험합니다(§1 표). develop 에서 고치고 새 릴리스를 냅니다 |

### 리허설 — 실제 릴리스를 만들지 않고 경로를 지나가 본다

릴리스 잡은 90분이고 트리거가 `master` push 하나뿐이라, 예전에는 **시험 실행이 곧 실제 릴리스**였습니다. 지금은 GitLab 의 Pipelines → **Run pipeline** 으로 아무 브랜치에서나 돌릴 수 있습니다. 그 경우 `RELEASE_DRY_RUN=1` 이 붙어 빌드·zip 까지 진짜로 하고 **패키지 업로드·Release 생성·🟢 카드·Jira 웹훅만 건너뜁니다.**

Jira 쪽: 릴리스 이슈는 `[Plan]`(또는 `[Infra]`) 작업으로, 제목에 버전(`[Plan] 릴리스 v0.1.0`). 자동화 R6·R7 의 소스·대상 브랜치 정규식은 **2026-09-07 에 `^(?:hotfix/|release/)?` 로 고쳤다**(3곳, [부록 A](./jira-gitlab-automation.md#부록-a--규칙을-api로-고치는-법-내부-api-규약-2026-08-27-실측) 방식) — 그래서 릴리스 브랜치 MR 을 머지하면 릴리스 이슈가 자동 완료된다. [jira-gitlab-automation.md](./jira-gitlab-automation.md) R6·R7.
