"""이벤트 파일을 판 한 줄로 만드는가 (S15P21B202-499).

실행: python scripts/ci/tests/test_sessions.py
네트워크·서버 없이 돈다 — 파일 모양을 글자로 넣어 본다.

**무엇을 막는 검사인가.** 이 요약은 **기획 판단의 입력**이다. 조용히 틀리면 없는
결론이 만들어진다 — "아무도 조립을 안 한다" 같은 말이 잘못된 집계에서 나오면
그것으로 게임을 고치게 된다.

그래서 지키는 것이 셋이다.

  · **없는 값을 0 으로 읽지 않는다** — `floors` 가 없는 것과 `[0,0,0,0]` 은 다르다
  · **유실을 숨기지 않는다** — 깨진 줄·빠진 `seq`·끝 줄 없음을 세어 싣는다
  · **채널을 가른다** — 팀원이 자기 자리에서 만든 빌드의 판이 배포 서버에 함께
    들어온다(실측 2026-09-21: 6건 중 5건). 사람 행동을 물을 때 섞이면 답이 틀린다

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "analytics"))
sys.stdout.reconfigure(encoding="utf-8")

import sessions as S  # noqa: E402

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def write(dirpath, name, text):
    p = os.path.join(dirpath, name)
    with io.open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return p


print()
print("== 판 요약 ==")

WORK = tempfile.mkdtemp()

# ── ① 머리줄과 본문을 가른다 ────────────────────────────────────────────
#
#     **머리줄의 `build` 와 본문의 `build` 는 다른 것이다**(`docs/data-pipeline.md` §1).
#     머리줄은 **올린** 빌드, 본문은 그 판을 **만든** 빌드다. 섞으면 "어느 빌드의
#     플레이인가" 를 영영 못 가른다.
full = write(WORK, "a.ndjson", """# user=55
# backend=prod
# build=v1.2.0
# client-file=20260918-172541.ndjson
# ---
{"seq":1,"t":0,"type":"session_start","backend":"prod","role":"solo","scene":"Stage1_Astra","build":"qa-9-abc","humans":1,"session":"S1"}
{"seq":2,"t":0.1,"type":"phase","name":"cutscene","stage":"Entry"}
{"seq":3,"t":1,"type":"assembly","floors":[2,1,0,0]}
{"seq":4,"t":2,"type":"blocked","code":"incomplete"}
{"seq":5,"t":9,"type":"session_end","result":"abandoned","elapsed":8.5,"cleared":false,"falls":2,"floors":[2,1,0,0],"blocked":"incomplete"}
""")
r = S.read_file(full)
check(r is not None, "멀쩡한 파일을 못 읽었다")
if r:
    check(r["uploaded_build"] == "v1.2.0", f"올린 빌드가 틀렸다: {r['uploaded_build']}")
    check(r["build"] == "qa-9-abc", f"만든 빌드가 틀렸다: {r['build']}")
    check(r["user"] == "55" and r["backend"] == "prod", r)
    check(r["session"] == "S1" and r["role"] == "solo" and r["humans"] == 1, r)
    check(r["result"] == "abandoned" and r["cleared"] is False, r)
    check(r["elapsed"] == 8.5 and r["falls"] == 2, r)
    check(r["floors"] == [2, 1, 0, 0] and r["blocked"] == "incomplete", r)
    check(r["last_phase"] == "cutscene" and r["last_stage"] == "Entry", r)
    check(r["ended"] is True and r["broken_lines"] == 0 and r["events"] == 5, r)

# ── ② 끝 줄이 없어도 버리지 않는다 ─────────────────────────────────────
#
#     강제 종료가 이 모양이다 — 마지막 줄을 못 남긴다. 그 판이야말로 "왜 그만뒀나"
#     의 답이 들어 있는 판이라, 버리면 가장 알고 싶은 것을 버리는 셈이다.
cut = write(WORK, "b.ndjson", """# user=7
# build=dev-20260920-abc
# ---
{"seq":1,"t":0,"type":"session_start","role":"host","scene":"Stage1_Astra","session":"S2"}
{"seq":2,"t":1,"type":"assembly","floors":[2,0,0,0]}
{"seq":3,"t":2,"type":"blocked","code":"no_engine"}
""")
r2 = S.read_file(cut)
check(r2 is not None, "끝 줄이 없다고 판을 버렸다")
if r2:
    check(r2["ended"] is False, "끝 줄이 없는데 끝난 것으로 본다")
    check(r2["result"] is None, f"없는 결과를 지어냈다: {r2['result']}")
    # 마지막 관찰값으로 메운다 — `session_end` 가 못 실은 것을 여기서 되살린다.
    check(r2["floors"] == [2, 0, 0, 0], f"마지막 층 상태를 못 되살렸다: {r2['floors']}")
    check(r2["blocked"] == "no_engine", f"마지막 막힌 사유를 못 되살렸다: {r2['blocked']}")

# ── ③ 유실을 숨기지 않는다 ─────────────────────────────────────────────
broken = write(WORK, "c.ndjson", """# user=9
# ---
{"seq":1,"t":0,"type":"session_start","role":"client","session":"S3"}
{"seq":5,"t":1,"type":"phase","name":"play"}
{"seq":6,"t":2,"type":"session_end","resu
""")
r3 = S.read_file(broken)
check(r3 is not None, "깨진 줄이 있다고 판을 통째로 버렸다")
if r3:
    check(r3["broken_lines"] == 1, f"깨진 줄을 안 셌다: {r3['broken_lines']}")
    check(r3["seq_gaps"], f"빠진 seq 를 안 셌다: {r3['seq_gaps']}")
    check(r3["ended"] is False, "깨진 끝 줄을 끝난 것으로 읽는다")

# ── ④ 없는 값과 0 을 가른다 ────────────────────────────────────────────
#
#     `floors` 가 없는 것과 `[0,0,0,0]`(아무것도 안 쌓음)은 **다른 사실**이다.
#     뭉치면 "아무도 조립을 안 한다" 는 결론이 데이터 없이도 나온다.
check(S.floor_reach(None) is None, "없는 floors 를 0층으로 읽는다")
check(S.floor_reach([0, 0, 0, 0]) == 0, "빈 탑을 못 읽는다")
check(S.floor_reach([2, 1, 0, 0]) == 2, "쌓은 층 수를 잘못 센다")
check(S.floor_reach("2,1") is None, "리스트가 아닌 것을 세려 한다")

# ── ⑤ 채널을 가른다 (실측이 시킨 것) ───────────────────────────────────
#
#     2026-09-21 배포 서버 실측: 판 6건 중 **5건이 팀원 로컬 빌드**였다.
#     `docs/data-pipeline.md` §11 이 경고한 섞임이 실제로 일어나고 있다.
for build, want in (("v1.2.0", "release"), ("v0.1.0", "release"),
                    ("qa-461-1a2b3c4", "qa"), ("dev-20260920-1a2b3c4", "dev"),
                    ("2026-09-20-23-30-9-codex-ai-team-slots-local", "local"),
                    ("", "(모름)"), (None, "(모름)")):
    got = S.channel_of({"uploaded_build": build})
    check(got == want, f"{build!r} 를 {got} 로 읽는다(기대 {want})")

#     **`v` 로 시작한다고 다 릴리스가 아니다** — 브랜치 이름이 `v` 로 시작할 수 있다.
check(S.channel_of({"uploaded_build": "verify-branch"}) == "local",
      "브랜치 이름을 릴리스로 읽는다")

# ── ⑥ 폴더를 훑고 시각순으로 준다 ──────────────────────────────────────
rows = S.collect([WORK])
check(len(rows) == 3, f"폴더에서 {len(rows)}건만 찾았다(기대 3)")
check(all(isinstance(x, dict) for x in rows), rows)
#     `.ndjson` 이 아닌 것은 안 읽는다 — 같은 폴더에 평문 로그가 함께 있다.
write(WORK, "d.log", "이건 평문 로그다\n")
check(len(S.collect([WORK])) == 3, "평문 로그를 판으로 읽었다")

if fails:
    print()
    for f in fails:
        print("❌ " + f)
    raise SystemExit(1)

print("  머리줄/본문 빌드 구분 · 끝 줄 없음 · 깨진 줄·seq 유실 · 없는 값 ≠ 0 ✅")
print("  채널 5종을 가른다 — 배포 서버에 섞여 들어오는 로컬 빌드를 걸러 낸다 ✅")
