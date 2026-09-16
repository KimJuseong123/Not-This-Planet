"""스토리 포인트 필수 검사 (S15P21B202-568).

실행: python scripts/ci/tests/test_lint_story_points.py
네트워크·토큰 없이 순수 함수로 돈다.

**왜 이 검사가 있나.** 포인트 강제는 전에 두 번 빠졌다 — `-88` 은 R5 자동화에서,
`-482` 는 생성 화면에서. 둘 다 **만들 때·진행 중으로 갈 때** 막는 것에 대한 반대였다.
MR 시점으로 옮겨 그 실패 모드를 피했는데, **여기서 가장 위험한 것은 검사 자체가 틀리는
것**이다: 필드 번호가 어긋나면 팀 전체의 MR 이 한꺼번에 막히고, `0` 을 빈 값으로 보면
0 포인트 이슈가 영영 통과하지 못한다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.setdefault("CI_PROJECT_ID", "1")
os.environ.setdefault("CI_MERGE_REQUEST_IID", "1")

import ci_common as C  # noqa: E402
import mr_lint as L  # noqa: E402

F = C.STORY_POINTS_FIELD


def run(fields):
    """검사를 한 번 돌리고 (실패들, 경고들, 통과들) 을 돌려준다."""
    rep = C.Report("검사")
    L.check_story_points(rep, "S15P21B202-1", fields)
    return rep.fails, rep.warns, rep.oks


# ── ① 비어 있으면 막는다 ────────────────────────────────────────────────
fails, warns, _ = run({F: None})
assert len(fails) == 1 and not warns, (fails, warns)
assert "스토리 포인트 없음" in fails[0], fails
assert "S15P21B202-1" in fails[0], "어느 이슈를 열어야 하는지 없다: " + fails[0]
assert "jira-convention" in fails[0], "척도를 어디서 보는지 없다: " + fails[0]
print("  빈 값 → 막는다 ✅")

# ── ② `0` 은 유효한 값이다 ──────────────────────────────────────────────
#     참/거짓으로 보면 0 포인트 이슈가 **영영 통과하지 못한다.** 실수하기 쉬운 자리라
#     값으로 못 박는다.
fails, warns, oks = run({F: 0})
assert not fails and not warns, (fails, warns)
assert oks and "0" in oks[0], oks
print("  0 포인트 → 통과 ✅")

# ── ③ 보통의 값 ────────────────────────────────────────────────────────
fails, warns, oks = run({F: 3.0})
assert not fails and not warns, (fails, warns)
assert "3" in oks[0] and "3.0" not in oks[0], "소수점이 그대로 보인다: " + oks[0]
print("  값이 있으면 통과하고 정수로 보인다 ✅")

# ── ④ 필드를 못 받았으면 **막지 않는다** ────────────────────────────────
#     Jira 는 모르는 필드를 조용히 빼고 준다. 번호가 어긋난 날 이것이 실패였다면
#     **팀 전체의 MR 이 한꺼번에 막힌다** — 그것은 우리 설정 문제이지 작성자의
#     잘못이 아니다.
fails, warns, _ = run({"summary": "x"})
assert not fails, "필드를 못 받았다고 MR 을 막는다 — 번호가 바뀌면 전원이 선다: " + str(fails)
assert len(warns) == 1 and "STORY_POINTS_ID" in warns[0], warns
print("  필드 미수신 → 경고만 (전원이 서지 않는다) ✅")

# ── ⑤ 번호가 한 곳에만 있다 ────────────────────────────────────────────
#     전에는 `mm_digest` 가 JQL 모양으로 따로 들고 있었다. 한쪽만 고쳐지면
#     **아침 점검과 mr-lint 가 서로 다른 필드를 본다.**
import mm_digest as D  # noqa: E402

assert D.STORY_POINTS == C.STORY_POINTS_JQL, (D.STORY_POINTS, C.STORY_POINTS_JQL)
assert C.STORY_POINTS_FIELD == f"customfield_{C.STORY_POINTS_ID}"
assert C.STORY_POINTS_JQL == f"cf[{C.STORY_POINTS_ID}]"

src = open(os.path.join(os.path.dirname(HERE), "mm_digest.py"), encoding="utf-8").read()
assert f"cf[{C.STORY_POINTS_ID}]" not in src, \
    "mm_digest 가 필드 번호를 또 적고 있다 — 한쪽만 고쳐지면 두 도구가 다른 필드를 본다"
print("  필드 번호가 한 곳에만 있다 ✅")

# ── ⑥ 이슈를 받아올 때 그 필드를 **요청**하는가 ─────────────────────────
#     안 받아오면 ④ 의 경고만 영원히 나가고 검사는 아무것도 안 한다.
import inspect  # noqa: E402

got = inspect.getsource(C.Jira.get_issue)
assert "STORY_POINTS_FIELD" in got, \
    "get_issue 가 스토리 포인트를 요청하지 않는다 — 검사가 늘 '필드 미수신' 으로 샌다"
print("  get_issue 가 그 필드를 요청한다 ✅")

print("TEST568 OK")
