"""음성 명령의 낱말 규칙이 판단기와 같은 뜻을 내는가 (S15P21B202-741).

실행: python scripts/ci/tests/test_ai_command_rules.py

**왜 규칙이 있나.** 목표가 정확도가 아니라 **말이 끝난 뒤 행동이 정해질 때까지의 시간**이다
(2026-09-25). 규칙이 정하면 워커가 판단기(qwen3)를 부르지 않는다 — 녹음 74개에서 6.4초가
2.0초가 됐다.

**무엇을 막나.**
  · 규칙이 판단기가 모르는 행동·목적지·대상을 낸다 → 게임이 받지 못하거나 엉뚱하게 푼다
  · 규칙이 **말에 없던 감정 표현**을 낸다 → `-736` 이 판단기에서 막은 헛행동이 규칙으로 샌다
  · `춤추지 마` 에 춤춘다 — 되돌려야 하는 행동은 못 알아듣는 것보다 나쁘다
  · 워커가 규칙을 건너뛰거나, 다른 프롬프트로 데우거나, 모델을 다시 내린다

`tools/AICompanionService` 의 자체 테스트는 fastapi·httpx 가 필요해 CI 에서 돌지 않는다. 규칙
모듈은 표준 라이브러리만 쓰므로 여기서 그대로 불러 시험한다. **`assert` 를 쓰지 않는다**(`python -O`).
"""
import ast
import importlib.util
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
SVC = os.path.join(ROOT, "tools", "AICompanionService")

failed = 0


def check(ok, msg):
    global failed
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if not ok:
        failed += 1


def source(name):
    return io.open(os.path.join(SVC, name), encoding="utf-8").read()


def constants(name, wanted):
    out = {}
    for node in ast.parse(source(name)).body:
        if isinstance(node, ast.Assign) and getattr(node.targets[0], "id", "") in wanted:
            out[node.targets[0].id] = ast.literal_eval(node.value)
    return out


spec = importlib.util.spec_from_file_location("command_rules", os.path.join(SVC, "command_rules.py"))
R = importlib.util.module_from_spec(spec)
spec.loader.exec_module(R)
P = constants("companion_plan.py", {"ACTIONS", "DESTINATIONS", "RECIPIENTS", "SOCIAL_WORDS"})

print("== 규칙 모듈은 표준 라이브러리만 쓴다 ==")
imports = {n.names[0].name.split(".")[0] if isinstance(n, ast.Import) else (n.module or "").split(".")[0]
           for n in ast.walk(ast.parse(source("command_rules.py"))) if isinstance(n, (ast.Import, ast.ImportFrom))}
check(imports <= {"re"}, f"import 는 re 뿐이다 (있는 것: {sorted(imports)}) — CI 와 PyInstaller 가 그대로 쓴다")

print("\n== 규칙이 내는 이름은 판단기가 아는 이름이다 ==")
for pattern, act in R.RULES:
    action, _, dest = act.partition(":")
    check(action in P["ACTIONS"] and (dest or "default") in P["DESTINATIONS"],
          f"{act:<16} ← /{pattern}/")
check(set(R.COLORS.values()) <= set(P["RECIPIENTS"]), "색 이름이 판단기의 대상 이름이다")
for said in ("다들 따라와", "나머지는 기다려", "초록아 모여", "따라와"):
    plan = R.decide(said)
    check(plan is not None and plan["orders"][0]["recipients"] in P["RECIPIENTS"],
          f"'{said}' 의 대상 {plan and plan['orders'][0]['recipients']} 가 판단기의 대상 이름이다")

print("\n== 규칙은 말에 없던 감정 표현을 내지 않는다 (-736) ==")
for pattern, act in R.RULES:
    if act in P["SOCIAL_WORDS"]:
        words = set(pattern.split("|"))
        check(words <= set(P["SOCIAL_WORDS"][act]),
              f"{act} 규칙의 낱말 {sorted(words)} 이 모두 SOCIAL_WORDS 에 있다 — 낱말 없이는 안 걸린다")

print("\n== 행동 ==")
CASES = [
    # 멈춤·부정문이 먼저다 — 되돌려야 하는 행동은 못 알아듣는 것보다 나쁘다
    ("춤추지 마", "cancel"), ("못은 건드리지 마", "cancel"), ("그만해", "cancel"), ("하던 거 멈춰", "cancel"),
    # 특정한 것이 먼저다
    ("방망이 가져와", "bring_bat"), ("방망이 주워", "pickup_bat"), ("몽둥이 들고 운석 막아", "defend_meteors"),
    ("용접건 가져와", "bring_welder"), ("용접건 집어", "pickup_welder"), ("여기 용접해", "weld"),
    ("용접한 거 떼 내", "cut_weld"),
    ("견인 장치에서 내려", "leave_tractor"), ("견인 장치에 타", "board_tractor"), ("들고 있는 거 내려놔", "drop_tool"),
    ("못 다 뽑아줘", "pull_all_pins"), ("고정못 뽑아", "pull_pin"), ("다들 못 뽑아", "pull_pin"),
    ("나 좀 들어 올려 줘", "lift_player"), ("파츠 들고 있어", "lift_part"), ("파츠 잡아", "grab_part"),
    ("파츠 놔", "release_part"), ("파츠 밀어", "carry_part"), ("파츠 조립대로 옮겨", "transport_part"),
    ("쓰러진 애 살려", "revive"), ("나 따라와", "follow"), ("여기 모여", "gather"), ("거기서 기다려", "wait"),
    ("저기 가 있어", "goto:aim"), ("조립대 앞에 가 있어", "goto:assembly"), ("언덕 위로 올라가", "goto:high"),
    ("춤 춰", "dance"), ("인사해", "greet"), ("하트 해", "heart"), ("따봉", "cheer"),
]
for said, want in CASES:
    plan = R.decide(said)
    got = None
    if plan:
        step = plan["orders"][0]["steps"][0]
        got = step["action"] + (":" + step["destination"] if step["action"] == "goto" else "")
    check(got == want, f"'{said}' → {got} (정답 {want})")

print("\n== 규칙이 나눌 수 없는 말은 판단기로 넘긴다 ==")
for said in ("빨강이랑 파랑이 파츠 밀어", "빨강 파랑 둘 다 따라와", "못 뽑고 그다음 파츠 잡아",
             "움직이지 말고 춤만", "", "   ", "오늘 진짜 재밌다", "지금 몇 시야"):
    check(R.decide(said) is None, f"'{said}' 는 판단기로 넘긴다")

print("\n== 게임이 받는 꼴은 판단기의 것과 같다 ==")
plan = R.decide("못 하나만 빼 줘")
order = plan["orders"][0]
check(set(plan) == {"status", "orders"} and plan["status"] == "ok" and len(plan["orders"]) == 1,
      "status·orders 두 칸, 지시 하나")
check(set(order) == {"recipients", "steps"} and set(order["steps"][0]) == {"action", "approach", "destination"}
      and order["steps"][0]["approach"] is False, "지시·단계의 칸이 validate 가 받는 것과 같다")

print("\n== 워커 배선 ==")
worker = source("portable_worker.py")
tree = ast.parse(worker)


def method(name):
    for node in ast.walk(tree):
        if isinstance(node, ast.FunctionDef) and node.name == name:
            return ast.get_source_segment(worker, node)
    return ""


check("import command_rules" in worker, "워커가 규칙을 불러온다 — PyInstaller 도 이 import 로 팩에 넣는다")
plan_src = method("plan")
check("command_rules.decide(text)" in plan_src and "companion_plan.classify(text, self.url)" in plan_src,
      "plan() 이 규칙을 먼저 보고, 못 정하면 판단기다")
check(plan_src.find("command_rules.decide") < plan_src.find("companion_plan.classify"),
      "규칙이 판단기보다 먼저다")
check("self.plan(text) if plan" in method("execute"), "음성·글 명령이 plan() 을 거친다")
warm = method("warmup")
check("companion_plan.classify(" in warm and "classify_companion(" not in warm,
      "준비할 때 게임이 쓰는 판단 프롬프트로 데운다 — 다른 프롬프트로 데우면 첫 명령이 캐시를 못 쓴다")
check(re.search(r'"keep_alive":\s*-1', source("companion_plan.py")) is not None,
      "판단 모델을 내리지 않는다 (keep_alive -1) — 10분 쉰 뒤의 첫 명령이 11.8초였다")

print("\n== 라이브 시연: 명령은 넷뿐이고 판단기를 부르지 않는다 (S15P21B202-746) ==")
unity = io.open(os.path.join(ROOT, "METAGAME", "Assets", "_Project", "Scripts", "Runtime", "Network", "AiDemoCommands.cs"),
                encoding="utf-8").read()
m = re.search(r'Defend = "(\w+)", Revive = "(\w+)", Pin = "(\w+)", Come = "(\w+)"', unity)
check(m is not None and set(m.groups()) == set(R.DEMO_ACTIONS),
      f"워커의 네 행동이 게임(AiDemoCommands)과 같다 — 게임 {m and m.groups()} · 워커 {R.DEMO_ACTIONS}")
check({a for _, a in R.DEMO_RULES} == set(R.DEMO_ACTIONS) and set(R.DEMO_EXAMPLES) == set(R.DEMO_ACTIONS),
      "규칙과 본보기가 네 행동을 빠짐없이 덮는다")
DEMO_CASES = [
    # 2026-09-25 QA 로그에 실제로 찍힌 받아쓰기 — 틀어진 말도 넷 중 맞는 것으로 가야 한다
    ("가서 반망이 집어", "defend_meteors"), ("전망이 들고 운속점 쳐내", "defend_meteors"),
    ("고정못 때", "pull_pin"), ("아서 고정못 때", "pull_pin"), ("고정못.", "pull_pin"), ("가서 고정못 대.", "pull_pin"),
    ("불 다 고정못 때", "pull_pin"),
    # 알려 주는 말
    ("방망이 주워서 운석 쳐내", "defend_meteors"), ("운석 막아", "defend_meteors"), ("방망이 가져와", "defend_meteors"),
    ("기절한 사람 살려", "revive"), ("쓰러진 애 일으켜", "revive"), ("구해줘", "revive"), ("일흐켜", "revive"),
    ("고정못 뽑아", "pull_pin"), ("고정못 빼", "pull_pin"),
    ("이쪽으로 와", "come"), ("이리 와", "come"), ("여기로 와 줘", "come"), ("나 따라와", "come"), ("이쪽으로 아", "come"),
]
for said, want in DEMO_CASES:
    plan = R.demo_decide(said)
    got = plan["orders"][0]["steps"][0]["action"] if plan["orders"] else None
    check(got == want, f"시연 '{said}' → {got} (정답 {want})")
for said in ("", "그만해", "춤 춰", "오늘 날씨 좋다", "지금 몇 시야", "용접건 수고", "견인 장치 타"):
    check(R.demo_decide(said) == {"status": "none", "orders": []}, f"시연 '{said}' 는 아무것도 하지 않는다")
plan = R.demo_decide("빨강아 고정못 뽑아")
check(plan["orders"][0]["recipients"] == "red", "색을 부르면 그 동료에게 간다")
check(R.similarity("반망이", "방망이") > R.similarity("반망이", "고정못"), "자모 유사도가 받아쓰기 오류를 가깝게 본다")
demo_src = method("plan_demo")
check(0 <= plan_src.find("command_rules.DEMO") < plan_src.find("companion_plan.classify"),
      "시연이면 시연 판단(plan_demo)으로 간다")
check("companion_plan.classify_demo(text, self.url)" in demo_src,
      "시연에서도 **LLM 이 판단한다** — AI 활용이 평가 요소다. 규칙만으로 정하면 받아쓰기만 남는다")
check(demo_src.find("classify_demo") < demo_src.find('"rules-fallback"'),
      "규칙은 LLM 뒤에서 거드는 쪽이다(none·실패일 때만)")
check("command_rules.DEMO_STOP.search" in demo_src, "멈추라는 말은 판단 없이 거절한다")
check("companion_plan.classify_demo(" in warm, "시연이면 시연 프롬프트로 데운다 — 첫 명령이 캐시를 쓴다")
D = constants("companion_plan.py", {"DEMO_CHOICES"})
check(set(D["DEMO_CHOICES"]) == set(R.DEMO_ACTIONS) | {"none"}, "LLM 의 선택지가 워커 규칙·게임의 넷과 같다")

print(f"\ntest_ai_command_rules: {'통과' if not failed else f'실패 {failed}'}")
sys.exit(1 if failed else 0)
