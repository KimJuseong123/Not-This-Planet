# METAGAME 개발 컨벤션

> 우리 팀이 합의한 규칙을 한 장에 모은 문서입니다. **처음 온 사람은 이것만 읽고 하루를 시작할 수 있고**, 밖에서 보는 사람은 이 팀이 무엇을 어떻게 강제하는지 알 수 있습니다.
> 각 줄의 링크가 그 규칙의 **단일 출처**입니다. 어긋나면 링크된 문서가 맞고, 이 문서를 고칩니다.
> 확정 2026-08-26 · 갱신 2026-08-28

**한 문장** — 모든 작업은 **Jira 이슈 → 브랜치 → MR → 팀장 머지** 로 흐르고, **이슈 상태는 사람이 손으로 바꾸지 않는다.**

## 1. 규칙 요약

| 항목 | 규칙 | 상세 |
| --- | --- | --- |
| 원칙 | **Jira 우선.** 코드·에셋·문서·회의·리서치까지 모든 작업이 Jira 이슈로 시작한다 | [jira-convention](./jira-convention.md) §0 |
| 이슈 구조 | **Epic → Task 2단계만.** Subtask 없음. 유형은 Task 로 통일. 제목 `[파트] 제목` | [jira-convention](./jira-convention.md) §1·§2 |
| 필수 필드 | 담당자 · 상위 Epic · 기한(비면 스프린트 종료일 자동) · 스프린트·스토리 포인트(진행 중 진입 시) | [jira-convention](./jira-convention.md) §4 |
| 스토리 포인트 | **크기·복잡도의 상대값(시간 아님).** 값 **1/2/3/5 뿐** · 이슈 ≤ 5(넘으면 쪼갠다) · Story·Task 에 각각 · 주간 합계는 회고에서 보정 | [jira-convention](./jira-convention.md) §5 |
| 상태 | **해야 할 일 → 진행 중 → 완료** 3개뿐. 진행 중 = 브랜치 생성, 완료 = MR 머지 | [jira-convention](./jira-convention.md) §7 |
| 파트 태그 | 13종. Task 의 파트는 **수행** 파트, Epic 의 파트는 **완료 판정** 파트 | [parts.md](./parts.md) |
| 브랜치 | `master` – `develop` – 작업 브랜치 3단. 작업 브랜치 이름은 `<이슈키>-<번호>-<영문설명>`(긴급 수정만 `hotfix/` 접두어) | [git-convention](./git-convention.md) §1·§3 |
| 브랜치 생성 | **Jira 이슈 화면 "브랜치 만들기"** — 이것이 진행 중으로 가는 길이다. 로컬에서 만들어도 이름이 이슈 키로 시작하면 같다 | [git-convention](./git-convention.md) §2 |
| 커밋 | `<type>(<scope>): <요약> (<이슈키>)` — Conventional Commits + 이슈 키 | [git-convention](./git-convention.md) §4 |
| MR | 대상 `develop` · 제목 `[파트] type: 요약 (이슈키)`(CI 가 자동 교정) · 템플릿 필수 · **Draft 금지** · squash 머지 | [git-convention](./git-convention.md) §5 |
| 승인 | **팀장(Maintainer)만 develop 에 머지**한다 — 팀장의 머지가 승인이다. 리뷰 기준 5항목은 MR 템플릿 하단 "리뷰어 확인". 머지 버튼을 잠그는 조건(파이프라인 성공·스레드 해소)은 GitLab 설정 | [mr-flow](./mr-flow.md) §3 · [ci-cd-pipeline](./ci-cd-pipeline.md) §3 |
| 겹침 | 다른 열린 MR 과 같은 파일을 고쳤으면 **나중에 올린 쪽이 그 위에 얹는다**(스택 MR). 머지는 develop 에 가까운 쪽부터 | [mr-flow](./mr-flow.md) |
| 릴리즈 | 마일스톤마다 `develop → master` MR + 버전 태그(`v0.1.0`). master 직접 커밋·push 없음 | [git-convention](./git-convention.md) §1 |
| Unity | `.meta` 는 에셋과 함께 · 씬·프리팹 동시 수정 금지(먼저 선언) · 새 에셋은 `Assets/_Project/` 아래 | [unity-convention](./unity-convention.md) |
| 대용량 파일 | 모델·텍스처·오디오·영상은 Git LFS. 씬·프리팹·meta 는 텍스트로 유지(diff 를 봐야 하므로) | [unity-convention](./unity-convention.md) |
| AI 사용 | 개인 계정·개인 토큰만. 공용 계정에 커넥터 연결 금지, 토큰 값은 리포·채팅·로그에 남기지 않는다 | [ai-tooling](./ai-tooling.md) |

## 2. 왜 이렇게 정했나

세 가지만 알면 나머지 규칙이 왜 있는지 따라옵니다.

1. **보드가 거짓말하지 않게 하려고** — 상태를 손으로 옮길 수 있으면 보드와 실제 작업이 어긋나고, 그 순간 보드를 아무도 신뢰하지 않게 됩니다. 그래서 `진행 중`은 브랜치가, `완료`는 머지가 만들고, 손으로 끌면 자동으로 되돌아옵니다.
2. **GitLab Free 에서는 "승인 필수"를 강제할 수 없어서** — 승인 규칙 기능이 없으므로, `develop` **머지 권한을 팀장에게만** 준 것이 실제 관문입니다. 즉 팀장의 머지 클릭이 승인 그 자체입니다.
3. **씬·프리팹은 병합이 사실상 불가능해서** — 텍스트 코드와 달리 되돌리기 어렵습니다. 그래서 통합 지점을 `develop` 하나로 모아 충돌을 빨리 드러내고, 작업 브랜치를 1~3일 안에 작게 머지하며, 씬을 만질 때는 미리 선언합니다.

결정 과정과 검토했던 대안은 [workflow-plan.md](./workflow-plan.md) 에 있습니다.

## 3. 어떻게 강제되나 — 다섯 겹

규칙을 외우지 않아도 되도록, 어긋나면 기계가 먼저 잡습니다. 무엇이 어디서 걸리는지:

| 겹 | 무엇이 | 막는 것 |
| --- | --- | --- |
| **GitLab 서버** | 보호 브랜치 | `develop`·`master` 직접 push, 팀장 외 머지 |
| **로컬 훅** [`.githooks/pre-push`](../.githooks/pre-push) | push 직전 검사 | 이슈 키 없는 브랜치 이름. 덧붙여 뒤처진 브랜치에 `develop` 최신을 자동으로 얹어 준다 |
| **CI** [`.gitlab-ci.yml`](../.gitlab-ci.yml) | MR 마다 도는 검사 | 제목·양식·이슈 키 실존·체크리스트 · 금지 파일과 `.meta` 짝 · 열린 MR 과의 겹침 · (참고용) AI 리뷰 |
| **Jira 자동화** | 규칙 8개 | 브랜치 없이 진행 중, 머지 없이 완료 → 되돌림 + 댓글 |
| **사람** | 팀장 리뷰 | 범위·완료 조건·씬/프리팹·구조·기록 (MR 템플릿 "리뷰어 확인") |

잡별로 무엇을 보는지는 [ci-and-notifications.md](./ci-and-notifications.md), 자동화 규칙 명세는 [jira-gitlab-automation.md](./jira-gitlab-automation.md).

## 4. 규칙이 실제로 지켜지는지 확인하는 방법

문서만 있고 동작하지 않는 규칙을 남기지 않으려고, 세 가지를 실제로 돌립니다.

| 무엇 | 어디에 | 내용 |
| --- | --- | --- |
| 실측 점검표 | [e2e-checklist.md](./e2e-checklist.md) | 시나리오를 하나씩 실제로 밟아 보고 결과를 적은 표 |
| 아침 점검 | 매일 09:30 Mattermost 📋 카드 | 형식 위반·기한 초과·정체 이슈·오래 열린 MR·머지됐는데 완료 안 된 이슈·스택 자식 MR 을 담당자별로 |
| 검사 스크립트 테스트 | `python scripts/ci/tests/run_all.py` | CI 검사 자체가 깨지지 않았는지 (외부 연결 없이 실행) |

## 5. 더 볼 것

문서 색인은 [docs/README.md](./README.md) — 지금 하려는 일별로 정리돼 있습니다. 흐름을 그림·표로 보려면 [workflow.md](./workflow.md).
