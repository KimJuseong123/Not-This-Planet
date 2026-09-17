# AI 동료 실험 서버 — S15P21B202-531

Unity 게임과 같은 PC에서 실행하는 로컬 실험 서버입니다. 공개 인터넷용 서비스가 아닙니다.
기존 독립 lab의 소스·비교 테스트 UI를 포함했으므로 저장소 밖의 개인 폴더가 필요하지 않습니다.
모델, Python 환경, 녹음/발화 로그, 결과 파일은 Git에 포함하지 않습니다.

## 현재 PC: 단일 폴더로 통합됨 (2026-09-16)

- 이 폴더가 실제 실행 서버이자 수정/공유할 소스입니다. 다른 서버 사본과 동기화할 필요가 없습니다.
- 저장소 루트 `START_AI.bat` 또는 이 폴더의 `START.bat`를 실행하세요.
- 기존 모델을 로컬 복사했고 `.venv`는 Python 3.10 및 캐시된 고정 의존성으로 새 위치에 구성했습니다. 이 PC에서는 setup/모델 다운로드를 다시 하지 않습니다.
- 이전 `Desktop/astra/ai-intent-lab-20260914` 폴더는 보존된 백업입니다. 그곳의 START.bat를 실행하면 안 됩니다.
- models/.venv/results/logs/fixtures는 Git 제외 상태를 유지합니다. 아래 준비 절차는 새 PC에서 필요할 때 사용합니다.

## 준비 (Windows)

최신 v4 게임 코드는 Unity 프로젝트 `METAGAME`의 `Assets/Scenes/AICompanionTest.unity`에서 실행합니다.
`AITrialBuild-tools-v4/AICompanion.exe`는 빌드 출력 예정 위치이며, 현재 빌드 대기 문제로 새 실행 파일이 없습니다.
v4는 v3 이동/못 작업에 **도구 줍기·내려놓기·공 타격·근거리 운석 방어**를 추가합니다.
못을 조준하지 않아도 화자와 가까운 못부터 선택합니다(12m 이내).
v1/v2/v3 경로는 이전 빌드 호환용으로 유지합니다.

필요 도구: Ollama, uv, Unity 프로젝트를 실행할 경우 Unity 6000.5.4f1 및 Git LFS.
이 폴더에서 PowerShell을 엽니다.

1. `./setup.ps1`: Python 3.10 환경 및 requirements.lock 라이브러리 설치. 모델 다운로드/서버 실행은 하지 않습니다.
2. `./start.ps1`: 같은 PC에 Python 서비스 8765와 전용 Ollama 11439를 실행합니다.
3. `& .venv/Scripts/python.exe prepare_models.py`: 모델을 처음 준비할 때 직접 실행합니다. Whisper base, Qwen3 0.6B/1.7B 비교 모델을 내려받습니다.
4. Unity에서 `Assets/Scenes/AICompanionTest.unity`를 실행합니다. 새 v4 실행 파일은 빌드 검증 완료 후 사용합니다.
5. V로 음성, F7로 텍스트, F8로 즉시 중단. 못 가까이에서 “이거 못 뽑아줘”를 요청합니다.

실험 전체 준비는 비교 모델까지 받습니다. Unity v2 동작 자체에 필요한 것은 Whisper base + Qwen3 1.7B뿐이며,
최종 배포팩에 비교 모델을 포함하자는 뜻이 아닙니다.
Whisper small 비교는 선택: `& .venv/Scripts/python.exe prepare_small.py`.
음성 비교 페이지는 small과 합성 준비 음성이 필요합니다:
`./make_synthetic_audio.ps1`은 Windows 한국어 TTS가 설치되어 있어야 합니다. 실제 녹음 데이터는 제공하지 않습니다.

이미 8765/11439를 쓰는 서버가 있으면 기존 앱을 임의 종료하지 말고 소유 폴더를 확인하세요.
특히 기존 Desktop/astra/ai-intent-lab-20260914가 실행 중이면 새 사본을 중복 실행하지 마세요.
이전 로컬 서버 폴더는 백업으로 보존했습니다. 이제 이 폴더의 서버만 실행합니다.
게임을 종료한 뒤 본인이 실행한 AI 서버를 종료하려면:
`Invoke-RestMethod http://127.0.0.1:8765/api/shutdown -Method Post`
처리 중이면 409로 거절합니다. 종료 시 그 앱이 띄운 전용 Ollama만 정리합니다.

## 구조

- `app.py`: HTTP/음성 입력, 로컬 접근 검사, 공유 작업 잠금, 전용 Ollama 생명주기.
- `companion_v2.py`: Unity용 4개 행동 분류. 기존 `companion.py` v1도 보존.
- `companion_v3.py`: 6개 행동 분류. 실제 행동 실행 코드는 Unity에 있으며 LLM은 JSON 행동만 선택합니다.
- `companion_v4.py`: 12개 실행 행동 + none/clarify. 모델은 행동 이름만 선택하며 대상/이동/집기/타격 판정은 게임이 담당합니다.
- `engine.py`, `benchmark.py`, `guided.py`, `static/`: 앞서 만든 10개 행동 비교 실험과 음성 비교 UI.
- `requirements.lock`: 당시 실행 환경의 고정 버전. 새 PC의 설치 가능성은 해당 PC에서 확인이 필요합니다.
- `models/`: 다운로드된 모델. `.venv/`: Python 환경. 둘 다 Git 제외.
- `logs/`, `results/`: 개인 발화와 자원 측정 기록이 생기므로 공유/커밋하지 않습니다.
- `fixtures/`: 합성 검사용 오디오를 로컬에서 생성하는 자리. Git 제외.

## 최신 v4 연결 확인

- GET `/api/health`: 모델 호출 없이 실행 폴더/PID/12개 행동/로드 당시 소스 해시를 반환합니다. protocol은 companion-v4입니다.
- start.ps1은 health를 확인하므로 모델이 바빠도 가벼운 연결 확인이 가능합니다.
- 오래된 실행 코드나 다른 폴더의 서버는 정상으로 재사용하지 않습니다.
- 포트 충돌은 PID를 표시하고 멈춥니다. 사용자의 다른 서버/모델을 자동 종료하지 않습니다.
- 시작 오류는 해당 실행의 로그 마지막 부분과 파일 위치를 보여줍니다.
- POST `/api/companion-v4/text`, `/api/companion-v4/voice`: v2/v3와 같은 입력 형식.
- 추가 행동: pickup_tool / pickup_bat / pickup_welder / drop_tool / hit_target / defend_meteors. 모델 설정은 동일합니다.
- 검사: `python -m unittest test_companion_v3 test_companion_v4 test_service_health -v`.
- 실제 로컬 모델 검사: `python verify_live_v4.py`. 42개 문장을 전용 Ollama로 분류합니다. HTTP/STT 검사는 아닙니다.
- 이전 v3 HTTP 검사: `python verify_live_v3.py`. 26문장과 무음 WAV를 처리하며 결과는 기존 results 기록에 남습니다.
  실제 마이크 발화 인식률 검사는 아닙니다.

## 이전 Unity v2 계약

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

## STT와 LLM 성능 분리 측정

"못 알아듣는다"의 원인이 STT인지 LLM인지 나누기 위한 도구입니다. 게임 행동 성공률 측정이 아닙니다.

| 파일 | 역할 |
|---|---|
| `eval_stt_script.json` | 낭독용 대본 35문장. 12개 행동과 부정문을 포함합니다 |
| `eval_stt.py` | Whisper 단독 인식 정확도. CER·키워드 보존·부정 뒤집힘·설정 A/B |
| `eval_stt_register.py` | 녹음 파일을 `fixtures/stt_cases.jsonl` 평가 케이스로 등록 |
| `watch_captures.py` | 녹음 중 도착 여부를 실시간 확인. 대본 진행과 매핑 TSV를 함께 기록 |

음성 보관은 **기본 꺼짐**입니다. `fixtures/captures/ENABLED` 파일이 있을 때만 `/api/companion-v4/voice`가
받은 원본 WAV를 `fixtures/captures/`에 남깁니다. 켜고 끄는 데 서버 재시작은 필요 없습니다.
개인 발화이므로 `fixtures/`와 `results/`는 Git 제외입니다. 커밋하지 마세요.

곁에 저장되는 `stt_text`는 Whisper가 들은 **초안**이며 정답 대본이 아닙니다. 정답은 사람이 확정합니다.

```powershell
Set-Location 'C:\Users\SSAFY\Desktop\astra\S15P21B202-ai-lobby-20260915\tools\AICompanionService'

# 1. 보관 켜기
New-Item -ItemType Directory -Force fixtures\captures | Out-Null
Set-Content fixtures\captures\ENABLED 'on'

# 2. 옆 터미널에 감시기를 띄우고, 게임에서 V로 대본을 순서대로 읽는다
& .venv\Scripts\python.exe watch_captures.py

# 3. 감시기가 쓴 TSV의 3번째 칸을 확인하고 등록
& .venv\Scripts\python.exe eval_stt_register.py apply-tsv
& .venv\Scripts\python.exe eval_stt_register.py apply-tsv --apply

# 4. 측정
& .venv\Scripts\python.exe eval_stt.py --kind read --presets all

# 5. 보관 끄기
Remove-Item fixtures\captures\ENABLED
```

감시기 없이 모았거나 순서가 어긋났으면 `align`으로 자동 정렬한 뒤 TSV를 검토합니다.
재녹음 기준은 **도착 여부**이지 인식 정확도가 아닙니다. 잘 인식된 발화만 남기면 정확도가 부풀려집니다.

자유 발화를 모았을 때는 `make-tsv`로 전사 목록을 만들고 실제로 말한 내용을 채운 뒤 `apply-tsv`로 등록합니다.
파일 이름을 `pin01.m4a`처럼 대본 id로 지으면 `by-name`으로도 등록됩니다. m4a/mp3/webm도 읽힙니다.

`eval_stt.py`는 서버와 별개 프로세스에서 Whisper를 한 번 더 올립니다. 게임이나 음성 테스트와 동시에 실행하지 마세요.

## STT 엔진 비교

세 엔진을 같은 녹음·같은 지표(`stt_metrics.py`)로 비교합니다. 결과 JSON 형식이 같아
`eval_llm.py --stt-result <파일> --preset <이름>` 으로 어느 엔진이든 최종 행동 정확도를 낼 수 있습니다.

| 러너 | 실행 환경 | 대상 |
|---|---|---|
| `eval_stt.py --model <폴더>` | `.venv` | Whisper 계열 (CTranslate2) |
| `eval_wav2vec2_onnx.py` | `.venv` | wav2vec2 CTC (ONNX) |
| `eval_vosk.py` | `.venv-eval` | Vosk (자유 전사 / 문법 제약) |

`vosk`는 서버의 고정된 `.venv`를 건드리지 않도록 별도 `.venv-eval`에만 설치합니다. 둘 다 Git 제외입니다.

```powershell
Set-Location 'C:\Users\SSAFY\Desktop\astra\S15P21B202-ai-lobby-20260915\tools\AICompanionService'

& '.\.venv\Scripts\python.exe' eval_stt.py --model whisper-small-ko-zeroth --kind read --presets current
& '.\.venv\Scripts\python.exe' eval_wav2vec2_onnx.py --kind read
& '.\.venv-eval\Scripts\python.exe' eval_vosk.py --kind read
```

받아 둔 모델 (`models/`, Git 제외): `whisper-base`, `whisper-small`, `whisper-small-ko-zeroth`,
`vosk-model-small-ko-0.22`, `wav2vec2-ko-onnx`.

주의할 점:

- `initial_prompt`는 쓰지 않습니다. 음성이 불명확하면 Whisper가 프롬프트를 그대로 되뱉습니다(실측 4건).
  키워드 보존율도 함께 오염되므로 프롬프트 없는 preset이 올바른 기준선입니다.
- 짧은 명령에서는 CER 분모가 작아 값이 크게 튑니다. 키워드 보존율과 최종 행동 정확도를 함께 봅니다.
- Vosk 문법 제약은 모델 사전에 없는 단어(`고정못`, `뽑아줘` 등)를 조용히 무시합니다. 실행 시 경고를 확인하세요.
- 닫힌집합 매칭 수치는 사용자가 미리 정한 문장 중 하나를 말한다고 가정한 값이라 실사용보다 낙관적입니다.
