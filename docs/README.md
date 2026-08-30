# 프로젝트 문서

**무엇을 언제 읽는지**로 정리했습니다. 처음부터 다 읽지 않아도 됩니다 — 지금 하려는 일에 해당하는 줄만 보세요.

## 지금 하려는 일별로

| 상황 | 읽을 것 |
| --- | --- |
| **처음 왔다** (오늘 하루 안에) | [workflow.md](./workflow.md) §0 흐름도 → [convention-proposal.md](./convention-proposal.md) 한 장 요약 → 아래 "클론 후 1회 설정" |
| **이슈를 만든다** | [jira-convention.md](./jira-convention.md) (Epic/Task·필수 필드·파트 레이블·스토리 포인트) |
| **브랜치를 만들고 작업한다** | [git-convention.md](./git-convention.md) (브랜치 이름·커밋 메시지·push 전 자동 반영·Unity 규칙) |
| **MR 을 올린다** | [git-convention.md](./git-convention.md) §5 (MR 규칙·승인) · MR 템플릿은 자동으로 들어온다 |
| **검사가 빨간불이다** | [ci-and-notifications.md](./ci-and-notifications.md) (잡별로 무엇을 보는지·실패 이유를 어디서 보는지) |
| **팀장이 리뷰·머지한다** | [leader-checklist.md](./leader-checklist.md) |
| **Jira 자동화가 이상하다** | [jira-gitlab-automation.md](./jira-gitlab-automation.md) (규칙 R1~R9 명세·실측 기록·사고 이력) |
| **CI·알림을 고친다** (Infra) | [ci-and-notifications.md](./ci-and-notifications.md) → `scripts/ci/` → 오프라인 테스트 `python scripts/ci/tests/run_all.py` |
| **자동화를 사람이 확인한다** | [e2e-checklist.md](./e2e-checklist.md) (시나리오별 준비·행동·기대·실측 결과) |
| **왜 이렇게 정했는지 알고 싶다** | [workflow-plan.md](./workflow-plan.md) (결정 근거·대안 검토) |
| **AI 도구를 쓴다** | [ai-tooling.md](./ai-tooling.md) (공용 계정 금지·로컬 MCP·토큰 취급) |

## 파트별로

| 파트 | 읽을 것 |
| --- | --- |
| 전체 · 역할 배분 | [parts.md](./parts.md) — 파트 태그 정의와 담당 (**지속 갱신**) |
| Backend | [backend-guidelines.md](./backend-guidelines.md) — 기술스택·설계 원칙·로컬 개발 환경 |
| Art · Level | [asset-catalog.md](./asset-catalog.md) — 에셋 프리뷰·배치 크기·`Texture→Material→Prefab` 연결 (**에셋 추가 시 갱신**) |
| Plan | [planning/](./planning/) — 기획 문서 |

## 폴더

| 경로 | 무엇이 들어가나 |
| --- | --- |
| `docs/*.md` | 팀 전체가 지키는 규칙·명세. 한 주제 한 파일 |
| [`docs/records/`](./records/) | 한 이슈에서 나온 **작업 기록**. 파일 이름은 `<이슈키>-<slug>.md` — [규칙](./records/README.md) |
| `docs/templates/` | [jira-issue-description.md](./templates/jira-issue-description.md) (Jira 설명 틀, 이슈 생성 시 자동 삽입) · [work-record.md](./templates/work-record.md) (비코드 작업 기록 1장) |
| `docs/planning/` | 기획 문서 |
| `docs/assets/previews/` | 문서에 넣는 이미지 |

## 문서가 아닌 곳에 있는 것들

| 무엇 | 어디 |
| --- | --- |
| CI 파이프라인 정의 | [`.gitlab-ci.yml`](../.gitlab-ci.yml) |
| CI 스크립트와 그 테스트 | [`scripts/ci/`](../scripts/ci/) — 러너에 Python 3.12+ 만 필요 |
| 러너 설치·환경 스크립트 | [`scripts/runner/`](../scripts/runner/) |
| MR 템플릿 | [`.gitlab/merge_request_templates/Default.md`](../.gitlab/merge_request_templates/Default.md) — MR 생성 시 자동 적용 |
| push 전 로컬 검사 훅 | [`.githooks/pre-push`](../.githooks/pre-push) |
| Unity 프로젝트 | `METAGAME/` |
| 백엔드 | `backend/` |

## 클론 후 1회 설정

```bash
git config core.hooksPath .githooks   # push 전 브랜치명 검사 · develop 최신 자동 반영
git lfs install --skip-repo           # --skip-repo 필수: 우리 훅을 덮어쓰지 않는다
```

`git lfs update --force` 는 **실행하지 마세요** — `.githooks/pre-push` 를 지웁니다.

## 이 문서들을 고칠 때

- 같은 규칙을 두 문서가 다르게 말하지 않게 합니다. 규칙의 **단일 출처**는 위 표의 담당 문서입니다.
- 버전 번호·개수(예: "시나리오 25종")는 문서 안에 두고 이 색인에는 적지 않습니다 — 색인이 먼저 낡습니다.
- [workflow.md](./workflow.md) 와 [e2e-checklist.md](./e2e-checklist.md) 는 **팀 외부 공유·제출용**입니다. 내용을 바꿀 때는 공유본(`Desktop/METAGAME-share/`)도 함께 갱신합니다.
