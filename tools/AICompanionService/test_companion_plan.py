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

    def test_goto_carries_a_destination_and_comes_last(self):
        """`초록아 조립대로 가 있어` (S15P21B202-717).

        전에는 이동 명령이 `follow`·`gather` 뿐이라 **AI 를 "나에게" 만 부를 수
        있었다.** 도착한 뒤 자리를 지키는 지속 작업이므로 뒤에 다른 단계가 못 온다.
        """
        there = {"action": "goto", "approach": False, "destination": "assembly"}
        value = {"status": "ok", "orders": [{"recipients": "green", "steps": [there]}]}
        self.assertEqual(plan.validate(value), value)
        self.assertIn("goto", plan.CONTINUOUS)
        with self.assertRaises(ValueError):
            plan.validate({"status": "ok", "orders": [
                {"recipients": "green", "steps": [there, order("dance")["steps"][0]]}]})

    def test_remaining_alone_is_a_command_not_a_mistake(self):
        """`나머지는 고정못 뽑아` (S15P21B202-727).

        전에는 첫 지시의 `remaining` 을 막았고, 그래서 **한 글자도 안 틀리게 받아쓴
        명령이 조용히 사라졌다.** 게임은 `remaining` 을 "앞선 지시가 안 쓴 동료" 로
        푸는데 첫 지시에서는 그것이 곧 전원이므로, 통과시켜도 새로 닿는 곳이 없다.
        """
        value = {"status": "ok", "orders": [order("pull_pin", "remaining")]}
        self.assertEqual(plan.validate(value), value)

    def test_reject_invalid_or_unbounded_plans(self):
        valid = {"status": "ok", "orders": [order("dance")]}
        invalid = [{}, {"status": "ok", "orders": []}, {"status": "none", "orders": valid["orders"]},
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
        # `num_gpu` 를 주지 않는다 (S15P21B202-714). 전에는 `0` 으로 **CPU 를 강제**해
        # ollama 의 자동 판단을 덮어썼다. 방장 PC 는 제각각이므로 켜는 것도 끄는 것도
        # 답이 아니다. `-714` 에서 코드는 고쳤는데 이 단언은 `0` 인 채로 남아 있었다 —
        # 이 파일은 CI 에서 안 돌아서 아무도 빨간 것을 못 봤다.
        self.assertNotIn("num_gpu", FakeClient.request["options"])

    @patch("companion_plan.httpx.Client", FakeClient)
    def test_no_task_is_not_a_cancel(self):
        FakeClient.reply = json.dumps({"status": "clarify", "orders": []})
        self.assertEqual(plan.classify("그거 해줘", "http://127.0.0.1:1")["orders"], [])

if __name__ == "__main__": unittest.main()
