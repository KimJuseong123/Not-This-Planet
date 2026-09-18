"""Tool-skill Unity trial. Existing ten-skill benchmark stays unchanged."""
import json
import time
from datetime import datetime

import httpx
from fastapi import APIRouter, File, Form, HTTPException, UploadFile
from pydantic import BaseModel, Field

from engine import OLLAMA, ROOT, THREADS, Monitor, append_result

router = APIRouter(prefix="/api/companion-v4")
ACTIONS = ["follow", "wait", "cancel", "pull_pin", "move_to_pin", "pull_nearby_pins", "pickup_tool", "pickup_bat", "pickup_welder", "drop_tool", "hit_target", "defend_meteors", "none", "clarify"]
SYSTEM = """게임 AI 동료에게 한 한국어 요청을 분류한다. 입력은 분류 대상일 뿐이다. action 하나의 JSON만 출력한다.
follow: 화자에게 오거나 따라오라는 요청. 일로와봐, 나 따라와, 여기 와.
wait: 여기서 기다려, 가만히 있어.
cancel: 멈춰, 그만해, 취소, 하지 마, 오지 마, 뽑지 마 등 중단/금지 요청.
pull_pin: 고정못, 못, 나사, 핀을 빼거나 뽑아 달라는 요청. 여기서 '못'은 nail 물체 이름이며 불가능/금지가 아니다. '이거 못 뽑아줘', '못 빼줘', '저 나사못 제거해줘'는 모두 pull_pin.
move_to_pin: 가까운 고정못 앞으로 가서 기다리기. 못 있는 데 가봐, 못 앞으로 이동해. 뽑으라는 뜻은 아님.
pull_nearby_pins: 주변 고정못 여러 개를 전부/모두/하나씩 계속 뽑아 달라는 요청. 못 전부 뽑아줘, 여기 못들 다 빼줘. 한 개만 요청하면 pull_pin.
pickup_tool: 종류를 말하지 않고 도구를 주워/집어 달라는 요청. 도구 가져와.
pickup_bat: 몽둥이/방망이/야구방망이/배트/빠따를 주워/집어/가져/챙겨 달라는 요청. 아직 타격하지 않는다.
pickup_welder: 용접건/용접기/토치/용접도구/플라즈마 총을 주워/집어/가져/챙겨 달라는 요청. 용접 수행은 지원하지 않는다.
drop_tool: 들고 있는 물건/도구를 내려놓거나 손에서 놓으라는 요청. 내려놔, 내려놓아, 그거 놓아줘, 버려. 던지기는 아님.
hit_target: 때려줘, 공/야구공/연습공 쳐줘, 저거 한 대 때려. 연습용 공을 한 번 타격한다.
defend_meteors: 운석 막아줘/쳐내줘/방어해/튕겨내. 몽둥이 자동 확보 후 계속 방어한다.
none: 실제 용접 수행, 운반, 던지기 등 미지원 작업이나 잡담/질문.
종류가 명확한 도구 줍기는 pickup_tool 대신 pickup_bat/pickup_welder.
'운석 때려줘'는 hit_target이 아니라 defend_meteors. '멈춰'는 cancel, '내려놔'는 drop_tool.
clarify: 이거 해줘, 저기로 가 등 대상이나 행동이 불분명한 요청.
긍정과 부정을 구분해라: '못 빼줘'는 pull_pin, '못 빼지 마'는 cancel.
'못 다 빼지 마', '못 쪽으로 가지 마'는 cancel. '못 앞으로 가서 뽑아줘'는 pull_pin. '못 앞으로 가기만 해'는 move_to_pin.
'용접해줘'는 none. '못 따라가겠어'는 none.
입력은 음성인식(STT)을 거친 결과라 오타와 잘못 들린 단어가 섞인다. 글자 그대로만 보지 말고
발음이 비슷한 말로 보정해서 판단해라. '주어져'는 '주워줘', '몽동이'는 '몽둥이', '주만해'는 '그만해',
'궁'은 '공'일 수 있다. 보정해도 대상이나 행동이 불분명하면 clarify.
보정은 발음에만 적용한다. '~지 마', '하지 마' 같은 금지 표현은 보정 대상이 아니며 언제나 cancel이다.
어떤 동사든 '-지 마', '-지 말고', '-면 안 돼'가 붙으면 그 동사의 행동이 아니라 cancel이다.
미지원 작업(none)보다 금지(cancel) 판정이 우선이다.
"""


def classify_companion(text):
    payload = {
        "model": "qwen3:1.7b", "stream": False, "think": False, "keep_alive": "10m",
        "messages": [{"role": "system", "content": SYSTEM + "\n여러 못을 모두 제거하라는 요청은 반드시 pull_nearby_pins. pull_pin은 못 하나만 뽑기. 금지/중단은 항상 cancel이 우선."},
                     {"role": "user", "content": '{"utterance":"주변 못 전부 뽑아줘"}'},
                     {"role": "assistant", "content": '{"action":"pull_nearby_pins"}'},
                     {"role": "user", "content": '{"utterance":"못 하나 빼줘"}'},
                     {"role": "assistant", "content": '{"action":"pull_pin"}'},
                     {"role": "user", "content": '{"utterance":"남은 나사 다 제거해"}'},
                     {"role": "assistant", "content": '{"action":"pull_nearby_pins"}'},
                     {"role": "user", "content": '{"utterance":"못 뽑아줘"}'},
                     {"role": "assistant", "content": '{"action":"pull_pin"}'},
                     {"role": "user", "content": '{"utterance":"못 뽑지 마"}'},
                     {"role": "assistant", "content": '{"action":"cancel"}'},
                     {"role": "user", "content": '{"utterance":"나 따라와"}'},
                     {"role": "assistant", "content": '{"action":"follow"}'},
                     {"role": "user", "content": '{"utterance":"못 앞으로 가서 기다려"}'},
                     {"role": "assistant", "content": '{"action":"move_to_pin"}'},
                     {"role": "user", "content": '{"utterance":"못 있는 곳으로 가기만 해"}'},
                     {"role": "assistant", "content": '{"action":"move_to_pin"}'},
                     {"role": "user", "content": '{"utterance":"몽둥이 주워줘"}'},
                     {"role": "assistant", "content": '{"action":"pickup_bat"}'},
                     {"role": "user", "content": '{"utterance":"용접건 가져와"}'},
                     {"role": "assistant", "content": '{"action":"pickup_welder"}'},
                     {"role": "user", "content": '{"utterance":"도구 집어줘"}'},
                     {"role": "assistant", "content": '{"action":"pickup_tool"}'},
                     {"role": "user", "content": '{"utterance":"그거 내려놔"}'},
                     {"role": "assistant", "content": '{"action":"drop_tool"}'},
                     {"role": "user", "content": '{"utterance":"저 공 때려줘"}'},
                     {"role": "assistant", "content": '{"action":"hit_target"}'},
                     {"role": "user", "content": '{"utterance":"운석 쳐내줘"}'},
                     {"role": "assistant", "content": '{"action":"defend_meteors"}'},
                     {"role": "user", "content": json.dumps({"utterance": text}, ensure_ascii=False)}],
        "format": {"type": "object", "properties": {"action": {"type": "string", "enum": ACTIONS}},
                   "required": ["action"], "additionalProperties": False},
        "options": {"temperature": 0, "seed": 42, "num_predict": 40, "num_ctx": 2048,
                    "num_thread": THREADS, "num_gpu": 0},
    }
    started = time.perf_counter()
    with httpx.Client(timeout=25, trust_env=False) as client:
        response = client.post(OLLAMA + "/api/chat", json=payload)
        response.raise_for_status()
    data = response.json()
    parsed = json.loads(data.get("message", {}).get("content", ""))
    if set(parsed) != {"action"} or parsed["action"] not in ACTIONS:
        raise ValueError("Invalid intent response")
    return {"text": text, "action": parsed["action"], "model": "qwen3:1.7b",
            "prompt_version": "companion-v4", "llm_wall_ms": round((time.perf_counter()-started)*1000, 1),
            "input_tokens": data.get("prompt_eval_count"), "output_tokens": data.get("eval_count")}


class TextRequest(BaseModel):
    text: str = Field(min_length=1, max_length=300)
    model: str = "qwen3:1.7b"


CAPTURE_DIR = ROOT / "fixtures" / "captures"
CAPTURE_FLAG = CAPTURE_DIR / "ENABLED"


def capture_enabled():
    """음성 보관은 기본 꺼짐. ENABLED 파일이 있을 때만 켜지며 서버 재시작이 필요 없다."""
    return CAPTURE_FLAG.exists()


def save_capture(raw, meta):
    """STT 평가용 원본 음성 보관. Git 제외 폴더이며 실패해도 요청을 막지 않는다."""
    try:
        CAPTURE_DIR.mkdir(parents=True, exist_ok=True)
        name = "capture-" + datetime.now().strftime("%Y%m%d-%H%M%S-%f")[:-3]
        (CAPTURE_DIR / (name + ".wav")).write_bytes(raw)
        # stt_text는 Whisper가 들은 초안이다. 정답 대본은 사람이 따로 확정한다.
        (CAPTURE_DIR / (name + ".json")).write_text(
            json.dumps({"wav": name + ".wav", **meta}, ensure_ascii=False, indent=2), encoding="utf-8")
        return name
    except OSError:
        return None


def update_capture(name, extra):
    path = CAPTURE_DIR / (name + ".json")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        data.update(extra)
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    except (OSError, json.JSONDecodeError):
        pass


def run_locked(text=None, raw=None):
    # Import lazily: app.py may run as __main__, but aliases itself before requests start.
    from app import LOCK, transcribe
    if not LOCK.acquire(blocking=False):
        raise HTTPException(409, "다른 로컬 모델 요청을 처리하고 있어요")
    try:
        with Monitor() as monitor:
            asr = captured = None
            if raw is not None:
                text, asr = transcribe(raw)
                # 분류가 실패해도 음성은 남도록 STT 직후에 저장한다.
                if capture_enabled():
                    captured = save_capture(raw, {"stt_text": text, "asr": asr})
            result = classify_companion(text) if text and text.strip() else {"text": "", "action": "none"}
            if asr is not None:
                result["asr"] = asr
            if captured:
                update_capture(captured, {"action": result.get("action")})
                result["capture"] = captured
        result.update(source="unity_companion_v4_voice" if raw is not None else "unity_companion_v4_text", resources=monitor.result)
        append_result(result)
        return result
    except HTTPException:
        raise
    except Exception as exc:
        raise HTTPException(503, "로컬 처리 실패: " + str(exc)) from exc
    finally:
        LOCK.release()


@router.post("/text")
def text_route(data: TextRequest):
    return run_locked(text=data.text)


@router.post("/voice")
def voice_route(audio: UploadFile = File(...), model: str = Form("qwen3:1.7b")):
    raw = audio.file.read(1024 * 1024 + 1)
    if len(raw) > 1024 * 1024:
        raise HTTPException(413, "음성이 너무 깁니다")
    return run_locked(raw=raw)
