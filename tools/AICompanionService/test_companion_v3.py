import unittest
from unittest.mock import patch

import companion_v2
import companion_v3
from test_lab import FakeClient


class CompanionV3Tests(unittest.TestCase):
    def test_old_endpoint_keeps_old_action_set(self):
        self.assertNotIn('move_to_pin', companion_v2.ACTIONS)
        self.assertNotIn('pull_nearby_pins', companion_v2.ACTIONS)

    @patch('companion_v3.httpx.Client', FakeClient)
    def test_six_skills_use_cpu_model_and_strict_schema(self):
        for action in companion_v3.ACTIONS:
            FakeClient.reply = '{"action":"' + action + '"}'
            result = companion_v3.classify_companion('자연어 요청')
            self.assertEqual(action, result['action'])
            self.assertEqual('companion-v3', result['prompt_version'])
            self.assertEqual(0, FakeClient.request['options']['num_gpu'])
            self.assertEqual(companion_v3.ACTIONS, FakeClient.request['format']['properties']['action']['enum'])

    @patch('companion_v3.httpx.Client', FakeClient)
    def test_unknown_or_extra_output_is_not_executed(self):
        for value in ['{"action":"launch_rocket"}', '{"action":"pull_pin","code":"anything"}', 'broken']:
            FakeClient.reply = value
            with self.assertRaises(ValueError):
                companion_v3.classify_companion('못 뽑아줘')


if __name__ == '__main__':
    unittest.main()
