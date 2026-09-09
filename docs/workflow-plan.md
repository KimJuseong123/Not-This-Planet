# METAGAME 워크플로우 구현 계획

> **상태: v1.1 — 확정·적용 완료 (2026-08-27).** 0단계(Jira 자동화 8개 · GitLab 보호 브랜치/머지 설정 · 템플릿 · 훅)를 적용하고 E2E로 검증했다(결과: [jira-gitlab-automation.md §8](./jira-gitlab-automation.md)). 남은 것은 팀장 머지(R6 검증)와 1·2단계(러너). 아래는 v1.0 확정 당시 내용으로, 각 구현 항목의 실현 가능성을
> 공식 문서로 검증한 결과를 붙였다. 이 문서가 기준이며, 컨벤션 문서(`jira-convention.md`, `git-convention.md`,
> `jira-gitlab-automation.md`)는 이 문서에 맞춰 갱신되어 있다.
>
> ⚠️ **이 문서는 결정 근거의 기록**이다. 지금 지켜야 하는 규칙은 [conventions.md](./conventions.md) 와 각 컨벤션 문서를 본다. 특히 본문의 파트 태그 **9종**은 v1.0 당시 값이며 2026-08-27 에 **13종**으로 늘었다([parts.md](./parts.md)).

> **검증 범례** — ✅ 공식 문서·실제 동작으로 확인됨 · ⚠ 조건부: 우리 환경(Jira 플랜·프로젝트 유형·GitLab 티어) 확인 후 확정 ·
> ❌ 불가: 대안으로 대체함. 출처는 부록 D.

## 0. 전체 흐름 한 장

```
 ┌────────────────────────────────────────────────────────────────────────────┐
 │ PART 1  Jira 작성 (사람 또는 AI)                                            │
 │   이슈 생성 → 필수 필드(폼에서 강제) → ⚙ 레이블·기한·설명 보정           │
 │   상태 [해야 할 일]                                                         │
 └───────────────┬────────────────────────────────────────────────────────────┘
                 │ 담당자가 이슈 화면 "브랜치 만들기" (소스 develop, 이름 KEY-번호-영문)
                 │   = [진행 중]으로 가는 유일한 길. 카드를 손으로 끌면 되돌려짐
 ┌───────────────▼────────────────────────────────────────────────────────────┐
 │ PART 2  브랜치 · 작업 · MR                                                 │
 │   GitLab: 이슈 키 없는 이름은 브랜치 생성 자체가 거부됨 (보호 브랜치 규칙)    │
 │   ⚙ 브랜치 생성 감지 → [진행 중]                                            │
 │   작업(코드/에셋/씬/문서·기록) → 커밋 → push → MR 생성(템플릿 자동 적용)     │
 └───────────────┬────────────────────────────────────────────────────────────┘
                 │
 ┌───────────────▼────────────────────────────────────────────────────────────┐
 │ PART 3  MR → develop 머지                                                  │
 │   ⚙ 자동 검사: (1단계) MR 린트·리포 위생 · (2단계) Unity 컴파일/테스트·AI 리뷰 │
 │   👤 팀장(Maintainer)만 develop에 머지 가능 = 승인 관문. 체크리스트 5항목     │
 │   머지(squash) → ⚙ [완료]                                                   │
 └────────────────────────────────────────────────────────────────────────────┘
```

**원칙:** 기계가 검사할 수 있는 것은 전부 기계가 하고, 사람(팀장)은 *판단*이 필요한 것만 본다.

---

## PART 1 · Jira 작성

### 1.1 작성 주체 — 사람 또는 AI (개인 선택) ✅
직접 작성하거나 개인 Atlassian 계정 권한을 AI에 주어 작성시킨다. AI에는 `docs/jira-convention.md`를 컨텍스트로 준다.
개인 토큰만 사용, 팀 공용 토큰을 개인 AI 툴에 넣지 않는다.
**근거:** 채워져야 할 것을 폼과 자동화가 강제하므로 작성 수단은 결과에 영향이 없다.

### 1.2 작업 이슈(Task)의 필드 규칙 — 확정

| 필드 | 규칙 | 누가 | 검증 |
| --- | --- | --- | --- |
| **제목** | `[파트] 제목` — 9종 태그(`docs/parts.md`) | 작성자 | ✅ 형식은 R2가 검사 |
| **담당자** | 실제 작업자 1명. **필수** | 작성자 | ✅ 팀 관리형: 작업 유형 설정에서 "필수" 체크 가능. 회사 관리형: 필드 구성(Jira 관리자) |
| **보고자** | 생성자 그대로 — **보고자 규칙 폐기(2026-08-27)**. 대신 "Task는 상위 Epic 필수, Epic 담당자가 하위 Task 책임"으로 단순화 | — | R1의 보고자 치환 액션 삭제 |
| **레이블 (=파트)** | 값은 그 이슈의 제목 `[파트]`와 **글자까지 동일**(`Network`, 대소문자 구분). Task는 수행 파트, Epic은 검증 파트. **제목에서 자동 설정** | ⚙ R1/R2 | ✅ `{{issue.summary.match("^\[(\w+)\]")}}`로 추출(match는 첫 캡처 그룹 반환). 레이블 9종 + `needs-fix`를 미리 만들어 둔다 |
| **기한** | **필수.** 비어 있으면 스프린트 종료일로 자동 | 작성자 / ⚙ R1·R8 | ✅ `{{issue.customfield_10020.last.endDate.jiraDate}}` — `.jiraDate` 포맷 필수(Atlassian KB) |
| **스프린트** | 백로그는 비어도 됨. **진행 중 진입 시 필수** | 스프린트 계획 | ✅ R5가 진입 시 검사 |
| **우선순위** | 기본 Medium. Highest=이번 스프린트 다른 작업 차단 / High=이번 스프린트 필수 / Medium=기본 / Low=여유 시 / Lowest=아이디어 | 작성자 | ✅ |
| **상위 항목(Epic)** | **필수.** 잡무는 `[Infra] 프로젝트 운영` Epic | 작성자 | ✅ 필수 필드 설정 |
| **스토리 포인트** | §1.3 척도. 스프린트 배정 시 필수 | 작성자 | ✅ R5가 진입 시 검사 |
| **설명** | 비어 있으면 템플릿(목적·완료 조건·산출물) **자동 삽입**. 필수 지정은 하지 않음 | ⚙ R1 | ✅ Edit issue 액션. 팀장 체크리스트 "완료 조건"의 기준이므로 채우는 것을 강력 권장 |
| 이슈 타입 | `Task` 통일 | 작성자 | ✅ |

### 1.3 스토리 포인트 척도 — 확정 ✅

| 규칙 | 값 | 근거 |
| --- | --- | --- |
| 단위 | **1 pt = 집중 작업 1시간** | 6명이 같은 단위를 쓰는 것이 상대 추정보다 우선. "주 200pt" 사례는 척도 부재의 결과 |
| 허용 값 | 1 · 2 · 3 · 5 · 8 · 13 | 큰 수를 정밀하게 맞추려는 낭비 방지 |
| 이슈 상한 | 13 (넘으면 쪼갠다) | 1~3일 규칙과 일치, 리뷰 크기 제한 |
| 개인 주간 합계 | **목표 30~40 · 상한 60** | 항상 두 자리. 보드 백로그의 담당자 아바타에 스프린트 합계가 표시됨 |

### 1.4 강제 방식 ✅

| 검사 | 방법 | 위반 시 |
| --- | --- | --- |
| 담당자 · 상위 Epic · 기한 비어 있음 | Jira 필드 "필수" 설정 | 생성 폼에서 막힘 (사람·AI 공통) |
| 제목 `[파트]` 형식 | R2 정규식 `^\[(Plan\|Art\|UI\|Sound\|Character\|Puzzle\|Network\|QA\|Infra)\] .+` | 라벨 `needs-fix` + 담당자 멘션. 해소 시 제거 |
| 스프린트 · 스토리 포인트 | R5가 진행 중 진입 시 검사 | 되돌림 + 코멘트 |
| `needs-fix` 잔존 | R4 조건에서 제외 | 진행 중으로 못 감 |

### 1.5 ⚠ Jira Automation 월 실행 한도 — 0단계 첫 확인 항목

| 플랜 | 월 한도 | 우리 예상 사용량 대비 |
| --- | --- | --- |
| Free | **100회** | ❌ 1주 안에 소진 → 자동화 불가 |
| Standard | **1,700회** | ✅ 여유 (아래 추정 600~800회) |
| Premium | 사용자당 1,000회 | ✅ |

- **카운트 규칙(공식):** 액션을 1개 이상 실행한 "성공 실행"만 카운트. 조건에서 멈춘 실행은 카운트되지 않음.
  한 실행에서 액션이 여러 개여도 1회. → **규칙은 "트리거 1개 = 액션 여러 개"로 합치고, 조건을 앞에 세운다.**
- **추정:** 이슈 1건당 생성(1) + 브랜치 생성(1) + MR 머지(1) + 스프린트 변경(0~1) ≈ 3~4회. 6명 × 주 8건 × 4주 ≈ 200건/월 → **600~800회/월.**
- **확인 방법:** Jira → 설정 → 시스템 → 자동화 규칙 → **사용량(Usage)** 탭에 플랜별 한도와 현재 소비량이 표시된다.
- **Free일 경우 대안:** Jira Automation 대신 GitLab **Settings → Integrations → Jira issues**의 전환 기능(MR 머지 시 `Closes KEY` 키워드로 완료)만 사용하고, 진행 중 이동은 수동. 이 문서의 R1~R9는 보류.

---

## PART 2 · 브랜치 → 작업 → MR

### 2.1 모든 이슈는 브랜치를 가진다 (비코드 포함) ✅ 팀 결정
기획 문서·프로토타입·리서치·회의도 브랜치 → MR → 머지. 브랜치에는 최소 §2.6의 작업 기록 1장.
**근거:** ① Jira는 프로젝트 후 접근이 끊기지만 리포는 남는다 ② 포트폴리오 증거 = MR 목록 ③ 단일 흐름이 자동화를 단순하게 한다.
**비용:** 기록성 MR도 팀장 승인이 필요 → 항목 1개만 보는 빠른 승인 경로(§3.3).

### 2.2 상태 3개, 전환은 이벤트가 만든다 ✅ 팀 결정

```
해야 할 일 ──(브랜치 생성)──▶ 진행 중 ──(MR 머지)──▶ 완료
```

| 상태 | 들어오는 유일한 길 | 규칙 | 검증 |
| --- | --- | --- | --- |
| 해야 할 일 | 이슈 생성 | — | — |
| 진행 중 | 이슈 화면 **"브랜치 만들기"** (또는 이슈 키가 든 브랜치 push) | R4 `Branch created` | ✅ DevOps 트리거, 연동 앱 필요(설치됨) |
| 완료 | MR이 develop에 **머지** | R6 `Pull request merged` | ✅ |

MR이 머지 없이 닫히면 상태 유지 + 코멘트(R7).

### 2.3 "진행 중은 브랜치로만" — 검증 결과와 최종 구성

| 겹 | 방법 | 검증 | 조건 |
| --- | --- | --- | --- |
| **1. 되돌림 (R5)** | 트리거 `Issue transitioned → 진행 중`. 규칙 상세의 "다른 규칙의 액션이 이 규칙을 트리거하도록 허용"을 **끔** → R4(자동화)가 옮긴 전환에는 발동하지 않고 사람이 끈 경우에만 발동. 조건: **JQL `(development[commits].all > 0 OR development[pullrequests].all > 0)`** 로 연결된 브랜치 존재 여부 판별(If/Else). 없으면 → `해야 할 일`로 되돌리고 코멘트. 스프린트·스토리 포인트 비어 있어도 되돌림 | ❌ Jira Cloud JQL에는 `development[branches]` 속성이 **없다**(2026-08-26 실측: builds·commits·reviews·pullrequests만). v0.2의 `{{issue.development.branch.count}}` 스마트 값도 미문서 → 둘 다 폐기. ✅ 대신 **이슈 키가 든 커밋 또는 MR 존재**(`development[commits].all > 0 OR development[pullrequests].all > 0`)로 판별 — 정상 경로는 R4가 처리하므로 R5는 "제목 수정 후 커밋을 이미 올린" 예외 경로만 통과시키면 된다. `{{initiator}}`는 보조 조건으로 사용 가능 | 프로젝트 자동화 편집 권한(있음) |
| **2. 전환 제한** | **회사 관리형:** 전환 `해야 할 일 → 진행 중`에 조건 **"User is in project role: `atlassian-addons-project-access`"** — 기본 실행자 "Automation for Jira"가 이 역할에 속함. ❌ "Hide transition from user" 조건은 자동화도 막으므로 사용 금지. **팀 관리형:** 규칙 "이슈 이동 제한(Restrict who can move)"에 "Automation for Jira"를 선택할 수 없음(JRACLOUD 미해결) → **R4·R6의 실행자를 [Infra] 실계정으로 지정하고 그 계정만 허용** | ⚠ 프로젝트 유형·워크플로우 편집 권한 확인 후. 팀 관리형은 프로젝트 관리자로 가능, 회사 관리형은 Jira 관리자 필요 | 권한 확인 |
| **완료도 동일 (R9)** | `Issue transitioned → 완료`, 트리거 허용 끔 → 사람이 끈 경우에만 → **무조건** `진행 중`으로 되돌림 + "완료는 MR 머지로만". 모든 이슈가 MR을 갖기로 했으므로 수동 완료의 정당한 경우가 없다 | ✅ dev-info 조건 불필요. (Cloud JQL은 `development[pullrequests].merged`를 지원하지 않아 머지 여부 판별은 불가 — 그래서 무조건 되돌림) | — |

### 2.4 "브랜치는 반드시 Jira로만" — 검증 결과

| 요구 | 결론 | 설명 |
| --- | --- | --- |
| Jira "브랜치 만들기" 버튼을 통해서만 생성 | **❌ 문자 그대로는 불가** | ① 버튼은 사용자를 GitLab 브랜치 생성 화면으로 보내고 **사용자 본인 권한**으로 만든다 → 서버는 버튼/UI/`git push`를 구분할 수 없다. ② GitLab에는 "브랜치 생성 권한"이 따로 없다 — 생성 = push 권한(보호 브랜치의 Allowed to push). 생성을 막으면 자기 브랜치에 커밋 push도 막힌다(Free·Premium 공통). ③ 출처(`GL_PROTOCOL` web/ssh)를 볼 수 있는 pre-receive 서버 훅은 인스턴스 관리자 전용. (fork 워크플로우로 우회 가능하지만 Jira 연동이 fork 브랜치를 추적하지 못해 목적에 반함) |
| **이슈 키 없는 이름의 브랜치는 생성 불가 (실질적 동치)** | ✅ 가능했으나 **2026-08-27 해제** | 와일드카드 보호로 서버가 거부하던 방식은 머지 시 자동 삭제를 막아, CI `mr-lint` 도입 후 해제. 지금은 로컬 훅 + CI + Jira 3겹 |
| 이슈 키가 **실존**하는지 서버에서 확인 | ❌ Free에서 불가 → 1단계 CI 린트가 Jira API로 확인 | 존재하지 않는 키의 브랜치는 Jira도 무시하므로 보드에 아무 일이 안 생겨 즉시 드러남 |

### 2.5 GitLab 보호 브랜치 규칙 — 서버 측 이름 강제 ✅ (부록 B)

| 패턴 | Allowed to push | Allowed to merge | Force push | 효과 |
| --- | --- | --- | --- | --- |
| `*` | **No one** | **No one** | off | 아래 규칙에 안 맞는 이름은 **생성·push·삭제 전부 거부** |
| `hotfix/S15P21B202-*` | Maintainers | No one | on | master 긴급 수정 |
| `develop` | No one | **Maintainers** | off | MR로만, 팀장이 머지 |
| `master` | No one | Maintainers | off | 릴리즈 MR만 |

- **검증:** 여러 규칙이 겹치면 **가장 허용적인 규칙**이 적용됨(공식). 와일드카드 보호는 push뿐 아니라 **생성**도 제한하며, "push: No one"이어도 "merge" 권한이 있으면 생성 가능하므로 `*`는 **둘 다 No one**이어야 한다(공식). 보호 브랜치는 Free 티어.
- **비용 (반드시 인지):** 작업 브랜치가 보호 대상이 되므로 머지 시 "소스 브랜치 삭제"가 비활성된다. → **실제로 이 비용이 커서 2026-08-27 `*`·`S15P21B202-*` 규칙을 삭제**했다(CI mr-lint 가 이름 규칙을 대신 검사). 이제 머지 시 자동 삭제 ON.
- **되돌리기:** 이 비용이 크다고 판단되면 `*` 규칙 하나만 지우면 서버 강제만 사라지고 나머지(훅·CI·Jira)는 그대로다.
- Jira "브랜치 만들기"가 제안하는 이름 `KEY-번호-…`는 `S15P21B202-*`에 걸리므로 그대로 통과한다.

### 2.6 브랜치명 · 산출물 · MR — 확정

- **브랜치명** `<JIRA키>-<번호>-<짧은-영문-설명>`. Jira가 제안한 이름의 설명부가 한글 때문에 비면 GitLab 생성 화면에서 영문으로 채운다. `.githooks/pre-push`가 로컬에서도 검사.
- **비코드 산출물:** 코드 `METAGAME/Assets/_Project/Scripts/…` · 에셋 `Assets/_Project/<종류>/…`(원본 `docs/prototypes/<KEY>/`) · 기획 문서 `docs/design/…` · 회의·리서치·외부 툴 작업 → **작업 기록 1장** `docs/records/<KEY>-<slug>.md` (템플릿 `docs/templates/work-record.md`).
- **MR:** `.gitlab/merge_request_templates/Default.md` 자동 적용(✅ Free, `Default.md` 파일명 자동 인식). 제목 `[파트] type: 요약 (KEY-번호)`. 대상 `develop`. **Draft MR 금지** — MR 생성 = 리뷰 요청.

---

## PART 3 · MR → develop 머지

### 3.1 원칙
기계: 형식·재현 가능한 검사 / AI: 참고 의견(비차단) / 사람(팀장): 판단 — 유일한 차단 관문.

### 3.2 팀장 승인의 구현 — 검증 결과

| 방법 | 검증 | 결론 |
| --- | --- | --- |
| GitLab "필수 승인 수 = 1" | ❌ **Free에서는 승인이 선택 사항** — 승인 없이도 머지 가능. 필수 승인·승인 규칙·작성자 자기 승인 방지 등은 전부 Premium | 사용 불가 (lab.ssafy.com이 Premium이면 추가) |
| **develop 보호 브랜치 "Allowed to merge = Maintainers"** | ✅ Free | **채택.** Maintainer = 팀장 + [Infra](설정용). 팀장의 머지 클릭 = 승인. [Infra]는 §3.5 대리 규칙 외에는 머지하지 않는다 |
| "All threads must be resolved" | ✅ Free (Settings → Merge requests → Merge checks) | 채택 |
| "Pipelines must succeed" | ✅ Free. 파이프라인이 없으면 머지가 막히므로 **러너 확보 후(1-c)** 켠다. "Skipped pipelines are considered successful" 옵션 있음 | 1단계에서 채택 |
| Squash 기본 체크 · 소스 브랜치 삭제 기본값 | ✅ Free | 채택 (삭제는 §2.5 비용 참고) |
| 승인 "선택" 기능 | ✅ Free | 팀장이 머지 전 **승인 버튼도 누른다** — 기록용. 강제력은 없지만 MR 목록에서 "승인됨"으로 필터 가능 |

### 3.3 팀장이 보는 것 (MR 템플릿 "리뷰어 확인"과 동일) ✅

| # | 항목 | 왜 사람이 |
| --- | --- | --- |
| 1 | **범위** — MR 변경 = Jira 이슈 범위 | 기계는 이슈 의도를 모른다 |
| 2 | **완료 조건** — 이슈 설명의 완료 조건을 "확인 방법"대로 재현 | 게임플레이 감각 |
| 3 | **씬/프리팹** — 변경 선언과 실제 일치, 사전 선언 | 충돌 예방 |
| 4 | **구조** — 폴더·네이밍·어셈블리 경계(Core ← Gameplay ← UI) | 설계 의도 = 팀장이 지키려는 flow |
| 5 | **기록** — 이후 작업에 영향 주는 결정이 남았는가 | 지식 유실 방지 |

보지 않는 것(기계가 확인): 형식, 템플릿 채움, `.meta`, 금지 파일, 컴파일, 테스트, 최신 develop 반영. **기록성 MR은 항목 2만, 목표 1분.**

### 3.4 자동 검사 단계 ✅

| 단계 | 검사 | 도구 | 차단 | 선행 조건 |
| --- | --- | --- | --- | --- |
| **0 (지금)** | Jira 규칙 R1~R9 · 보호 브랜치 · 머지 체크(스레드) · 훅 | Jira Automation · GitLab 설정 · `.githooks/pre-push` | 전환 되돌림 / 브랜치 생성 거부 / push 거부 | 없음 |
| **1 (러너 후)** | MR 린트(제목·브랜치·Jira 키 **실존 확인**·템플릿 섹션·체크박스) · 리포 위생(`.meta` 짝, 금지 경로, LFS 포인터) | GitLab CI | 머지 차단 ("Pipelines must succeed") | GitLab Runner 1대(⚠ lab.ssafy.com 공유 러너 유무 확인), Jira API 토큰(읽기, CI 변수) |
| **2** | Unity 컴파일 · EditMode 테스트 | Windows 러너 `Unity.exe -batchmode -runTests -testPlatform EditMode -testResults …` | 머너 차단 | Unity 설치된 러너 PC (✅ CLI 인자 공식 문서 확인) |
| 2 | AI 리뷰 코멘트 | CI + Anthropic API | **비차단** | API 키(마스킹 변수) · 비용 |

### 3.5 병목 완화 ✅ 확정
응답 SLA **업무 시간 24h** · 불가 시 **해당 Epic 담당 파트원**이 대리 머지(그 사람에게 임시 Maintainer 부여 → 필요 시에만) · MR 크기 1~3일/13pt · 측정: 열린 MR 경과 시간·주간 머지 수 · 재검토: 스프린트 3회 후 또는 대기 48h 초과 2주 연속.

---

## 4. 검증 결과 총괄

| # | 항목 | 결론 | 조치 |
| --- | --- | --- | --- |
| 1 | 브랜치 생성 → 진행 중 / MR 머지 → 완료 (DevOps 트리거) | ✅ | R4·R6 |
| 2 | 진행 중은 브랜치로만 — 되돌림 | ✅ JQL `development[commits].all > 0 OR development[pullrequests].all > 0` (Cloud에 `branches` 속성 없음 — 실측) | R5 |
| 3 | 진행 중은 브랜치로만 — 전환 제한 | ⚠ 프로젝트 유형·권한 확인 | 회사형: 역할 조건 / 팀형: 실행자 = [Infra] 계정 |
| 4 | 완료는 머지로만 | ✅ 무조건 되돌림 (+전환 제한) | R9 |
| 5 | "브랜치는 Jira 버튼으로만" | ❌ 구분 불가 → **이름 강제**로 대체 | §2.4 |
| 6 | 이슈 키 없는 브랜치 서버에서 거부 | ✅ 가능(와일드카드 보호) → **08-27 해제** | 자동 삭제 불가 비용 때문. CI mr-lint 가 대체 |
| 7 | 이슈 키 실존 확인 | ❌ 서버 불가 → CI 린트 | 1단계 |
| 8 | 팀장 승인 필수 | ❌ Free 승인 필수 불가 → **머지 권한 제한** | develop merge = Maintainers |
| 9 | 스레드 해결 필수 · 파이프라인 성공 필수 · squash · 템플릿 자동 적용 | ✅ Free | 부록 B |
| 10 | ~~보고자 = Epic 담당자 자동~~ 폐기(2026-08-27) — 보고자는 생성자 | — | R1에서 액션 삭제 |
| 11 | 레이블 자동 설정 (제목 파싱) | ✅ | R1·R2 |
| 12 | 기한 = 스프린트 종료일 | ✅ `.jiraDate` 필수 | R1·R8 |
| 13 | 필수 필드 (담당자·Epic·기한) | ✅ 팀형 프로젝트 관리자 / 회사형 Jira 관리자 | 0-a |
| 14 | Automation 월 한도 | ⚠ **플랜 확인 필수.** Free면 자동화 불가 | §1.5 |
| 15 | 러너 없이 CI | ❌ → 러너 1대 필요 | 1-a |
| 16 | Unity 테스트 CLI | ✅ | 2-a |

## 5. 구현 단계 — 확정

**적용 현황(2026-08-27):** 0-0·0-a(필수 필드 제외)·0-b·0-d·0-e·0-f 완료, 0-c(워크플로우 전환 제한)는 Jira 사이트 관리자 권한이 없어 미적용(되돌림 규칙 R5·R9로 운영), 0-g는 MR !1 팀장 머지 대기. 1·2단계는 러너 확보 후.

| 단계 | 항목 | 담당 | 선행 | 완료 확인 |
| --- | --- | --- | --- | --- |
| **0-0** | **환경 확인** (§6 체크리스트): Jira 플랜·한도, 프로젝트 유형, 워크플로우 편집 권한, GitLab 티어, 공유 러너 유무 | [Infra] | — | §6 표 채움 |
| 0-a | Jira: 필수 필드(담당자·Epic·기한), 레이블 9종 + `needs-fix`, 상태명 확인 | [Infra] + Jira 프로젝트 관리자 | 0-0 | 담당자 비운 채 생성 불가 |
| 0-b | Jira Automation R1~R9 (부록 A). R5·R9 "다른 규칙이 트리거 허용" 끔 | [Infra] | 0-a | 테스트 이슈로 전 흐름 1회 |
| 0-c | (권한 있으면) 워크플로우 전환 제한 §2.3-2 | [Infra] (+Jira 관리자) | 0-0 | 드래그로 진행 중·완료 불가 |
| 0-d | GitLab 보호 브랜치 5규칙 · 머지 설정 (부록 B) | [Infra] (Maintainer) | — | 키 없는 이름 브랜치 push가 서버에서 거부 |
| 0-e | 템플릿 3종 확정 반영 (이미 파일 존재) | [Infra] | — | 첫 MR에 자동 적용 |
| 0-f | 각자 `git config core.hooksPath .githooks`, `git lfs install` | 전원 | — | — |
| 0-g | **이 문서·컨벤션 문서를 첫 이슈/브랜치/MR로 올려 전 흐름 시연** | [Infra] → 팀장 승인 | 0-a~0-e | develop에 머지, Jira 완료 |
| **1-a** | GitLab Runner 등록 (Unity 설치된 팀원 PC 또는 VM) | [Infra] + 제공자 | 0-0 | 러너 online |
| 1-b | `.gitlab-ci.yml` `mr-lint`(Jira API로 키 실존 확인 포함) · `repo-hygiene` | [Infra] | 1-a | 위반 MR 빨간불 |
| 1-c | "Pipelines must succeed" 켬 | [Infra] | 1-b 안정화 1주 | — |
| **2-a** | Unity 컴파일·EditMode 잡 | [Infra] | Unity 러너 | 컴파일 에러 MR 차단 |
| 2-b | AI 리뷰 코멘트 잡 (비차단) | [Infra] | API 키·비용 합의 | MR 자동 코멘트 |
| 2-c | 정책 재검토 (§3.5) | 전원 | 스프린트 3회 | 회고 기록 |

## 6. 환경 확인 체크리스트 (0-0) — ✅ 확인 완료, 결과는 표의 굵은 글씨

| 확인 | 어디서 | 결과에 따라 |
| --- | --- | --- |
| Jira 플랜 / Automation 월 한도 | Jira 설정 → 시스템 → 자동화 규칙 → 사용량 | **결과(08-26):** 사용량만 표시되고 한도 미표기 → 일단 무시. 넘으면 규칙이 멈춰 드러남 |
| 프로젝트 유형 (팀 관리형 / 회사 관리형) | REST `project.style` | **결과: `classic` = 회사 관리형.** 필수 필드는 필드 구성(Jira 관리자), 전환 제한은 워크플로우 조건 "project role = atlassian-addons-project-access" |
| 워크플로우 편집 권한 · 프로젝트 관리 권한 | `mypermissions` | **결과: 금준영은 `ADMINISTER_PROJECTS` 없음** (프로젝트 리드 = 김수호). Automation 규칙 생성·필드 필수 지정·워크플로우 편집 모두 프로젝트 관리자/Jira 관리자 필요 → **김수호가 프로젝트 역할 "Administrators"에 금준영 추가** 또는 직접 설정 |
| 상태 이름 정확한 표기 | REST `project/statuses` | **결과: `해야 할 일` → `진행 중` → `완료`** (Task·Epic·Story·Bug 동일). 이슈 유형 표기: 작업(Task)·에픽·스토리·버그·하위 작업 |
| GitLab 티어 (Free/Premium) | `GET /metadata` | **결과: GitLab 18.11.5, `enterprise: false` = CE(Free).** 필수 승인·push rules 불가 확정 → 머지 권한 제한으로 진행 |
| GitLab 내 권한 | `project.permissions` | **결과: 금준영 = Developer(30).** 직접 멤버: 김수호 Maintainer(40) + 금준영·김주성·송승우·이수진·최재현 Developer(30). 코치·관리자는 그룹 상속. **0-d(보호 브랜치·머지 설정)는 Maintainer 필요 → 김수호가 금준영을 Maintainer로** 또는 직접 적용 |
| 공유 러너 유무 | Settings → CI/CD → Runners | 미확인 — 러너 API가 Developer에게 403. `shared_runners_enabled=true`이지만 인스턴스에 러너가 있는지는 Maintainer 승급 후 확인 |
| 현재 GitLab 설정 | API | develop·master 보호(push No one / merge Maintainers) ✅ 이미 적용 · merge_method=merge · squash=default_off(→ default_on) · 소스 브랜치 삭제 기본 ✅ · 스레드 해결 필수 ✗(→ on) · 파이프라인 필수 ✗(1-c) |
| 기존 Jira 이슈 | JQL | **29건 이미 존재** (S15P21B202-1~29), 제목에 `[파트]` 없음, 일부 스토리 타입 → 컨벤션 적용 시 기존 이슈 제목·레이블 일괄 정리 필요(0-a에 추가) |
| 사이트 레이블 | REST `label` | 142개(타 팀 것 포함). 우리 9종 + `needs-fix`는 신규 생성, 대소문자 정확히 |
| Jira ↔ GitLab 연동 앱 | 이슈 화면 개발 패널 "브랜치 만들기" | ✅ 확인됨 (2026-08-25) |

## 7. 갱신된 문서 (v1.0 당시)

> 지금의 문서 목록·색인은 [docs/README.md](./README.md) 를 본다. 아래는 이 계획을 확정할 때 무엇을 함께 고쳤는지의 기록이다.

| 문서 | 반영 |
| --- | --- |
| `docs/jira-convention.md` | 단일 흐름, 필수 필드·레이블·SP 척도, 진행 중/완료 진입 규칙 |
| `docs/git-convention.md` | 브랜치 생성 = Jira 버튼, 서버 이름 강제, 브랜치 정리 규칙, MR·승인 규칙 |
| `docs/jira-gitlab-automation.md` | 규칙 R1~R9 명세 + 전환 제한 + GitLab 설정 |
| `docs/convention-proposal.md` | v1.2 요약으로 갱신 (2026-08-28 에 `docs/conventions.md` 로 이름 변경) |
| `.gitlab/merge_request_templates/Default.md` · `docs/templates/*` | 유지 |

## 8. 권한 제약 하 운영 모드 (2026-08-26 결정)

[Infra](금준영)는 **GitLab Developer · Jira 일반 사용자**로 유지된다(승급 없음). 따라서 권한이 필요한 설정은 팀장(김수호, GitLab Maintainer)이 클릭하고, [Infra]는 그 외 전부를 준비·실행한다.

| 구분 | [Infra]가 직접 (지금) | 팀장 클릭 필요 (당시 팀장 체크리스트로 관리 — 지금은 [ci-cd-pipeline.md](./ci-cd-pipeline.md) §8) |
| --- | --- | --- |
| GitLab | 문서·템플릿·훅·`.mcp.json`·CI 파일을 이슈 브랜치 → MR로 올리기 · 브랜치 정리 요청 | 보호 브랜치 `*`/`S15P21B202-*`/`hotfix/*` 추가 · squash 기본 · 스레드 해결 필수 · 러너 확인 · 보호된 작업 브랜치 삭제 |
| Jira | 레이블 10종 생성 · 기존 이슈 제목/레이블/담당자 정리(팀장 OK 후) · 테스트 이슈로 흐름 검증 · 수동 전환(자동화 전까지) | Automation R1~R9 · (Jira 관리자) 필수 필드 · 워크플로우 전환 제한 · Epic 담당자 결정 |
| 이미 되어 있는 것 | — | develop·master 보호(merge Maintainers) → **"팀장만 머지" 관문은 지금도 유효** |

- **팀장이 Jira 프로젝트 관리자가 아닐 수도 있다**(역할 조회 401로 미확인). 그 경우 SSAFY 관리자에게 Administrators 역할 요청 → 불가하면 **Plan B: 로컬 동기화 봇** — 금준영 토큰으로 5분 주기 GitLab 브랜치/MR을 읽어 Jira 전환·보정(TRANSITION_ISSUES·EDIT_ISSUES·MODIFY_REPORTER 권한 확인됨). 한계: 봇 PC가 켜져 있어야 하고 액션이 금준영 이름으로 남음.
- 자동화가 켜지기 전까지의 과도기: 상태 전환은 담당자가 직접 옮긴다(되돌림 규칙도 없으므로 브랜치 먼저 → 카드 이동 순서를 스크럼에서 확인).

---

## 부록 A · Jira Automation 규칙 명세 (한도 절약형 — 트리거 1개에 액션 묶음)

> ⚠️ 아래는 **v1.0 확정 당시의 설계안**이다. 실제로 적용된 규칙의 현재 명세는 [jira-gitlab-automation.md §2](./jira-gitlab-automation.md) 가 단일 출처다. 특히 파트 태그는 그 뒤 **13종**으로 늘었고(2026-08-27), R1 의 보고자 액션은 폐기됐고, R2 에는 상위 Epic 필수 검사가 붙었다.

| # | 이름 | 트리거 | 조건 | 액션 | 실행 카운트 |
| --- | --- | --- | --- | --- | --- |
| **R1** | 이슈 생성 보정 | Issue created | 타입 ≠ Epic | ① 설명 비어 있으면 템플릿 삽입 ② Reporter = `{{issue.parent.assignee}}` ③ Labels += `{{issue.summary.match("^\[(\w+)\]")}}` ④ 기한 비어 있고 스프린트 있으면 Due = `{{issue.customfield_10020.last.endDate.jiraDate}}` (①④는 If 블록) | 이슈당 1 |
| **R2** | 제목 형식 검사 | Issue created / Field value changed(Summary) | — | If summary가 `^\[(Plan\|Art\|UI\|Sound\|Character\|Puzzle\|Network\|QA\|Infra)\] .+`에 **불일치** → 라벨 `needs-fix` + 코멘트 "@{{assignee}} 제목은 `[파트] 제목`" / Else → `needs-fix` 제거, 파트 레이블 재설정 | 이슈당 1 (+수정 시) |
| **R4** | 브랜치 생성 → 진행 중 | Branch created | 상태 = 해야 할 일 · 라벨 ∌ needs-fix | Transition → 진행 중 | 1 |
| **R5** | 손으로 끈 진행 중 되돌림 | Issue transitioned → 진행 중 · **"다른 규칙 트리거 허용" 끔** | (보조) `{{initiator}}` ≠ 실행자 | If JQL `(development[commits].all > 0 OR development[pullrequests].all > 0)` **AND** 스프린트·SP 있음 → 통과(액션 없음 = 카운트 0) / Else → Transition → 해야 할 일 + 코멘트 "진행 중은 이슈 화면의 **브랜치 만들기**로만 진입합니다(스프린트·포인트도 필요)" | 위반 시만 1 |
| **R6** | MR 머지 → 완료 | Pull request merged | — | Transition → 완료 | 1 |
| **R7** | MR 닫힘 알림 | Pull request declined | — | 코멘트 "MR이 머지 없이 닫혔습니다" | 드물게 1 |
| **R8** | 기한 자동 채움 | Field value changed(Sprint) | 기한 비어 있음 | Due = `{{issue.customfield_10020.last.endDate.jiraDate}}` | 스프린트 배정 시 1 |
| **R9** | 손으로 끈 완료 되돌림 | Issue transitioned → 완료 · **트리거 허용 끔** | — | Transition → 진행 중 + 코멘트 "완료는 MR 머지로만" | 위반 시만 1 |

- 실행자: 기본 "Automation for Jira" (회사 관리형 전환 제한 시 그대로 사용). 팀 관리형에서 전환 제한을 쓰면 R4·R6·R5·R9의 실행자를 [Infra] 계정으로.
- R5·R9의 "다른 규칙의 액션이 이 규칙을 트리거하도록 허용" 체크박스는 **반드시 꺼 둔다** — 켜면 R4의 전환에 R5가 반응해 되돌림 루프가 된다.
- 상태명은 보드 실제 표기(`해야 할 일`/`진행 중`/`완료`)와 일치시킬 것.

## 부록 B · GitLab 프로젝트 설정 (Free 티어 기준)

- [x] Settings → Repository → **Protected branches** — 현재 `master`·`develop`(push No one/merge Maintainers), `hotfix/S15P21B202-*`(Maintainers). `*`·`S15P21B202-*` 는 08-27 해제. Settings → Merge requests → "Delete source branch" 기본값 ON, squash 기본 ON
- [ ] Settings → Repository → Default branch: `develop`
- [ ] Settings → Merge requests → Merge method: Merge commit · Squash commits: **Encourage** · "Enable Delete source branch by default" ✔ (보호된 작업 브랜치에는 적용되지 않음 — §2.5)
- [ ] Settings → Merge requests → Merge checks: **All threads must be resolved** ✔ · Pipelines must succeed (1-c에서) ✔ · Skipped pipelines are considered successful ✔
- [ ] Members: 팀장·[Infra] = **Maintainer**, 나머지 = Developer
- [ ] `.gitlab/merge_request_templates/Default.md` 자동 적용 확인
- [ ] (Premium일 때만) Merge request approvals: Approvals required 1
- [x] ~~주 1회 머지된 브랜치 삭제~~ → 08-27 부터 머지 시 자동 삭제. 해제 전 남은 브랜치는 팀 결정으로 유지

## 부록 C · CI 잡 명세 (1·2단계)

```yaml
workflow:
  rules:
    - if: $CI_PIPELINE_SOURCE == "merge_request_event"

mr-lint:            # 제목·브랜치·Jira 키 실존(Jira REST /issue/KEY, 읽기 토큰)·템플릿 섹션·체크박스
  stage: lint
  script: [ "bash ci/mr-lint.sh" ]

repo-hygiene:       # .meta 짝, 금지 경로(Library/, *.csproj, Builds/), LFS 포인터
  stage: lint
  script: [ "bash ci/repo-hygiene.sh" ]

unity-editmode:     # 2단계 — Unity 설치된 Windows 러너
  stage: test
  tags: [ unity-win ]
  script:
    - '& "$env:UNITY_PATH" -batchmode -nographics -projectPath METAGAME -runTests -testPlatform EditMode -testResults results.xml -logFile -'
  artifacts: { reports: { junit: results.xml } }

ai-review:          # 2단계 — 비차단
  stage: review
  allow_failure: true
  script: [ "bash ci/ai-review.sh" ]
```

검사 규칙(mr-lint): 제목 `^\[(Plan|Art|UI|Sound|Character|Puzzle|Network|QA|Infra)\] (feat|fix|asset|refactor|docs|test|perf|build|chore)(\(.+\))?: .+ \([A-Z][A-Z0-9]+-[0-9]+\)$` ·
소스 브랜치 `^(hotfix/)?[A-Z][A-Z0-9]+-[0-9]+(-[a-z0-9._-]+)?$` · 본문에 `## Jira`, `## 무엇을 · 왜`, `## 확인 방법`, `## 작성자 체크리스트` 존재 · `- [ ]` 잔존 시 실패 · Jira 키 GET 404 시 실패.

## 부록 D · 검증 출처

- GitLab 보호 브랜치(와일드카드, 가장 허용적 규칙, 생성 제한, 삭제): https://docs.gitlab.com/user/project/repository/branches/protected/
- GitLab MR 승인 티어(Free는 선택 승인만): https://docs.gitlab.com/user/project/merge_requests/approvals/ · 승인 설정 Premium: https://docs.gitlab.com/user/project/merge_requests/approvals/settings/
- GitLab Pipelines must succeed(Free): https://docs.gitlab.com/user/project/merge_requests/auto_merge/ · 스레드 해결 머지 체크: https://docs.gitlab.com/user/project/merge_requests/
- GitLab 설명 템플릿 `Default.md`: https://docs.gitlab.com/user/project/description_templates/ · Push rules Premium: https://docs.gitlab.com/user/project/repository/push_rules/
- GitLab for Jira Cloud 앱(셀프호스팅 지원, 동기화 항목, 브랜치 생성): https://docs.gitlab.com/integration/jira/connect-app/
- Jira Automation 트리거(DevOps): https://support.atlassian.com/cloud-automation/docs/jira-automation-triggers/ · 개발 스마트값: https://support.atlassian.com/cloud-automation/docs/jira-smart-values-development/ · 사용자 `{{initiator}}`: https://support.atlassian.com/cloud-automation/docs/jira-smart-values-users/ · 텍스트 함수: https://support.atlassian.com/cloud-automation/docs/jira-smart-values-text-fields/
- Jira 사용량 계산(조건 실패는 미카운트): https://support.atlassian.com/cloud-automation/docs/how-is-my-usage-calculated/ · 플랜별 한도: https://support.atlassian.com/automation/kb/difference-between-automation-service-limits-and-automation-usage-limits/
- 스프린트 종료일 → 기한(`.jiraDate`): https://support.atlassian.com/jira/kb/automation-the-due-date-set-by-automation-rule-which-is-copied-from-sprint-end-date-is-different/
- 팀 관리형 워크플로우 규칙: https://support.atlassian.com/jira-software-cloud/docs/add-or-remove-workflow-rules-in-team-managed-projects/ · "Automation for Jira" 선택 불가: https://jira.atlassian.com/browse/JRACLOUD-80778 · 팀 관리형 필수 필드: https://support.atlassian.com/jira-software-cloud/docs/customize-an-issues-fields-in-team-managed-projects/
- "Hide transition from user"가 자동화를 막는 문제: https://jira.atlassian.com/browse/JSDSERVER-4783 · 역할 조건 우회: https://community.atlassian.com/forums/Jira-questions/Hide-transition-from-user-but-allow-automation-to-execute-it/qaq-p/1264468
- JQL development 필드(Cloud: branches.all / pullrequests.all·open): https://help.gitkraken.com/git-integration-for-jira-cloud/jql-searching-for-commits-and-pull-requests-gij-cloud/ · https://community.atlassian.com/forums/Jira-questions/JQL-documentation-on-development-property/qaq-p/1195282
- Unity 테스트 CLI: https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html
