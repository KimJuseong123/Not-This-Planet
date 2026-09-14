"""아침 점검 🚀 줄이 **백엔드의 지금 상태**를 말하는지 (S15P21B202-490).

실행: python scripts/ci/tests/test_deploy_line.py
네트워크 없이 돈다 — 잡 dict 를 글자로 넣어 본다.

전에는 `🚀 개발 서버: a7b81017 (09/14 04:05)` 였다. 해시만으로는 그 서버가 최신인지
알 수 없고, 알아보려면 GitLab 에 붙여 넣어 봐야 해서 카드를 읽는 뜻이 없었다.

**한 번 잘못 고쳤다가 되돌린 자리다.** 처음에는 머지 커밋의 MR 제목을 붙였는데,
개발 서버는 새벽 스케줄이 develop 머리를 `changes` 를 보지 않고 배포하므로 그때 머리가
씬 MR 이면 `🚀 개발 서버: … [Plan] scene: v1.1.0` 이 됐다 — **백엔드 줄에 백엔드와
무관한 문구**다. 그래서 "무엇을 올렸나" 대신 **"백엔드가 지금 최신인가"** 를 적는다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x"})

import mm_digest as D  # noqa: E402

URL = "https://lab.ssafy.com/g/p/-/commit/a7b81017df"


def job(commit) -> dict:
    return {"finished_at": "2026-09-14T04:05:09.771+09:00", "commit": commit}


def st(commit, **over) -> dict:
    d = {"live": job(commit), "attempt": None, "pending": 0, "complete": True, "drift": []}
    d.update(over)
    return d


def main() -> int:
    full = {"short_id": "a7b81017", "id": "a7b81017df", "web_url": URL}

    # ── ① 어느 서버의 **백엔드**인지 적는다 ──────────────────────────────
    line = D.deploy_line(st(full), "배포")
    assert "배포 서버 백엔드:" in line, line
    assert f"[`a7b81017`]({URL})" in line, line
    assert "09/14 04:05" in line, line
    print("  배포 서버 — '백엔드' 를 명시하고 해시는 링크")
    print("   ", line)

    # ── ② **최신이면 최신이라고 말한다** ────────────────────────────────
    #    아무 말이 없으면 "최신이라 조용한 것" 과 "확인을 못 한 것" 이 구별되지 않는다.
    assert "develop 과 같음" in D.deploy_line(st(full), "개발"), D.deploy_line(st(full), "개발")
    line = D.deploy_line(st(full, drift=None), "개발")
    assert "확인 못 함" in line and "develop 과 같음" not in line, line
    print("  같으면 '같음' · 확인 실패면 '확인 못 함' — 둘을 가른다")

    # ── ③ MR 제목은 **싣지 않는다** ─────────────────────────────────────
    #    개발 서버는 develop 머리를 그대로 배포하므로 그 제목이 씬·아트일 수 있다.
    scene = dict(full, title="Merge branch 'S15P21B202-368-plan-1-0-1' into 'develop'",
                 message="Merge branch 'x'\n\n[Plan] scene: v1.1.0 (S15P21B202-368)\n")
    line = D.deploy_line(st(scene), "개발")
    assert "scene" not in line and "[Plan]" not in line, f"백엔드 줄에 씬 문구가 실렸다: {line}"
    print("  씬·아트 MR 제목이 백엔드 줄에 실리지 않는다")

    # ── ④ 커밋 정보가 모자라도 줄이 깨지지 않는다 ───────────────────────
    line = D.deploy_line(st({k: v for k, v in full.items() if k != "web_url"}), "개발")
    assert "`a7b81017`" in line and "](" not in line, line
    for c in (None, {}, {"short_id": "", "id": ""}):
        line = D.deploy_line(st(c), "개발")
        assert line.startswith("🚀 개발 서버 백엔드:") and "09/14 04:05" in line, (c, line)
    print("  주소·커밋이 없어도 줄은 나온다")

    # ── ⑤ **기존 경고는 그대로 붙는다** ─────────────────────────────────
    #    읽기 좋게 만들다가 정작 조치가 필요한 문구를 떨어뜨리면 본말전도다.
    assert "2건 배포 대기" in D.deploy_line(st(full, pending=2), "개발")
    assert "배포 대기 잡이 없는데" in D.deploy_line(st(full, drift=["backend/a.java"]), "배포")
    print("  배포 대기·드리프트 경고는 그대로")

    # ── ⑥ 배포 이력이 아예 없으면 최신 여부를 **붙이지 않는다** ─────────
    #    올린 적이 없는데 "확인 못 했다" 를 덧붙이면 이미 한 말에 군더더기가 붙는다.
    line = D.deploy_line({"live": None, "attempt": None, "pending": 0,
                          "complete": True, "drift": None}, "개발", "힌트")
    assert "CI 로 올린 이력 없음" in line and "힌트" in line, line
    assert "확인 못 함" not in line, f"이력이 없는데 최신 여부를 말했다: {line}"
    print("  이력이 없으면 — 그 말만 하고 끝낸다")

    print("TEST_DEPLOY_LINE OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
