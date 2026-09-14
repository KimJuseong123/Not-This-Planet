# AI 동료 실험 서버 — S15P21B202-531

Unity 게임과 같은 PC에서 실행하는 로컬 실험 서버입니다. 공개 인터넷용 서비스가 아닙니다.
기존 독립 lab의 소스·비교 테스트 UI를 포함했으므로 저장소 밖의 개인 폴더가 필요하지 않습니다.
모델, Python 환경, 녹음/발화 로그, 결과 파일은 Git에 포함하지 않습니다.

## 준비 (Windows)

필요 도구: Ollama, uv, Unity 프로젝트를 실행할 경우 Unity 6000.5.4f1 및 Git LFS.
이 폴더에서 PowerShell을 엽니다.

1. `./setup.ps1`: Python 3.10 환경 및 requirements.lock 라이브러리 설치. 모델 다운로드/서버 실행은 하지 않습니다.
2. `./start.ps1`: 같은 PC에 Python 서비스 8765와 전용 Ollama 11439를 실행합니다.
3. `& .venv/Scripts/python.exe prepare_models.py`: 모델을 처음 준비할 때 직접 실행합니다. Whisper base, Qwen3 0.6B/1.7B 비교 모델을 내려받습니다.
4. Unity에서 `Assets/Scenes/AICompanionTest.unity`를 실행하거나 별도로 만든 v2 게임 빌드를 실행합니다.
5. V로 음성, F7로 텍스트, F8로 즉시 중단. 못 가까이에서 “이거 못 뽑아줘”를 요청합니다.

실험 전체 준비는 비교 모델까지 받습니다. Unity v2 동작 자체에 필요한 것은 Whisper base + Qwen3 1.7B뿐이며,
최종 배포팩에 비교 모델을 포함하자는 뜻이 아닙니다.
Whisper small 비교는 선택: `& .venv/Scripts/python.exe prepare_small.py`.
음성 비교 페이지는 small과 합성 준비 음성이 필요합니다:
`./make_synthetic_audio.ps1`은 Windows 한국어 TTS가 설치되어 있어야 합니다. 실제 녹음 데이터는 제공하지 않습니다.

이미 8765/11439를 쓰는 서버가 있으면 기존 앱을 임의 종료하지 말고 소유 폴더를 확인하세요.
특히 기존 Desktop/astra/ai-intent-lab-20260914가 실행 중이면 새 사본을 중복 실행하지 마세요.
이번 커밋 후에도 사용자의 기존 로컬 서버는 그대로 보존했습니다.
게임을 종료한 뒤 본인이 실행한 AI 서버를 종료하려면:
`Invoke-RestMethod http://127.0.0.1:8765/api/shutdown -Method Post`
처리 중이면 409로 거절합니다. 종료 시 그 앱이 띄운 전용 Ollama만 정리합니다.

## 구조

- `app.py`: HTTP/음성 입력, 로컬 접근 검사, 공유 작업 잠금, 전용 Ollama 생명주기.
- `companion_v2.py`: Unity용 4개 행동 분류. 기존 `companion.py` v1도 보존.
- `engine.py`, `benchmark.py`, `guided.py`, `static/`: 앞서 만든 10개 행동 비교 실험과 음성 비교 UI.
- `requirements.lock`: 당시 실행 환경의 고정 버전. 새 PC의 설치 가능성은 해당 PC에서 확인이 필요합니다.
- `models/`: 다운로드된 모델. `.venv/`: Python 환경. 둘 다 Git 제외.
- `logs/`, `results/`: 개인 발화와 자원 측정 기록이 생기므로 공유/커밋하지 않습니다.
- `fixtures/`: 합성 검사용 오디오를 로컬에서 생성하는 자리. Git 제외.

## 현재 Unity v2 계약

- POST `/api/companion-v2/text`: JSON `{"text":"이거 못 뽑아줘"}`
- POST `/api/companion-v2/voice`: multipart WAV `audio`, 최대 1MiB.
- 반환: text, action, 처리시간, 자원 측정.
- 실행 행동: follow / wait / cancel / pull_pin.
- none / clarify / 잘못된 응답 / 통신 실패는 Unity에서 기존 행동을 유지하고 사유를 표시합니다.
- 대상 고정못 선택과 도달 가능성 확인, 실제 잡기/당기기는 Unity의 기존 PlayerRig 경로가 담당합니다.
- Qwen3 1.7B, CPU 4 threads, GPU 0, no-thinking, temperature 0, context 2048.
- Whisper base, CPU INT8, 한국어, VAD, beam 1. 클라우드 API 키 미사용.
- 로컬 HTTP를 쓴다고 중앙 서버형 제품인 것은 아닙니다. 최종 로컬/중앙 서버 선택은 미정입니다.
- 모델 이름/태그는 upstream에서 바뀔 수 있습니다. 완전한 재현을 위한 모델 revision/digest 고정은 배포 단계의 후속 작업입니다.

## 검사

```powershell
& .venv/Scripts/python.exe -m unittest test_lab -v
& .venv/Scripts/python.exe check_companion.py
```

첫 명령은 모델 호출을 모의 처리하는 7개 단위 테스트입니다.
두 번째는 실행 중인 실제 로컬 서버에 확인 문장 12개를 보내며, 정확도 벤치마크나 마이크 테스트가 아닙니다.
10개 행동 비교 UI와 Unity의 4개 행동 허용 목록은 다릅니다. UI에서 “용접”이 선택돼도 게임에서 구현됐다는 의미가 아닙니다.

## 공유 및 배포

현재 소스 공유 브랜치만 제공합니다. 빌드/모델/실행팩 Release 게시나 MR은 하지 않습니다.
게임 빌드만 복사하면 AI는 동작하지 않습니다. 이 서버와 모델도 필요합니다.
계획 및 한계: [작업 기록](../../docs/records/S15P21B202-531-ai-companion-prototype.md).
