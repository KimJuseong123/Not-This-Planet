# Jira ↔ GitLab 자동화 설정 가이드

> 관련 문서: [워크플로우 구현 계획](./workflow-plan.md) (검증 근거·출처) · [Jira 컨벤션](./jira-convention.md) · [Git 컨벤션](./git-convention.md)
> 기준 버전: workflow-plan v1.0 (2026-08-26)

## 목표

```
이슈 생성 ──▶ [해야 할 일]   ⚙ 보고자·레이블·기한·설명 자동 보정, 제목 형식 검사
브랜치 생성 ─▶ [진행 중]     ⚙ 손으로 끌면 되돌림 (브랜치·스프린트·포인트 없을 때)
MR 머지 ────▶ [완료]         ⚙ 손으로 끌면 무조건 되돌림
```

전제: Jira–GitLab 연동 앱(GitLab for Jira Cloud)이 설치되어 이슈 화면에 "브랜치 만들기"가 보임 — **2026-08-25 확인됨.**

## 0. 환경 확인 결과 (2026-08-26~27)

| 항목 | 결과 |
| --- | --- |
| Jira Automation 월 한도 | 사용량 페이지에 한도 미표기 → 무시. 넘으면 규칙이 멈춰 드러남 |
| 프로젝트 유형 | **회사 관리형(classic)** — 필수 필드 지정·워크플로우 편집은 Jira 사이트 관리자 영역 |
| 권한 | 금준영: GitLab Developer(설정은 팀장이 적용), Jira는 임시 프로젝트 관리자로 규칙 생성 완료 |
| 상태 이름 | UI `해야 할 일`/`진행 중`/`완료` — 내부적으로는 영문 `To Do`/`In Progress`/`Done` |
| GitLab | 18.11.5 CE(Free) — 필수 승인 불가 → develop 머지 권한 Maintainer로 대체 |
| 스프린트 필드 | `customfield_10020`, 표시명 한글 → 스마트값은 필드 ID로 접근 (§7 주의) |

**실행 카운트 규칙:** 액션을 1개 이상 실행한 실행만 카운트된다. 조건에서 멈추면 카운트 없음. 액션이 여러 개여도 1회.

## 1. Jira 사전 설정

1. **레이블:** 미리 만들 필요 없음 — R2가 제목의 `[파트]`를 읽어 자동으로 붙이고, `needs-fix`도 규칙이 생성한다. 값은 파트 태그와 글자까지 동일(대소문자 구분).
2. **필수 필드:** Task 유형에서 **담당자 · 상위 항목 · 기한**을 필수로.
   - 팀 관리형: 프로젝트 설정 → 이슈 유형 → Task → 각 필드의 "필수" 체크 (프로젝트 관리자)
   - 회사 관리형: 필드 구성에서 Required (Jira 관리자에게 요청)
3. **설명 템플릿 본문**을 [`docs/templates/jira-issue-description.md`](./templates/jira-issue-description.md)에서 복사해 둔다 (R1에 붙임).

## 2. Automation 규칙 R1~R9


프로젝트 설정 → **자동화 → 규칙 만들기.** 범위는 이 프로젝트. 실행자는 기본 "Automation for Jira" (팀 관리형에서 §3 전환 제한을 적용할 때만 [Infra] 계정으로 변경).

### R1 · 이슈 생성 보정
- 트리거 **이슈 생성됨**
- 조건 이슈 유형 ≠ Epic
- 액션 **If** 설명 비어 있음 → **이슈 편집** 설명 = 템플릿 본문
- 액션 **이슈 편집** 보고자 = `{{issue.parent.assignee}}`
  (첫 테스트에서 비어 있으면: 액션 **이슈 조회(Lookup issues)** JQL `key = {{issue.parent.key}}` → 보고자 = `{{lookupIssues.first.assignee}}`)
- 액션 **이슈 편집** 레이블 추가 = `{{issue.summary.match("^\[(\w+)\]")}}`
- 액션 **If** 기한 비어 있음 AND 스프린트 있음 → **이슈 편집** 기한 = `{{issue.customfield_10020.last.endDate.jiraDate}}`

### R2 · 제목 형식 검사
- 트리거 **이슈 생성됨** + **필드 값 변경됨**(요약) — 규칙 2개 또는 하나에 두 트리거 불가하면 복제
- 액션 **If/Else**: 고급 비교 `{{issue.summary}}` **정규식 불일치** `^\[(Plan|Art|UI|Sound|Character|Puzzle|Network|QA|Infra)\] .+`
  - If → 레이블 추가 `needs-fix` · 코멘트 `@{{issue.assignee.displayName}} 제목은 [파트] 제목 형식이어야 합니다 (docs/parts.md)`
  - Else → 레이블 제거 `needs-fix` · 레이블에서 기존 파트 값 제거 후 `{{issue.summary.match("^\[(\w+)\]")}}` 추가

### R4 · 브랜치 생성 → 진행 중
- 트리거 **브랜치 생성됨** (DevOps 그룹)
- 조건 상태 = `해야 할 일` · 레이블 ∌ `needs-fix`
- 액션 **이슈 전환** → `진행 중`

### R5 · 손으로 끈 "진행 중" 되돌림
- 트리거 **이슈 전환됨** (도착: `진행 중`)
- 규칙 상세 → **"다른 규칙의 액션이 이 규칙을 트리거하도록 허용" 체크 해제** (필수 — R4의 전환에 반응하면 루프)
- (보조 조건) 고급 비교 `{{initiator.displayName}}` ≠ `Automation for Jira`
- 액션 **If/Else**: **JQL 조건** `(development[commits].all > 0 OR development[pullrequests].all > 0) AND sprint is not EMPTY AND "Story Points" is not EMPTY`
  - If → (액션 없음: 통과, 카운트되지 않음)
  - Else → **이슈 전환** → `해야 할 일` · 코멘트 `진행 중은 이슈 화면의 **브랜치 만들기**로만 진입합니다. 스프린트와 스토리 포인트도 채워 주세요.`

  > 스토리 포인트 필드명은 이 프로젝트에서 `Story Points`(customfield_10031)로 확인됨.
  > `development[branches]`는 Cloud JQL에 없음(실측) → 이슈 키가 든 커밋/MR 존재로 대체. 정상 경로는 R4가 처리.

### R6 · MR 머지 → 완료
- 트리거 **풀 리퀘스트 병합됨**
- 액션 **이슈 전환** → `완료`

### R7 · MR 닫힘 알림
- 트리거 **풀 리퀘스트 거부됨(declined)**
- 액션 코멘트 `MR이 머지 없이 닫혔습니다. 다시 열거나 새 MR을 만드세요.`

### R8 · 기한 자동 채움
- 트리거 **필드 값 변경됨**(스프린트)
- 조건 기한 비어 있음
- 액션 **이슈 편집** 기한 = `{{issue.customfield_10020.last.endDate.jiraDate}}`

### R9 · 손으로 끈 "완료" 되돌림
- 트리거 **이슈 전환됨** (도착: `완료`) · **"다른 규칙 트리거 허용" 체크 해제**
- 조건 이슈 유형 ≠ Epic (Epic은 담당자가 수동 완료)
- 액션 **이슈 전환** → `진행 중` · 코멘트 `완료는 MR 머지로만 처리됩니다.`

## 3. (권한 있으면) 워크플로우 전환 제한 — 드래그 자체 차단

| 프로젝트 유형 | 설정 | 주의 |
| --- | --- | --- |
| **회사 관리형** | 워크플로우 편집 → 전환 `해야 할 일 → 진행 중`, `진행 중 → 완료`에 조건 **"User is in project role"** = `atlassian-addons-project-access` | "Automation for Jira"가 기본으로 속한 역할. **"Hide transition from user" 조건은 자동화도 막으므로 사용 금지** |
| **팀 관리형** | 워크플로우 → 전환 → 규칙 **"이슈 이동 제한(Restrict who can move)"** → [Infra] 계정만 허용. R4·R5·R6·R9의 **실행자를 [Infra] 계정으로** 변경 | "Automation for Jira"는 선택지에 없음(미해결 이슈) |

적용 후에도 R5·R9는 안전망으로 유지한다.

## 4. GitLab 설정 (Maintainer, Free 티어)

### 4.1 보호 브랜치 — Settings → Repository → Protected branches

| 패턴 | Allowed to push | Allowed to merge | Allowed to force push |
| --- | --- | --- | --- |
| `*` | **No one** | **No one** | off |
| `S15P21B202-*` | Developers + Maintainers | No one | **on** |
| `hotfix/S15P21B202-*` | Maintainers | No one | on |
| `develop` | No one | **Maintainers** | off |
| `master` | No one | Maintainers | off |

- 여러 규칙이 겹치면 **가장 허용적인 규칙**이 적용된다. `*`가 "생성 금지"의 바탕이 되고, 키 패턴 규칙이 생성을 열어 준다.
- `*`는 push와 merge를 **모두** No one으로 — merge 권한만 있어도 브랜치 생성이 허용되기 때문.
- 부작용: 작업 브랜치가 보호 대상 → 머지 시 "소스 브랜치 삭제"가 비활성. **[Infra]가 주 1회** Repository → Branches → Merged 필터에서 삭제.

### 4.2 머지 설정 — Settings → Merge requests

- Merge method **Merge commit** · Squash commits **Encourage** · Enable "Delete source branch" by default ✔
- Merge checks: **All threads must be resolved** ✔ · Pipelines must succeed (러너 확보 후) ✔ · Skipped pipelines are considered successful ✔
- (Premium일 때만) Approval rules: Approvals required 1

### 4.3 멤버 — Settings → Members
- 팀장 · [Infra] = **Maintainer** · 나머지 = Developer

### 4.4 기본 브랜치 — Settings → Repository → Default branch: `develop`

## 5. 동작 테스트

1. Jira에 `[Infra] 워크플로우 자동화 테스트` Task 생성 (Epic·담당자·기한 채움) → 보고자·레이블·기한 자동 보정 확인 (R1)
2. 제목을 `테스트`로 바꿔 보기 → `needs-fix` 붙는지 → 다시 `[Infra] …`로 → 제거되는지 (R2)
3. 보드에서 카드를 진행 중으로 **끌어 보기** → 되돌아오고 코멘트 달리는지 (R5)
4. 이슈 화면 → 브랜치 만들기 → develop 소스 → 생성 → **진행 중**으로 바뀌는지 (R4)
5. 로컬에서 `git push origin HEAD:test-branch` → **서버가 거부**하는지 (보호 브랜치 `*`)
6. 빈 커밋 하나 → MR 생성(템플릿 자동 적용 확인) → 카드를 완료로 끌어 보기 → 되돌아오는지 (R9)
7. 팀장 승인·머지 → **완료**로 바뀌는지 (R6) → Jira 설정 → 자동화 → 사용량에서 실행 횟수 확인
8. 안 움직이면: 자동화 → **감사 로그**, GitLab Jira 앱 → 동기화 상태

## 6. 선택 확장
- 리뷰 상태가 필요해지면 `Pull request created` 트리거로 `리뷰 중` 상태 추가 가능 (현재 3단계 유지 결정).
- 비코드 기록 리마인더: 스케줄 규칙 JQL `status = "진행 중" AND updated <= -5d` → 담당자 코멘트.

## 7. 적용 현황 (2026-08-27)

| 규칙 | Jira 규칙 ID | 상태 | 비고 |
| --- | --- | --- | --- |
| R1 생성 보정 | 5799874 | ✅ 활성 | 설명 템플릿 · 보고자=에픽 담당자(담당자 있을 때) · 기한=스프린트 종료(스프린트 있을 때) |
| R2 제목 형식 검사 | 5800352 | ✅ 활성 | 트리거 "필드 값 변경(요약)". 형식 불일치 → `needs-fix` + 멘션 댓글 / 일치 → 제거 + 파트 레이블 |
| R4 브랜치 생성 → 진행 중 | 5793964 | ✅ 활성 | 조건: 상태=해야 할 일, JQL `labels is EMPTY OR labels not in (needs-fix)` |
| R5 진행 중은 브랜치로만 | 5793968 | ✅ 활성 | initiator ≠ 자동화 · IF JQL(커밋/MR 있음 AND 스프린트 AND Story Points) / ELSE 되돌림+댓글 |
| R6 MR 병합 → 완료 | 5793966 | ✅ 활성 | 조건 상태 ≠ 완료 |
| R7 MR 닫힘 알림 | 5800235 | ✅ 활성 | 트리거 "풀 리퀘스트 거부됨" |
| R8 기한 = 스프린트 종료 | 5800237 | ✅ 활성 | 트리거 "필드 값 변경(스프린트)", 다른 규칙 트리거 허용 ON |
| R9 완료는 머지로만 | 5799839 | ✅ 활성 | 유형 ≠ 에픽 · initiator ≠ 자동화 → 진행 중으로 되돌림 |
| (팀장) 자동 스프린트 활성 | 5782244 | ⏸ 비활성 | 팀장 의도는 "백로그 자동 생성"이었음 → 스프린트가 비면 곧 백로그이므로 규칙 불필요, 비활성화 |

- 생성 방식: UI에서 직접(R5·R9·R1 초안) + 공개 API 템플릿 껍데기 + 내부 API PUT(R2·R7·R8·R1 보정·R4 조건). 내부 API 규약은 계획서 부록 참고.
- 상태 이름은 내부적으로 영문(`To Do` / `In Progress` / `Done`)이며 UI에만 한글로 표시된다 — 규칙 JSON을 다룰 때 주의.
- **R3 번호가 비어 있는 이유:** v0.1의 R3 "브랜치 자동 생성(Jira → GitLab API)"은 팀 결정으로 폐기되었고, v0.2에서 "보고자·레이블 자동 설정"으로 재사용되었다가 v1.0에서 실행 한도 절약을 위해 R1(생성 보정)·R2(레이블)에 흡수되었다. 번호는 문서·기록 전반의 식별자로 쓰이므로 재번호 없이 비워 둔다.
- 규칙 이름은 `R# 동작 요약`, 설명은 `트리거 → 동작. 조건.` 형식으로 통일한다(설명이 비면 목록에 표시가 뜬다).

## 8. E2E 검증 결과 (2026-08-27, 이슈 S15P21B202-34 · MR !2)

| 단계 | 행동 | 기대 규칙 | 결과 |
| --- | --- | --- | --- |
| A | 잘못된 제목·빈 설명으로 생성 | R1 설명 템플릿 · R2 `needs-fix`+멘션 댓글 | ✅ 둘 다 20초 내 |
| B | 제목 `[Infra] …`로 수정 | R2 `needs-fix` 제거 + `Infra` 레이블 | ✅ |
| C | 활성 스프린트 배정 | R8 기한 = 스프린트 종료일 | ✅ (교정 후) 기한 `2026-08-28`. 처음엔 `{{issue.sprint.endDate.jiraDate}}`가 빈 값이라 실패 — 한글 필드명 환경에서는 `{{issue.customfield_10020.last.endDate.jiraDate}}` 필요 |
| D | 카드를 손으로 진행 중 | R5 되돌림 + 댓글 | ✅ 6초 |
| E | 이슈 키 브랜치 push | R4 진행 중 | ✅ 30초 내 |
| F | 기록 파일 커밋 → MR 생성 | 개발 패널 MR 연결 | ✅ 40초 내 |
| G | 카드를 손으로 완료 | R9 진행 중으로 되돌림 + 댓글 | ✅ 8초 |
| H | MR 머지 없이 닫기 → 재오픈 | R7 댓글 | ✅ 45초 내 |
| I | 팀장이 MR 머지 | R6 완료 | ⏳ 팀장 머지 대기 (MR !1·!2) |

- 테스트 산출물: `docs/records/S15P21B202-34-e2e-test.md` (MR !2). 테스트 브랜치 `S15P21B202-33-automation-test`, `S15P21B202-34-e2e-test`는 보호 대상이라 팀장이 삭제.

> **스프린트 종료일 스마트값 주의 (2026-08-27 실측):** 이 사이트는 스프린트 필드 표시명이 한글("스프린트")이라 `{{issue.sprint…}}`, `{{issue.Sprint…}}` 계열이 **모두 빈 값**으로 렌더링된다. 반드시 필드 ID로 접근: `{{issue.customfield_10020.last.endDate.jiraDate}}` (`.last`는 단일 스프린트에서도 동작하며, 이월로 스프린트가 여러 개 달린 이슈에서 마지막 스프린트를 고른다 — 실측). 종료 시각은 UTC 15:00(=KST 다음날 00:00)이므로 `.jiraDate`(UTC 기준)가 스프린트 마지막 날(08-28)을 내고, `convertToTimeZone("Asia/Seoul")`을 붙이면 다음날(08-29)이 되므로 붙이지 않는다.

## 부록 A · 규칙을 API로 고치는 법 (내부 API 규약, 2026-08-27 실측)

프로젝트 관리자 권한 + **브라우저 로그인 세션**으로만 동작한다(API 토큰은 읽기·수정 모두 403). 공개 Rule Management API는 `POST template/create`만 허용된다.

| 동작 | 요청 |
| --- | --- |
| 목록 | `POST {base}/{projectId}/rules` body `{"offset":0,"limit":50,"filter":"","actionTypes":[],"triggerTypes":[],"orchestrationTypes":[],"systemLabelId":"CONTAINER","sortKey":"NAME","sortOrder":"ASC"}` |
| 상세 | `GET {base}/{projectId}/rule/{숫자 id}` |
| 수정 | `PUT {base}/{projectId}/rule/{ruleUuid}` body `{"ruleConfigBean": <GET 결과를 고친 것>, "newConnections": []}` |
| 활성/비활성 | `PATCH {base}/{projectId}/rule/{ruleUuid}` body `[{"op":"replace","path":"/state","value":"ENABLED"}]` |
| 신규 | 내부 `POST /rule`은 403 → 공개 API `POST …/automation/public/jira/{cloudId}/rest/v1/template/create` `{"templateId":"software_template_26","ruleHome":"<프로젝트 ARI>","state":"DISABLED"}`로 껍데기를 만든 뒤 PUT으로 덮어쓴다 |

- `{base}` = `https://ssafy.atlassian.net/gateway/api/automation/internal-api/jira/{cloudId}/pro/rest`, projectId `22557`, cloudId `15a21def-11f5-4358-ab48-074194685963`.
- **새 컴포넌트의 id는 `"__NEW__COMPONENT__<uuid>"`** (null·임의 UUID는 "컴포넌트 ID 불일치/중복" 400). 중첩은 `children`/`conditions` 배열로.
- 컴포넌트 타입: `jira.issue.event.trigger:created|transitioned`, `jira.issue.field.changed`(value `{"changeType":"ANY_CHANGE","fields":[{"value":"<fieldId>","type":"field"}],"actions":[],"eventFilters":[ARI]}`), `devops.branch.event.trigger:created`, `devops.pr.event.trigger:merged|declined`, `jira.issue.condition`, `jira.comparator.condition`, `jira.jql.condition`(value=JQL 문자열), `jira.condition.container.block` + `jira.condition.if.block`(ELSE = 조건 없는 if.block), `jira.issue.transition`(destinationStatus NAME 영문), `jira.issue.comment`, `jira.issue.edit`(`advancedFields`에 Jira REST 편집 JSON 문자열), `jira.issue.refresh.issue`, `codebarrel.action.log`.
- 세션 확보는 Playwright(`launch_persistent_context`, channel="chrome")로 로그인 창을 띄운 뒤 같은 컨텍스트의 `page.request`로 호출. 동기 API 대기는 `time.sleep` 대신 `page.wait_for_timeout`.
## 부록 B · 자동화 편집기 조작법 (UI에서 규칙을 만들거나 고칠 때)

```
규칙 만들기
 └ ① 트리거 선택 (검색창에 이름 입력) → 옵션 설정 → [다음]
 └ ② [구성 요소 추가] 반복:  조건 | 작업 | IF/ELSE 블록 | 분기
      각 구성 요소 설정 후 [다음]
 └ ③ 오른쪽 위 [규칙 켜기] → 규칙 이름 입력 → [규칙 켜기]
```

- **규칙 세부 정보**(왼쪽 상단 규칙 이름 클릭): 범위 = 이 프로젝트 · 실행 주체(Actor) = `Automation for Jira` · **"다른 규칙 작업이 이 규칙을 트리거하도록 허용" = 해제(기본값)**. R5·R9에서 특히 중요.
- **스마트 값**은 텍스트 입력란에 `{{...}}`를 그대로 친다. 사용자 필드(보고자 등)는 드롭다운에서 **"스마트 값"** 을 고른 뒤 입력하거나, 편집 작업 하단 **"추가 필드"(JSON)** 를 쓴다.
- **필드 조건**(작업 항목 필드 조건): 필드 / 조건(같음·같지 않음·비어 있음·비어 있지 않음·포함·포함하지 않음) / 값.
- **고급 비교 조건**: 첫 값 `{{스마트 값}}` / 조건(같음·같지 않음·정규식과 일치·정규식과 일치하지 않음…) / 두 번째 값.
- **JQL 조건**("작업 항목이 JQL과 일치"): JQL 입력 후 **검증** 버튼으로 문법 확인.
- 저장 후 규칙 목록에서 **감사 로그**로 실행 결과를 본다.


## 부록 C · 막힐 때

| 증상 | 원인 | 해결 |
| --- | --- | --- |
| 트리거 목록에 "브랜치 생성됨"이 없음/회색 | 소스 코드 도구 연동 없음 | 우리 프로젝트는 연동 확인됨 — 다른 사이트/프로젝트인지 확인 |
| JQL 조건 검증 실패 `development` | 문법 | 정확히 `(development[commits].all > 0 OR development[pullrequests].all > 0)`. 대괄호·소문자 |
| "전환을 수행할 수 없음" | 상태명 오타 / 워크플로우 조건이 자동화를 막음 | 상태 드롭다운에서 선택(직접 타이핑 X). §3 전환 제한을 걸었다면 실행 주체가 허용 역할인지 |
| R4로 옮긴 직후 R5가 되돌림 (루프) | R5의 "다른 규칙 작업이 이 규칙을 트리거하도록 허용"이 켜짐 | 해제 |
| 보고자 편집 실패 | 상위 에픽 담당자 없음 / 실행 주체에 "보고자 수정" 권한 없음 | 에픽 담당자 지정 · IF 블록 B 확인 |
| 레이블에 스마트 값이 문자 그대로 들어감 | 필드 UI가 스마트 값 미지원 | 추가 필드 JSON 방식으로 |
| `{{issue.customfield_10020.last.endDate.jiraDate}}`가 비어 있음 | 스프린트 여러 개/종료 스프린트 | `{{issue.customfield_10020.last.endDate.jiraDate}}` |
| 이슈 생성 시 필드가 비어 보임 | 생성 트리거 타이밍 | R1 첫 작업에 "데이터 다시 가져오기" |
| 규칙이 전혀 안 돔 | 규칙 꺼짐 / 범위가 다른 프로젝트 / 월 한도 초과 | 목록의 토글 · 규칙 세부 정보 범위 · 사용량 |
