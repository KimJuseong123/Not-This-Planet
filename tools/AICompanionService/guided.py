"""Paired, local-only speech comparison. Reference sentences never enter ASR prompts."""
import io
import json
import random
import time
import unicodedata
from uuid import uuid4

from fastapi import APIRouter, File, Form, HTTPException, UploadFile
from fastapi.responses import FileResponse
from engine import ROOT, THREADS, SKILLS, Monitor, classify

router = APIRouter()
CASES = [
    ('pin', '야 이거 못 좀 뽑아줘', 'pull_pin', '평소 친구에게 부탁하듯 말해 주세요.'),
    ('no_pin', '그 못은 뽑지 마', 'cancel', '부정 표현을 제대로 구분하는지 봅니다.'),
    ('follow', '야 내 쪽으로 좀 와봐', 'follow', '특별히 또박또박하지 말고 자연스럽게.'),
    ('wait', '나 올 때까지 여기서 기다려', 'wait', '따라오기와 대기를 구분합니다.'),
    ('weld', '이 두 파츠 용접해서 붙여줘', 'weld', '게임 용어를 알아듣는지 확인합니다.'),
    ('unweld', '잘못 붙였어 용접한 거 끊어줘', 'unweld', '용접하기와 해제를 구분합니다.'),
    ('correction', '용접하지 말고 조립대로 옮겨줘', 'carry_part', '앞부분만 듣고 잘못 고르지 않는지 봅니다.'),
    ('meteor', '운석 날아온다 저거 쳐내줘', 'defend_meteor', '위협에 대응하라는 부탁입니다.'),
]
PROFILES = [
    ('base', 'base · 기본', 'base', None),
    ('base_hint', 'base · 게임 용어 힌트', 'base', '고정못, 파츠, 조립대, 용접건, 용접, 견인 모듈, 운석, 몽둥이.'),
    ('small', 'small · 기본', 'small', None),
]
CACHE = {}


def normalized(text):
    return ''.join(c for c in unicodedata.normalize('NFC', text) if c.isalnum())


def cer(reference, hypothesis):
    ref, hyp = normalized(reference), normalized(hypothesis)
    prev = list(range(len(hyp) + 1))
    for i, a in enumerate(ref, 1):
        row = [i]
        for j, b in enumerate(hyp, 1):
            row.append(min(row[-1]+1, prev[j]+1, prev[j-1]+(a != b)))
        prev = row
    return round(100 * prev[-1] / max(1, len(ref)), 1)


def get_model(size):
    from faster_whisper import WhisperModel
    start = time.perf_counter()
    cold = size not in CACHE
    if cold:
        path = ROOT/'models'/f'whisper-{size}'
        if not (path/'model.bin').exists():
            raise ValueError(f'{size} 모델이 없습니다. prepare_small.py로 준비해 주세요.')
        CACHE[size] = WhisperModel(str(path), device='cpu', compute_type='int8', cpu_threads=THREADS,
                                  num_workers=1, local_files_only=True)
    return CACHE[size], cold, round((time.perf_counter()-start)*1000, 1)


def recognize(audio, profile):
    _, label, size, hint = profile
    with Monitor() as monitor:
        model, cold, load_ms = get_model(size)
        start = time.perf_counter()
        kwargs = dict(language='ko', beam_size=1, vad_filter=True, condition_on_previous_text=False)
        if hint:
            kwargs['initial_prompt'] = hint
        segments, _ = model.transcribe(audio, **kwargs)
        text = ' '.join(s.text.strip() for s in segments).strip()
        infer_ms = round((time.perf_counter()-start)*1000, 1)
    return dict(profile=profile[0], label=label, text=text, cold=cold, load_ms=load_ms,
                asr_ms=infer_ms, resources=monitor.result)


@router.get('/guided')
def page():
    return FileResponse(ROOT/'static'/'guided.html')


@router.get('/api/guided/cases')
def cases():
    return {'cases': [dict(id=i, text=t, expected=e, label=SKILLS[e][0], hint=h) for i,t,e,h in CASES],
            'ready': all(p[2] in CACHE for p in PROFILES),
            'small_downloaded': (ROOT/'models'/'whisper-small'/'model.bin').exists()}


@router.post('/api/guided/prepare')
def prepare():
    from app import LOCK
    if not LOCK.acquire(blocking=False):
        raise HTTPException(409, '다른 시험이 진행 중입니다.')
    try:
        from faster_whisper.audio import decode_audio
        # Existing artificial fixture only; no microphone access or user reference is used.
        sample = decode_audio(str(ROOT/'fixtures'/'synthetic-follow.wav'), sampling_rate=16000)
        for profile in PROFILES:
            recognize(sample, profile)
        classify('여기서 기다려', 'qwen3:1.7b', '')
        return {'ready': True, 'message': '3가지 설정 예열 완료. 녹음을 시작하세요.'}
    except Exception as exc:
        raise HTTPException(503, str(exc)) from exc
    finally:
        LOCK.release()


@router.post('/api/guided/compare')
def compare(audio: UploadFile = File(...), case_id: str = Form(...), source: str = Form('guided_microphone')):
    from app import LOCK
    case = next((c for c in CASES if c[0] == case_id), None)
    if not case or source not in {'guided_microphone', 'synthetic_validation'}:
        raise HTTPException(400, '알 수 없는 시험 항목')
    raw = audio.file.read(10*1024*1024+1)
    if len(raw) > 10*1024*1024:
        raise HTTPException(413, '파일은 10MB 이하여야 합니다.')
    if not LOCK.acquire(blocking=False):
        raise HTTPException(409, '다른 시험이 진행 중입니다.')
    try:
        from faster_whisper.audio import decode_audio
        started = time.perf_counter()
        decoded = decode_audio(io.BytesIO(raw), sampling_rate=16000)
        if not 0 < len(decoded) <= 30*16000:
            raise ValueError('녹음은 0초 초과 30초 이하여야 합니다.')
        decode_ms = round((time.perf_counter()-started)*1000, 1)
        order = list(PROFILES)
        random.SystemRandom().shuffle(order)
        rows = []
        for profile in order:
            try:
                row = recognize(decoded, profile)
                row['cer_percent'] = cer(case[1], row['text'])
                rows.append(row)
            except Exception as exc:
                rows.append({'profile': profile[0], 'label': profile[1], 'error': str(exc)})
        # The reference is tested separately, never supplied as ASR/LLM context for hypotheses.
        try:
            reference_result = classify(case[1], 'qwen3:1.7b', '')
        except Exception as exc:
            reference_result = {'error': str(exc)}
        for row in rows:
            if 'error' in row:
                continue
            try:
                row['intent'] = classify(row['text'], 'qwen3:1.7b', '') if row['text'] else {
                    'action': None, 'label': '인식 없음', 'llm_wall_ms': 0}
                row['action_correct'] = row['intent']['action'] == case[2]
            except Exception as exc:
                row['intent_error'] = str(exc)
                row['action_correct'] = False
        result = dict(id=str(uuid4()), timestamp=time.strftime('%Y-%m-%dT%H:%M:%S%z'),
                      source=source, case_id=case_id, reference=case[1], expected=case[2],
                      reference_intent=reference_result, audio_seconds=round(len(decoded)/16000, 2),
                      decode_ms=decode_ms, order=[p[0] for p in order], rows=rows,
                      settings={'cpu_threads':THREADS,'compute_type':'int8','beam_size':1,
                                'vad':True,'language':'ko','hints':PROFILES[1][3]},
                      comparison_wall_ms=round((time.perf_counter()-started)*1000,1))
        out = ROOT/'results'/'guided-comparison.jsonl'
        out.parent.mkdir(exist_ok=True)
        with out.open('a', encoding='utf-8') as file:
            file.write(json.dumps(result, ensure_ascii=False)+'\n')
        return result
    except Exception as exc:
        raise HTTPException(422, str(exc)) from exc
    finally:
        LOCK.release()


@router.get('/api/guided/results')
def history():
    path = ROOT/'results'/'guided-comparison.jsonl'
    rows = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()] if path.exists() else []
    return {'rows': rows[-100:]}


@router.get('/api/guided/download')
def download():
    path = ROOT/'results'/'guided-comparison.jsonl'
    if not path.exists():
        raise HTTPException(404, '아직 시험 기록이 없습니다.')
    return FileResponse(path, filename=path.name, media_type='application/x-ndjson')
