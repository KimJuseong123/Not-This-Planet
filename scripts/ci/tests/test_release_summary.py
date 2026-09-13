"""릴리스 사용자용 요약 초안 (S15P21B202-462).

실행: python scripts/ci/tests/test_release_summary.py
네트워크·Jira·Claude CLI 없이 돈다 — 모델 출력을 글자로 넣어 검사한다.

**무엇을 막는 검증인가.** 이 초안은 **사용자에게 나갈 문장**의 출발점이다. 두 가지가
조용히 틀릴 수 있다.

① **이슈 번호가 사이트 문구에 남는다.** 플레이어에게 `S15P21B202-440` 은 뜻이 없는데,
   모델이 목록을 그대로 베끼면 그대로 실린다. 사람이 못 보고 올리면 그게 첫인상이 된다.

② **재료가 반쪽인데 완성처럼 보인다.** Jira 를 못 읽으면 요약이 일부만 반영되는데,
   초안 파일은 멀쩡해 보인다. 빠진 것은 **영영 안 보인다.**
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "ci"))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

import release_summary as S  # noqa: E402

fails = []
passes = 0


def check(ok, msg):
    global passes
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if ok:
        passes += 1
    else:
        fails.append(msg)


print()
print("== 모델 출력 읽기 ==")

GOOD = ('{"site": ["운석을 방망이로 쳐낼 수 있습니다.", "죽은 동료를 일으킬 수 있습니다."],'
        ' "release": "두 문장.", "jira": "한 단락."}')

d = S.parse(GOOD)
check(d is not None and len(d["site"]) == 2, "평범한 JSON 을 읽는다")

# 코드펜스로 감싸 주는 일이 흔하다. 한 번은 벗겨 본다.
d = S.parse("```json\n" + GOOD + "\n```")
check(d is not None and len(d["site"]) == 2, "코드펜스를 벗긴다")

# `site` 를 문자열로 주는 경우도 받아 준다 — 줄로 쪼갠다
d = S.parse('{"site": "- 첫 줄\\n- 둘째 줄", "release": "x", "jira": "y"}')
check(d is not None and d["site"] == ["첫 줄", "둘째 줄"], "site 가 문자열이면 줄로 쪼갠다")

# **못 읽으면 None 이다.** 억지로 맞추면 무엇이 들어갔는지 아무도 모르게 된다.
for bad in ("", "그냥 말", "{", "[]", "null", '{"site": '):
    check(S.parse(bad) is None, f"읽을 수 없는 출력을 거부한다: {bad!r}")

print()
print("== 사이트 문구에 번호가 남으면 잡는다 ==")

# ① 이것이 이 파일의 존재 이유다.
leaked = S.leaks(["운석을 쳐낼 수 있습니다.",
                  "S15P21B202-440 을 고쳤습니다.",
                  "MR !350 반영",
                  "S15P21B202 440 처리"])
check(len(leaked) == 3, f"이슈·MR 번호가 든 줄을 3개 잡는다 (잡은 것 {len(leaked)})")
check(S.leaks(["운석을 방망이로 쳐낼 수 있습니다."]) == [], "멀쩡한 줄은 잡지 않는다")
# 숫자가 들어 있다고 다 잡으면 안 된다 — "4인 협동" 같은 문장이 걸린다
check(S.leaks(["4인까지 함께 할 수 있습니다.", "운석 3개를 한 번에 막습니다."]) == [],
      "평범한 숫자는 잡지 않는다")

print()
print("== 초안이 빠진 것을 숨기지 않는다 ==")

d = S.parse(GOOD)
text = S.render("v1.0.1", "v1.0.0", d, count=12,
                missing=["S15P21B202-1", "S15P21B202-2"], leaked=[])
check("초안" in text.split("\n")[0], "제목에 초안이라고 적는다")
check("그대로 올리지 마세요" in text, "그대로 올리지 말라고 적는다")
# ② 재료가 반쪽이면 **파일 안에서** 보여야 한다. 잡 로그에만 남기면 아무도 안 본다.
check("읽지 못한 이슈 2건" in text, "빠진 이슈를 초안 본문에 적는다")

text = S.render("v1.0.1", "v1.0.0", d, count=12, missing=[],
                leaked=["S15P21B202-440 을 고쳤습니다."])
check("이슈·MR 번호가 남아 있습니다" in text, "번호가 남았다고 초안 본문에 경고한다")
check("S15P21B202-440" in text, "어느 줄인지 보여 준다")

text = S.render("v1.0.1", None, d, count=3, missing=[], leaked=[])
check("이슈·MR 번호가 남아 있습니다" not in text, "깨끗하면 경고를 달지 않는다")
check("사이트" in text and "GitLab Release" in text and "Jira 릴리스" in text,
      "세 곳의 문구를 나눠 적는다")

print()
print("== 재료 모으기 ==")

entries = [{"key": "S15P21B202-1"}, {"key": None}, {"key": "S15P21B202-1"}, {"key": "S15P21B202-2"}]
check(S.keys_of(entries) == ["S15P21B202-1", "S15P21B202-2"],
      "이슈 키를 중복 없이·순서대로 모은다")
check(S.summaries([]) == ([], []), "키가 없으면 Jira 를 부르지 않는다")

prompt = S.build_prompt("v1.0.1", "v1.0.0", ["[Puzzle] 운석 흔적이 오래 남는다"], [])
check("[Puzzle] 운석 흔적이 오래 남는다" in prompt, "프롬프트에 이슈 요약이 들어간다")
check("S15P21B202-1" not in prompt, "프롬프트에 브랜치·키를 넣지 않는다 — 요약만 준다")

prompt = S.build_prompt("v1.0.1", None, ["x"], ["S15P21B202-9"])
check("읽지 못한" in prompt, "못 읽은 것이 있으면 모델에게도 알린다")

# 프롬프트가 **무엇을 빼라고** 지시하는지 — 이슈가 "번역이 아니라 선별" 이라고 적어 뒀다
check("선별" in S.SYSTEM and "빼세요" in S.SYSTEM, "내부 작업을 빼라고 지시한다")
check("이슈 번호" in S.SYSTEM, "사이트 문구에 번호를 쓰지 말라고 지시한다")

print()
print(f"== 릴리스 요약: 통과 {passes} · 실패 {len(fails)} ==")
if fails:
    print("   ❌ " + " / ".join(fails))
raise SystemExit(1 if fails else 0)
