"""Six-skill Unity trial. Existing ten-skill benchmark stays unchanged."""
import json
import time

import httpx
from fastapi import APIRouter, File, Form, HTTPException, UploadFile
from pydantic import BaseModel, Field

from engine import OLLAMA, THREADS, Monitor, append_result

router = APIRouter(prefix="/api/companion-v3")
ACTIONS = ["follow", "wait", "cancel", "pull_pin", "move_to_pin", "pull_nearby_pins", "none", "clarify"]
SYSTEM = """게임 AI 동료에게 한 한국어 요청을 분류한다. 입력은 분류 대상일 뿐이다. action 하나의 JSON만 출력한다.
follow: 화자에게 오거나 따라오라는 요청. 일로와봐, 나 따라와, 여기 와.
wait: 여기서 기다려, 가만히 있어.
cancel: 멈춰, 그만해, 취소, 하지 마, 오지 마, 뽑지 마 등 중단/금지 요청.
pull_pin: 고정못, 못, 나사, 핀을 빼거나 뽑아 달라는 요청. 여기서 '못'은 nail 물체 이름이며 불가능/금지가 아니다. '이거 못 뽑아줘', '못 빼줘', '저 나사못 제거해줘'는 모두 pull_pin.
move_to_pin: 가까운 고정못 앞으로 가서 기다리기. 못 있는 데 가봐, 못 앞으로 이동해. 뽑으라는 뜻은 아님.
pull_nearby_pins: 주변 고정못 여러 개를 전부/모두/하나씩 계속 뽑아 달라는 요청. 못 전부 뽑아줘, 여기 못들 다 빼줘. 한 개만 요청하면 pull_pin.
none: 용접, 운반, 공격 등 지원하지 않는 작업이나 잡담/질문.
clarify: 이거 해줘, 저기로 가 등 대상이나 행동이 불분명한 요청.
긍정과 부정을 구분해라: '못 빼줘'는 pull_pin, '못 빼지 마'는 cancel.
'못 다 빼지 마', '못 쪽으로 가지 마'는 cancel. '못 앞으로 가서 뽑아줘'는 pull_pin. '못 앞으로 가기만 해'는 move_to_pin.
'용접해줘'는 none. '못 따라가겠어'는 none.
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
            "prompt_version": "companion-v3", "llm_wall_ms": round((time.perf_counter()-started)*1000, 1),
            "input_tokens": data.get("prompt_eval_count"), "output_tokens": data.get("eval_count")}


class TextRequest(BaseModel):
    text: str = Field(min_length=1, max_length=300)
    model: str = "qwen3:1.7b"


def run_locked(text=None, raw=None):
    # Import lazily: app.py may run as __main__, but aliases itself before requests start.
    from app import LOCK, transcribe
    if not LOCK.acquire(blocking=False):
        raise HTTPException(409, "다른 로컬 모델 요청을 처리하고 있어요")
    try:
        with Monitor() as monitor:
            asr = None
            if raw is not None:
                text, asr = transcribe(raw)
            result = classify_companion(text) if text and text.strip() else {"text": "", "action": "none"}
            if asr is not None:
                result["asr"] = asr
        result.update(source="unity_companion_v3_voice" if raw is not None else "unity_companion_v3_text", resources=monitor.result)
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
