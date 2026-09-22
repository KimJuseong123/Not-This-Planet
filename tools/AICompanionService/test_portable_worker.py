import json
from pathlib import Path
import tempfile
import socket
import sys
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
