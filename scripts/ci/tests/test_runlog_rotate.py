"""판이 끝나면 그 판의 로그가 **올라갈 수 있는 상태**가 되는가 (S15P21B202-685).

실행: python scripts/ci/tests/test_runlog_rotate.py
Unity 없이 저장소의 C# 소스를 읽는다.

**왜 글자로 검사하나.** `RunLog` 는 `Application.isEditor` 면 아무것도 하지 않는다 —
에디터의 `dataPath` 가 저장소 안이라 리포에 `METAGAME/logs/` 가 생기기 때문이다. 그래서
회전의 **파일 효과는 EditMode 검사로 볼 수 없다.** 볼 수 있는 것은 배선이고, 이 결함은
실제로 배선에 있었다 — 기능은 다 있는데 이어져 있지 않았다.

**무엇이 틀렸었나.** `-607` 이 "판이 끝났으니 보낸다"(`RunLogUpload.Request`)를 만들고
`PlayObserver` 가 그것을 부르고 있었다. 그런데 쓸이(`Pending`)는 **지금 쓰고 있는
파일을 일부러 제외**한다(아직 쓰는 중인 파일을 보내면 꼬리가 잘린다). 그래서 보내지는
것은 닫힌 이벤트 파일뿐이고 **런로그는 다음 실행까지 남았다.** 2026-09-22 QA 에서 가장
보고 싶은 세 판이 전부 로컬에만 있어 사람이 손으로 옮겼다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

RUNTIME = os.path.join(ROOT, "METAGAME", "Assets", "_Project", "Scripts", "Runtime")
RUNLOG = os.path.join(RUNTIME, "Core", "RunLog.cs")
UPLOAD = os.path.join(RUNTIME, "Core", "RunLogUpload.cs")
OBSERVER = os.path.join(RUNTIME, "Bootstrap", "PlayObserver.cs")

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def read(path):
    with io.open(path, encoding="utf-8") as f:
        return f.read()


def body(src, signature):
    """중괄호 짝을 세어 그 메서드 본문만 떼어 온다.

    정규식으로 `}` 까지 훑지 않는다 — 안쪽 블록의 닫는 괄호에서 멈춘다.
    """
    at = src.index(signature)
    start = src.index("{", at)
    depth = 0
    for i in range(start, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return src[start:i + 1]
    raise AssertionError("본문의 끝을 찾지 못했다: " + signature)


LOG = read(RUNLOG)
UP = read(UPLOAD)
OB = read(OBSERVER)

print()
print("== 판이 끝나면 로그가 보낼 수 있는 상태가 된다 (S15P21B202-685) ==")


# ── ① 판이 끝났다고 알리는 자리가 **런로그를 닫는다**
req = body(UP, "public static void Request()")
check("RunLog.Rotate()" in req,
      "Request() 가 RunLog.Rotate() 를 부르지 않는다 — 방금 한 판의 로그가 "
      "다음 실행까지 올라가지 않는다(Pending 이 쓰는 중인 파일을 제외한다)")

#     **듣는 쪽이 없어도 닫아야 한다.** 여기서 먼저 돌아나가면 로그인 전에 끝난 판은
#     영영 열린 파일로 남고, 다음 쓸이도 그것을 제외한다.
i_rot = req.find("RunLog.Rotate()")
i_ret = req.find("if (handler == null) return;")
check(0 <= i_rot < i_ret,
      "듣는 쪽이 없으면 돌아나가는 줄보다 Rotate() 가 뒤에 있다 — 로그인 전에 끝난 "
      "판은 열린 파일로 남는다")
print("  판이 끝나면 런로그를 닫는다 · 듣는 쪽이 없어도 닫는다 ✅")


# ── ② **앱 종료 경로에서는 부르지 않는다** — 거기서 시작한 전송은 끝나지 못한다
#     `-607` 이 이미 정한 것이고 `if (sweep)` 이 그것을 지킨다. 회전을 `Request()`
#     안에 둔 덕에 그 가드가 회전에도 그대로 적용된다 — 따로 지킬 것이 늘지 않았다.
check("if (sweep) RunLogUpload.Request();" in OB,
      "PlayObserver 가 sweep 가드 없이 Request() 를 부른다 — 종료 경로에서 전송을 "
      "시작하면 끝나지 못한다")
print("  종료 경로에서는 부르지 않는다 (sweep 가드) ✅")


# ── ③ 회전은 **로그 훅을 떼지 않는다**
#     `Stop()` 은 끝내는 길이라 `logMessageReceivedThreaded` 를 뗀다. 회전이 그것을
#     따라 하면 **판이 막 끝난 직후의 정리 로그가 사라진다** — 그때가 줄이 가장 많다.
rot = body(LOG, "internal static string Rotate()")
check("logMessageReceivedThreaded" not in rot,
      "Rotate() 가 로그 훅을 건드린다 — 떼었다 붙이는 사이의 줄이 사라진다")
check("writer = null" in rot and "new StreamWriter" in rot,
      "Rotate() 가 닫고 다시 열지 않는다")
print("  회전은 로그 훅을 건드리지 않는다 ✅")


# ── ④ 닫은 파일을 **지우지 않는다** — 같은 초에 두 번 회전하면 이름이 겹친다
#     `new StreamWriter(path, false)` 는 있는 파일을 자른다. 겹친 이름으로 다시 열면
#     **보내기도 전에 앞 판이 사라진다.**
check("NextFreePath" in rot,
      "Rotate() 가 이름 겹침을 피하지 않는다 — 같은 초에 두 번이면 앞 판을 지운다")
nfp = body(LOG, "public static string NextFreePath(string folder, DateTime when)")
check("File.Exists" in nfp, "NextFreePath 가 있는 파일을 확인하지 않는다")
check(re.search(r"for\s*\(", nfp) is not None,
      "NextFreePath 가 한 번만 비켜 본다 — 세 번째 회전에서 또 겹친다")
print("  같은 초에 두 번 회전해도 앞 판을 지우지 않는다 ✅")


# ── ⑤ 실패를 **조용히 넘기지 않는다**
#     새 파일을 못 열면 이 판 뒤의 로그가 아무 데도 안 남는다. 그 사실은 닫은 파일에
#     들어갈 수 없으므로 화면 로그로라도 남아야 한다.
check("Debug.LogWarning" in rot,
      "새 파일을 못 열었을 때 아무 말도 하지 않는다 — 그 뒤의 로그가 조용히 사라진다")
print("  새 파일을 못 열면 그 사실을 남긴다 ✅")


# ── ⑥ 에디터에서는 돌지 않는다 (리포에 `METAGAME/logs/` 를 만들지 않는다)
check("Application.isEditor" in rot,
      "Rotate() 에 에디터 가드가 없다 — 검사가 돌 때 저장소 안에 로그 폴더가 생긴다")
print("  에디터에서는 돌지 않는다 ✅")


if fails:
    print(f"\n❌ 실패 {len(fails)}건")
    for f in fails:
        print(f"  · {f}")
    raise SystemExit(1)
print("TEST685_ROTATE OK")
