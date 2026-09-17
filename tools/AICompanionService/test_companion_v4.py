import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import companion_v3
import companion_v4
import app
from fastapi.testclient import TestClient
from test_lab import FakeClient

class CompanionV4Tests(unittest.TestCase):
    def test_v3_is_preserved(self):
        self.assertNotIn("pickup_bat", companion_v3.ACTIONS)

    @patch("companion_v4.httpx.Client", FakeClient)
    def test_all_allowed_actions_and_cpu_model(self):
        for action in companion_v4.ACTIONS:
            FakeClient.reply = '{"action":"' + action + '"}'
            result = companion_v4.classify_companion("자연어 명령")
            self.assertEqual(action, result["action"])
            self.assertEqual("companion-v4", result["prompt_version"])
            self.assertEqual(0, FakeClient.request["options"]["num_gpu"])
        self.assertEqual(14, len(companion_v4.ACTIONS))

    @patch("companion_v4.httpx.Client", FakeClient)
    def test_malformed_never_falls_back_to_an_action(self):
        for raw in ('{}', '{"action":"weld"}', '{"action":"pickup_bat","execute":true}', 'broken'):
            FakeClient.reply = raw
            with self.assertRaises(ValueError):
                companion_v4.classify_companion("요청")

    def test_text_and_voice_route_contracts_without_starting_a_server(self):
        # No lifespan context: this ASGI unit test never starts/stops Ollama or binds a port.
        client = TestClient(app.app, base_url="http://127.0.0.1:8765")
        with patch("companion_v4.run_locked", return_value={"text": "몽둥이 주워", "action": "pickup_bat"}) as run:
            response = client.post("/api/companion-v4/text", json={"text": "몽둥이 주워"})
            self.assertEqual(200, response.status_code)
            run.assert_called_once_with(text="몽둥이 주워")
            self.assertEqual(422, client.post("/api/companion-v4/text", json={"text": ""}).status_code)
        with patch("companion_v4.run_locked", return_value={"text": "내려놔", "action": "drop_tool"}) as run:
            response = client.post("/api/companion-v4/voice", files={"audio": ("test.wav", b"fixture", "audio/wav")})
            self.assertEqual(200, response.status_code)
            run.assert_called_once_with(raw=b"fixture")
            response = client.post("/api/companion-v4/voice", files={"audio": ("large.wav", b"0" * (1024 * 1024 + 1), "audio/wav")})
            self.assertEqual(413, response.status_code)
        client.close()

    def _run_voice(self, folder):
        """음성 경로를 모델 없이 한 번 태운다. 실제 Whisper/Ollama를 호출하지 않는다."""
        with patch.object(companion_v4, "CAPTURE_DIR", folder), \
                patch.object(companion_v4, "CAPTURE_FLAG", folder / "ENABLED"), \
                patch.object(companion_v4, "append_result"), \
                patch("app.transcribe", return_value=("못 뽑아줘", {"asr_ms": 1.0})), \
                patch("companion_v4.classify_companion",
                      return_value={"text": "못 뽑아줘", "action": "pull_pin"}):
            return companion_v4.run_locked(raw=b"RIFFfixture")

    def test_voice_capture_stays_off_without_the_flag_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "captures"
            result = self._run_voice(folder)
            self.assertNotIn("capture", result)
            self.assertFalse(folder.exists())

    def test_voice_capture_keeps_audio_and_final_action_when_enabled(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "captures"
            folder.mkdir()
            (folder / "ENABLED").write_text("on", encoding="utf-8")
            result = self._run_voice(folder)
            name = result["capture"]
            self.assertEqual(b"RIFFfixture", (folder / (name + ".wav")).read_bytes())
            saved = json.loads((folder / (name + ".json")).read_text(encoding="utf-8"))
            # 정답 대본이 아니라 Whisper 초안으로만 남는다.
            self.assertEqual("못 뽑아줘", saved["stt_text"])
            self.assertEqual("pull_pin", saved["action"])

    def test_busy_request_is_rejected_without_model_execution(self):
        from fastapi import HTTPException
        with patch("companion_v4.classify_companion", side_effect=AssertionError("Busy requests must not call model")), app.LOCK:
            with self.assertRaises(HTTPException) as caught:
                companion_v4.run_locked(text="운석 막아줘")
            self.assertEqual(409, caught.exception.status_code)

if __name__ == "__main__":
    unittest.main()
