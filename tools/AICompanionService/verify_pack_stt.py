# -*- coding: utf-8 -*-
"""구운 추가팩에 **저장된 녹음을 넣어** 실제 받아쓰기를 확인한다.

    .pack-venv\\Scripts\\python.exe verify_pack_stt.py <팩 폴더> <녹음 폴더>

**왜 필요한가.** `smoke_portable.py` 는 워커가 뜨고 `ready` 가 되는지까지만 본다.
그런데 이번 변경의 전부는 **무엇을 어떻게 알아듣는가**다 — 모델을 바꾸고 힌트를 바꿨다.
워커가 떠도 옛 모델을 보거나 힌트를 안 넘기면 **아무것도 좋아지지 않은 채로 나간다.**

EC2 에 1.9GB 를 올리기 전 마지막 관문이다. 2026-09-23 기준값은 **낱말 적중 77%**
(53/69, 저장 녹음 24개). 크게 낮으면 **구운 것이 시험한 것과 다르다.**

사람 목소리를 새로 받지 않는다 — 이미 저장해 둔 녹음을 그대로 쓴다. 그래야 개발 중
측정과 **같은 소리로** 비교된다.

**빈 칸을 받아쓰기 탓으로 세지 마라.** 워커는 계획 단계가 터져도 `text` 없이 돌려주므로
"못 들었다" 와 "듣고 버렸다" 가 똑같아 보인다. 처음 이 도구를 돌렸을 때 그 때문에 70% 가
나왔고, 받아쓰기를 애먼 곳에서 의심했다(실제로는 77%). 아래에서 `elapsed_ms` 로 가른다.
"""
import json
import secrets
import subprocess
import sys
import time
from pathlib import Path

import httpx
import psutil

#: 명령이 서려면 살아야 하는 말. `stt_sweep.KEYWORDS` 와 같은 뜻이다.
KEYWORDS = [
    "파랑", "초록", "빨강", "노랑", "다들", "전원", "나머지",
    "고정못", "파츠", "도구", "몽둥이", "방망이", "용접건", "견인", "조립대", "운석", "연습공",
    "따라", "기다", "모여", "그만", "멈춰", "중단",
    "뽑", "집", "가져", "내려놔", "잡", "놓", "밀", "끌",
    "때려", "막", "용접", "떼", "들", "올려", "운반", "쌓",
    "춤", "인사", "하트", "따봉", "전부",
]


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    pack, recordings = Path(sys.argv[1]), Path(sys.argv[2])
    exe = pack / "runtime/ntp-ai-worker/ntp-ai-worker.exe"
    models = pack / "models"
    index = recordings / "index.json"
    for path in (exe, models, index):
        if not path.exists():
            sys.exit("없습니다: " + str(path))

    cases = json.loads(index.read_text(encoding="utf-8"))
    print("팩 %s · 녹음 %d개" % (pack.name, len(cases)), flush=True)

    # 워커의 부모 노릇. 워커는 부모가 죽으면 스스로 내려간다.
    #
    # **`--parent-started` 를 빠뜨리면 준비를 시작조차 않는다.** pid 는 재사용되므로
    # 워치독이 "같은 pid 의 다른 프로세스" 를 가려내려고 시작 시각까지 본다. 처음에
    # 이것을 빼고 돌렸다가 15분을 헛되이 기다렸다 — 오류도 없이 조용히 안 뜬다.
    parent = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(1200)"])
    token = secrets.token_hex(32)
    worker = None
    try:
        worker = subprocess.Popen(
            [str(exe), "--model-root", str(models), "--parent-pid", str(parent.pid),
             "--parent-started", str(psutil.Process(parent.pid).create_time())],
            stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        worker.stdin.write((token + "\n").encode())
        worker.stdin.flush()
        worker.stdin.close()

        base = "http://127.0.0.1:18765"
        client = httpx.Client(base_url=base, timeout=120, trust_env=False,
                              headers={"Authorization": "Bearer " + token})

        print("모델을 데우는 중 ...", flush=True)
        began = time.time()
        while time.time() - began < 900:
            try:
                health = client.get("/api/health").json()
                if health.get("state") == "ready":
                    break
                if health.get("state") == "failed":
                    sys.exit("워커가 실패했습니다: " + str(health.get("error")))
            except httpx.HTTPError:
                pass
            time.sleep(2)
        else:
            sys.exit("15분 안에 ready 가 되지 않았습니다")
        print("  ready (%.0f초) · 모델 %s" % (time.time() - began, health.get("asr_model", "?")), flush=True)

        want = hit = dropped = 0
        print()
        for case in cases:
            wav = (recordings / case["wav"]).read_bytes()
            r = client.post("/api/companion-plan/voice",
                            files={"audio": (case["wav"], wav, "audio/wav")})
            r.raise_for_status()
            data = r.json()
            heard = (data.get("text") or "").strip()
            keys = [k for k in KEYWORDS if k in case["expected"]]
            miss = [k for k in keys if k not in heard]
            want += len(keys)
            hit += len(keys) - len(miss)

            # **빈 칸은 두 가지 뜻이다.** 워커는 받아쓰기가 비었을 때도, 계획 단계가
            # 터졌을 때도 `text` 없이 돌려준다 — 둘이 같아 보이면 받아쓰기를 애먼
            # 곳에서 의심하게 된다. `elapsed_ms` 로 가른다: 받아쓰기가 빈 것이면 그
            # 시간이 들어 있고(1초 이상), 계획이 터진 것이면 0 이다.
            note = ""
            if not heard:
                if data.get("elapsed_ms", 0) > 500:
                    note = "  ← 받아쓰기가 비었다"
                else:
                    dropped += 1
                    note = ("  ← 받아쓰기는 됐고 계획 단계가 터졌다 (elapsed_ms=%s)"
                            % data.get("elapsed_ms"))
            print("  %s %-32s → %s%s" % ("OK  " if not miss else "놓침",
                                         case["expected"], heard, note),
                  flush=True)

        print()
        print("  낱말 적중 %d/%d (%.0f%%)" % (hit, want, hit / want * 100 if want else 0))
        print("  2026-09-23 기준값은 77%% 였다 — 크게 낮으면 구운 것이 시험한 것과 다르다.")
        if dropped:
            print("  계획 단계가 %d건을 버렸다 (S15P21B202-727) — 그만큼은 받아쓰기 탓이 아니다."
                  % dropped)
    finally:
        for p in (worker, parent):
            if p is not None and p.poll() is None:
                p.terminate()
                try:
                    p.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    p.kill()


if __name__ == "__main__":
    main()
