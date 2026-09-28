# 00. 프로젝트 구조와 인수 범위

## 서비스 구조

```text
Windows 게임(Unity, Mirror)
 ├─ HTTPS/WSS → nginx → Spring Boot → MySQL
 │                       └─ 릴레이 제어 TCP 17779
 ├─ UDP 17778 → Python 릴레이 → 방장 게임(Mirror 권위 서버)
 └─ 선택 AI → 로컬 워커 HTTP 18765 → 전용 Ollama 11439

웹 /play/ → 런처 → /builds/ 매니페스트·압축 파일 → 게임 설치
게임 AI 설치 → /ai-packs/ → 실행기·모델 추가팩
```

Spring은 게스트 인증, 방·준비·시작, 재접속, 랭킹과 로그 업로드를 처리합니다. 실제 물리와 플레이 동기화는 방장 PC의 Mirror 게임 서버가 처리합니다. 릴레이가 게임 시뮬레이션을 대신하지 않습니다. 일반 게임에 Photon 계정이나 소셜 로그인은 필요하지 않습니다.

## 폴더별 역할

| 위치 | 내용 | 인수 시 주의 |
| --- | --- | --- |
| `METAGAME/Assets/_Project/Scripts/Runtime/Core` | 입력, 설정, 빌드 식별, 스테이지 목록 | `Controls`, `BackendEndpoints`, `StageCatalog`가 공통 기준 |
| `Runtime/Gameplay`, `Runtime/Network` | 게임 규칙·물리, Mirror/방/AI 연동 | 방장 권위와 참가자 동기화를 함께 검증 |
| `Runtime/UI`, `Runtime/Bootstrap` | UI, 씬 진입, 컷신, 초기화 | 버튼 문구와 씬 전환의 근거 |
| `METAGAME/Assets/_Project/Scripts/Editor` | 빌드·에셋 생성·검증 | 일부 빌드 전처리가 프로젝트 에셋을 갱신하므로 전용 체크아웃 권장 |
| `METAGAME/Assets/_Project/Scripts/Tests` | Unity 테스트 | EditMode와 실제 멀티플레이 검증은 별도 |
| `METAGAME/Assets/Scenes` | 로비·연습실·1장·2장·연출 | `.unity`와 `.meta`를 함께 보존 |
| `METAGAME/Assets/_Project/ThirdParty` | Mirror 등 공급사 코드 | 프로젝트 자체 코드와 구분 |
| `METAGAME/Packages`, `ProjectSettings` | 패키지 고정·Unity 설정 | 임의 업그레이드 대신 기록된 버전으로 복원 |
| `backend/` | Java 서버, Gradle wrapper, 로컬 MySQL | 로컬 compose를 운영에 그대로 사용하지 않음 |
| `launcher/` | .NET Windows 업데이트 런처 | 게임과 별도 버전, `VERSION`은 현재 1.1.0 |
| `website/` | 정적 소개·다운로드·설문 사이트 | npm 빌드 없음, 미디어는 LFS |
| `tools/AICompanionService/` | AI 실험 서비스·추가팩 제작 | 모델/배포 ZIP은 별도 인계 |
| `scripts/relay/` | Python UDP 릴레이·서비스 유닛 | prod/dev가 공유하여 장애 영향도 공유 |
| `scripts/deploy/` | nginx/compose 사본·systemd 배포 | 사본과 현재 서버가 같은지는 별도 대조 필요 |
| `scripts/ci/`, `scripts/launcher/` | CI 검사·배포·매니페스트 | 자동화 계정/러너 설정은 Git 외부 |
| `docs/`, `artifacts/`, `outputs/` | 기획·결정 기록, 제작·검증 산출물 | 과거 기록의 수치와 현재 설정을 구분 |
| `.gitlab-ci.yml`, `.githooks/` | 파이프라인, 브랜치/LFS 훅 | 팀 협업·릴리스 규칙 |
| `exec/` | 이번 포팅·인수인계 묶음 | 시작점은 README |

## Unity 환경과 구조

확정 버전은 `ProjectVersion.txt`, `Packages/manifest.json`과 `packages-lock.json`을 기준으로 합니다. URP 17.5.0, Input System 1.19.0, AI Navigation 2.0.13, Test Framework 1.7.0, Newtonsoft JSON 3.2.2를 사용합니다. `activeInputHandler: 2`로 기존 Input Manager와 새 Input System을 함께 허용합니다. 기본 조작은 `Controls.cs`를 확인합니다.

Core·Gameplay·Network·UI·Bootstrap·Editor 등 asmdef로 책임을 나누며, Bootstrap이 씬 진입을 구성하고 Network가 서버·플레이어·AI 연결을 담당합니다. 게임 상태와 UI는 이벤트·공통 상태를 통해 연결합니다. 상세 기존 기록: [Unity 프로젝트 컨텍스트](../METAGAME/Docs/AI/UnityProjectContext.md).

활성 빌드 씬 순서:

1. Lobby
2. Main
3. Stage1_Astra
4. Stage1_Ending
5. Customizer
6. PracticeLobby
7. Stage1_EntryCutscene
8. Stage1_AssaultCutscene
9. Stage2_Earth

`SpaceBuild`는 필수 씬을 보완하고 활성 씬을 빌드합니다. `StageCatalog`는 `stage1`/`stage2`를 실제 씬에 대응시킵니다. 1장 완료 연출 뒤 `EndingSceneFlow`가 2장 진입을 처리합니다. 과거 README의 “Lobby → Main 두 씬”만 따라 설정하면 현재 콘텐츠를 빠뜨립니다.

## Git 밖에서 반드시 인계할 것

운영·개발 `.env`, EC2 접근 권한, DNS/TLS 관리 권한, GitLab 러너 설정/등록정보, CI 변수, DB 최신 백업, 웹 배포 디렉터리, 런처/게임 릴리스 저장소, AI 추가팩과 라이선스 고지, 로그 보관 디렉터리입니다. `Library/`, 캐시, `Temp/`와 개인 IDE 생성물은 새 PC에서 다시 만듭니다. 원본 워크스페이스의 커밋되지 않은 렌더링 변경은 이번 브랜치에 포함하지 않았습니다.
