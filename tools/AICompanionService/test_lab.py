import io
import json
import unittest
import wave
from unittest.mock import patch

import httpx

import engine
from benchmark import percentile, summarize


class FakeResponse:
    def __init__(self, data): self.data = data
    def raise_for_status(self): pass
    def json(self): return self.data


class FakeClient:
    reply = '{"action":"pull_pin"}'
    request = None
    def __init__(self, **kwargs): pass
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def post(self, url, json):
        FakeClient.request = json
        return FakeResponse({"message": {"content": self.reply}, "done_reason": "stop"})
    def get(self, url): return FakeResponse({"models": [{"size_vram": 0}]})


class Tests(unittest.TestCase):
    def test_disallow_external_model(self):
        with self.assertRaises(ValueError): engine.classify("hello", "cloud-model")
    def test_empty(self):
        with self.assertRaises(ValueError): engine.classify(" ", "qwen3:0.6b")
    @patch("engine.httpx.Client", FakeClient)
    def test_cpu_options_and_result(self):
        FakeClient.reply = '{"action":"pull_pin"}'
        result = engine.classify("야 못 좀 뽑아줘", "qwen3:0.6b")
        self.assertEqual(result["action"], "pull_pin")
        self.assertEqual(FakeClient.request["options"]["num_gpu"], 0)
        self.assertFalse(FakeClient.request["think"])
        self.assertEqual(result["model_vram_bytes"], 0)
    @patch("engine.httpx.Client", FakeClient)
    def test_no_silent_fallback_on_bad_output(self):
        FakeClient.reply = '{"action":"launch_rocket"}'
        with self.assertRaises(ValueError): engine.classify("발사", "qwen3:0.6b")
    @patch("engine.httpx.Client", FakeClient)
    def test_extra_fields_rejected(self):
        FakeClient.reply = '{"action":"pull_pin","execute":true}'
        with self.assertRaises(ValueError): engine.classify("못", "qwen3:0.6b")
    def test_summary_counts_error_as_failure(self):
        result = summarize([{"model":"qwen3:0.6b","text":"x","expected":"follow","error":"timeout"}])
        self.assertEqual(result[0]["accuracy_percent"], 0)
        self.assertEqual(result[0]["errors"], 1)
    def test_percentile(self):
        self.assertEqual(percentile(list(range(1, 101)), .95), 95)


if __name__ == "__main__": unittest.main()
