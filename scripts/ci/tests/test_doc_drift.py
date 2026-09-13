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
assert {"deploy-dev", "deploy-prod", "mr-lint"} <= set(JOBS), sorted(JOBS)
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

# ── 거르는 것들이 **실제로 문서에 있는데도** 지적되지 않는지. 안 그러면 위 '0건' 은 의미가 없다.
#    `unity-win` 은 `-271` 로 `unity-compile` 이 EC2 로 옮겨진 뒤 **태그 목록에서 빠졌다.** 그래서
#    러너 태그 필터로는 더 안 걸러지고, 그 줄들은 문서를 고치거나 drift-ok 로 표시했다 — 태그 필터
#    자체는 아래 임시 디렉터리 케이스에서 검사한다.
#    `deploy-dev` 를 쓰던 자리를 `deploy-backend` 로 바꿨다 — -284 에서 그것이
#    **실제 잡이 되어** 이 케이스가 무의미해졌다(테스트가 그 사실을 스스로 말했다).
#    `deploy-backend` 는 반대 방향으로 같은 종류다: 기록 문서가 설명하는데
#    **이제** 없는 잡이다.
for token, why in [("unity-win", "문서 수정 또는 drift-ok 표시"),
                   ("deploy-backend", "기록 문서가 설명하는 이제 없는 잡"),
                   ("jira-complete", "역사 서술(drift-ok 표시)")]:
    assert f"`{token}`" in ALL, f"{token} 이 문서에 없다 — 이 케이스가 무의미해졌다"
    assert token not in JOBS, f"{token} 이 실제 잡이 됐다 — 케이스를 갱신하라"
    assert not any(f"`{token}`" in b for b in now), f"{why} 인 {token} 이 지적됐다"
print("  기록·역사 서술·수정 3종 오탐 없음 ✅")

# ── 러너 태그 필터를 저장소 내용에 의존하지 않고 검사한다. 임시 디렉터리에 통제된 파일을 만든다.
import tempfile  # noqa: E402
from pathlib import Path  # noqa: E402

with tempfile.TemporaryDirectory() as td:
    root = Path(td)
    (root / "docs").mkdir()
    (root / ".gitlab-ci.yml").write_text(
        "mr-lint:\n  tags: [linux]\n\nunity-compile:\n  tags: [unity-win]\n", encoding="utf-8")
    (root / "docs" / "guide.md").write_text(
        "러너 태그는 `unity-win` 이고 잡은 `unity-compile` 이다.\n"
        "없어진 잡: `mr-ghost`\n"
        "표시한 줄: `mm-ghost`  <!-- drift-ok -->\n", encoding="utf-8")
    here = os.getcwd()
    os.chdir(root)
    try:
        jobs = R.ci_jobs()
        assert jobs == {"mr-lint", "unity-compile"}, jobs
        assert R.ci_tags() == {"linux", "unity-win"}, R.ci_tags()
        got = R.doc_job_drift(jobs)
        assert len(got) == 1 and "`mr-ghost`" in got[0], got
    finally:
        os.chdir(here)
print("  러너 태그 필터 · 미표시 이름만 지적 (임시 디렉터리) ✅")


# ── 스크립트가 **잡 이름으로 무언가를 가르는 자리**는 실제 잡과 같아야 한다 (S15P21B202-464)
#
#     위 검사는 `docs/**` 만 본다. 그래서 `-284` 가 `deploy-backend` 를 둘로 가를 때
#     **스크립트 쪽 상수 두 개가 옛 이름으로 남은 것**을 아무도 못 잡았다:
#       · `mm_digest.DEV_JOB` — 아침 카드의 배포 줄이 **늘 "이력 없음"** 이었다
#       · `deploy_backend.NOTIFY_JOBS` — 배포 성공·실패 카드가 **한 장도 안 나갔다**
#     둘 다 "조용해진다" 는 모양이라 증상이 정상과 구별되지 않았다. 이름은 한 글자만
#     달라도 조용히 꺼지므로 사람 눈이 아니라 이것이 지켜야 한다.
_jobs = R.ci_jobs()
import deploy_backend as _DB  # noqa: E402
import mm_digest as _MD  # noqa: E402

_names: list[tuple[str, str]] = []
for _mod in (_MD, _DB):
    for _attr in dir(_mod):
        if not (_attr.endswith("_JOB") or _attr.endswith("_JOBS")):
            continue
        _val = getattr(_mod, _attr)
        for _n in ((_val,) if isinstance(_val, str) else tuple(_val)):
            _names.append((f"{_mod.__name__}.{_attr}", _n))

assert _names, "잡 이름 상수를 하나도 못 찾았다 — 이 검사가 무의미해졌다"
for _where, _name in _names:
    assert _name in _jobs, (
        f"{_where} 가 `{_name}` 인데 .gitlab-ci.yml 에 그런 잡이 없다 — "
        "이름이 어긋나면 그 기능이 조용히 꺼진다")
print(f"  잡 이름 상수 {len(_names)}개가 실제 잡과 같다 ✅")

# ── 잡이 사라지면 반드시 걸린다. 이게 이 검사의 존재 이유다.
gone = R.doc_job_drift(JOBS - {"mr-lint"})
assert gone, "mr-lint 가 없어졌는데도 아무 것도 지적하지 않는다 — 검사가 죽어 있다"
assert all("`mr-lint`" in g for g in gone), gone
assert all(re.match(r"^docs[/\\][^/\\]+\.md:\d+ ", g) for g in gone), gone
print(f"  잡이 사라지면 지적함 ({len(gone)}곳, 파일:행 포함) ✅")

# ── 기록 문서는 보지 않는다 (그 시점의 사실이거나 앞으로 만들 것을 적는다)
assert not any(("records/" in g or "records\\" in g) for g in gone), gone
print("  docs/records/ 는 검사 대상 아님 ✅")

# ── drift-ok 표시가 그 줄만 빼는지 — 표시가 있는 문서를 **찾아서** 본다.
#    파일 이름 3개를 박아 두었더니 -323 이 e2e-checklist 의 역사 서술을 정리하며 표시를 없앤 순간 깨졌다(문서 정리는
#    정상이고, 표시 개수는 변하는 값이다). 검사 대상은 "표시가 있는 문서가 줄 단위로 걸러지는가" 이므로 개수는 보지 않는다.
marked = [f"docs/{f}" for f in sorted(os.listdir("docs"))
          if f.endswith(".md") and R.DRIFT_OK in open(f"docs/{f}", encoding="utf-8").read()]
assert marked, "drift-ok 표시가 있는 문서가 하나도 없다 — 표시 자체가 사라졌으면 줄 단위 필터를 검증할 수 없다"
# 표시된 파일에서도 **다른 줄**은 여전히 검사된다
norm = [g.replace(chr(92), "/") for g in gone]                      # Windows 경로 구분자
still = [g for g in norm if any(g.startswith(m) for m in marked)]
assert still, "표시된 파일이 통째로 빠졌다 — 줄 단위여야 한다"
print(f"  drift-ok 는 줄 단위 (표시 파일에서 {len(still)}곳 여전히 검사) ✅")

print("TEST292 OK")
