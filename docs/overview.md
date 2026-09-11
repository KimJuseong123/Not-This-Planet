# 프로젝트 개요

[문서 안내](README.md) · 다음: [1장 기획](planning/README.md) 또는 [실행 준비](../README.md#빠른-시작)

## 어떤 게임인가

Not This Planet 은 행성 탐사대가 우주선을 조립해 탈출하는 협동 게임입니다. 1장에서는 탐사 기지의 건물이 우주선 파츠라는 사실을 발견하고, 네 파츠를 모아 우주선을 만듭니다. 처음 접하는 플레이어가 탐색·운반·조립·용접과 동료 구조를 배우는 튜토리얼입니다.

```text
태블릿으로 파츠 확인 → 탐색 → 고정못 해제 → 운반·적층 → 용접 → 탑승·발사
                                      ↑ 운석 방어·동료 구조·파손 복구 ↓
```

상세 기획은 1~4명 플레이, 2~4명 권장, 첫 성공 약 20분을 기준으로 합니다. 시간 제한은 없으며 이 시간은 초기 플레이 테스트 목표입니다. 역할은 캐릭터 직업 대신 견인 조종 모듈·용접기·운석 방망이 등의 장비로 나뉩니다. 조작과 판정, 수치는 [1장 상세 기획](planning/chapter-1-detailed-design.md)에서 확인합니다. 기획의 확정 규칙과 초기 테스트값, 구현 결과를 구분해 읽어야 합니다.

## 시스템은 어떻게 나뉘는가

| 구성 | 역할 | 코드·상세 문서 |
| --- | --- | --- |
| Unity 클라이언트 | 플레이어 입력, 물리 상호작용, 게임 화면과 UI | `METAGAME/` · [Unity 작성 규칙](unity-convention.md) |
| Mirror 매치 | 호스트가 게임 상태를 판정하고 참가자에게 동기화 | [동기화 설계](records/S15P21B202-145-sync-design.md) · [상호작용 동기화](records/S15P21B202-149-interaction-sync.md) |
| Spring 백엔드 | 게스트 인증, 방·세션 관리, 매치 연결 | `backend/` · [백엔드 지침](backend-guidelines.md) |
| EC2 릴레이 | 서로 다른 망의 호스트와 참가자 사이에서 패킷 중계 | `scripts/relay/` · [릴레이 구현 기록](records/S15P21B202-299-ec2-relay.md) |
| CI·배포 | MR 검사, 빌드, 서버 배포, Windows 릴리스와 알림 | `.gitlab-ci.yml`, `scripts/` · [CI/CD 구조](ci-cd-pipeline.md) |

네트워크 구조는 초기의 전용 게임 서버 계획에서 **리슨 서버(플레이어 호스트) + 릴레이**로 변경되었습니다. 전용 서버 운영 기록도 보존되어 있으므로, 현재 연결 경로를 이해할 때는 동기화 설계의 정정 내용과 릴레이 기록을 함께 읽습니다.

## 무엇부터 확인할 것인가

| 목적 | 순서 |
| --- | --- |
| 게임을 실행 | [루트 README](../README.md#빠른-시작) → [멀티플레이 접속 테스트](multiplayer-test-guide.md) |
| 게임 기능을 개발 | [상세 기획](planning/chapter-1-detailed-design.md) → [Unity 규칙](unity-convention.md) → [기능별 작업 기록](records/README.md) |
| 에셋을 제작·배치 | [에셋 카탈로그](asset-catalog.md) → 관련 모델·재질·프리팹 경로 |
| 협업에 참여 | [규칙 요약](conventions.md) → [이슈부터 완료까지](workflow.md) → 담당 분야의 상세 규칙 |
| 서버·자동화를 운영 | [CI/CD 구조](ci-cd-pipeline.md) → [CI 상세](ci-and-notifications.md) 또는 [Jira 자동화](jira-gitlab-automation.md) |

## 초기 README의 상태 기록

아래는 개편 전 README가 설명하던 초기 상태입니다. 당시의 정보는 보존하되 현재 실행 안내와 구분합니다. 현재 활성 씬은 `Lobby.unity`·`Main.unity`이며 테스트도 추가되어 있습니다.


- Unity **6000.5.4f1** · 렌더 파이프라인 URP 17.5.0 · 입력 Input System 1.19.0
- 주요 패키지: AI Navigation, uGUI, Timeline, Visual Scripting, Unity Test Framework
- 활성 빌드 씬: `Assets/_Project/Scenes/Surface.unity`
- 어셈블리: `Metagame.Core` → `Gameplay` → `UI` → `Bootstrap` (+ `Editor`)
- 상태: SpaceFlop 프로토타입 코드·에셋이 `Assets/_Project/` 아래에 들어와 있고, 프로젝트 테스트는 아직 없음

자세한 상태와 확인 근거는 [Unity 프로젝트 컨텍스트](../METAGAME/Docs/AI/UnityProjectContext.md).
