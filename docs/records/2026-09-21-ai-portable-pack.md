# AI 추가팩 포장 — 내부 preview

## 결과

현재 작업 폴더 `S15P21B202-ai-integration-20260920` 안에서만 구현했다.
Unity 게임/씬/AI 설정 코드는 이번 단계에 변경하지 않았다. 이전 seats 프로젝트는 참고만 했다.
새 모델 다운로드, 유료 API 사용, 서버 게시, Jira 생성, push/MR은 하지 않았다.
2026-09-21 사용자 요청으로 현재 구현을 로컬 체크포인트 커밋에 보존한다.
무료 PyInstaller 제작 도구와 고정 Python 의존성을 별도 `.pack-venv`에 준비했다.

정상 산출물은 **`artifacts/ai-pack/p3/`**. 앞선 `preview-20260921`, `preview-20260921-v2`는 실패한 제작 중간물이며 배포하지 않는다.

| 파일 | 크기(bytes) | 역할 |
|---|---:|---|
| runtime.zip | 128462979 | Python 런타임/의존성 + Ollama CPU + worker |
| models.zip | 2207845192 | Qwen3 1.7B + 한국어 Whisper small |
| pack-manifest.json | — | 크기/SHA-256/플랫폼/프로토콜/배포 미승인 표시 |
| smoke-result.json | — | 실제 EXE 검사 결과 |

압축 해제된 실행기/모델 파일 합계 약 2.64GB. 다운로드와 설치를 함께 보관할 때는 더 큰 여유 공간이 필요하다.
모델 팩을 게임 버전과 분리해 유지하는 구조이며 실제 설치 위치/다운로더는 후속 작업이다.
대용량 산출물과 제작 venv는 `.gitignore`로 제외했다.

## 구성과 확인

- `portable_worker.py`: 127.0.0.1 전용, 실행별 stdin 토큰, health/텍스트/음성/종료만 제공.
- 기존 분류 프롬프트는 `companion_v4.classify_companion` 재사용. 실험용 router는 등록하지 않는다.
- 실험 웹/벤치마크/발화 저장 경로는 실행하지 않는다. 접근 로그도 사용하지 않는다.
- 파일 존재만으로 ready를 주지 않고 ASR 로드/무음 처리/LLM 분류 성공을 확인한다.
- 이 ready는 추론기 준비이며 **게임 행동 실행 준비를 뜻하지 않는다**. health에 `game_actions_ready=false` 명시.
- Ollama cloud 비활성화, CPU 추론, 모델 자동 다운로드 없음.
- 자식 프로세스만 소유하고 Windows Job Object로 묶는다. PID/포트 이름으로 남의 서버를 종료하지 않는다.
- 부모 PID뿐 아니라 생성 시각도 확인한다.
- 기존 한국어 모델에 tokenizer.json이 없어 다른 multilingual Whisper 토크나이저를 사용하되 모든 어휘 ID 및 생략된 1501개 시간 토큰 순서를 검증 후 팩에 포함했다. 원본 폴더는 수정하지 않았다.
- Ollama manifest의 blob 크기/SHA-256 검증 후 1.7B 모델에 필요한 blob만 복사했다.

## 증거

PyInstaller 6.22.0 / Python 3.10.11 / Ollama 0.34.2, Windows x64 CPU.
`smoke_portable.py`는 기존 설치 경로를 PATH에서 빼고 빈 사용자/HF 캐시를 사용했다.

- EXE 자체 Python 의존성 import 성공.
- 모델 준비 약 20초 (이 PC의 최초 로딩/준비 시간, 일반 요청 지연을 의미하지 않음).
- 합성 텍스트 `나 따라와` → `follow`.
- 1초 합성 무음 WAV → `none`.
- 무토큰 접근 거절.
- 정상 종료, 부모 프로세스 종료, worker 강제 종료 각각 소유 자식 종료 확인.
- 단위 검사 **12/12 통과**: 인증/브라우저 Origin 차단, 미준비/동시 요청 거절, 모델 누락, 개인 발화 미저장 경로, PID 재사용, 모델 경로 검증, 점유 포트 비간섭을 포함한다.

실제 음성 인식 품질 평가나 Unity 2인 테스트는 하지 않았다. 실제 미설치 PC/장치 호환성 검사를 대신하지 않는다.
로컬 텍스트 테스트 창(`TEST-PORTABLE.bat`)을 추가했다. 테스트 창의 실행 → 준비 → `나 따라와` 분류 → 소유 실행기 종료 자동 검사를 통과했다. 마이크 입력 UI와 사용자 실제 음성 검증은 아직 없다.
현재 Unity 토글은 여전히 실행기 미연결 상태다. 이 파일 제작만으로 AI가 게임에서 행동하지 않는다.

## 출시 전 남은 것

1. 깨끗한 Windows PC에서 실행/시스템 런타임/CPU 호환성과 보안 프로그램 확인.
2. 모델/실행기/라이브러리 라이선스·고지 검토, 실행 파일 코드 서명 및 신뢰되는 manifest 배포 방식.
3. 승인된 호스팅 주소, 동의·용량 표시·취소·재시도·해시 검증·원자적 설치.
4. 게임에서 실행/토큰/부모 시각/상태 감시 연결, 이후 음성/행동 로직 연결.

상세 제작/연동 계약: [PORTABLE.md](../../tools/AICompanionService/PORTABLE.md).
