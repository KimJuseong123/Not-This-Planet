# 팀장이 할 일

> **MR 리뷰 기준**(범위 · 완료 조건 · 씬/프리팹 · 구조 · 기록)은 MR 본문 하단 **"리뷰어 확인"** 에 자동으로 들어옵니다. 머지 순서가 얽힌 MR 은 MR 코멘트의 안내를 따르면 되고, 원리는 [mr-flow.md §3](./mr-flow.md) 에 그림으로 있습니다.
> 이 문서는 **팀장 권한이나 팀장 결정이 없으면 진행할 수 없는 것**만 모읍니다. 갱신 2026-08-28.

## 1. 남은 설정 — 권한이 필요한 것

| # | 무엇 | 어디서 | 왜 |
| --- | --- | --- | --- |
| 1-1 | **Pipelines must succeed** 켜기 | Settings → Merge requests | 지금은 검사가 빨간불이어도 머지 버튼이 살아 있다. 켜면 규칙 위반 MR 은 머지 자체가 막힌다. **안정화 1주 뒤**로 미뤄 둔 항목 |
| 1-2 | Mattermost 통합의 Trigger 에서 **`Merge request` 체크 해제** | Settings → Integrations → Mattermost | CI 가 보내는 🟢 카드(담당자 멘션 포함)와 내용이 겹쳐 두 번 온다. `Pipeline` 만 남기면 된다 |
| 1-3 | (Unity 러너 준비 후) 변수 `UNITY_RUNNER=true` · `UNITY_PATH` | Settings → CI/CD → Variables | 컴파일 검사를 켜는 스위치. 러너 PC 의 Unity 가 **6000.5.4f1** 이어야 한다(현재 설치본은 6000.0.81f1) |
| 1-4 | EC2 지급 후 상시 러너로 이전 | EC2 + `scripts/runner/setup-linux-runner.sh` | 지금은 팀 PC 1대라 그 PC 가 꺼지면 검사가 멈춘다(2026-08-28 에 실제로 1시간 멈춤) |
| 1-5 | (선택) Jira 사이트 관리자에게 요청 — Task 필수 필드, 워크플로우 전환 제한 | Jira 관리 | 없어도 자동화 되돌림(R1·R5·R9)으로 운영된다. 있으면 애초에 드래그가 막힌다 |

## 2. 결정이 필요한 것

- **파트 담당 확정** — [parts.md](./parts.md) 의 담당 팀원 칸에서 `미정` 5개(UI · Sound · Character · Physics · Level · QA)를 채워 주세요. Epic 담당자도 그 파트는 당분간 팀장으로 되어 있습니다.
- **이미 채워진 칸도 확인 부탁드립니다** — 실제 이슈·MR 이력으로 채운 값입니다(Plan 송승우 · Art 이수진·김주성 · Puzzle 김주성 · Backend 최재현 · Network·Data·Infra 금준영). 틀리면 그 표와 `scripts/ci/team.json` 을 함께 고칩니다.

## 3. 스크럼에서 팀에 알릴 3가지

1. **상태는 손으로 옮기지 않습니다** — `진행 중`은 이슈 화면 **브랜치 만들기**로만, `완료`는 **MR 머지**로만. 카드를 끌면 되돌아오고 댓글이 달립니다.
2. **MR 본문의 `이슈: S15P21B202-번호` 줄은 지우지 마세요** — 키가 제목·본문에 없으면 머지해도 이슈가 완료로 바뀌지 않습니다. Draft 로 만들지 마세요.
3. **클론 후 한 번**: `git config core.hooksPath .githooks` · `git lfs install --skip-repo`. (`git lfs update --force` 는 훅을 지우니 금지)

## 4. 지금까지 끝난 것

GitLab 설정(보호 브랜치·머지 권한·squash·브랜치 자동 삭제) · Jira 자동화 규칙 8개 · MR 템플릿 · 로컬 훅 · CI 검사와 Mattermost 알림 · 러너 1대 · 아침 점검 스케줄(**매일** 09:30 — 실제 발송은 09:33, GitLab 이 프로젝트마다 몇 분 오프셋을 더한다) — **적용·검증 완료**.

- 적용 이력과 실측 결과: [jira-gitlab-automation.md](./jira-gitlab-automation.md) §7·§8
- 시나리오별 실제 확인 결과: [e2e-checklist.md](./e2e-checklist.md)
- 그날그날의 처리 내역은 git 이력과 Jira 이슈에 남아 있습니다(이 문서에는 다시 적지 않습니다).
