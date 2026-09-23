"""받아쓰기 설정이 **한 곳에만** 있는가 (S15P21B202-714).

실행: python scripts/ci/tests/test_ai_worker_constants.py

**무엇을 막나.** 모델 이름이 `app.py` · `portable_worker.py` · `build_portable.py`
세 곳에 박혀 있었다. 하나만 고치면 **굽는 것과 쓰는 것이 달라지는데**, 그 어긋남은
2.34GB 추가팩을 다 굽고 EC2 에 올린 뒤에야 드러난다.

힌트(`ASR_HOTWORDS` · `ASR_CONTEXT`)도 같다. 워커와 개발 서버가 각자 들고 있으면
**시험한 것과 게임에 나가는 것이 갈린다** — 그리고 시험 쪽이 늘 더 좋아 보인다.

**게임의 자리 색과도 맞춘다.** `ASR_CONTEXT` 는 `<색>아 <행동>` 본보기이므로 그 색이
`TeamSlots.Names` 와 같아야 한다. 게임에서 색을 바꾸고 여기를 안 고치면, 받아쓰기가
**없는 동료 이름**을 듣도록 배운 채로 나간다.

`tools/AICompanionService` 의 자체 테스트는 faster-whisper·httpx 가 필요해 CI 에서 돌지
않는다. 이 파일은 **표준 라이브러리만** 쓰므로 `ci-tests` 에서 돈다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import ast
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
AI = os.path.join(ROOT, "tools", "AICompanionService")
SLOTS = os.path.join(ROOT, "METAGAME", "Assets", "_Project", "Scripts",
                     "Runtime", "Network", "TeamSlots.cs")

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def read(*parts):
    with io.open(os.path.join(*parts), encoding="utf-8") as f:
        return f.read()


def constant(source, name):
    """`engine.py` 의 상수 값. `" ".join([...])` 형태도 푼다."""
    for node in ast.parse(source).body:
        if isinstance(node, ast.Assign) and getattr(node.targets[0], "id", "") == name:
            if isinstance(node.value, ast.Call):
                return " ".join(ast.literal_eval(node.value.args[0]))
            return ast.literal_eval(node.value)
    return None


print()
print("== 받아쓰기 설정이 한 곳에만 있는가 (S15P21B202-714) ==")

engine = read(AI, "engine.py")

# ── ① 세 상수가 engine.py 에 있다
for name in ("ASR_MODEL_NAME", "ASR_HOTWORDS", "ASR_CONTEXT"):
    check(constant(engine, name) is not None,
          f"engine.py 에 {name} 이 없다 — 설정이 흩어졌다는 뜻이다")
print("  세 상수가 engine.py 에 있다 ✅")

# ── ② 쓰는 쪽이 제 값을 따로 들고 있지 않다
#
#     `whisper-` 로 시작하는 문자열이 코드에 박혀 있으면 그것이 곧 두 번째 진실이다.
for name in ("app.py", "portable_worker.py", "build_portable.py"):
    source = read(AI, name)
    hard = [m for m in re.findall(r'"(whisper-[a-z0-9._-]+)"', source)]
    check(not hard,
          f"{name} 에 모델 이름이 박혀 있다: {hard} — engine.ASR_MODEL_NAME 을 쓸 것")
    check("ASR_MODEL_NAME" in source,
          f"{name} 이 ASR_MODEL_NAME 을 쓰지 않는다")
print("  쓰는 쪽 셋이 모두 공용 상수를 본다 ✅")

# ── ③ 받아쓰기가 힌트를 실제로 넘긴다
#
#     상수만 있고 안 넘기면 **아무 일도 일어나지 않는다.** 2026-09-23 실측으로 이것이
#     43% → 80% 의 차이였다(같은 녹음 24개, 낱말 적중률).
worker = read(AI, "portable_worker.py")
app = read(AI, "app.py")
for name, source in (("portable_worker.py", worker), ("app.py", app)):
    check("initial_prompt=ASR_CONTEXT" in source,
          f"{name} 이 문맥을 넘기지 않는다 — 상수만 있고 효과가 없다")
    check("hotwords=ASR_HOTWORDS" in source,
          f"{name} 이 hotwords 를 넘기지 않는다")
print("  워커와 개발 서버가 힌트를 실제로 넘긴다 ✅")

# ── ④ 본보기 문맥의 색이 게임의 자리 색과 같다
#
#     `ASR_CONTEXT` 는 "<색>아 <행동>" 본보기다. 게임에서 색을 바꾸고 여기를 안 고치면
#     받아쓰기가 **없는 동료 이름**을 듣도록 배운 채로 나간다.
context = constant(engine, "ASR_CONTEXT") or ""
hotwords = (constant(engine, "ASR_HOTWORDS") or "").split()
names = re.search(r'string\[\]\s+Names\s*=\s*\{([^}]*)\}', read(SLOTS))
check(names is not None, "TeamSlots.cs 에서 Names 를 찾지 못했다 — 이 검사가 헛돈다")
if names:
    colors = re.findall(r'"([^"]+)"', names.group(1))
    check(len(colors) == 4, f"자리 색이 4개가 아니다: {colors}")
    missing_ctx = [c for c in colors if c not in context]
    missing_hot = [c for c in colors if c not in hotwords]
    check(not missing_ctx,
          f"본보기 문맥에 없는 자리 색: {missing_ctx} — 그 동료는 이름이 안 들린다")
    check(not missing_hot,
          f"hotwords 에 없는 자리 색: {missing_hot} — 색이 '누구에게' 의 전부다")
    print(f"  본보기 문맥과 hotwords 가 게임 색 {colors} 를 덮는다 ✅")

# ── ⑤ LLM 을 CPU 로 강제하지 않는다
#
#     `num_gpu: 0` 은 ollama 의 자동 판단을 덮어썼다. 방장은 누구나 될 수 있으므로
#     GPU 강제도 CPU 강제도 답이 아니다 — PC 마다 알아서 정하게 둔다.
plan = read(AI, "companion_plan.py")
code = re.sub(r"#.*", "", plan)
check("num_gpu" not in code,
      "companion_plan.py 가 num_gpu 를 지정한다 — ollama 의 자동 판단을 덮어쓴다")
print("  LLM 을 CPU 로 강제하지 않는다 ✅")

if fails:
    print(f"\n❌ 실패 {len(fails)}건")
    for line in fails:
        print("   " + line)
    raise SystemExit(1)

print("\nTEST714_AI_WORKER_CONSTANTS OK")
