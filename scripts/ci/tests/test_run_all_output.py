"""시험이 실패하면 잡 로그 한 곳에서 이유가 보이는가 (S15P21B202-672).

실행: python scripts/ci/tests/test_run_all_output.py

**무엇을 막나.** `run_all.py` 가 실패한 시험의 출력을 stdout·stderr 각각 **마지막 6줄**만
찍었다. `assert ..., 카드본문` 꼴이면 `AssertionError:` 줄이 창 밖으로 밀려, 새벽 `ci-tests`
가 나흘 연속 같은 빨강인데도 어느 단정이 깨졌는지 알 수 없었다(파이프라인 204281 ~ 210651).

**`assert` 를 쓰지 않는다**(`python -O`).
"""
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import run_all  # noqa: E402

failed = 0


def check(ok, msg):
    global failed
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if not ok:
        failed += 1


# 2026-09-18 새벽 로그의 모양: 단정 줄 뒤에 카드 본문 8줄이 붙는다
card = "\n".join(["성공 카드 ✅", "| 항목 | 내용 |", "|---|---|", "| 커밋 | `deadbeef` |",
                  "| 결과 | 헬스체크 실패 |", "| 지금 서버 | 직전 jar |", "| 볼 곳 | 잡 로그 |", "끝"])
stderr = ("Traceback (most recent call last):\n  File \"test_deploy_notify.py\", line 88, in <module>\n"
          "AssertionError: 롤백 카드가 '직전 jar' 를 말하지 않는다\n" + card)
report = run_all.failure_report("  ✅ 앞의 검사\n  ❌ 롤백 블록의 셋째 단정", stderr)
text = "\n".join(report)

check("AssertionError: 롤백 카드가 '직전 jar' 를 말하지 않는다" in text,
      "단정 줄 뒤에 긴 본문이 붙어도 AssertionError 줄이 남는다")
check("❌ 롤백 블록의 셋째 단정" in text, "stdout 의 ❌ 줄이 남는다")
check(report.index("── 이유 ──") < report.index("── stderr ──"), "이유가 원문보다 먼저 나온다")
check("| 결과 | 헬스체크 실패 |" in text, "원문(카드 본문)도 그대로 남는다")

long_out = "\n".join(f"줄 {i}" for i in range(500))
long_report = run_all.failure_report(long_out, "", limit=50)
check("(앞 450줄 생략 · 전체 500줄)" in long_report, "잘랐으면 몇 줄을 잘랐는지 적는다")
check("줄 499" in long_report and "줄 449" not in long_report, "꼬리 50줄을 싣는다")
check(run_all.failure_report("", "") == [], "출력이 없으면 아무것도 싣지 않는다")
check(run_all.TAIL_LINES >= 100, f"꼬리 길이가 충분하다 ({run_all.TAIL_LINES}줄) — 6줄로 되돌아가지 않는다")

src = open(os.path.join(HERE, "run_all.py"), encoding="utf-8").read()
check("failure_report(r.stdout, r.stderr)" in src and "[-6:]" not in src,
      "run_all 이 실패 출력을 failure_report 로 싣는다")

print(f"\ntest_run_all_output: {'통과' if not failed else f'실패 {failed}'}")
sys.exit(1 if failed else 0)
