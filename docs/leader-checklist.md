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

- [ ] 머지된 브랜치 삭제 — **Maintainer/Owner만 가능**(보호 브랜치는 Developer가 API로도 삭제 불가, 08-27 실측 `You are not allowed to delete protected branches`). "Merged branches 삭제" 버튼도 보호 브랜치는 건너뛰므로 아래 스크립트를 주기적으로(스프린트 끝마다) 실행:

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
- [x] R1 보고자 액션 삭제 (11:00) · [x] R9 JQL 조건 추가 (10:48) → -21 완료 처리됨 · [x] R2 상위 Epic 필수 검사 추가·검증 (11:20, 부록 A 방식)
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
