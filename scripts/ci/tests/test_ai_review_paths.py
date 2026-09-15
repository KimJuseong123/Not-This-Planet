"""AI 리뷰가 코드 디렉터리를 빠뜨리지 않는가 (S15P21B202-544).

실행: python scripts/ci/tests/test_ai_review_paths.py
네트워크 없이 `.gitlab-ci.yml` 과 추적 파일 목록만 본다.

`ai-review` 의 `changes:` 는 **문서를 빼려고** 만든 목록인데, 그 뒤에 생긴
`backend/`·`launcher/`·`website/` 가 적히지 않은 채로 지나갔다. 그 셋만 바꾼 MR 은
리뷰 코멘트가 0건이고 `ai-review-gate` 도 안 생겨 ⛔ 로 막힐 길이 없었다 — 머지된
`!418`(백엔드)·`!421`·`!443`(런처)이 그렇게 들어갔다.

**"넣을 것" 목록은 잊으면 조용히 빠진다.** 그래서 반대로 검사한다: 저장소에 소스가
든 최상위 디렉터리가 있는데 목록에 없으면 여기서 실패한다.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.stdout.reconfigure(encoding="utf-8")

CI = os.path.join(ROOT, ".gitlab-ci.yml")

# 사람이 읽고 고치는 것. `.dll`·`.rsp`·`.mp4` 는 소스가 아니다.
SOURCE_EXT = {".cs", ".java", ".py", ".js", ".ts", ".sh", ".kt", ".gradle",
              ".css", ".html", ".yml", ".yaml"}

# 소스가 들어 있어도 리뷰에서 뺀 곳. **이유 없이 넣지 말 것** — 이 목록이 길어지는
# 만큼 검사가 하는 일이 줄어든다.
EXEMPT = {
    "docs": "문서다. 팀장 리뷰로 충분하고 구독 사용량을 아낀다(잡 주석의 원래 결정).",
}


def changes_of(job: str) -> list[str]:
    """그 잡의 `rules:` 안 `changes:` 목록. 잡 구간 밖은 보지 않는다."""
    lines = open(CI, encoding="utf-8").read().split("\n")
    start = next(i for i, l in enumerate(lines) if l.startswith(job + ":"))
    end = next((i for i in range(start + 1, len(lines))
                if lines[i] and not lines[i][0].isspace()), len(lines))
    out, on = [], False
    for l in lines[start:end]:
        s = l.strip()
        if s == "changes:":
            on = True
            continue
        if on:
            if s.startswith("- "):
                out.append(s[2:].strip().strip('"'))
            elif s and not s.startswith("#"):
                on = False
    return out


# ── 1) 두 잡의 목록이 같아야 한다 ──
# 어긋나면 리뷰는 도는데 게이트가 없거나(막을 수 없다), 리뷰가 없는데 게이트가 막는다.
review, gate = changes_of("ai-review"), changes_of("ai-review-gate")
assert review, "ai-review 의 changes 를 읽지 못했다"
assert review == gate, "두 목록이 다르다:\n  ai-review      %r\n  ai-review-gate %r" % (review, gate)
print("  두 잡의 changes 가 같다 (%d줄) ✅" % len(review))

# ── 2) 이번에 빠져 있던 셋이 들어 있는가 ──
for want in ("backend/**/*", "launcher/**/*", "website/**/*"):
    assert want in review, f"{want} 가 없다 — 그 디렉터리만 바꾼 MR 은 리뷰도 게이트도 없다"
print("  backend · launcher · website 있음 ✅")

# ── 3) 소스가 든 최상위 디렉터리가 전부 덮이는가 ──
# 목록을 "넣을 것" 으로 두는 한 새 디렉터리는 잊힌다. 반대 방향에서 잡는다.
covered = {c.split("/")[0] for c in review if "/" in c}
files = subprocess.run(["git", "-C", ROOT, "ls-files", "-z"],
                       capture_output=True, check=True).stdout.decode("utf-8").split("\0")
code_dirs: dict[str, str] = {}
for f in files:
    if "/" not in f:
        continue
    top = f.split("/")[0]
    if os.path.splitext(f)[1].lower() in SOURCE_EXT:
        code_dirs.setdefault(top, f)

missing = {d: ex for d, ex in code_dirs.items() if d not in covered and d not in EXEMPT}
assert not missing, (
    "소스가 든 디렉터리가 AI 리뷰 목록에 없다 — 그곳만 바꾼 MR 은 리뷰 없이 지나간다:\n"
    + "\n".join(f"  {d}/  (예: {ex})" for d, ex in sorted(missing.items()))
    + "\n  넣거나, 빼려면 EXEMPT 에 **이유와 함께** 적는다.")
print("  소스가 든 최상위 %d곳 전부 덮인다 (면제 %d곳) ✅"
      % (len(code_dirs), len(EXEMPT & code_dirs.keys())))

# ── 4) 면제는 이유가 있어야 한다 ──
for d, why in EXEMPT.items():
    assert len(why) > 10, f"{d} 의 면제 이유가 비었다"
print("  면제마다 이유가 적혀 있다 ✅")

print("OK  ai-review 경로 — 목록 일치 · 세 곳 포함 · 빠진 코드 디렉터리 없음")
