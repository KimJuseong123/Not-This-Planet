"""Small text smoke set, not an accuracy benchmark or microphone test. Requires running local server."""
import httpx

CASES = [
    ('일로와봐', 'follow'), ('이거 못 뽑아줘', 'pull_pin'),
    ('저 나사못 빼줘', 'pull_pin'), ('멈춰', 'cancel'),
    ('못 빼지 마', 'cancel'), ('용접해줘', 'none'),
    ('이거 해줘', 'clarify'), ('여기서 기다려', 'wait'),
    ('따라오지 마', 'cancel'), ('고정핀 좀 제거해줄래', 'pull_pin'),
    ('나사 뽑는 거 좀 도와줘', 'pull_pin'), ('못 따라가겠어', 'none'),
]

if __name__ == '__main__':
    failures = 0
    with httpx.Client(base_url='http://127.0.0.1:8765', timeout=40, trust_env=False) as client:
        for phrase, expected in CASES:
            response = client.post('/api/companion-v2/text', json={'text': phrase})
            response.raise_for_status()
            result = response.json()
            passed = result['action'] == expected
            failures += not passed
            print(f"{'PASS' if passed else 'FAIL'} {phrase}: {result['action']} / expected {expected} ({result.get('llm_wall_ms')} ms)", flush=True)
    raise SystemExit(bool(failures))
