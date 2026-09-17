import unittest
from types import SimpleNamespace
from unittest.mock import patch

from fastapi import HTTPException
import app


class ServiceHealthTests(unittest.TestCase):
    def test_health_is_cheap_and_advertises_all_twelve_skills(self):
        with patch("app.httpx.Client", side_effect=AssertionError("Health must not call models")):
            result = app.health()
        self.assertEqual("companion-v4", result["protocol"])
        self.assertEqual(str(app.ROOT), result["service_root"])
        self.assertTrue({"follow", "wait", "cancel", "pull_pin", "move_to_pin", "pull_nearby_pins",
                         "pickup_tool", "pickup_bat", "pickup_welder", "drop_tool", "hit_target", "defend_meteors"} <= set(result["actions"]))
        self.assertEqual({"app.py", "companion_v3.py", "companion_v4.py"}, set(result["source_hashes"]))

    def test_all_version_routes_are_registered(self):
        paths = app.app.openapi()["paths"]
        for path in ("/api/health", "/api/companion-v4/text", "/api/companion-v4/voice", "/api/companion-v3/text", "/api/companion-v3/voice",
                     "/api/companion-v2/text", "/api/companion/text"):
            self.assertIn(path, paths)

    def test_shutdown_without_control_never_reports_false_success(self):
        with patch.object(app, "SERVER", None):
            with self.assertRaises(HTTPException) as caught:
                app.shutdown()
        self.assertEqual(503, caught.exception.status_code)

    def test_shutdown_signals_owned_server(self):
        server = SimpleNamespace(should_exit=False)
        with patch.object(app, "SERVER", server):
            self.assertEqual({"stopping": True}, app.shutdown())
        self.assertTrue(server.should_exit)

    def test_busy_shutdown_is_rejected(self):
        server = SimpleNamespace(should_exit=False)
        with patch.object(app, "SERVER", server), app.LOCK:
            with self.assertRaises(HTTPException) as caught:
                app.shutdown()
        self.assertEqual(409, caught.exception.status_code)
        self.assertFalse(server.should_exit)


if __name__ == "__main__":
    unittest.main()
