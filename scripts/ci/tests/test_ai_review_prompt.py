"""AI 리뷰 입력과 출력 계약이 지금 MR 템플릿·asmdef 와 맞는가 (S15P21B202-740).

실행: python scripts/ci/tests/test_ai_review_prompt.py
네트워크·모델 없이 돈다.

지키려는 것
  · **리뷰어가 없는 칸을 근거로 삼지 않는다.** 템플릿이 4절이 된 뒤(-419)에도 지시가
    `씬 / 프리팹 변경`·`확인 방법` 을 가리켰고, 리뷰어는 우리가 diff 에서 계산한
    `씬/프리팹 변경: 없음` 줄을 "MR 본문의 선언" 으로 읽었다 — 60개 코멘트 중 56개,
    판정은 60개 전부 ✅ 였다. 지시가 가리키는 절은 **템플릿에 실제로 있어야** 한다.
  · **잘린 입력은 잘렸다고 말한다.** 모르고 잘린 diff 는 "충족 근거 없음" 으로 읽힌다.
  · **역할의 항목 키는 스키마가 요구한다.** 말로 부탁하면 빠지고 판정 표에 ❓ 가 뜬다.
  · **어셈블리 방향이 실제 asmdef 와 같다.**
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.dirname(HERE))
sys.stdout.reconfigure(encoding="utf-8")

import ai_review as A

failed = 0


def check(ok, msg):
    global failed
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if not ok:
        failed += 1


print("== 지시가 가리키는 절이 템플릿에 있다 ==")
template = open(os.path.join(ROOT, ".gitlab", "merge_request_templates", "Default.md"), encoding="utf-8").read()
sections = set(re.findall(r"^## (.+?)\s*$", template, re.M))
prompts = "\n".join(r["system"] for r in A.ROLES.values()) + A.COMMON_RULES + A.SUMMARY_SYSTEM
quoted = set(re.findall(r"MR 본문 '([^']+)'", prompts))
check(quoted, f"지시가 MR 본문의 절을 이름으로 가리킨다 ({', '.join(sorted(quoted))})")
for name in sorted(quoted):
    check(name in sections, f"'{name}' 절이 MR 템플릿에 있다 (템플릿 절: {', '.join(sorted(sections))})")
for gone in ("확인 방법", "씬 / 프리팹 변경"):
    check(gone not in prompts, f"없어진 '{gone}' 을 찾으라고 하지 않는다")

print("\n== 계산한 줄을 본문 선언으로 읽지 않게 한다 ==")
info = {"diff": "", "code": [], "assets": [], "skipped": [], "scene_like": []}
head = A.user_payload({"description": "본문"}, "제목", "(Jira)", info, "scope")
scene_line = next((l for l in head.splitlines() if ".unity/.prefab" in l), "")
check("diff 에서 계산" in scene_line and "선언이 아니다" in scene_line,
      f"씬/프리팹 요약 줄이 출처를 밝힌다: {scene_line!r}")

print("\n== 잘린 입력을 알린다 ==")
big = {"diff": "x" * 60000, "code": ["a.cs"], "assets": ["METAGAME/Assets/a.cs"],
       "skipped": [("Big.cs", "예산 초과 (diff 총량 90000자 상한)"), ("a.png", "바이너리·메타")], "scene_like": []}
scope = A.user_payload({"description": "d" * 7000}, "제목", "(Jira)", big, "scope")
check("원문 7000자 중 앞 6000자" in scope, "MR 본문이 잘리면 알린다")
check("원문 60000자 중 앞 50000자" in scope, "scope diff 가 잘리면 알린다")
check(scope.find("원문 60000자") > scope.find("--- END UNTRUSTED GIT DIFF ---") >= 0,
      "안내는 신뢰할 수 없는 블록 밖에 둔다 — 우리가 쓴 말이다")
check("Big.cs" in scope and "a.png" not in scope.split("# 변경 요약")[1].split("\n")[2],
      "예산 초과로 빠진 파일은 이름을 주고, 바이너리는 늘어놓지 않는다")
assets = A.user_payload({"description": "d"}, "제목", "(Jira)", big, "assets")
check("원문 60000자 중 앞 30000자" in assets, "assets diff 가 잘리면 알린다")
code = A.user_payload({"description": "d"}, "제목", "(Jira)", big, "code")
check("[!] diff" not in code, "code 는 자르지 않으므로 안내도 없다")
small = A.user_payload({"description": "d"}, "제목", "(Jira)", dict(big, diff="x" * 10), "scope")
check("[!]" not in small, "안 잘렸으면 안내가 없다 — 항상 붙이면 신호가 죽는다")

print("\n== 스키마가 항목 키를 요구한다 ==")
for role, spec in A.ROLES.items():
    s = A.review_schema(spec["checks"])
    json.dumps(s)
    check(s["properties"]["checks"]["required"] == spec["checks"],
          f"{role}: checks 에 {spec['checks']} 가 모두 required")
    check(s["properties"]["verdict"]["enum"] == ["ok", "warn", "block"], f"{role}: verdict 가 셋 중 하나")
    check(s["properties"]["checks"]["additionalProperties"] is False,
          f"{role}: 다른 역할의 항목 키를 섞지 않는다")
check(set(A.SUMMARY_SCHEMA["required"]) == {"reason", "todo"}, "요약 스키마가 reason·todo 를 요구한다")
check("JSON 하나만" not in prompts and "도구를 쓰지 말고" not in prompts,
      "형식을 말로 부탁하지 않는다 — 구조화 출력이 도구를 쓴다")

print("\n== 어셈블리 방향이 실제 asmdef 와 같다 ==")
code_prompt = A.ROLES["code"]["system"]
m = re.search(r"의존 방향\(([^—)]+)", code_prompt)
chain = [x.strip() for x in (m.group(1) if m else "").split("←")]
check(len(chain) >= 3, f"의존 방향을 체인으로 적는다: {chain}")
runtime = os.path.join(ROOT, "METAGAME", "Assets", "_Project", "Scripts", "Runtime")
for i, name in enumerate(chain):
    path = os.path.join(runtime, name, f"Metagame.{name}.asmdef")
    if not os.path.exists(path):
        check(False, f"{name}: {path} 가 없다")
        continue
    refs = {r for r in json.load(open(path, encoding="utf-8-sig")).get("references", []) if r.startswith("Metagame.")}
    allowed = {f"Metagame.{n}" for n in chain[:i]}
    check(refs <= allowed, f"{name} 은 왼쪽만 가리킨다 (references {sorted(refs) or '없음'})")
listed = {f"Metagame.{n}" for n in chain}
actual = {f[:-7] for _, _, fs in os.walk(runtime) for f in fs if f.endswith(".asmdef")}
check(actual <= listed, f"런타임 어셈블리가 모두 체인에 있다 (빠진 것: {sorted(actual - listed) or '없음'})")

print(f"\ntest_ai_review_prompt: {'통과' if not failed else f'실패 {failed}'}")
sys.exit(1 if failed else 0)
