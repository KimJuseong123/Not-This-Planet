# 프로젝트 문서

팀 개발 환경·컨벤션 문서 모음. 새 팀원은 위에서 아래 순서로 읽으면 된다.

| 문서 | 내용 |
| --- | --- |
| [workflow-plan.md](./workflow-plan.md) | **워크플로우 구현 계획 v1.1 (확정·적용 완료)** — 결정 근거 · 실현 가능성 검증 · 단계 |
| [convention-proposal.md](./convention-proposal.md) | 확정 컨벤션 v1.2 한 장 요약 |
| [parts.md](./parts.md) | 파트 태그 정의 및 역할 배분 (**지속 갱신**) |
| [git-convention.md](./git-convention.md) | 브랜치 전략 · Jira에서 브랜치 생성 · 네이밍 · MR/승인 · 규칙 강제 4겹 · Unity 규칙 |
| [jira-convention.md](./jira-convention.md) | Epic/Task 구조 · 필수 필드 · 레이블=파트 · 스토리 포인트 척도 · 상태 진입 규칙 |
| [jira-gitlab-automation.md](./jira-gitlab-automation.md) | Automation 규칙 R1~R9 명세 · 적용 현황 · E2E 검증 결과 · GitLab 설정 · 편집기/API 부록 |
| [ai-tooling.md](./ai-tooling.md) | AI 연동 가이드 — 공용 계정 대신 로컬 MCP(`mcp-atlassian`)·`glab` CLI 사용, 토큰 관리 |
| [ci-and-notifications.md](./ci-and-notifications.md) | **1·2단계 CI·알림** — MR 파이프라인 잡(mr-title·mr-lint·repo-hygiene·unity-compile·ai-review·mm-digest) · 변수/토큰 · 러너 · Mattermost 알림 3소스 |
| [leader-checklist.md](./leader-checklist.md) | 팀장 남은 일 — MR 머지 · 브랜치/테스트 이슈 정리 · Epic 담당자 · CI 변수/러너/MM 통합 |
| [backend-guidelines.md](./backend-guidelines.md) | 백엔드 기술스택·설계 원칙 — raw WebSocket · 게스트 로그인 · MySQL/메모리 분리 · GameState · 체크포인트 · 로컬 개발 환경 |
| [asset-catalog.md](./asset-catalog.md) | **Unity 에셋 카탈로그** — 프리뷰 이미지 · 실제 배치 크기 · `Texture→Material→Prefab` 연결 체인 · 미연결 에셋 목록 (**에셋 추가 시 갱신**) |

CI 파이프라인은 [.gitlab-ci.yml](../.gitlab-ci.yml)과 [scripts/ci/](../scripts/ci/)에 있다(러너에 Python 3.12+만 필요).
MR 템플릿은 [.gitlab/merge_request_templates/Default.md](../.gitlab/merge_request_templates/Default.md)에 있으며 MR 생성 시 자동 적용된다.
브랜치명 검사 훅은 [.githooks/pre-push](../.githooks/pre-push)에 있으며, 클론 후 `git config core.hooksPath .githooks`를 한 번 실행해 켠다.

템플릿 파일: [templates/jira-issue-description.md](./templates/jira-issue-description.md) (Jira 설명 틀, 자동 삽입) · [templates/work-record.md](./templates/work-record.md) (비코드 작업 기록 1장)
