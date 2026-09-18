# METAGAME 개발 컨벤션

개발에 참여할 때 필요한 공통 규칙을 요약합니다. Task·Bug는 **Jira 이슈 → 작업 브랜치 → MR → 팀장 머지** 순서로 진행합니다. Story·Epic은 담당자가 관련 작업을 확인하고 상태를 변경합니다.

[문서 안내](README.md) · 다음: [실제 작업 절차](workflow.md)

> 각 줄의 링크가 그 규칙의 **단일 출처**입니다. 어긋나면 링크된 문서가 맞고, 이 문서를 고칩니다.
> 확정 2026-08-26 · 갱신 2026-08-28

Task·Bug의 상태는 브랜치 생성과 MR 머지를 기준으로 자동 전환합니다. 이벤트가 누락되면 Infra가 `automation-missed` 예외를 적용합니다.

**이 문서의 내용**

- [1. 규칙 요약](#section-1)
- [2. 왜 이렇게 정했나](#section-2)
- [3. 규칙을 검사하는 장치](#section-3)
- [4. 규칙이 실제로 지켜지는지 확인하는 방법](#section-4)
- [5. 더 볼 것](#section-5)

<a id="section-1"></a>

## 1. 규칙 요약

| 항목 | 규칙 | 상세 |
| --- | --- | --- |
| 원칙 | **Jira 우선.** 코드·에셋·문서·회의·리서치까지 모든 작업이 Jira 이슈로 시작한다 | [jira-convention](./jira-convention.md) §0 |
| 이슈 구조 | **Epic → Story·Task·Bug의 2단계.** Subtask 없음. 작업 브랜치·MR은 Task·Bug에 생성. 제목은 유형별 상세 규칙 참조 | [jira-convention](./jira-convention.md) §1·§2 |
| 필수 필드 | 담당자 · 상위 Epic · 기한(비면 스프린트 종료일 자동) · 스프린트·스토리 포인트(진행 중 진입 시) | [jira-convention](./jira-convention.md) §4 |
| 스토리 포인트 | **크기·복잡도의 상대값(시간 아님).** 값 **1/2/3/5 뿐** · 이슈 ≤ 5(넘으면 쪼갠다) · Story·Task에 각각 · 주간 합계는 회고에서 보정 | [jira-convention](./jira-convention.md) §5 |
| 상태 | **해야 할 일 → 진행 중 → 완료** 3개뿐. 진행 중 = 브랜치 생성, 완료 = MR 머지 | [jira-convention](./jira-convention.md) §7 |
| 파트 태그 | 14종. Task의 파트는 **수행** 파트, Epic의 파트는 **완료 판정** 파트 | [parts.md](./parts.md) |
| 브랜치 | `master` – `develop` – 작업 브랜치 3단. 작업 브랜치 이름은 `<이슈키>-<번호>-<영문설명>`(긴급 수정만 `hotfix/` 접두어) | [git-convention](./git-convention.md) §1·§3 |
| 브랜치 생성 | **Jira 이슈 화면 "브랜치 만들기"** — 이것이 진행 중으로 가는 길이다. 로컬에서 만들어도 이름이 이슈 키로 시작하면 같다 | [git-convention](./git-convention.md) §2 |
| 커밋 | `<type>(<scope>): <요약> (<이슈키>)` — Conventional Commits + 이슈 키 | [git-convention](./git-convention.md) §4 |
| MR | 대상 `develop` · 제목 `[파트] type: 요약 (이슈키)`(CI가 자동 교정) · 템플릿 필수 · **Draft 금지** · squash 머지 | [git-convention](./git-convention.md) §5 |
| 승인 | **팀장(Maintainer)만 develop에 머지**한다 — 팀장의 머지가 승인이다. 볼 것은 AI 리뷰가 채워 둔 판정표의 ⚠️·❓ 와 MR 본문 "머지 전에 팀장이 확인할 것" 이다 — 사람이 누르는 체크리스트는 없다(`-395`·`-415`). 머지 버튼을 잠그는 조건(파이프라인 성공·스레드 해소)은 GitLab 설정 | [mr-flow](./mr-flow.md) §3 · [ci-cd-pipeline](./ci-cd-pipeline.md) §3 |
| 겹침 | 다른 열린 MR과 같은 파일을 고쳤으면 **나중에 올린 쪽이 그 위에 얹는다**(스택 MR). 머지는 develop에 가까운 쪽부터 | [mr-flow](./mr-flow.md) |
| 릴리즈 | develop에서 딴 `release/<이슈키>-vX.Y.Z` 브랜치 → master MR(squash 해제는 CI 가). 머지되면 CI가 태그·GitLab Release(빌드 zip)를 만든다. 릴리스 브랜치에 커밋 없음 · master 직접 커밋·push 없음 · **빌드 산출물 커밋 금지** | [git-convention](./git-convention.md) §8 |
| Unity | `.meta`는 에셋과 함께 · 씬·프리팹 동시 수정 금지(먼저 선언) · 새 에셋은 `Assets/_Project/` 아래 | [unity-convention](./unity-convention.md) |
| 대용량 파일 | 모델·텍스처·오디오·영상은 Git LFS. 씬·프리팹·meta는 텍스트로 유지(diff를 봐야 하므로) | [unity-convention](./unity-convention.md) |
| AI 사용 | 개인 계정·개인 토큰만. 공용 계정에 커넥터 연결 금지, 토큰 값은 저장소·채팅·로그에 남기지 않는다 | [ai-tooling](./ai-tooling.md) |

<a id="section-2"></a>

## 2. 왜 이렇게 정했나

규칙을 정한 주요 이유는 다음과 같습니다.

1. **보드 상태와 실제 작업을 일치시키기 위해** — 상태를 손으로 옮길 수 있으면 보드와 실제 작업이 어긋나고, 그 순간 보드를 아무도 신뢰하지 않게 됩니다. 그래서 `진행 중`은 브랜치가, `완료`는 머지가 만들고, 손으로 끌면 자동으로 되돌아옵니다. 다만 자동화가 이벤트를 놓쳐 이슈가 갇히는 경우가 있어, 그때는 Infra가 `automation-missed` 레이블로 풀어 줍니다.
2. **GitLab Free 에서는 "승인 필수"를 강제할 수 없어서** — 승인 규칙 기능이 없으므로, `develop` **머지 권한을 팀장에게만** 준 것이 실제 관문입니다. 즉 팀장의 머지 클릭이 승인 그 자체입니다.
3. **씬·프리팹은 병합이 사실상 불가능해서** — 텍스트 코드와 달리 되돌리기 어렵습니다. 그래서 통합 지점을 `develop` 하나로 모아 충돌을 빨리 드러내고, 작업 브랜치를 1~3일 안에 작게 머지하며, 씬을 만질 때는 미리 선언합니다.

결정 과정과 검토했던 대안은 [workflow-plan.md](./records/2026-08-26-workflow-plan-v1.md) 에 있습니다.

<a id="3-어떻게-강제되나--다섯-겹"></a>

<a id="section-3"></a>

## 3. 규칙을 검사하는 장치

검사 가능한 규칙은 다음 장치에서 확인합니다.

| 겹 | 무엇이 | 막는 것 |
| --- | --- | --- |
| **GitLab 서버** | 보호 브랜치 | `develop`·`master` 직접 push, 팀장 외 머지 |
| **로컬 훅** [`.githooks/pre-push`](../.githooks/pre-push) | push 직전 검사 | 이슈 키 없는 브랜치 이름. 덧붙여 뒤처진 브랜치에 `develop` 최신을 자동으로 얹어 준다 |
| **CI** [`.gitlab-ci.yml`](../.gitlab-ci.yml) | MR 마다 도는 검사 | 제목·양식·이슈 키 실존 · 금지 파일과 `.meta` 짝 · 열린 MR 과의 겹침 · AI 리뷰(**⛔ 보류 권고만 막는다**, ⚠️·✅ 는 참고) |
| **Jira 자동화** | 규칙 8개 | 브랜치 없이 진행 중, 머지 없이 완료 → 되돌림 + 댓글 (`automation-missed` 레이블이면 통과) |
| **사람** | 팀장 리뷰 | 범위·완료 조건·씬/프리팹·구조·기록 — **AI 가 판정표로 미리 채우고**(`ai_review.py` 의 `CHECK_LABELS`) 팀장은 ⚠️·❓ 만 본다 |

잡별로 무엇을 보는지는 [ci-and-notifications.md](./ci-and-notifications.md), 자동화 규칙 명세는 [jira-gitlab-automation.md](./jira-gitlab-automation.md).

<a id="section-4"></a>

## 4. 규칙이 실제로 지켜지는지 확인하는 방법

문서만 있고 동작하지 않는 규칙을 남기지 않으려고, 다음 세 가지 방법으로 확인합니다.

| 무엇 | 어디에 | 내용 |
| --- | --- | --- |
| 실측 점검표 | [e2e-checklist.md](./e2e-checklist.md) | 시나리오를 하나씩 실제로 밟아 보고 결과를 적은 표 |
| 아침 점검 | 매일 09:30 Mattermost 📋 카드 | 형식 위반·기한 초과·정체 이슈·오래 열린 MR·머지됐는데 완료 안 된 이슈·스택 자식 MR을 담당자별로 |
| 검사 스크립트 테스트 | `python scripts/ci/tests/run_all.py` | CI 검사 자체가 깨지지 않았는지 (외부 연결 없이 실행) |

<a id="section-5"></a>

## 5. 더 볼 것

문서 색인은 [docs/README.md](./README.md) — 지금 하려는 일별로 정리돼 있습니다. 흐름을 그림·표로 보려면 [workflow.md](./workflow.md).
