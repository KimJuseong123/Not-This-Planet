"""`[PERF]` 파서 (S15P21B202-376).

실행: python scripts/ci/tests/test_perflog.py

이 파서가 읽는 형식은 `PerfLog.Line` 이 만든다. 양쪽이 어긋나면 **로그는 남아 있는데
도구만 빈손**이 되므로, 여기서 실제 로그 모양(스택 트레이스가 섞인 것)으로 잡는다.
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import perflog as P  # noqa: E402

print("― S15P21B202-376 [PERF] 파서 ―")

# Unity 로그는 한 줄 뒤에 스택 트레이스가 따라붙는다. 그 모양 그대로 만든다.
REAL = """\
[빌드] 2026-09-10 10:46 · abc1234 · S15P21B202-376 · +커밋안한변경
UnityEngine.Debug:ExtractStackTraceNoAlloc (byte*,int,string)
[빌드] 실험 374-rate0
[PERF] 시작 · 실험 374-rate0 · 5초마다 한 줄
[PERF] 형식: t fps min p95 frames players props
[PERF] 값 fixedDeltaTime=0.02 vSync=1 targetFrameRate=-1 quality=2
[PERF] t=5.0 fps=118.0 min=90.0 p95=11.2 frames=590 players=1 props=0
UnityEngine.StackTraceUtility:ExtractStackTrace ()
[PERF] t=10.0 fps=112.5 min=71.0 p95=14.1 frames=562 players=2 props=3
[PERF] t=15.0 fps=61.0 min=22.0 p95=45.5 frames=305 players=4 props=18
[PERF] t=20.0 fps=48.5 min=9.5 p95=105.3 frames=242 players=4 props=31
"""

_rows = P.samples(REAL)
assert len(_rows) == 4, f"표본 4건이어야 한다: {len(_rows)}"
assert _rows[0] == {"t": 5.0, "fps": 118.0, "min": 90.0, "p95": 11.2,
                    "frames": 590, "players": 1, "props": 0}, _rows[0]
print("  스택 트레이스가 섞인 실제 로그에서 표본을 뽑는다 ✅")

# 머리 줄(빌드·실험·값)을 문맥으로 싣는다 — 무엇을 재고 있었는지가 거기 있다
_ctx = P.context(REAL)
assert any("374-rate0" in c for c in _ctx), _ctx
assert any("fixedDeltaTime" in c for c in _ctx), _ctx
assert any("abc1234" in c for c in _ctx), "빌드 커밋이 문맥에 있어야 한다: " + str(_ctx)
print("  실험 라벨·파라미터·빌드 커밋을 문맥으로 싣는다 ✅")

# 인원수로 묶는다 — 이것이 "몇 명부터 꺾이나" 를 보여 준다
_groups = P.by_players(_rows)
assert list(_groups) == [1, 2, 4], list(_groups)
assert len(_groups[4]) == 2, _groups[4]
print("  인원수로 묶고 오름차순으로 준다 ✅")

# 최저는 **가장 나쁜 표본**에서 온다. 평균으로 뭉개면 -374 를 놓친다
assert P.worst(_groups[4], "min") == 9.5, P.worst(_groups[4], "min")
assert abs(P.mean(_groups[4], "fps") - 54.75) < 0.001, P.mean(_groups[4], "fps")
print("  최저는 최악 표본에서 · 평균과 따로 준다 ✅")

# 표본이 없으면 **왜 없는지**를 말한다. 조용히 0건으로 끝내면 다음 할 일을 모른다
_buf = io.StringIO()
P.report(os.devnull, out=_buf)
_out = _buf.getvalue()
assert "표본이 없다" in _out and "SPACE_EXPERIMENT" in _out, _out
print("  표본이 없으면 원인을 짚는다 ✅")

# 보고서에 가장 끊긴 순간이 있다 — 표는 경향, 이 줄은 재현 지점
_tmp = os.path.join(os.environ.get("TEMP", "."), "perflog-test-sample.log")
io.open(_tmp, "w", encoding="utf-8").write(REAL)
try:
    _buf = io.StringIO()
    _rows2 = P.report(_tmp, out=_buf)
    _out = _buf.getvalue()
    assert len(_rows2) == 4
    assert "가장 끊긴 순간" in _out and "t=20" in _out, _out
    assert "9.5fps" in _out, _out
    # 인원별 표가 1·2·4 를 모두 담는다
    for n in ("   1 |", "   2 |", "   4 |"):
        assert n in _out, f"인원 {n.strip()} 행이 없다:\n{_out}"
finally:
    os.remove(_tmp)
print("  보고서에 인원별 표와 가장 끊긴 순간이 있다 ✅")

# 소수점이 쉼표인 형식은 읽지 않는다(그런 줄이 나오면 C# 쪽 로케일 고정이 깨진 것이다)
assert P.samples("[PERF] t=5,0 fps=118,0 min=90,0 p95=11,2 frames=590 players=1 props=0") == []
print("  쉼표 소수점은 표본으로 세지 않는다 ✅")

print("TEST376 OK")
