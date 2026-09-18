"""이벤트 정의가 코드와 어긋나면 잡는다 (S15P21B202-552).

실행: python scripts/ci/tests/test_event_schema.py
네트워크·Unity 없이 저장소의 실제 소스를 읽는다.

**왜 이 검사가 있나.** 수집할 이벤트는 게임 코드의 값에 얹혀 있다. `floors: [2,1,0,0]`
에서 2가 `Welded` 라는 것은 `FloorState` 의 **순서**가 정한다. 거기에 값이 하나
끼어들면 어제까지 쌓인 데이터의 뜻이 **조용히** 달라진다 — 숫자는 그대로 들어오고
질의도 그대로 돌아가며, 틀린 답만 나온다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 `assert` 는 통째로
사라져 이 파일이 아무것도 안 하면서 성공으로 끝난다. 검사를 무력화하는 가장 조용한
길이라 막아 둔다.
"""
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "analytics"))
sys.stdout.reconfigure(encoding="utf-8")

import schema_source as S  # noqa: E402

CONTRACT = os.path.join(ROOT, "scripts", "analytics", "event-schema.json")


NL2 = chr(10)


def fail(msg):
    raise AssertionError(msg)


def check(ok, msg):
    if not ok:
        fail(msg)


saved = json.load(io.open(CONTRACT, encoding="utf-8"))
now = S.contract()

HOWTO = ("고치는 법 — 아래를 그대로 저장하면 된다:\n\n"
         "    python scripts/analytics/schema_source.py > scripts/analytics/event-schema.json\n")

# ── 1) 계약의 모든 값이 같은가 ──
# 지문만 보면 안 된다: 지문은 **데이터의 뜻**만 묶으므로 한글 문구가 바뀌어도 그대로다.
# 문구가 바뀐 것은 데이터를 가르지는 않지만 사람이 확인해야 한다 — 순서가 뒤바뀐
# 것이라면 집계의 뜻이 통째로 밀린다.
diff = []
for key in ("floor_state", "parts", "blocker_codes", "blocker_messages"):
    a, b = saved.get(key, []), now[key]
    if a == b:
        continue
    diff.append(f"  [{key}]")
    for x in a:
        if x not in b:
            diff.append(f"    - 없어짐  {x}")
    for x in b:
        if x not in a:
            diff.append(f"    + 새로    {x}")
    if sorted(a) == sorted(b) and a != b:
        diff.append("    ! 순서가 바뀌었다 — 숫자의 뜻과 코드 매핑이 달라진다")
    diff.append("")

if diff:
    same = saved.get("fingerprint") == now["fingerprint"]
    split = ("지문은 그대로다 — **한글 문구만** 바뀌었으므로 데이터는 안 갈린다. 확인만 하면 된다."
             if same else
             "**옛 데이터의 뜻이 달라진다.** 버리라는 말이 아니라 지문으로 나눠 보라는 말이다.")
    fail(chr(10).join(["이벤트가 기대는 게임 코드의 정의가 바뀌었다.", ""] + diff
                      + [split, "", HOWTO, "새 계약:", json.dumps(now, ensure_ascii=False, indent=2)]))
check(saved.get("fingerprint") == now["fingerprint"],
      f"값은 같은데 지문이 다르다 — 계약이 손으로 고쳐졌다.\n\n{HOWTO}")
print(f"  지문 {now['fingerprint']} — 코드와 계약이 같다 ✅")

# ── 2) 지문이 뜻만 묶는가 ──
# 한글 문구가 지문에 섞이면 오타 하나로 데이터가 갈린다.
check("blocker_messages" not in S.MEANING,
      "지문에 한글 문구가 들어 있다 — 오타를 고치기만 해도 옛 데이터와 비교할 수 없게 된다")
check(set(S.MEANING) == {"floor_state", "parts", "blocker_codes"}, f"MEANING 이 바뀌었다: {S.MEANING}")
print("  지문은 뜻만 묶는다 (문구는 뺀다) ✅")

# ── 3) 뽑기가 조용히 실패하지 않았는가 ──
#
# **계약과 견주면 안 된다** (`!461` AI 리뷰). 1) 이 이미 값 전체를 견주므로 여기까지
# 왔다면 개수는 무조건 같다 — 늘 참인 죽은 검사였다. 그리고 잡으려던 경우(사람이
# 시키는 대로 계약만 다시 만듦)에는 계약 쪽도 이미 갱신돼 있어 애초에 못 잡는다.
#
# 그래서 **여기에 못 박는다.** 값을 바꾸려면 이 줄을 손으로 고쳐야 하고, 그 순간
# 사람이 "정말 층이 늘었나" 를 한 번 본다.
EXPECTED = {
    "floor_state": 5,      # Empty · Stacked · Welded · WrongPart · Tilted
    "parts": 4,            # Part_1 ~ Part_4 (조립 층 수 = floors 배열 길이)
    "blocker_messages": 5,  # Evaluate() 의 조기 탈출 다섯 자리
}
for key, want in EXPECTED.items():
    check(len(now[key]) == want,
          f"{key} 가 {want}개가 아니라 {len(now[key])}개다." + NL2 +
          "  뽑기가 깨졌는지 **먼저** 확인한다 — 정규식이 표기 변화로 절반만 잡으면" + NL2 +
          "  여기 걸린다. 정말로 늘거나 줄었으면 이 파일의 EXPECTED 를 고친다." + NL2 +
          f"  뽑은 것: {now[key]}")
print(f"  층 {len(now['floor_state'])}종 · 파츠 {len(now['parts'])}개 · "
      f"막힘 {len(now['blocker_messages'])}가지 ✅")
# ── 4) 막힘을 **이름으로** 짝짓는가 ──
#
# 전에는 이름 목록을 손으로 적어 두고 `Evaluate()` 의 **순서**에 맞췄다. 그러면
# 막는 자리의 순서만 바뀌어도 집계의 뜻이 통째로 밀리는데(`tilted` 로 센 것이
# `off_pad` 가 된다) 지문은 그대로라 옛 데이터와 섞였다 (`!461` AI 리뷰).
#
# 이제 각 대입 옆의 `// code: …` 가 짝을 정한다 — 순서를 바꾸면 `blocker_codes` 의
# 순서가 바뀌고, 그것이 지문에 들어 있으므로 **데이터가 갈린다.**
check(len(now["blocker_codes"]) == len(now["blocker_messages"]),
      "이름과 문구의 개수가 다르다 — 짝짓기가 깨졌다")
check(len(set(now["blocker_codes"])) == len(now["blocker_codes"]),
      f"막는 사유의 이름이 겹친다: {now['blocker_codes']}")
check("blocker_codes" in S.MEANING,
      "이름이 지문에 없다 — 순서가 바뀌어도 데이터가 안 갈린다")
check(not hasattr(S, "BLOCKER_CODES"),
      "손으로 적는 이름 목록이 아직 있다 — 위치로 짝짓던 방식이 남아 있다")
print("  막힘을 이름으로 짝짓는다 (순서가 바뀌면 지문도 바뀐다) ✅")

# ── 5) 파서를 **직접 불러** 확인한다 ──
#
# 전에는 이 검사가 split 로직을 안에 베껴 두고 흉내만 냈다 — 진짜 파서가 줄 단위
# 방식으로 되돌아가도 계속 통과했다(`!461` AI 리뷰). 검사가 있는데 아무것도 안
# 지키는 상태였다. 이제 `S.enum_members` 를 그대로 부른다.

# 맨 끝에 쉼표가 없는 값 — 실제로 뚫려 있던 자리다.
check(S.enum_members("A = 0, B, C") == ["A", "B", "C"],
      f"쉼표 없는 마지막 값을 놓친다: {S.enum_members('A = 0, B, C')}")

# 주석이 섞여도 이름만 남아야 한다.
sample = chr(10).join([
    "    A = 0,",
    "    /// <summary>주석</summary>",
    "    B,          // 꼬리 주석",
    "    C",
])
check(S.enum_members(S.strip_comments(sample)) == ["A", "B", "C"],
      f"주석이 섞이면 놓친다: {S.enum_members(S.strip_comments(sample))}")
print("  파서를 직접 불러 확인했다 — 쉼표 없는 끝값 · 주석 ✅")

# ── 5b) 문자열 안의 `//` 를 주석으로 보지 않는가 ──
# `"https://…"` 한 줄만 들어와도 계약이 조용히 틀리게 만들어진다.
kept = S.strip_comments('x = "a//b";  // 진짜 주석')
check("a//b" in kept and "진짜 주석" not in kept, f"문자열 안의 // 를 잘랐다: {kept}")

# 주석 안의 중괄호가 깊이를 흔들면 엉뚱한 자리에서 잘린다.
sample2 = chr(10).join(['void F() {  // }"{"', '  int a;', '}'])
body = S.braced_body(sample2, 'void F()')
check("int a;" in body, f"주석 안의 중괄호에 흔들린다: {body!r}")
print("  문자열·주석 안의 기호에 안 흔들린다 ✅")
# ── 5c) `All` 배열 밖의 선언에 안 속는가 ──
# 파일 전체를 훑으면 시험용·안 쓰는 선언 하나에 목록과 순서가 바뀐다. 그 값이
# `floors` 인덱스의 뜻을 정하므로 조용히 틀리면 데이터 전체가 틀린다.
sample3 = chr(10).join([
    'static readonly PartSpec[] Unused = { new("Part_9", "x", "y", null) };',
    'public static readonly PartSpec[] All =',
    '{',
    '    new("Part_1", "a", "b", null),',
    '    new("Part_2", "c", "d", null),',
    '};',
])
got3 = [m.group(1) for m in __import__("re").finditer(
    r'new\("(Part_\d+)",', S.braced_body(sample3, "PartSpec[] All ="))]
check(got3 == ["Part_1", "Part_2"], f"All 밖의 선언에 속는다: {got3}")
print("  All 배열 안에서만 찾는다 ✅")

# ── 5d) `Evaluate()` **밖의** 대입에 안 속는가 ──
#
# `blockers()` 는 `Evaluate()` 범위로 자른다고 적혀 있었는데 실제로는 파일 전체를
# 훑고 있었다 (`!461` AI 리뷰). 주석과 코드가 어긋나면 사람은 주석을 믿으므로,
# 다른 메서드에 `Blocker = "…"` 가 하나만 생겨도 아무도 모르게 집계가 밀린다.
#
# **진짜 함수를 부른다.** 여기서 로직을 베끼면 파서가 되돌아가도 검사가 통과한다.
def _blockers_with(text):
    real = S.read
    S.read = lambda rel: text if rel == S.ASSEMBLY else real(rel)
    try:
        return S.blockers()
    finally:
        S.read = real


asm = S.read(S.ASSEMBLY)
spot = asm.index("public void Evaluate()")
intruder = ("    void Preview() { Blocker = " + chr(34) + "미리보기" + chr(34)
            + "; }  // code: preview" + NL2 + NL2)
codes = [k for k, _ in _blockers_with(asm[:spot] + intruder + asm[spot:])]
check("preview" not in codes,
      "Evaluate() 밖의 Blocker 대입이 집계에 섞인다: " + str(codes) + NL2 +
      "  blockers() 가 braced_body 로 자른 구간이 아니라 파일 전체를 보고 있다.")
check([k for k, _ in _blockers_with(asm)] == now["blocker_codes"],
      "검사가 진짜 blockers() 를 부르고 있지 않다")
print("  Evaluate() 안에서만 막는 사유를 읽는다 ✅")

# ── 6) 계약이 사람이 읽을 수 있는가 ──
check(bool(saved.get("note")), "계약에 note 가 없다 — 이 파일이 무엇인지 적혀 있어야 한다")
print("  계약에 설명이 있다 ✅")

print("OK  이벤트 스키마 — 코드와 계약이 어긋나지 않았다")
