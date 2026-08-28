# 팀장 체크리스트 — 남은 일

> 기준: [workflow-plan.md](./workflow-plan.md) v1.1 · 갱신 2026-08-27 10:10
> GitLab 설정(보호 브랜치·머지 규칙)과 Jira 자동화 8개는 **적용·검증 완료**, R6까지 실전 검증 끝([jira-gitlab-automation.md §7·§8](./jira-gitlab-automation.md)). 아래는 팀장 권한이나 결정이 필요한 것만.

## 1. 머지

- [x] !1 `S15P21B202-30-dev-conventions` → -30 완료 (R6 첫 실검증, 08-27 09:22)
- [x] !2 `S15P21B202-34-e2e-test` → -34 완료
- [x] !4 `S15P21B202-32-chapter1-plan`(송승우) → -32 완료
- [ ] !5 `S15P21B202-38-docs-followup` — 이 문서 포함 후속 갱신 (기록성 MR, "완료 조건"만 확인)
- [x] !3 `S15P21B202-21`(김주성) 머지됨(10:24) — MR 제목·본문에 키가 없어 R6 미발동 → [Infra]가 MR 본문에 키 보정, R9에 JQL 조건 추가 후 수동 완료로 처리 완료(11:0x). 재발 방지는 §4 공지 + 1단계 CI

## 2. 정리 (Maintainer/관리자만 가능)

- [x] ~~머지된 브랜치 삭제~~ — 2026-08-27 작업 브랜치 보호 해제 + 머지 시 자동 삭제 기본값 ON 으로 해소. 해제 전 남은 브랜치는 그대로 둠(팀 결정). (아래 스크립트는 참고용)
- 이전 안내 — **Maintainer/Owner만 가능**(보호 브랜치는 Developer가 API로도 삭제 불가, 08-27 실측 `You are not allowed to delete protected branches`). "Merged branches 삭제" 버튼도 보호 브랜치는 건너뛰므로 아래 스크립트를 주기적으로(스프린트 끝마다) 실행:

  ```powershell
  # 팀장 PC, glab 로그인 상태. 머지된 MR의 소스 브랜치만 삭제 (develop/master 제외)
  $glab = "$env:LOCALAPPDATA\Programs\glab\glab.exe"
  & $glab api "projects/1443653/merge_requests?state=merged&per_page=100" | ConvertFrom-Json |
    Where-Object { $_.source_branch -match "^(hotfix/)?S15P21B202-\d+" } | Select-Object -ExpandProperty source_branch -Unique |
    ForEach-Object { & $glab api -X DELETE "projects/1443653/repository/branches/$([uri]::EscapeDataString($_))" 2>$null; "삭제: $_" }
  ```

  현재 대상: `S15P21B202-30-dev-conventions`, `S15P21B202-32-chapter1-plan`, `S15P21B202-34-e2e-test`, (머지 후) `S15P21B202-38-docs-followup`. 테스트 브랜치 `S15P21B202-33-automation-test`, `S15P21B202-35-sprint-autoadd-test`는 MR이 없으니 이름으로 직접 삭제.
- [x] 테스트 이슈 -33·-35 삭제 완료 (2026-08-27, 금준영이 UI에서)
- [x] 금준영 Jira 프로젝트 관리자 권한 — **유지로 결정**(2026-08-27, 자동화 감사 로그 열람·규칙 유지보수용)
- [x] R1 보고자 액션 삭제 (11:00) · [x] R9 JQL 조건 추가 (10:48) → -21 완료 처리됨 · [x] R2 상위 Epic 필수 검사 추가·검증 (11:20, 부록 A 방식) · [x] MM 알림 v2.1(표 카드·@멘션) 적용·검증 (16:2x) · [x] R6/R7 브랜치 키 조건 재도입 (17:00, 다음 머지에서 최종 확인 후 탐침 제거)
- [x] -39 `백엔드 개발환경 세팅` → `[Backend]` 제목·레이블, 상위 Epic -43으로 이동 (11:30). -40·-46은 최재현이 `[Network]`로 태그했으므로 본인 판단(서버 쪽이면 -43)
- [x] **R1·R2 생성 직후 레이스 보정** 적용 (11:45, 부록 A 방식) — R1 "데이터 다시 가져오기" 3초 지연, R2 맨 앞에 3초 지연 재조회. 원인: -46 감사 로그에서 생성 2초 뒤 편집이 `이슈가 존재하지 않거나 권한 없음`으로 실패(인덱싱 지연)
- [ ] (선택) Jira 사이트 관리자에게 요청: Task 필수 필드(담당자·상위 항목·기한), 워크플로우 전환 제한 — 없어도 R1·R5·R9 되돌림으로 운영 가능

## 3. 결정 (권한 무관)

### 3.1 Epic·Task를 13종 파트 태그에 맞추기 — **적용 완료** (2026-08-27 10:22, [Infra]가 MCP로 일괄 변경)
- Epic 13개: 기존 9개(-8~-16)를 영문 태그로 개명·레이블 부여 + 신설 `S15P21B202-41 [Physics] 물리 엔진·행성 환경`, `-42 [Level] 레벨 구성·기믹`, `-43 [Backend] 서버·API`, `-44 [Data] 데이터·저장`
- Task: -20~-29·-32 제목 `[파트] …`·레이블 정리, 상위 항목 이동 -21·-26 → -41(Physics), -27 → -14(Puzzle), -28 → -42(Level), -22 활성 스프린트 편입(R8 기한 자동)
- 변경 전 제목은 각 이슈 히스토리에 남아 있음. 파트 정의는 [parts.md](./parts.md)

### 3.2 Epic 담당자(검증 책임 파트원) 지정 — **대기**
**2026-08-27 결정: 보고자 규칙 폐기(생성자 그대로). 대신 Task는 상위 Epic 필수(R2가 검사), Epic 담당자가 하위 Task 전체를 책임진다.** 담당자는 **Network·Infra·Data = 금준영, Backend = 최재현(확정)**, 나머지 9개는 **임시로 팀장(김수호)**(11:30). 파트별 대표가 확정되면 아래 표를 채워 알려 주면 [Infra]가 바꿉니다.

| Epic | 파트 | 담당자 |
| --- | --- | --- |
| -8 | Plan | (임시) 김수호 |
| -10 | Art | (임시) 김수호 |
| -12 | Sound | (임시) 김수호 |
| -11 | UI | (임시) 김수호 |
| -13 | Character | (임시) 김수호 |
| -41 | Physics | (임시) 김수호 |
| -14 | Puzzle | (임시) 김수호 |
| -42 | Level | (임시) 김수호 |
| -15 | Network | 금준영 |
| -43 | Backend | **최재현** (확정, 08-27) |
| -9 | Infra | 금준영 |
| -44 | Data | 금준영 |
| -16 | QA | (임시) 김수호 |

## 4. 팀 공지 (스크럼 때 1분) — **오늘 아침 손으로 옮긴 카드 7회가 전부 되돌아갔음**

- 진행 중은 이슈 화면 개발 패널의 **브랜치 만들기**로만(백로그 이슈도 브랜치를 만들면 활성 스프린트에 자동 추가), 완료는 **MR 머지**로만 — 카드를 끌면 되돌아오고 댓글이 달립니다
- 이슈 제목은 `[파트] 제목`(13종은 parts.md), 담당자 필수 — 어긋나면 `needs-fix`가 자동으로 붙습니다
- MR 제목은 `[파트] type: 요약 (S15P21B202-번호)` — GitLab 기본 제목(브랜치명) 그대로 두지 말 것. **본문 템플릿의 `이슈: S15P21B202-번호` 줄은 반드시 채우고 지우지 말 것** — 키가 제목·본문 어디에도 없으면 Jira에 MR이 연결되지 않아 머지해도 완료로 안 바뀜(오늘 -21 사례). Draft로 만들지 말 것
- 새 이슈는 백로그에 생기고, 스프린트에 넣으면 기한이 자동으로 채워집니다
- 클론 후 `git config core.hooksPath .githooks`, `git lfs install` 1회

## 5. 1·2단계(CI·알림) — 팀장(Maintainer) 권한이 필요한 것 · 2026-08-27 추가

절차 상세는 [ci-and-notifications.md](./ci-and-notifications.md). 순서대로 하면 됩니다(약 20분).

- [x] **CI/CD 변수 등록** (Settings → CI/CD → Variables — 토큰류만 Masked, `CI_ENABLE`·`AI_REVIEW_CLI` 같은 플래그는 Visible; 마스킹은 8자 이상만 허용): `CI_ENABLE=true`(러너 준비 후) · `JIRA_USER`·`JIRA_API_TOKEN`(Jira 읽기 전용 토큰 — 팀장 계정에서 발급) · `GITLAB_TOKEN`(Settings → Access Tokens → 프로젝트 액세스 토큰, 역할 Developer, 스코프 `api`, 만료 프로젝트 종료일) · `MM_WEBHOOK_URL`(Mattermost 수신 웹훅) · **`AI_REVIEW_CLI=true`**(AI 리뷰는 러너 호스트의 로그인된 Claude Code CLI 로 — Team 플랜, API 키 불필요; 러너가 이 PC 의 Claude 로그인을 쓰게 하는 `runner-claude-env.ps1` 는 금준영이 실행)
- [x] **프로젝트 러너 등록(필수)** — 완료 2026-08-27 `METAGAME-DESKTOP-UB20P0O`(shell=pwsh, CLAUDE_CONFIG_DIR 주입). 공유 러너 없음 확정(MR !10 파이프라인 9분 pending). **상시 러너는 지급 EC2**(`scripts/runner/setup-linux-runner.sh`, Docker), 지급 전엔 팀 PC 임시 Windows 러너. Settings → CI/CD → Runners → New project runner(태그 `windows`, Run untagged ✔) → 토큰을 등록자에게 직접 전달 → 등록자가 `scripts/runner/setup-windows-runner.ps1` 실행(관리자 PowerShell, 절차 문서 §4) → 러너 초록불 확인 → 변수 **`CI_ENABLE=true`**
- [x] Mattermost 통합(GitLab 측) 활성 — 2026-08-27. **후속: Trigger 에서 `Merge request` 체크 해제**(CI `mm-mr-opened` 가 팀장 @멘션 포함으로 대체), `Pipeline` 만 유지(Notify only broken ✔, Branches = Default branch). Username 재정의는 MM 서버가 막아 발신자가 금준영 BOT 으로 보임(수용)
- [ ] **스케줄 파이프라인**: Build → Pipeline schedules → 평일 09:30 `develop` (mm-digest 가 needs-fix·기한 초과·정체 이슈·오래 열린 MR 요약 전송)
- [ ] (안정화 1주 후) Settings → Merge requests → **Pipelines must succeed** 켬 → 위반 MR은 머지 버튼이 비활성
- [ ] (Unity 러너 준비 후) 변수 `UNITY_RUNNER=true`, `UNITY_PATH` = Unity **6000.5.4f1** Editor 경로(현재 SSAFY PC엔 6000.0.81f1만 설치 — 버전 맞춰야 함), 러너 태그 `unity-win`

## 6. 2026-08-27 저녁 추가 (E2E 준비 · 워크플로우 v2.1 피드백에서 나온 것)

| # | 할 일 | 어디서 | 비고 |
| --- | --- | --- | --- |
| 6-1 | **아침 점검 스케줄 등록** — CI/CD → Schedules → New: 설명 `mm-digest`, Interval `30 9 * * 1-5`, Timezone `Asia/Seoul`, Target `develop` | GitLab | 보호 브랜치라 소유자가 Maintainer 여야 파이프라인이 생성됨. 등록 후 ▶ 로 1회 실행해 📋 카드 확인 |
| 6-2 | ~~규칙 실패 이메일 끄기~~ → **켜 둔다**(2026-08-27 결정). 아침 점검의 "머지됐는데 완료 안 된 이슈"(-83)는 보조 장치 | Jira | 규칙 소유자(금준영) 이메일로 계속 수신 |
| 6-3 | MR !35(-81 LFS 훅) · !36(-82 AI 리뷰 v2) · !37(-83 완료 누락 감지) · -79(워크플로우 문서) 머지 | GitLab | !36 은 파이프라인의 `ai-review` 코멘트가 새 형식인지 보고 머지. !37 머지 후 6-1 스케줄 ▶ 1회 실행 |
| 6-4 | (선택) Mattermost 통합(Pipeline) 끄기 — develop 에는 파이프라인이 생기지 않아 어차피 조용함 | GitLab | 노이즈 없음, 그대로 둬도 무해 |
