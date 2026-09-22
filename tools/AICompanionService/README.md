# AI 동료 로컬 서버

> 배포용 추가팩 제작/실행 계약은 [PORTABLE.md](PORTABLE.md)를 참고하세요.
> 아래 설치 절차는 기존 개발·실험용이며 일반 사용자에게 요구할 절차가 아닙니다.

Unity 게임과 **같은 PC**에서 도는 로컬 서버입니다. 음성과 인식 결과가 이 PC를 떠나지 않습니다.
공개 서비스가 아니며 127.0.0.1 로만 바인딩합니다.

```
V 음성 / F7 문장  →  Whisper(STT)  →  Qwen3:1.7b(행동 분류)  →  허용 행동 하나  →  게임
```

모델은 저장소에 없습니다. 아래 절차로 각자 PC에 받습니다 — 게임 업데이트마다 수 GB를
다시 받지 않기 위해서입니다(계획: `docs/ai-companion.md` §10).

## 처음 받은 사람 — 설치

**미리 필요한 것**

| 도구 | 확인 |
|---|---|
| [Ollama](https://ollama.com/download) | `ollama --version` |
| [uv](https://docs.astral.sh/uv/getting-started/installation/) | `uv --version` |
| Unity 6000.5.4f1 (게임까지 볼 경우) | Unity Hub |

**받는 용량과 시간**: 모델 약 **2.9GB** (Qwen3 1.7B·0.6B, Whisper 한국어·기본).
네트워크에 따라 **20~60분**. 디스크는 여유 5GB를 봅니다.

**순서** (이 폴더에서 PowerShell)

```powershell
# 1. 파이썬 3.10 환경과 고정 의존성
./setup.ps1

# 2. 서버 실행 (8765) + 전용 Ollama (11439)
./START.bat

# 3. 모델 내려받기 — 서버가 떠 있어야 합니다
& .venv/Scripts/python.exe prepare_models.py

# 4. 확인
Invoke-RestMethod http://127.0.0.1:8765/api/health | Select-Object protocol, busy
Invoke-RestMethod http://127.0.0.1:8765/api/status | Select-Object asr_model
```

`protocol: companion-v4` 와 `asr_model: whisper-small-ko-zeroth` 가 나오면 준비된 것입니다.

**동작 확인** — 게임 없이 한 줄로 시험할 수 있습니다.

```powershell
$body = [Text.Encoding]::UTF8.GetBytes('{"text":"몽둥이 주워줘"}')
Invoke-RestMethod http://127.0.0.1:8765/api/companion-v4/text -Method Post `
  -ContentType 'application/json; charset=utf-8' -Body $body | Select-Object action
```

`action: pickup_bat` 이 나오면 분류까지 정상입니다.

> 한글을 보낼 때는 반드시 UTF-8 바이트로 넘깁니다. PowerShell 5.1 은 문자열 본문을
> 기본 코드페이지로 보내서 한글이 깨지고, 그러면 `clarify` 가 나옵니다.

## Unity 쪽

1. `Tools > Not This Planet > AI 실험 > 실험 씬을 이 PC 빌드 목록에 등록` 을 **한 번** 누릅니다.
   실험 씬은 공유 빌드 목록에 넣지 않습니다(출시 빌드에 딸려 들어가는 것을 막기 위해).
   누르지 않으면 진입 화면이 버튼 대신 이 안내를 띄웁니다.
2. `Assets/Scenes/AICompanionTest.unity` 를 열고 Play → **혼자 테스트**
3. **F7** 문장 입력 · **V** 누르는 동안 음성 · **F8** 즉시 중단

못 가까이에서 "이거 못 뽑아줘" 처럼 말합니다. 화자 12m 안의 가까운 못을 고릅니다.

**또렷하게 말하는 것이 인식률에 가장 크게 작용합니다** — 측정에서 CER 0.60 → 0.29.
마이크에 가까이, 조금 느리게 말해 보세요. 연속 2회 못 알아들으면 화면이 안내합니다.

## 잘 안 될 때

| 증상 | 원인과 조치 |
|---|---|
| `AI 서버 연결 실패/시간 초과` | 서버가 꺼져 있습니다. `START.bat` 실행 |
| `음성 모델이 없습니다: ...` | 3단계를 건너뛰었습니다. `prepare_models.py` 실행 |
| `Ollama 설치가 필요합니다` | Ollama 미설치. 설치 후 새 터미널에서 재시도 |
| `Port 8765 is occupied (PID ...)` | 이미 다른 서버가 떠 있습니다. **PID를 죽이지 말고** 소유 폴더와 health 를 확인 |
| 진입 화면이 검은색 | Unity 쪽 1번(씬 등록)을 안 눌렀습니다 |
| 첫 요청이 느림 | 모델 로딩입니다. 이후 요청은 STT 약 1.6초 + 분류 약 0.8초 |

서버를 끄려면:

```powershell
Invoke-RestMethod http://127.0.0.1:8765/api/shutdown -Method Post
```

처리 중이면 409 로 거절합니다. 그 앱이 띄운 전용 Ollama만 함께 정리합니다.

## 모델을 바꾸려면

기본값은 한국어 파인튜닝 `whisper-small-ko-zeroth` 입니다. 측정에서 `whisper-base` 대비
CER 0.86 → 0.61 이었고 속도는 비슷했습니다. 다른 모델로 시험하려면:

```powershell
$env:COMPANION_ASR_MODEL = 'whisper-base'   # models/ 아래 폴더 이름
./START.bat
```

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
