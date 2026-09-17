"""파트 목록이 여러 곳에서 같은가 (S15P21B202-586).

실행: python scripts/ci/tests/test_parts_agree.py

**무엇을 막는 검사인가.** 파트 태그는 **네 곳**에 적혀 있다(`docs/parts.md` §태그를
늘리거나 이름을 바꿀 때).

    ① docs/parts.md 의 표          — 사람이 읽는 기준
    ② scripts/ci/ci_common.py PARTS — MR 제목·레이블 검사
    ③ scripts/ci/team.json          — 파트 → 팀원 멘션
    ④ Jira 자동화 R2 의 정규식      — 이슈 제목 검사

**한 곳만 고치면 다른 데서 막힌다.** 2026-09-17 에 `[AI]` 에픽(`-585`)을 만들자
R2 가 곧바로 `needs-fix` 를 붙였다 — 네 곳 다 비어 있었고, 그 전까지 아무도 이
목록들이 따로 논다는 것을 몰랐다.

**④ 는 여기서 못 본다.** Jira 자동화는 저장소 밖이고 오프라인 검사에는 토큰이 없다.
그래서 이 파일은 ①②③ 만 대조하고, ④ 는 **사람이 맞춘다**고 아래에서 소리 내어 말한다 —
검사가 덮는 범위를 넘겨짚는 것이 이 자리에서 가장 조용한 실패다.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

import ci_common as C  # noqa: E402

DOC = os.path.join(ROOT, "docs", "parts.md")
TEAM = os.path.join(ROOT, "scripts", "ci", "team.json")

fails = []
passes = 0


def check(ok, msg):
    global passes
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if ok:
        passes += 1
    else:
        fails.append(msg)


with open(DOC, encoding="utf-8") as f:
    doc = f.read()

# 표의 첫 칸: `| `[Plan]` | …`
ROW = re.compile(r"^\|\s*`\[([A-Za-z]+)\]`\s*\|", re.M)
doc_parts = ROW.findall(doc)

with open(TEAM, encoding="utf-8") as f:
    team = json.load(f)
team_parts = sorted({p for m in team.get("members", []) for p in m.get("parts", [])})

print()
print("== 세 곳이 같은가 ==")

check(len(doc_parts) == len(set(doc_parts)),
      f"문서 표에 중복이 없다 ({len(doc_parts)}줄)")

only_doc = sorted(set(doc_parts) - set(C.PARTS))
only_code = sorted(set(C.PARTS) - set(doc_parts))
check(not only_doc and not only_code,
      "docs/parts.md 의 표와 ci_common.PARTS 가 같다"
      + (f" — 문서에만 {only_doc}" if only_doc else "")
      + (f" · 코드에만 {only_code}" if only_code else ""))

unknown = sorted(set(team_parts) - set(C.PARTS))
check(not unknown, "team.json 의 파트가 전부 PARTS 안에 있다"
      + (f" — 모르는 것 {unknown}" if unknown else ""))

# 제목의 개수도 같이 센다. 표만 고치고 제목을 두면 "13종" 이라고 쓰인 문서가 14줄을 싣는다.
m = re.search(r"##\s*1\.\s*파트 태그\s*(\d+)\s*종", doc)
check(m is not None, "문서 제목이 파트 개수를 적는다")
if m:
    check(int(m.group(1)) == len(C.PARTS),
          f"제목의 개수가 실제와 같다 (제목 {m.group(1)} · 실제 {len(C.PARTS)})")

print()
print("== 제목 검사가 그 목록을 따라가는가 ==")

# `TITLE_RE` 는 PARTS 로 만들어진다 — 목록에 넣으면 따라오는 것이 맞는지 실제로 본다.
for part in C.PARTS:
    title = f"[{part}] feat: 무언가를 한다 ({C.PROJECT_KEY}-1)"
    check(C.TITLE_RE.match(title) is not None, f"[{part}] 제목을 받는다")

check(C.TITLE_RE.match(f"[Nope] feat: 무언가 ({C.PROJECT_KEY}-1)") is None,
      "목록에 없는 파트는 거부한다")

print()
print("== 담당과 에픽이 비어 있지 않은가 ==")

# 파트마다 Epic 이 있어야 이슈의 상위를 걸 수 있다. 담당은 `미정` 이어도 되지만
# 에픽 번호가 비면 그 파트의 이슈는 상위 없이 떠돌고 R2 가 needs-fix 를 붙인다.
missing_epic = []
for line in doc.split("\n"):
    m2 = re.match(r"^\|\s*`\[([A-Za-z]+)\]`\s*\|(.+)\|\s*$", line)
    if not m2:
        continue
    cells = [c.strip() for c in m2.group(2).split("|")]
    if not cells or not re.match(r"^-\d+$", cells[-1]):
        missing_epic.append(m2.group(1))
check(not missing_epic, "파트마다 Epic 번호가 적혀 있다"
      + (f" — 빈 것 {missing_epic}" if missing_epic else ""))

print()
print("== 이 검사가 못 보는 곳 ==")
print("   Jira 자동화 R2 의 제목 정규식은 저장소 밖이라 여기서 못 본다.")
print("   파트를 늘렸으면 R2 도 사람이 맞춰야 한다 — 안 맞추면 이슈가 needs-fix 로 막힌다.")
print("   (docs/jira-gitlab-automation.md §R2)")

print()
print(f"== 파트 목록: 통과 {passes} · 실패 {len(fails)} ==")
if fails:
    print("   ❌ " + " / ".join(fails))
raise SystemExit(1 if fails else 0)
