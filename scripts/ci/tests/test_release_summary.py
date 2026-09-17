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
print("== Jira 를 부르는 자리 (S15P21B202-579) ==")


class StubJira:
    """`ci_common.Jira` 대역. **`available` 은 속성이다** — 진짜와 같은 모양이어야

    이 검사가 뜻을 갖는다. 예전 코드는 `jira.available()` 이라 여기서 바로 죽었는데,
    그 자리를 아무도 부르지 않아 26개가 통과하는 채로 릴리스까지 갔다."""

    def __init__(self, ok=True, table=None):
        self._ok, self._table = ok, table or {}

    @property
    def available(self):
        return self._ok

    def search_all(self, jql, fields, page_size=50, max_pages=3):
        import re as _re
        hit = [k for k in _re.findall(r"S15P21B202-\d+", jql) if k in self._table]
        return [{"key": k, "fields": {"summary": self._table[k]}} for k in hit], None


def with_stub(jira, fn):
    real = S.C.Jira
    S.C.Jira = lambda: jira
    try:
        return fn()
    finally:
        S.C.Jira = real


# ① 이것이 이번 결함 전부다. `()` 가 다시 붙으면 여기서 TypeError 가 난다.
try:
    got = with_stub(StubJira(ok=False), lambda: S.summaries(["S15P21B202-1"]))
    check(got == ([], ["S15P21B202-1"]),
          "Jira 를 못 쓰면 '읽은 것 없음 · 전부 못 읽음' 으로 돌려준다")
except TypeError as e:
    check(False, f"available 을 함수로 불렀다 — {e}")

got = with_stub(StubJira(table={"S15P21B202-1": "[Puzzle] 운석 흔적이 오래 남는다"}),
                lambda: S.summaries(["S15P21B202-1", "S15P21B202-2"]))
check(got == (["[Puzzle] 운석 흔적이 오래 남는다"], ["S15P21B202-2"]),
      "읽은 것과 못 읽은 것을 나눠 돌려준다")

# **저장소 어디에도 `.available()` 이 없어야 한다.** 다섯 곳은 속성으로 읽고 한 곳만
# 함수로 불렀다 — 그 한 줄을 사람이 다시 쓰지 못하게 막는다.
import glob  # noqa: E402
import re as _re  # noqa: E402

def code_only(line: str) -> str:
    """주석과 백틱 안을 걷어낸 나머지. **이 파일 자신이 첫 오탐이었다** — 설명 문구에

    적은 `jira.available()` 세 곳이 걸렸다. 파이썬 코드에는 백틱이 없으므로 백틱 안은
    전부 글이다. 완벽한 파서는 아니지만 이 검사가 잡으려는 것은 실제 호출 한 줄이다."""
    return _re.sub(r"`[^`]*`", "", line.split("#", 1)[0])


bad = []
for path in sorted(glob.glob(os.path.join(ROOT, "scripts", "ci", "**", "*.py"), recursive=True)):
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            if _re.search(r"\.available\s*\(", code_only(line)) and "def available" not in line:
                bad.append(f"{os.path.relpath(path, ROOT)}:{n}")
check(not bad, "`.available()` 로 부르는 곳이 없다 (속성이다)" + (f" — {', '.join(bad)}" if bad else ""))

print()
print("== 요약 잡이 claude 가 있는 이미지에서 돈다 (S15P21B202-579) ==")

with open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8") as f:
    CI = f.read().split("\n")

# 잡 블록: 0열에서 시작하는 `이름:` 줄부터 다음 0열 줄 전까지.
HEAD = _re.compile(r"^([A-Za-z_][A-Za-z0-9_.-]*):")
_starts = [(i, HEAD.match(l).group(1)) for i, l in enumerate(CI) if HEAD.match(l)]
BLOCKS = {}
for (i, name), nxt in zip(_starts, [s[0] for s in _starts[1:]] + [len(CI)]):
    BLOCKS[name] = CI[i:nxt]


def runs(block, script):
    return any(script in l and l.lstrip().startswith("-") for l in block)


owners = [n for n, b in BLOCKS.items() if runs(b, "release_summary.py")]
check(owners == ["release-summary"],
      f"release_summary.py 를 부르는 잡은 release-summary 하나다 (찾은 것: {owners})")

if owners == ["release-summary"]:
    block = BLOCKS["release-summary"]
    # **이미지를 덮어쓰면 안 된다.** claude CLI 는 기본 이미지에만 있다. `release-windows`
    # (Unity 이미지) 안에 있던 동안 `find_claude()` 가 None 이라 초안이 한 번도 안 나왔다.
    over = [l for l in block if _re.match(r"^  image:", l)]
    check(not over, "요약 잡이 image 를 덮어쓰지 않는다 — 기본 이미지에만 claude 가 있다")
    check(not any(_re.match(r"^  tags:", l) for l in block),
          "요약 잡이 tags 를 덮어쓰지 않는다 — ai-review 와 같은 러너여야 한다")
    # 같은 근거로 `ai-review` 도 기본 이미지다. 둘이 갈리면 한쪽만 조용히 안 돈다.
    check(not any(_re.match(r"^  image:", l) for l in BLOCKS.get("ai-review", [])),
          "ai-review 도 기본 이미지다 — 두 잡의 전제가 같다")
    check(any("allow_failure: true" in l for l in block), "릴리스를 막지 않는다(allow_failure)")
    # `|| true` 는 실패를 **성공으로** 만든다 — 그래서 아무도 못 봤다. allow_failure 로 바꿨다.
    check(not any("release_summary.py" in l and "|| true" in l for l in block),
          "`|| true` 로 실패를 감추지 않는다")
    check(any("release-summary.md" in l for l in block), "초안을 아티팩트로 남긴다")
    check(any("release-windows" in l for l in block), "release-windows 뒤에 돈다(버전을 물려받는다)")

print()
print("== 초안 만들기 끝까지 (S15P21B202-579) ==")

import tempfile  # noqa: E402

CALLS = []


def run_main(argv, backend="cli", answer=GOOD, boom=None, jira=None):
    """`main()` 을 Jira·모델 없이 끝까지 돌린다. 돌려주는 것: (종료코드, 파일 내용)."""
    real = (S.R.previous_tag, S.R.changelog, S.AI.pick_backend, S.AI.call, S.C.Jira)

    def fake_call(be, model, system, user):
        CALLS.append((be, model, user))
        if boom:
            raise RuntimeError(boom)
        return answer

    S.R.previous_tag = lambda v: "v1.1.0"
    S.R.changelog = lambda prev, head="HEAD": ([{"key": "S15P21B202-1"}], 0)
    S.AI.pick_backend = lambda: backend
    S.AI.call = fake_call
    S.C.Jira = lambda: (jira or StubJira(table={"S15P21B202-1": "[Puzzle] 운석 흔적이 오래 남는다"}))
    try:
        rc = S.main(argv)
    finally:
        (S.R.previous_tag, S.R.changelog, S.AI.pick_backend, S.AI.call, S.C.Jira) = real
    try:
        with open(argv[-1], encoding="utf-8") as f:
            return rc, f.read()
    except OSError:
        return rc, None


tmp = os.path.join(tempfile.mkdtemp(), "release-summary.md")

rc, text = run_main(["v1.2.0", "--out", tmp])
check(rc == 0 and text is not None, f"평범한 경우 초안 파일이 나온다 (종료 {rc})")
check(text is not None and "운석을 방망이로 쳐낼 수 있습니다." in text, "모델이 쓴 줄이 파일에 들어간다")
check(CALLS and CALLS[-1][0] == "cli", "고른 백엔드로 부른다 — call_cli 를 직접 부르지 않는다")
check(CALLS and "[Puzzle] 운석 흔적이 오래 남는다" in CALLS[-1][2], "Jira 요약이 프롬프트에 실린다")

# 백엔드가 없으면 **그렇다고 말하고 끝낸다.** 빈 파일을 남기면 "요약이 없구나" 로 읽힌다.
os.remove(tmp)
rc, text = run_main(["v1.2.0", "--out", tmp], backend="")
check(rc == 1 and text is None, "AI 백엔드가 없으면 파일을 만들지 않고 실패한다")

# 모델 호출이 터져도 스택이 아니라 종료 코드로 끝난다 — 잡이 죽는 모양이 달라지면 안 된다.
rc, text = run_main(["v1.2.0", "--out", tmp], boom="claude -p 종료 코드 1 category=authentication")
check(rc == 1 and text is None, "모델 호출이 실패하면 파일을 만들지 않고 실패한다")

rc, text = run_main(["v1.2.0", "--out", tmp], answer="그냥 말")
check(rc == 1 and text is None, "모델 출력을 못 읽으면 파일을 만들지 않고 실패한다")

# 재료가 하나도 없으면 모델을 부르지도 않는다
before = len(CALLS)
rc, text = run_main(["v1.2.0", "--out", tmp], jira=StubJira(ok=False))
check(rc == 1 and len(CALLS) == before, "Jira 를 못 읽으면 모델을 부르지 않고 실패한다")

print()
print(f"== 릴리스 요약: 통과 {passes} · 실패 {len(fails)} ==")
if fails:
    print("   ❌ " + " / ".join(fails))
raise SystemExit(1 if fails else 0)
