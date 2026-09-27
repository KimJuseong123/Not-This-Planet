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

    def test_unsaid_emotes_do_not_run(self):
        """말에 없던 감정 표현은 실행하지 않는다 (S15P21B202-736).

        2026-09-23 QA(시끄러운 방)에서 `빛만이 사방` 이 `dance` 로 실행됐다.
        못 알아듣는 것보다 나쁘다 — **엉뚱한 행동은 되돌려야** 한다.

        **정상 명령을 죽이지 않는 것이 이 검사의 절반이다.** 저장 녹음 24개의
        감정 표현 4문장은 모두 제 낱말을 품고 있다.
        """
        def one(action):
            return {"status": "ok", "orders": [order(action, "green")]}

        for said in ("빛만이 사방", "동방에 들고 나가서 눈썹 스펙", ""):
            self.assertEqual(plan.social_without_the_word(said, one("dance")), "dance", said)

        for said, action in [("초록아 춤 춰봐", "dance"), ("파랑아 인사해", "greet"),
                             ("노랑아 하트 해줘", "heart"), ("빨강아 따봉 해", "cheer"),
                             ("다들 응원해줘", "cheer"), ("초록아 안녕", "greet")]:
            self.assertIsNone(plan.social_without_the_word(said, one(action)), said)

        # 감정 표현이 아닌 행동은 낱말을 묻지 않는다 — 어휘 전체로 거르면
        # `이리 와` 같은 정상 명령까지 죽는다.
        self.assertIsNone(plan.social_without_the_word("이리 와", one("follow")))

    @patch("companion_plan.httpx.Client", FakeClient)
    def test_the_model_stays_loaded(self):
        """판단 모델을 내리지 않는다 (S15P21B202-741).

        `keep_alive: "10m"` 이면 10분 조용한 뒤의 첫 명령이 모델을 다시 올리고 프롬프트를
        처음부터 읽는다 — 2026-09-25 측정 11.8초. 워커가 끝날 때 ollama 도 함께 내려간다.
        """
        FakeClient.reply = json.dumps({"status": "none", "orders": []})
        plan.classify("준비 검사입니다", "http://127.0.0.1:1")
        self.assertEqual(FakeClient.request["keep_alive"], -1)

    @patch("companion_plan.httpx.Client", FakeClient)
    def test_a_ghost_emote_becomes_none(self):
        """막았으면 계획이 통째로 `none` 이 되고, 무엇을 막았는지 남는다."""
        FakeClient.reply = json.dumps({"status": "ok", "orders": [order("dance", "green")]})
        result = plan.classify("빛만이 사방", "http://127.0.0.1:1")
        self.assertEqual(result["status"], "none")
        self.assertEqual(result["orders"], [])
        self.assertEqual(result["unsaid_social"], "dance")
        # 들은 말은 그대로 돌려준다 — 화면이 그것을 보여 준다 (S15P21B202-727 · -736)
        self.assertEqual(result["text"], "빛만이 사방")

    def test_revive_is_a_finishing_job(self):
        """`초록아 쟤 좀 살려줘` (S15P21B202-716).

        일으키고 나면 할 일이 남으므로 지속 작업이 아니다 — `쟤 살리고 따라와` 가
        서야 한다.
        """
        up = {"action": "revive", "approach": False, "destination": "default"}
        follow = {"action": "follow", "approach": False, "destination": "default"}
        value = {"status": "ok", "orders": [{"recipients": "green", "steps": [up, follow]}]}
        self.assertEqual(plan.validate(value), value)
        self.assertNotIn("revive", plan.CONTINUOUS)

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
