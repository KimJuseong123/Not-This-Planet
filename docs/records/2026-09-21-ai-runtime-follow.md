# 추가팩 → 게임 텍스트 명령 연결 (로컬 검증)

기준: `cdefa970`, `codex/ai-team-slots-local`. 기존 렌더링/빌드 설정과 옛 seats 프로젝트는 수정하지 않는다.
이번 범위는 Windows 로컬 추가팩 감지/실행과 방장 전용 텍스트 → follow/wait/cancel이다.
STT, 다운로드/설치, 원격 참가자 명령, 명령 중재, 못 뽑기, NavMesh 경로 탐색은 포함하지 않는다.

## 실행

- Unity 프로젝트: `S15P21B202-ai-integration-20260920/METAGAME`.
- Editor는 저장소의 `artifacts/ai-pack/p3`를 사용한다. 별도 Python 서버/테스트 창은 켤 필요 없다.
- Windows Player는 `Application.persistentDataPath/AI/p3`를 사용한다. 해당 폴더 아래 `runtime`과 `models`를 둔다. 현재 자동 설치는 없다.
- 로비에서 방을 만들고 `AI 동료 사용`을 켠다. `대기 AI 테스트`는 끈다.
- 추가팩 준비 완료를 기다리고 게임을 시작한다. 방장 PC의 준비 상태로만 빈자리를 채운다.
- Editor 또는 Development Build에서 Esc 메뉴 → AI 선택 → `나 따라와`, `여기서 기다려`, `그만해` 전송.
- 메뉴를 닫고 움직인다. `모든 AI 즉시 중단`은 LLM 없이 입력을 비운다.
- F7/F8은 기존 치트 기능과 충돌하므로 사용하지 않는다. 일반 Release 빌드에는 텍스트 테스트 UI가 없다.

## 경계

- 따라오기는 기존 NetPlayer → InputFrame → PlayerRig 경로다. 물리/속도 설정과 씬/프리팹은 변경하지 않는다.
- 직선 추종만 지원하며 근접/높이 차이/원거리/장애물/낭떠러지 앞에서 대기한다. 우회 경로, 사다리, 운반체 탑승은 미지원이다.
- 사람으로 교체되거나 장면이 바뀐 AI에 늦게 도착한 명령을 적용하지 않는다.
- 지원하지 않는 행동과 통신 오류는 기존 행동 유지. 즉시 중단은 진행 중 응답을 폐기한다.
- 추가팩은 localhost:18765, 자체 Ollama는 localhost:11439 사용. 점유된 다른 서버를 종료하지 않는다.
- 인증 토큰은 프로세스 표준입력과 HTTP 헤더에만 전달한다. 발화/토큰을 로그나 저장 파일로 남기지 않는다.
- 종료/역할 상실 시 소유 실행기만 종료하며 실행기의 Windows Job Object가 자식을 정리한다.
- 추가팩 자체 health의 `game_actions_ready=false`는 변경하지 않는다. Unity가 자신의 제한된 행동 실행 코드와 추가팩 준비 상태를 결합한다.
- 로컬 미승인 preview 추가팩이다. 서명/배포용 manifest 검증과 설치 프로그램은 후속 범위다.

## 검증

- 기준 회귀: 이전 정책/자리 검사 38개 통과 기록.
- Unity 6000.5.4f1, EditMode + 임시 PlayMode 호스트: **50/50 통과**, 실패/건너뜀 없음.
- 결과: `METAGAME/Logs/ai-runtime-v2-tests.xml`, `ai-runtime-v2.log`.
- AiRuntimeTests / AiHostPolicyTests / TeamRosterTests / TeamSlotsTests.
- 실제 p3 실행기 로딩, 비활성화/재활성화, Qwen 텍스트 분류, 서버 AI 3명 생성, `나 따라와`로 실제 1m 이상 이동, `여기서 기다려` 입력 정지, 즉시 중단 후 늦은 응답 무시, 미지원 명령 거절, 종료 후 Missing 확인.
- v1은 49/50 통과: Destroy 호출 직후 한 프레임만 기다린 종료 상태 검사 실패. OnDisable 정리 및 실제 파괴 완료 조건 대기로 보완한 v2에서 재검증했다.
- 로컬 모델 검사는 `NTP_TEST_LOCAL_AI=1` 명시 시에만 실행한다. 일반 CI에서는 모델을 다운로드하거나 실행하지 않는다.
- 실제 2대 PC, UI 시각 검수, Player 빌드는 별도 확인이 필요하다.
