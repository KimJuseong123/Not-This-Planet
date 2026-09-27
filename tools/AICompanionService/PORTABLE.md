# Windows AI 추가팩 — 내부 검증용 preview

## 범위

Python 서버를 Windows x64 CPU용 `ntp-ai-worker.exe`와 내부 라이브러리로 묶는다.
Ollama CPU 실행 파일을 포함하므로 사용자에게 Python/Ollama 설치를 요구하지 않는다.
기존 `app.py`, `START.bat`, 실험 웹 UI는 변경하지 않는다.

이 단계는 **추론 실행기 포장**이다. Unity의 모델 설치 화면/다운로드/실행 연결과 행동 실행은 아직 없다.
`health.state=ready`도 추론 준비만 의미하며 `game_actions_ready=false`를 명시한다.
게임의 `IAiRuntimeStatus.Ready`로 그대로 연결하면 안 된다.

## 추가팩 구성

- `runtime.zip`: 실행 파일, Python 의존 라이브러리, Ollama CPU 실행기/라이브러리, 수집된 서드파티 고지.
- `models.zip`: Qwen3 1.7B와 한국어 Whisper small만. 비교용 0.6B/base/다른 STT 모델은 제외.
- `pack-manifest.json`: 버전, 플랫폼, 프로토콜, 각 ZIP 크기/SHA-256. 다운로드 URL은 아직 없으며 원격에 게시하지 않는다.
- ZIP 안의 `files.json`: 개별 파일 크기/SHA-256. 배포 시 검증할 자료이며, 자체 서명/신뢰 근거는 아니다.

모델 팩과 실행기 팩은 별도 버전으로 관리할 수 있게 분리했다. 향후 게임 업데이트 위치와 별도로
사용자 데이터 경로에 설치해야 한다. 이번에는 실제 사용자 설치 경로에 쓰지 않고 artifacts에만 만든다.

**배포 승인 상태는 false다.** 깨끗한 PC 확인, 모델/실행기 재배포 라이선스 및 고지 검토,
게임 연동이 끝나기 전 팀 외부에 최종 배포 파일로 전달하지 않는다. 실행 파일은 코드 서명하지 않았다.

## 실행 계약

게임은 방장 PC에서만 아래처럼 실행한다. `--parent-started`는 PID 재사용을 구분할 부모 생성 시각(Unix seconds).

```text
ntp-ai-worker.exe --model-root <모델팩 폴더> --parent-pid <게임 PID> --parent-started <생성 시각>
```

- 기본 HTTP 주소 `127.0.0.1:18765`, 전용 Ollama `127.0.0.1:11439`. 테스트만 별도 포트를 쓸 수 있다.
- 매 실행 32바이트 난수 토큰을 hex로 생성해 **리다이렉트한 stdin 첫 줄**로 전달하고 닫는다.
  명령행/파일/로그에 토큰을 넣지 않는다. 게임은 숨김 프로세스로 실행한다.
- HTTP 요청 모두 `Authorization: Bearer <토큰>`. 브라우저 Origin 요청은 거절한다.
- `GET /api/health`: loading / ready / failed, 오류 코드, busy. 모델을 다시 호출하지 않는 경량 확인.
- `POST /api/companion-v4/text`: `{ "text": "나 따라와" }` → 허용 행동 하나. 실행 로직은 게임 담당.
- `POST /api/companion-v4/voice`: multipart `audio`, 최대 1 MiB/30초. 음성·인식 문장·응답은 파일로 저장하지 않는다.
- `POST /api/shutdown`: 자신의 실행기를 종료. 게임 종료도 부모 감시로 감지한다.

게임은 `ready`만으로 AI 캐릭터를 만들면 안 된다. 명령 전달·행동 실행 연결 준비까지 통과해야 한다.
준비 전 요청은 503, 동시에 다른 작업이 있으면 409. 오류는 발화/내부 예외를 포함하지 않는 코드로 반환한다.
기존 서버가 포트를 점유하면 실패한다. 다른 프로세스를 종료하거나 기존 서버에 임의 연결하지 않는다.
Windows Job Object가 이번 실행기의 Ollama 자식들을 묶어서 강제 종료 시에도 정리한다.

## 오프라인 검사

### 이 PC에서 직접 문장 테스트

`TEST-PORTABLE.bat`를 더블클릭하면 Windows 테스트 창이 열린다. 제작용 Python은 필요 없다.
기본 추가팩 경로는 프로젝트의 `artifacts/ai-pack/p3`이며 다른 출력은
`portable_tester.ps1 -PackRoot <폴더>`로 지정할 수 있다.

1. `AI 실행` → 약 20초 모델 준비를 기다린다.
2. 문장을 입력하거나 예시를 선택하고 `판단 요청`을 누른다.
3. 선택한 행동과 처리 시간을 확인한다. 실제 게임 행동은 실행하지 않는다.
4. `종료 및 실행기 정리` 또는 창 닫기를 누른다.

이 창은 텍스트 분류 테스트이며 마이크를 사용하지 않는다. 화면의 문장/결과도 파일에 저장하지 않는다.
예상 예시: `나 따라와` → follow, `이거 못 뽑아줘` → pull_pin,
`주변 못 전부 뽑아줘` → pull_nearby_pins, `못 뽑지 마` → cancel.
개별 예시의 실제 정확도는 실행해서 평가해야 한다.
UI는 실행기용 비밀 토큰을 메모리에서만 생성하고 자신이 시작한 실행기만 종료한다.
별도 테스트 포트 18785/11459를 사용하며 이미 사용 중이면 시작하지 않는다.

### 자동 검사

- 모델 다운로드 API를 사용하지 않는다. 명시한 로컬 모델 파일만 로드한다.
- 토크나이저를 팩에 포함한다. 캐시의 `openai/whisper-tiny`를 암묵적으로 받지 않는다.
- 준비 시 음성 모델 로드/무음 추론, LLM의 JSON 분류까지 실행한다.
- 단위 테스트: `python -m unittest test_portable_worker -v`
- EXE 의존성 검사: `ntp-ai-worker.exe --self-test` (모델 추론을 의미하지 않음).
- 패키지 검사: `python smoke_portable.py <빌드 출력 폴더>`.
  PATH·사용자/HF 캐시를 격리하고 합성 무음/문장으로 준비·분류·종료·부모 종료·강제 종료를 검사한다.
  이는 **실제 Python/Ollama 미설치 PC 검사와 동일하지 않다.**

## 제작

제작자만 Python 3.10 x64와 `requirements-pack.txt`가 필요하다. 기존 실험 환경은 수정하지 말고 별도 venv를 쓴다.

```text
python build_portable.py --output <새 폴더> --ollama-dir <검증한 Ollama 설치 폴더> --model-root <기존 모델 폴더> --tokenizer <검증할 multilingual tokenizer.json>
```

모델/도구를 인터넷에서 자동으로 받지 않는다. 원본을 읽기만 하고 새 출력 폴더에 복사한다.
기존 출력 폴더를 지우지 않는다. 실패한 산출물을 정상 패키지로 간주하지 않는다.
한국어 모델에 tokenizer.json이 없으면 명시적으로 제공해야 하며 어휘 ID가 같은지 검증한다.
faster-whisper에서 토크나이저 JSON에 생략하는 마지막 1501개 시간 토큰은 모델 어휘의 정확한 순서를 검사한다.
Ollama 모델 blob도 원본 manifest의 크기와 SHA-256을 확인하고 복사한다.

PyInstaller는 시작 시 대용량 임시 압축 해제를 피하고 문제 추적이 쉬운 one-folder 모드로 사용한다.
참고: [공식 동작 설명](https://pyinstaller.org/en/stable/operating-mode.html),
[공식 옵션 문서](https://www.pyinstaller.org/en/stable/usage.html).

## 후속 작업

1. 실제 미설치 Windows PC에서 CPU 호환성/필요 시스템 런타임/권한/보안 프로그램 확인.
2. 사용 모델 카드·라이선스·실행기/Python 고지 검토 및 버전 고정, 배포용 서명/신뢰 경로 결정.
3. 저장소/다운로드 주소 결정 후 게임의 동의·용량 표시·취소·실패 복구·해시 검증·원자적 설치 구현.
4. 게임에서 실행기 기동·토큰·상태 감시 연결, 이후 음성 전달과 실제 AI 행동 연결.
