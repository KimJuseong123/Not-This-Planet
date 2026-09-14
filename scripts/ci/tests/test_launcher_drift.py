"""아침 점검이 **내보내지 않은 런처**를 부르는지 (S15P21B202-508).

실행: python scripts/ci/tests/test_launcher_drift.py
네트워크 없이 http() 를 가짜로 바꿔 돌린다.

런처는 **빌드가 자동이고 내보내기가 버튼**이다. 그래서 "고쳐서 머지했는데 아무도
안 누른" 상태가 남는다 — 2026-09-14 에 그 상태가 **세 번** 쌓여 있었는데
(`-406` 개발 모드 · `-460` 채널 · `-507` 로그 보존) 아무것도 빨갛지 않았다.
`--dev` 가 안 먹어서야 드러났다.

**여기서 조용히 틀릴 수 있는 것은 "판정 실패" 를 "같음" 으로 읽는 것이다.**
버전을 못 읽었는데 아무 말도 안 하면, 이 검사 자체가 죽어도 화면은 똑같다 —
이 이슈가 막으려는 바로 그 모양이다.
"""
import io
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))   # scripts/ci/tests → 저장소 루트
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x"})

import ci_common  # noqa: E402
import mm_digest as D  # noqa: E402


def serve(status, payload):
    def http(method, url, headers=None, body=None, timeout=30):
        return status, payload
    ci_common.http = http
    D.http = http


def with_version(text):
    """`launcher/VERSION` 을 임시 파일로 바꿔 준다."""
    tmp = tempfile.NamedTemporaryFile("w", suffix=".VERSION", delete=False,
                                      encoding="utf-8", newline="\n")
    tmp.write(text)
    tmp.close()
    D.LAUNCHER_VERSION_FILE = tmp.name
    return tmp.name


def main() -> int:
    made = []

    # ── ① 같으면 조용하다 — 이 카드의 규칙 ──────────────────────────────
    made.append(with_version("1.1.0\n"))
    serve(200, {"version": "v1.1.0"})
    assert D.launcher_drift() is None, D.launcher_drift()
    # `v` 접두사가 있든 없든 같은 것으로 본다
    serve(200, {"version": "1.1.0"})
    assert D.launcher_drift() is None, D.launcher_drift()
    print("  같으면 — 아무 말도 안 한다 (v 접두사 무관)")

    # ── ② 다르면 **두 버전을 다 적고** 무엇을 누를지 말한다 ─────────────
    serve(200, {"version": "v1.0.0"})
    line = D.launcher_drift()
    assert line and "내보내지 않은 런처" in line, line
    assert "v1.1.0" in line and "v1.0.0" in line, f"두 버전이 다 안 적혔다: {line}"
    assert "promote-launcher" in line, f"무엇을 누를지 안 적혔다: {line}"
    assert D.INFRA_MM and f"@{D.INFRA_MM}" in line, "조치할 사람이 안 붙었다"
    print("  다르면 — 저장소·내보낸 것 · 누를 버튼 · 담당")

    # ── ③ **판정 실패를 '같음' 으로 읽지 않는다** ───────────────────────
    #    이것이 이 검사의 핵심이다. 못 읽었는데 조용하면 검사가 죽어도 화면이 같다.
    for st, payload, why in ((404, None, "404"),
                             (200, "이건 JSON 이 아니다", "본문이 dict 가 아님"),
                             (500, {"version": "v1.1.0"}, "서버 오류")):
        serve(st, payload)
        line = D.launcher_drift()
        assert line is not None, f"{why}: 판정 실패인데 조용했다"
        assert "읽지 못했다" in line, (why, line)
    print("  못 읽으면 — 조용하지 않고 '읽지 못했다' 로 말한다")

    # ── ④ 버전 파일이 없거나 비었을 때 ──────────────────────────────────
    D.LAUNCHER_VERSION_FILE = os.path.join(tempfile.gettempdir(), "없는파일-508.VERSION")
    serve(200, {"version": "v1.1.0"})
    line = D.launcher_drift()
    assert line and "읽지 못했다" in line, line

    made.append(with_version("   \n"))
    line = D.launcher_drift()
    assert line and "비었다" in line, line
    print("  버전 파일이 없거나 비면 — 그 사실을 말한다")

    # ── ⑤ **기본 경로가 절대 경로이고 실제로 읽힌다** (`!423` 리뷰) ────────
    #    위 검사들은 LAUNCHER_VERSION_FILE 을 임시 파일로 덮어써서, 상대 경로였을
    #    때 잡의 작업 디렉터리에 따라 매일 "읽지 못했다" 가 뜨는 것을 **가렸다.**
    #    `-485` 에서 같은 버그를 고쳤는데 이쪽으로 옮기지 못했었다.
    import importlib
    importlib.reload(D)
    assert os.path.isabs(D.LAUNCHER_VERSION_FILE), D.LAUNCHER_VERSION_FILE
    here = os.getcwd()
    try:
        os.chdir(tempfile.gettempdir())      # 일부러 딴 데서 돈다
        serve(200, {"version": "v" + io.open(D.LAUNCHER_VERSION_FILE, encoding="utf-8").read().strip()})
        assert D.launcher_drift() is None, D.launcher_drift()
    finally:
        os.chdir(here)
    print("  기본 경로가 절대 경로 — 작업 디렉터리와 무관하게 읽는다")

    # ── ⑥ 저장소에 실제 파일이 있고 꼴이 맞다 ──────────────────────────
    real = os.path.join(ROOT, "launcher", "VERSION")
    assert os.path.exists(real), f"launcher/VERSION 이 없다: {real}"
    ver = io.open(real, encoding="utf-8").read().strip()
    assert ver and all(c.isdigit() or c == "." for c in ver), f"버전 꼴이 아니다: {ver!r}"
    print(f"  저장소의 launcher/VERSION = {ver}")

    for f in made:
        try:
            os.unlink(f)
        except OSError:
            pass

    print("TEST_LAUNCHER_DRIFT OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
