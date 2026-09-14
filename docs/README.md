# 문서 안내

[프로젝트 README](../README.md) → **문서 안내** → 분야별 안내 → 상세 규칙·설계 → 작업 기록 순서로 구성했습니다.

처음이라면 [프로젝트 개요](overview.md)를 읽고 [실행 준비](../README.md#빠른-시작)를 진행합니다. 개발에 참여할 때는 [규칙 요약](conventions.md)과 [작업 흐름](workflow.md)을 이어서 읽습니다. 이후에는 맡은 작업에 해당하는 문서를 선택하면 됩니다.

## 1. 게임과 프로젝트 이해

| 문서 | 확인할 내용 |
| --- | --- |
| [프로젝트 개요](overview.md) | 게임 목표, 플레이 흐름, Unity·백엔드·릴레이의 역할 |
| [1장 기획 안내](planning/README.md) | 기본 기획에서 상세 조작·판정으로 이어지는 읽기 순서 |
| [1장 기본 기획](planning/chapter-1-plan.md) | 배경, 핵심 경험, 콘텐츠와 필요한 산출물 |
| [1장 상세 기획](planning/chapter-1-detailed-design.md) | 조작, 장비, 협동, 조립, 용접, 운석, 실패·복구 규칙과 테스트값 |
| [파트와 담당](parts.md) | 작업 영역별 태그, 담당 팀원과 Epic 책임. 담당 변경 시 갱신 |

## 2. 실행과 기능 개발

| 하려는 일 | 문서 |
| --- | --- |
| 저장소를 설정하고 Unity 실행 | [루트 README의 빠른 시작](../README.md#빠른-시작) |
| Unity 코드·에셋 작성 | [Unity·C# 작성 규칙](unity-convention.md): 어셈블리, 폴더, `.meta`, LFS |
| 에셋 선택·배치 | [에셋 카탈로그](asset-catalog.md): 프리뷰, 크기, Texture → Material → Prefab 연결. 에셋 추가 시 갱신 |
| 백엔드 로컬 실행·개발 | [백엔드 지침](backend-guidelines.md): 기술 스택, 인증·상태 설계, 개발 환경 |
| 네트워크에 닿는 동작을 만든다 | [네트워크 동기화 정책](network-sync-policy.md): 동기화 방식 P1~P4 와 각 동작이 어디에 속하는지. **동작을 추가하면 그 표에 한 줄 더한다** |
| 멀티플레이 동기화 구현 | [설계 결정](records/S15P21B202-145-sync-design.md) → [상호작용 규칙](records/S15P21B202-149-interaction-sync.md) → [오브젝트 동기화](records/S15P21B202-352-object-sync.md) |
| 두 PC에서 접속 확인 | [멀티플레이 테스트 안내](multiplayer-test-guide.md) → [지연·대역폭 측정 기록](records/S15P21B202-151-multiplay-measurement.md) |

## 3. 이슈부터 MR 완료까지

| 단계 | 문서 |
| --- | --- |
| 공통 규칙 확인 | [개발 컨벤션](conventions.md): 팀 규칙 요약과 상세 기준 |
| 전체 절차 이해 | [개발 워크플로우](workflow.md): 사람·자동화·AI의 역할과 단계별 절차 |
| 이슈 작성 | [Jira 컨벤션](jira-convention.md): 유형, 필수 필드, 파트, 스토리 포인트 |
| 브랜치 생성·커밋 | [Git 컨벤션](git-convention.md): 이름 규칙, push, 릴리스 |
| MR 제출·충돌 해결·머지 | [MR 흐름](mr-flow.md): 검사, 스택 구성, 부모부터 머지하는 순서 |
| 팀장 리뷰 | MR 템플릿 하단 “리뷰어 확인” 5항목 → [MR 흐름](mr-flow.md) §3 → [CI/CD 구조](ci-cd-pipeline.md) §8의 남은 설정 |
| AI 도구 연결 | [AI 도구 연동](ai-tooling.md): 개인 계정, 로컬 MCP·CLI, 토큰 취급 |
| 이슈 설명·작업 기록 작성 | [템플릿 안내](templates/README.md) |

## 4. 운영·배포·검증

| 목적 | 먼저 읽을 문서 | 상세 근거 |
| --- | --- | --- |
| CI/CD 구성 이해 | [CI/CD 구조](ci-cd-pipeline.md) | [CI·알림 상세](ci-and-notifications.md) |
| 검사 실패 해결 | [CI/CD 구조](ci-cd-pipeline.md) §2 잡 목록·§7 증상별 확인 | [CI·알림 상세](ci-and-notifications.md) |
| Jira 상태·자동화 문제 해결 | [Jira 자동화 설정](jira-gitlab-automation.md) | [규칙 실측 기록](records/S15P21B202-336-jira-rules-2026-09-09.md) |
| 자동화 변경 검증 | [E2E 점검표](e2e-checklist.md) | 준비·행동·기대 결과·실측 결과를 시나리오별로 확인 |
| CI·알림 스크립트 수정 | [CI/CD 구조](ci-cd-pipeline.md) → [CI 상세](ci-and-notifications.md) → `scripts/ci/` | 오프라인 검사: `python scripts/ci/tests/run_all.py` |
| 값을 바꿔 보고 다시 재는 빌드를 만든다 | [실험 빌드 규칙](experiment-builds.md) | 라벨 규칙과 로그 표시 — 없으면 나중에 어느 실험인지 가릴 수 없다 |

## 5. 결정 배경과 작업 이력

[컨벤션의 결정 배경](conventions.md)에서 주요 이유를 확인하고, 대안 검토가 필요하면 [초기 워크플로우 계획](records/2026-08-26-workflow-plan-v1.md)을 읽습니다. 이 계획은 당시의 결정 기록이며 현재 규칙은 각 컨벤션 문서를 따릅니다.

[작업 기록 색인](records/README.md)은 기능·운영 주제별로 모든 기록을 연결합니다. 예를 들어 [develop 전체 AI 리뷰](records/S15P21B202-82-develop-ai-review.md)와 [머지된 MR 리뷰 대조](records/S15P21B202-134-merged-mr-ai-review.md)는 당시의 지적과 처리 결과를 확인할 때 읽습니다.

| 위치 | 문서의 역할 |
| --- | --- |
| `docs/*.md` | 프로젝트 개요, 공통 규칙, 개발·운영 안내. 주제별로 관리 |
| `docs/planning/` | 게임 기획과 상세 설계 |
| `docs/records/` | 이슈별 결정·구현·측정·사고 대응 이력. 이름은 `<이슈키>-<slug>.md` |
| `docs/templates/` | Jira 설명과 작업 기록 작성 양식 |
| `docs/assets/previews/` | 문서에서 사용하는 에셋 프리뷰 이미지 |

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

`git lfs update --force`는 **실행하지 마세요** — `.githooks/pre-push`를 지웁니다.

## 문서 작성·갱신 기준

- 같은 규칙을 두 문서가 다르게 말하지 않게 합니다. 각 규칙은 이 색인에 연결된 상세 문서를 기준으로 합니다.
- 버전 번호와 항목 수(예: “시나리오 25종”)는 상세 문서에서 관리합니다. 색인에 중복하면 갱신 시점이 달라질 수 있습니다.
- 규칙의 상세 내용은 담당 문서 한 곳에서 관리하고 다른 문서는 요약과 링크를 제공합니다. 과거에 [파트 문서](./parts.md)는 9종, Jira·CI는 13종으로 달랐던 사례가 있으므로 관련 설정도 함께 확인합니다.
- [conventions.md](./conventions.md) 는 **요약**입니다. 값이 링크된 상세 문서와 어긋나면 상세 문서가 맞고, 요약을 고칩니다.
- [workflow.md](./workflow.md) 와 [e2e-checklist.md](./e2e-checklist.md) 는 **팀 외부 공유·제출용**입니다. 내용을 바꿀 때는 공유본(`Desktop/METAGAME-share/`)도 함께 갱신합니다 — 그 폴더의 HTML·PDF는 md 로부터 만든 생성물이라 저장소에 넣지 않고, 만드는 법만 둡니다: `python scripts/docs/render_share.py docs/workflow.md docs/e2e-checklist.md --out <공유 폴더>` (`pip install --user markdown` 필요, PDF는 Edge 헤드리스).
