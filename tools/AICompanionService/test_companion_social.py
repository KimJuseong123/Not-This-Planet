import json
import unittest
from unittest.mock import patch
import companion_social as social
from test_lab import FakeClient

class SocialTests(unittest.TestCase):
    @patch("companion_social.httpx.Client",FakeClient)
    def test_prohibition_never_starts_misclassified_dance(self):
        FakeClient.reply=json.dumps(dict(action="dance",recipients="default",approach=False))
        result=social.classify("다들 춤추지 마","http://127.0.0.1:1")
        self.assertEqual(result["action"],"cancel")
        self.assertEqual(result["recipients"],"all")
        self.assertTrue(result["safety_override"])
        self.assertEqual(result["raw_decision"]["action"],"dance")
    @patch("companion_social.httpx.Client",FakeClient)
    def test_schema_and_cpu(self):
        for action in social.ACTIONS:
            FakeClient.reply=json.dumps(dict(action=action,recipients="default",approach=False))
            self.assertEqual(social.classify("테스트","http://127.0.0.1:1")["action"],action)
        self.assertEqual(FakeClient.request["options"]["num_gpu"],0)

    @patch("companion_social.httpx.Client",FakeClient)
    def test_reject_unknown_or_partial_decisions(self):
        for value in ({},dict(action="dance"),dict(action="dance",recipients="human",approach=False),
                      dict(action="cancel",recipients="all",approach=True),
                      dict(action="greet",recipients="all",approach="true")):
            FakeClient.reply=json.dumps(value)
            with self.assertRaises(ValueError): social.classify("테스트","http://127.0.0.1:1")

if __name__=="__main__": unittest.main()
