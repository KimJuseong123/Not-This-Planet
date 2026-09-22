import copy
import json
import unittest
from unittest.mock import patch
import companion_plan as plan
from test_lab import FakeClient

def order(action, recipient="default"):
    return {"recipients": recipient, "steps": [{"action": action, "approach": False, "destination": "default"}]}

class PlanTests(unittest.TestCase):
    def test_every_action_has_a_valid_single_step(self):
        for action in plan.ACTIONS:
            value = {"status": "ok", "orders": [order(action)]}
            self.assertEqual(plan.validate(value), value)

    def test_sequence_and_split(self):
        first = order("pickup_bat", "two")
        first["steps"] += order("defend_meteors")["steps"]
        plan.validate({"status": "ok", "orders": [first, order("wait", "remaining")]})

    def test_reject_invalid_or_unbounded_plans(self):
        valid = {"status": "ok", "orders": [order("dance")]}
        invalid = [{}, {"status": "ok", "orders": []}, {"status": "none", "orders": valid["orders"]},
                   {"status": "ok", "orders": [order("dance", "remaining")]},
                   {"status": "ok", "orders": [order("execute_code")]},
                   {"status": "ok", "orders": [order("dance")] * 4}]
        for key, val in (("approach", "true"), ("destination", "arbitrary_xyz")):
            bad = copy.deepcopy(valid); bad["orders"][0]["steps"][0][key] = val; invalid.append(bad)
        sequence = order("follow"); sequence["steps"] += order("dance")["steps"]
        invalid.append({"status": "ok", "orders": [sequence]})
        for value in invalid:
            with self.assertRaises(ValueError): plan.validate(value)

    @patch("companion_plan.httpx.Client", FakeClient)
    def test_stop_cannot_start_misclassified_work(self):
        FakeClient.reply = json.dumps({"status": "ok", "orders": [order("dance", "green")]})
        result = plan.classify("초록아 춤 그만 춰", "http://127.0.0.1:1")
        self.assertEqual(result["orders"][0]["steps"][0]["action"], "cancel")
        self.assertEqual(result["raw_decision"]["orders"][0]["steps"][0]["action"], "dance")
        self.assertEqual(FakeClient.request["options"]["num_gpu"], 0)

    @patch("companion_plan.httpx.Client", FakeClient)
    def test_no_task_is_not_a_cancel(self):
        FakeClient.reply = json.dumps({"status": "clarify", "orders": []})
        self.assertEqual(plan.classify("그거 해줘", "http://127.0.0.1:1")["orders"], [])

if __name__ == "__main__": unittest.main()
