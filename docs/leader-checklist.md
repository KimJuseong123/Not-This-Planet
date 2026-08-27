# 팀장 체크리스트 — 남은 일

> 기준: [workflow-plan.md](./workflow-plan.md) v1.1 · 갱신 2026-08-27 10:10
> GitLab 설정(보호 브랜치·머지 규칙)과 Jira 자동화 8개는 **적용·검증 완료**, R6까지 실전 검증 끝([jira-gitlab-automation.md §7·§8](./jira-gitlab-automation.md)). 아래는 팀장 권한이나 결정이 필요한 것만.

## 1. 머지

- [x] !1 `S15P21B202-30-dev-conventions` → -30 완료 (R6 첫 실검증, 08-27 09:22)
- [x] !2 `S15P21B202-34-e2e-test` → -34 완료
- [x] !4 `S15P21B202-32-chapter1-plan`(송승우) → -32 완료
- [ ] !5 `S15P21B202-38-docs-followup` — 이 문서 포함 후속 갱신 (기록성 MR, "완료 조건"만 확인)
- [ ] !3 Draft `S15P21B202-21`(김주성) — 작성자가 Draft 해제하면 리뷰

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
- [ ] 테스트 이슈 삭제: `S15P21B202-33`, `S15P21B202-35` — [Infra]가 자동화 검증용으로 만든 이슈(각각 브랜치 생성→진행 중, 백로그→스프린트 자동 추가 테스트). 실제 작업이 아니므로 스프린트 통계에서 빼려면 삭제가 맞음. OK 시 [Infra]가 MCP로 삭제
- [x] 금준영 Jira 프로젝트 관리자 권한 — **유지로 결정**(2026-08-27, 자동화 감사 로그 열람·규칙 유지보수용)
- [ ] (선택) Jira 사이트 관리자에게 요청: Task 필수 필드(담당자·상위 항목·기한), 워크플로우 전환 제한 — 없어도 R1·R5·R9 되돌림으로 운영 가능

## 3. 결정 2개 (권한 무관)

### 3.1 Epic 제목·구조를 13종 파트 태그에 맞추기 — 제안
Epic 9개는 아직 한글 태그입니다. R2(제목 형식 검사)와 레이블이 영문 13종([parts.md](./parts.md))을 기준으로 하므로 **Epic 제목도 영문 태그로** 바꾸고, 파트가 갈라진 두 Epic은 분리하는 것을 제안합니다. OK만 하면 [Infra]가 MCP로 일괄 변경합니다(Task -20~-29·-32의 `[파트]` 제목·레이블·상위 항목 정리 포함. -36·-37은 이미 `[Art]` 형식).

| 현재 Epic | → 제안 | 레이블 |
| --- | --- | --- |
| S15P21B202-8 `[기획/설계] 게임 기획 및 레벨 디자인` | `[Plan] 게임 기획 및 레벨 디자인` | Plan |
| S15P21B202-10 `[3D 그래픽] 3D 캐릭터/맵 에셋 및 파이프라인` | `[Art] 3D 캐릭터/맵 에셋 및 파이프라인` | Art |
| S15P21B202-11 `[UI/UX] UI/UX 프레임워크 및 메뉴 시스템` | `[UI] UI/UX 프레임워크 및 메뉴 시스템` | UI |
| S15P21B202-12 `[사운드/오디오] 오디오 시스템 및 사운드 에셋` | `[Sound] 오디오 시스템 및 사운드 에셋` | Sound |
| S15P21B202-13 `[핵심 로직] 캐릭터 조작 및 물리 엔진` | `[Character] 캐릭터 조작` + 신설 `[Physics] 물리 엔진·행성 환경` (-21·-26 → Physics) | Character / Physics |
| S15P21B202-14 `[게임플레이] 레벨 상호작용 및 퍼즐 시스템` | `[Puzzle] 퍼즐·상호작용 시스템` + 신설 `[Level] 레벨 구성·기믹` (-27 → Puzzle, -28 → Level) | Puzzle / Level |
| S15P21B202-15 `[네트워크] 멀티플레이어 및 동기화` | `[Network] 멀티플레이어 및 동기화` | Network |
| S15P21B202-16 `[QA/최적화] 테스트 품질 관리 및 성능 최적화` | `[QA] 테스트 품질 관리 및 성능 최적화` | QA |
| S15P21B202-9 `[인프라/환경] 개발 환경 및 Unity 초기화` | `[Infra] 개발 환경 및 Unity 초기화` | Infra |
| (신설) | `[Backend] 서버·API`, `[Data] 데이터·저장` | Backend / Data |

### 3.2 Epic 담당자(검증 책임 파트원) 지정
Epic 모두 담당자가 비어 있습니다. 컨벤션상 Epic 담당자 = 최종 검증 책임자, 보고자 = 팀장. 담당자가 정해지면 R1이 하위 Task의 보고자를 그 사람으로 자동 지정하기 시작합니다. 파트별 담당자 이름만 주면 [Infra]가 입력합니다.

## 4. 팀 공지 (스크럼 때 1분) — **오늘 아침 손으로 옮긴 카드 7회가 전부 되돌아갔음**

- 진행 중은 이슈 화면 개발 패널의 **브랜치 만들기**로만(백로그 이슈도 브랜치를 만들면 활성 스프린트에 자동 추가), 완료는 **MR 머지**로만 — 카드를 끌면 되돌아오고 댓글이 달립니다
- 이슈 제목은 `[파트] 제목`(13종은 parts.md), 담당자 필수 — 어긋나면 `needs-fix`가 자동으로 붙습니다
- MR 제목은 `[파트] type: 요약 (S15P21B202-번호)` — GitLab 기본 제목(브랜치명) 그대로 두지 말 것. 템플릿 섹션은 지우지 말 것
- 새 이슈는 백로그에 생기고, 스프린트에 넣으면 기한이 자동으로 채워집니다
- 클론 후 `git config core.hooksPath .githooks`, `git lfs install` 1회
