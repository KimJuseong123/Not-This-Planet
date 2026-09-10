"""미해소 스레드 경고 — 봇이 스스로 관리하는 스레드는 세지 않는다 (S15P21B202-420).

2026-09-10 에 !320·!321·!322·!324 가 파이프라인 초록인 채 조용히 잠겨 있었다.
원인은 사람이 남긴 답글이 스레드가 된 것이었다. 그래서 mr-lint 가 알리게 했는데,
stack-order 는 **일부러** 스레드를 남겨 순서를 강제하므로 그것까지 세면 스택 MR 마다
경고가 떠서 정작 찾으려던 사람 답글이 묻힌다.
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import mr_lint as L

print()
print("== 미해소 스레드 경고 ==")


# ── ① 봇 마커가 첫 줄에 있으면 봇 스레드다 ──────────────────────────────
for _marker in ("<!-- stack-order:metagame -->", "<!-- ai-review:metagame -->",
                "<!-- mr-lint:advice -->", "<!-- unity-compile:metagame -->",
                "<!-- mr-overlap:metagame:!320 -->", "<!-- ci-failed:metagame -->"):
    assert L.is_bot_thread({"body": _marker + "\n본문"}), _marker
print("  봇 마커 6종을 모두 봇으로 본다 ✅")


# ── ② 사람 답글을 봇으로 오인하지 않는다 ────────────────────────────────
#     주석이 본문 **안**에 있는 경우가 함정이다 — 첫 줄만 본다.
for _human in ("고쳤습니다", "확인 부탁드립니다\n<!-- stack-order:metagame -->",
               "이 부분 <!-- 왜 --> 확인 부탁드립니다", "", "   "):
    assert not L.is_bot_thread({"body": _human}), repr(_human)
# body 가 없거나 None 이어도 죽지 않는다 — API 응답에 없을 수 있다.
assert not L.is_bot_thread({})
assert not L.is_bot_thread({"body": None})
print("  사람 답글·빈 본문을 봇으로 오인하지 않는다 ✅")


# ── 가짜 GitLab ─────────────────────────────────────────────────────────
class _Gl:
    token = "x"

    def __init__(self, threads):
        self._t = threads

    def project_flag(self, name):
        assert name == "only_allow_merge_if_all_discussions_are_resolved", name
        return True

    def discussions(self):
        return self._t


def _thread(body, author, resolved=False):
    return {"notes": [{"body": body, "resolvable": True, "resolved": resolved,
                       "author": {"username": author}}]}


def _said(rep):
    return " ".join(rep.oks + rep.warns + rep.fails)


# ── ③ 봇 스레드만 남으면 경고가 아니라 안내다 ───────────────────────────
#     경고로 만들면 스택 MR 이 늘 노란불이 되고, 그러면 사람이 노란불을 무시한다.
_rep = L.Report("t")
L.check_open_threads(_rep, _Gl([_thread("<!-- stack-order:metagame -->\n순서", "ci-bot")]))
assert not _rep.warns, "봇 스레드만 있는데 경고했다: " + _said(_rep)
assert "봇 스레드 1개" in _said(_rep), _said(_rep)
print("  봇 스레드만 남으면 경고하지 않고 안내한다 ✅")


# ── ④ 사람 스레드는 누가 몇 개인지 밝힌다 ───────────────────────────────
_rep2 = L.Report("t")
L.check_open_threads(_rep2, _Gl([
    _thread("답했습니다", "rmawnsdud"),
    _thread("여기도", "rmawnsdud"),
    _thread("확인", "suho6347"),
    _thread("<!-- stack-order:metagame -->\n순서", "ci-bot"),
    _thread("해결된 것", "suho6347", resolved=True),
]))
assert len(_rep2.warns) == 1, _rep2.warns
_w = _rep2.warns[0]
assert "미해소 스레드 3개" in _w, _w
assert "rmawnsdud 2개" in _w and "suho6347 1개" in _w, _w
assert "잠깁니다" in _w, "머지가 잠긴다는 말이 없다: " + _w
print("  사람 스레드 개수와 남긴 사람을 밝힌다 ✅  (해결된 것·봇 것은 제외)")


# ── ⑤ 설정이 꺼졌거나 못 읽으면 말하지 않는다 ───────────────────────────
#     잠기지 않는 상태를 경고하면 늑대 소년이 된다.
class _Off(_Gl):
    def project_flag(self, name):
        return False


class _Unknown(_Gl):
    def project_flag(self, name):
        return None


for _cls in (_Off, _Unknown):
    _r = L.Report("t")
    L.check_open_threads(_r, _cls([_thread("답했습니다", "rmawnsdud")]))
    assert not _said(_r), f"{_cls.__name__}: 말하지 않아야 한다 — {_said(_r)}"

# 스레드 목록을 못 가져와도(권한·오프라인) 조용히 넘긴다.
_r = L.Report("t")
L.check_open_threads(_r, _Gl(None))
assert not _said(_r), _said(_r)

# 토큰이 없으면 아예 묻지 않는다.
class _NoToken(_Gl):
    token = ""

    def project_flag(self, name):
        raise AssertionError("토큰이 없는데 설정을 물었다")


_r = L.Report("t")
L.check_open_threads(_r, _NoToken([]))
assert not _said(_r), _said(_r)
print("  설정이 꺼졌거나 확인 못 하면 말하지 않는다 ✅")

print("== 통과 ==")
