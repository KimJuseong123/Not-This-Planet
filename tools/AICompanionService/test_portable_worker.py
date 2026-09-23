import json
from pathlib import Path
import tempfile
import socket
import sys
import time
import unittest
from unittest.mock import patch
from fastapi.testclient import TestClient
import portable_worker as p


class PortableTests(unittest.TestCase):
    def worker(self):
        return p.Worker("missing-models", "missing-ollama", 1, 1, "a" * 64, 11439)

    def test_token_required(self):
        w = self.worker()
        c = TestClient(p.make_app(w))
        self.assertEqual(c.get("/api/health").status_code, 401)
        self.assertEqual(c.post("/api/shutdown").status_code, 401)
        self.assertFalse(w.stopping.is_set())

    def test_browser_origin_rejected_even_with_token(self):
        c = TestClient(p.make_app(self.worker()))
        self.assertEqual(c.get("/api/health", headers={"Authorization": "Bearer " + "a"*64, "Origin": "http://localhost"}).status_code, 403)

    def test_health_never_claims_game_actions_ready(self):
        w = self.worker()
        c = TestClient(p.make_app(w), headers={"Authorization": "Bearer " + w.token})
        with patch.object(p.companion_v4, "classify_companion", side_effect=AssertionError("no model call")):
            r = c.get("/api/health")
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()["state"], "loading")
        self.assertFalse(r.json()["game_actions_ready"])

    def test_no_lab_or_capture_routes(self):
        paths = {r.path for r in p.make_app(self.worker()).routes}
        self.assertEqual(paths, {"/api/health", "/api/shutdown", "/api/companion-v4/text", "/api/companion-v4/voice", "/api/companion-social/text", "/api/companion-plan/text", "/api/companion-plan/voice"})

    def test_voice_uses_current_plan_classifier(self):
        w = self.worker(); w.state = "ready"
        with patch.object(w, "transcribe", return_value="못 다 뽑아줘"), patch.object(p.companion_plan, "classify", return_value={"status": "ok", "orders": []}) as classify:
            w.execute(raw=b"audio", plan=True)
            classify.assert_called_once_with("못 다 뽑아줘", w.url)

    def test_the_two_slow_parts_are_reported_apart(self):
        """받아쓰기와 판단을 **따로** 돌려준다 (S15P21B202-734).

        2026-09-23 QA 에 19.7초가 찍혔는데 어디서 났는지 가릴 수 없었다 —
        `execute` 가 나눠 잰 값을 합계로 덮어썼기 때문이다.
        """
        w = self.worker(); w.state = "ready"

        def slow_transcribe(_raw):
            time.sleep(.05)
            return "초록아 고정못 뽑아"

        with patch.object(w, "transcribe", side_effect=slow_transcribe), \
             patch.object(p.companion_plan, "classify",
                          return_value={"status": "ok", "orders": [], "llm_ms": 1234}):
            result = w.execute(raw=b"audio", plan=True)

        self.assertGreaterEqual(result["asr_ms"], 40, "받아쓰기 시간이 안 잡힌다")
        self.assertEqual(result["llm_ms"], 1234, "판단 시간이 합계에 덮였다")
        self.assertGreaterEqual(result["elapsed_ms"], result["asr_ms"],
                                "합계가 부분보다 작다")

    def test_rejected_plan_still_reports_what_was_heard(self):
        """계획이 터져도 **받아쓴 말은 돌려준다** (S15P21B202-727).

        전에는 `text` 없이 돌려줘서 게임의 `[AI] 들음:` 로그에 아무것도 안 남았다 —
        밖에서 보면 **"못 알아들었다" 와 "듣고 버렸다" 가 똑같아 보인다.** 2026-09-23
        측정에서 실제로 받아쓰기를 애먼 곳에서 의심하게 만들었다.
        """
        w = self.worker(); w.state = "ready"
        with patch.object(w, "transcribe", return_value="나머지는 고정못 뽑아"), \
             patch.object(p.companion_plan, "classify", side_effect=ValueError("Invalid recipients")):
            result = w.execute(raw=b"audio", plan=True)
        self.assertEqual(result["status"], "none")
        self.assertEqual(result["text"], "나머지는 고정못 뽑아")

    def test_silence_reports_an_empty_transcript_not_a_missing_one(self):
        """빈 받아쓰기도 `text` 자리를 비워 둔 채 돌려준다 (S15P21B202-727).

        키가 아예 없으면 받는 쪽이 "말이 없었다" 와 "응답이 이상하다" 를 못 가른다.
        """
        w = self.worker(); w.state = "ready"
        with patch.object(w, "transcribe", return_value=""), \
             patch.object(p.companion_plan, "classify", side_effect=AssertionError("no model call")):
            result = w.execute(raw=b"audio", plan=True)
        self.assertEqual(result["text"], "")
        self.assertEqual(result["status"], "none")

    def test_invalid_plan_does_not_kill_runtime(self):
        w = self.worker(); w.state = "ready"
        with patch.object(p.companion_plan, "classify", side_effect=ValueError("invalid plan")):
            self.assertEqual(w.execute(text="명령", plan=True)["status"], "none")
            self.assertEqual(w.state, "ready")

    def test_non_social_approach_flag_is_ignored_without_changing_intent(self):
        raw = {"status": "ok", "orders": [{"recipients": "one", "steps": [{"action": "follow", "approach": True, "destination": "speaker"}]}]}
        result = p.companion_plan.validate(p.companion_plan.normalize_irrelevant_approach(raw))
        self.assertFalse(result["orders"][0]["steps"][0]["approach"])
        self.assertTrue(raw["orders"][0]["steps"][0]["approach"])
        self.assertEqual(result["orders"][0]["steps"][0]["action"], "follow")

    def test_silent_voice_is_empty_plan_not_old_action_schema(self):
        w = self.worker(); w.state = "ready"
        with patch.object(w, "transcribe", return_value=""), patch.object(p.companion_plan, "classify") as classify:
            result = w.execute(raw=b"audio", plan=True)
            self.assertEqual(result["status"], "none")
            self.assertEqual(result["orders"], [])
            classify.assert_not_called()

    def test_voice_route_rejects_non_audio_and_large_upload(self):
        w = self.worker()
        c = TestClient(p.make_app(w), headers={"Authorization": "Bearer " + w.token})
        self.assertEqual(c.post("/api/companion-plan/voice", files={"audio": ("x.wav", b"bad", "audio/wav")}).status_code, 400)
        self.assertEqual(c.post("/api/companion-plan/voice", files={"audio": ("x.wav", b"a" * 256045, "audio/wav")}).status_code, 413)

    def test_not_ready_rejects_inference(self):
        c = TestClient(p.make_app(self.worker()), headers={"Authorization": "Bearer " + "a"*64})
        self.assertEqual(c.post("/api/companion-v4/text", json={"text": "따라와"}).status_code, 503)

    def test_busy_rejects_without_changing_state(self):
        w = self.worker(); w.state = "ready"
        with w.lock, self.assertRaises(p.HTTPException) as caught:
            w.execute(text="따라와")
        self.assertEqual(caught.exception.status_code, 409)
        self.assertEqual(w.state, "ready")

    def test_missing_models_does_not_launch_any_process(self):
        w = self.worker()
        with patch.object(p.subprocess, "Popen", side_effect=AssertionError("must not launch")):
            w.warmup()
        self.assertEqual(w.state, "failed")

    def test_inference_does_not_call_lab_logging(self):
        w = self.worker(); w.state = "ready"
        with patch.object(p.companion_v4, "classify_companion", return_value={"action": "follow"}), \
             patch.object(p.companion_v4, "append_result", side_effect=AssertionError("no saved text")):
            self.assertEqual(w.execute(text="따라와")["action"], "follow")

    def test_invalid_token_rejected(self):
        with self.assertRaises(ValueError):
            p.Worker(".", ".", 1, 1, "short", 11439)

    def test_occupied_ollama_port_is_not_taken_over(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0)); listener.listen()
            w = self.worker()
            w.ollama_exe = Path(sys.executable)
            w.ollama_port = listener.getsockname()[1]
            with patch.object(p, "check_models"), patch.object(p.subprocess, "Popen", side_effect=AssertionError("no takeover")):
                w.warmup()
            self.assertEqual(w.state, "failed")
            self.assertIsNone(w.process)
            self.assertGreater(listener.fileno(), -1)

    def test_parent_identity_checked_not_only_pid(self):
        w = self.worker()
        with patch.object(p.psutil, "Process") as proc:
            proc.return_value.create_time.return_value = 2
            self.assertFalse(w.parent_alive())

    def test_digest_path_traversal_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            asr = root / p.ASR_NAME; asr.mkdir()
            for name in ("model.bin", "config.json", "tokenizer.json"):
                (asr / name).touch()
            manifest = root / "ollama/manifests/registry.ollama.ai/library/qwen3/1.7b"
            manifest.parent.mkdir(parents=True)
            manifest.write_text(json.dumps({"config": {"digest": "../../bad", "size": 1}, "layers": []}))
            with self.assertRaises(ValueError):
                p.check_models(root)


if __name__ == "__main__":
    unittest.main()
