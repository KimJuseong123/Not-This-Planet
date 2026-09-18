"""Verify routes using existing artificial audio, never a human microphone."""
import json
import httpx
from engine import ROOT
from guided import cer, normalized

assert cer('못 뽑아줘!', '못뽑아줘') == 0
assert cer('못', '') == 100
assert cer('못', '못못') == 100

with httpx.Client(base_url='http://127.0.0.1:8765', timeout=180, trust_env=False) as client:
    for url in ['/guided','/static/guided.js','/static/guided.css','/api/guided/cases']:
        r=client.get(url);r.raise_for_status()
    r=client.post('/api/guided/prepare');r.raise_for_status();print(r.json(),flush=True)
    with (ROOT/'fixtures'/'synthetic-pull-pin.wav').open('rb') as f:
        r=client.post('/api/guided/compare',data={'case_id':'pin','source':'synthetic_validation'},files={'audio':('fixture.wav',f,'audio/wav')})
    r.raise_for_status();result=r.json()
    assert len(result['rows']) == 3
    assert {r['profile'] for r in result['rows']} == {'base','base_hint','small'}
    assert all('text' in row and 'intent' in row for row in result['rows']), result
    assert result['source'] == 'synthetic_validation'
    assert all(not row['cold'] for row in result['rows'])
    assert client.post('/api/guided/prepare',headers={'Origin':'https://example.com'}).status_code==403
    assert client.post('/api/guided/compare',data={'case_id':'invalid'},files={'audio':('a.wav',b'bad','audio/wav')}).status_code==400
    assert client.post('/api/guided/compare',data={'case_id':'pin'},files={'audio':('a.wav',b'bad','audio/wav')}).status_code==422
    print(json.dumps({'validation':'PASS (synthetic audio only)', 'rows':result['rows'], 'reference':result['reference_intent']},ensure_ascii=False),flush=True)
