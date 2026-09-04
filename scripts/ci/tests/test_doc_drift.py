"""문서가 없어진 CI 잡을 설명하는지 (S15P21B202-292).

실행: python scripts/ci/tests/test_doc_drift.py
네트워크·토큰 없이 돈다. 저장소의 실제 문서를 읽는다.

왜 이 검사가 있나: 문서가 틀리면 사람이 없는 잡을 찾다 시간을 버리고, 더 나쁘게는
"이미 처리된 일" 로 믿고 넘어간다. `-139` §6 의 "develop·manual 이 막는다" 가 그랬다 —
실제로는 안 막았고, 그 문장 때문에 `-252` 의 구멍이 한동안 검토된 위험으로 분류돼 있었다.

왜 이렇게 좁게 보나: 처음 짰을 때 9건을 지적했는데 **진짜 문제는 0건**이었다.
러너 태그(`unity-win`), 기록 문서가 설명하는 아직 없는 잡(`deploy-dev`), 그리고
"한동안 뒀지만 없앴습니다" 같은 정당한 역사 서술이 전부 걸렸다. 오탐이 섞이면
사람이 검사를 안 보게 되므로 셋 다 거른다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.dirname(HERE))

os.chdir(ROOT)

import repo_hygiene as R  # noqa: E402

JOBS = R.ci_jobs()
assert len(JOBS) >= 10, f"잡을 못 읽었다: {sorted(JOBS)}"
assert "deploy-backend" in JOBS and "mr-lint" in JOBS, sorted(JOBS)
print(f"  잡 {len(JOBS)}개 읽음 ✅")

TAGS = R.ci_tags()
assert {"linux", "deploy"} <= TAGS, sorted(TAGS)
print(f"  러너 태그 {sorted(TAGS)} 읽음 ✅")

# ── 지금 저장소는 깨끗해야 한다
now = R.doc_job_drift(JOBS)
assert now == [], "문서가 없는 CI 잡을 말한다:\n  " + "\n  ".join(now)
print("  현재 문서 지적 0건 ✅")


def docs_text() -> str:
    import glob
    return "\n".join(open(p, encoding="utf-8").read()
                     for p in glob.glob("docs/**/*.md", recursive=True))


ALL = docs_text()

# ── 거르는 셋이 **실제로 문서에 있는데도** 지적되지 않는지. 안 그러면 위 '0건' 은 의미가 없다.
for token, why in [("unity-win", "러너 태그"),
                   ("deploy-dev", "기록 문서가 설명하는 아직 없는 잡"),
                   ("jira-complete", "역사 서술(drift-ok 표시)")]:
    assert f"`{token}`" in ALL, f"{token} 이 문서에 없다 — 이 케이스가 무의미해졌다"
    assert token not in JOBS, f"{token} 이 실제 잡이 됐다 — 케이스를 갱신하라"
    assert not any(f"`{token}`" in b for b in now), f"{why} 인 {token} 이 지적됐다"
print("  태그·기록·역사 서술 3종 오탐 없음 ✅")

# ── 잡이 사라지면 반드시 걸린다. 이게 이 검사의 존재 이유다.
gone = R.doc_job_drift(JOBS - {"mr-lint"})
assert gone, "mr-lint 가 없어졌는데도 아무 것도 지적하지 않는다 — 검사가 죽어 있다"
assert all("`mr-lint`" in g for g in gone), gone
assert all(re.match(r"^docs[/\\][^/\\]+\.md:\d+ ", g) for g in gone), gone
print(f"  잡이 사라지면 지적함 ({len(gone)}곳, 파일:행 포함) ✅")

# ── 기록 문서는 보지 않는다 (그 시점의 사실이거나 앞으로 만들 것을 적는다)
assert not any(("records/" in g or "records\\" in g) for g in gone), gone
print("  docs/records/ 는 검사 대상 아님 ✅")

# ── drift-ok 표시가 그 줄만 빼는지
marked = [p for p in ("docs/e2e-checklist.md", "docs/jira-gitlab-automation.md", "docs/mr-flow.md")
          if R.DRIFT_OK in open(p, encoding="utf-8").read()]
assert len(marked) == 3, f"역사 서술 표시가 {len(marked)}곳뿐: {marked}"
# 표시된 파일에서도 **다른 줄**은 여전히 검사된다
norm = [g.replace(chr(92), "/") for g in gone]                      # Windows 경로 구분자
still = [g for g in norm if any(g.startswith(m) for m in marked)]
assert still, "표시된 파일이 통째로 빠졌다 — 줄 단위여야 한다"
print(f"  drift-ok 는 줄 단위 (표시 파일에서 {len(still)}곳 여전히 검사) ✅")

print("TEST292 OK")
