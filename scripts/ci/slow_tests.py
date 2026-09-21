"""느린 테스트를 **재서** 목록으로 남긴다 (S15P21B202-617).

실행:
    python scripts/ci/slow_tests.py measure <unity-results.xml> <내보낼 폴더>

**왜 재는가.** `-602` 가 느린 21건에 `[Category("Heavy")]` 를 손으로 붙여 MR 에서
뺐다. **하루 만에 낡았다** — 다음 날 5초 이상이 59건이 되어 MR 검사가 6분 → 13분
32초가 됐고, `-611` 이 59건을 다시 붙여 급한 불을 껐다. 그리고 2026-09-21 실측에서
같은 일이 또 있었다:

    테스트 1988건 · 합계 197.1초
    5초 이상 8건 · 119.9초 = 전체의 60.8%   ← 그 8건 **전부** Heavy 표시가 없다

표시를 붙이는 것이 사람 일인 한 이 상태는 반복된다. **그래서 사람에게 묻지 않고
매 전체 실행이 스스로 재게 한다.**

**여기서는 빼지 않는다.** 이 단계는 *생산자*다 — 전체 실행이 목록을 내놓고, 그것을
받아 `-testFilter` 로 빼는 것은 다음 몫이다. 순서가 이래야 하는 이유는 단순하다:
**전체 실행이 한 번 재기 전에는 뺄 목록이 없다.**

**왜 `-testFilter` 인가**(다음 단계의 근거를 여기 적어 둔다). Unity 에는 이름 목록으로
빼는 인자가 없다. 그런데 `-testFilter` 는 이름을 **정규식으로** 해석하므로
(`unity_report.testfilter_value` 가 이미 그 전제로 이스케이프한다) 부정 전방탐색
한 줄이면 제외가 된다. 세미콜론을 쓰지 않으므로 "구분자가 이 Unity 버전에서 통하나"
라는 물음도 피한다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.stdout.reconfigure(encoding="utf-8")

from unity_report import parse_nunit  # noqa: E402

#: 이 시간을 넘으면 "느리다" 로 본다(초).
#:
#: 2026-09-21 실측에서 5초 기준이 **8건으로 테스트 시간의 61%** 를 가져간다.
#: 2초로 내리면 14건 70%, 1초면 26건 77% 로 **얻는 것이 급히 줄면서 "MR 에서 안 도는
#: 테스트" 만 늘어난다.** 그래서 5초에서 시작하고, 실제 실행을 보고 옮긴다.
THRESHOLD_SECONDS = 5.0

#: `-testFilter` 에 그대로 넣는 값(정규식 한 줄). 비어 있으면 **전부 돌린다.**
FILTER_FILE = "unity-slow-filter.txt"

#: 사람이 읽는 목록. 무엇이 왜 빠지는지가 코드에 안 보이는 것이 이 방식의 대가라,
#: 그 대가를 갚는 자리다 — 잡 로그와 MR 코멘트가 이 파일을 싣는다.
LIST_FILE = "unity-slow.txt"

#: 거르고 나면 몇 건이 남아야 하는가. 받는 쪽이 **"정규식이 망가져 0건 실행에 초록"**
#: 을 잡는 데 쓴다 — 이 방식에서 가장 위험한 실패 모드다.
EXPECT_FILE = "unity-slow-expect.txt"


def slow_cases(cases, threshold: float = THRESHOLD_SECONDS) -> list[tuple[float, str]]:
    """(초, 전체이름) 목록. 느린 것부터.

    **이름은 `클래스.테스트` 다** — `unity_report.failed_names` 와 같은 모양이어야
    한다. 그쪽이 만든 값을 `-testFilter` 가 이미 받고 있으므로, 다르게 만들면
    한쪽만 매칭된다.
    """
    out = []
    for c in cases:
        try:
            secs = float(c.get("time") or 0.0)
        except (TypeError, ValueError):
            # 시간을 못 읽은 것을 0 으로 보면 **느린데 안 빠진다**. 그래도 여기서는
            # 빼는 것이 아니라 재는 것이므로, 모르는 것은 빼지 않고 넘긴다.
            continue
        if secs >= threshold:
            out.append((secs, f'{c["cls"]}.{c["name"]}'))
    out.sort(key=lambda t: -t[0])
    return out


def exclude_regex(names) -> str:
    """이 이름들만 빼는 정규식. 이름이 없으면 빈 문자열(= 전부 돌린다).

    **끝을 `$` 로 묶는다.** `^(?!.*(?:이름)).*` 처럼 담기만 하면 `Foo` 를 뺄 때
    `FooBar` 까지 빠진다 — 파라미터화 이름(`Foo("a")`)이 있는 우리 테스트에서는
    실제로 일어나는 일이다. 정확히 그 이름일 때만 뺀다.

    이스케이프는 `unity_report.testfilter_value` 와 같은 이유로 한다 — `Load(1,2)`
    같은 이름을 그대로 넣으면 정규식으로 해석돼 엉뚱한 것을 잡는다.
    """
    names = sorted(set(names))
    if not names:
        return ""
    return "^(?!(?:" + "|".join(re.escape(n) for n in names) + ")$).*$"


def write(path: str, text: str) -> None:
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def cmd_measure(results_path: str, out_dir: str) -> int:
    """전체 실행의 결과에서 느린 것을 골라 세 파일로 남긴다."""
    try:
        with open(results_path, encoding="utf-8", errors="replace") as f:
            data = parse_nunit(f.read())
    except Exception as e:  # noqa: BLE001
        # **재지 못한 것은 빈 목록이 아니다.** 빈 목록을 남기면 받는 쪽이 그것을
        # "뺄 것이 없다" 로 읽고 전부 돌린다 — 느리지만 안전하다. 그래서 여기서는
        # 아무 파일도 쓰지 않고 끝낸다(앞 실행의 목록이 남아 있으면 그것이 쓰인다).
        print(f"[warn] 결과를 읽지 못했다({type(e).__name__}: {e}) — 목록을 남기지 않는다")
        return 0

    cases = data.get("cases") or []
    slow = slow_cases(cases)
    total = len(cases)
    remain = total - len(slow)

    write(os.path.join(out_dir, FILTER_FILE), exclude_regex(n for _s, n in slow))
    write(os.path.join(out_dir, EXPECT_FILE), f"{remain}\n")
    write(os.path.join(out_dir, LIST_FILE),
          "".join(f"{s:8.2f}\t{n}\n" for s, n in slow))

    spent = sum(s for s, _n in slow)
    whole = sum(float(c.get("time") or 0) for c in cases
                if str(c.get("time") or "").replace(".", "", 1).isdigit())
    share = (100.0 * spent / whole) if whole else 0.0
    print(f"느린 테스트 {len(slow)}건 · {spent:.1f}초 "
          f"(전체 {total}건 · {whole:.1f}초의 {share:.1f}%) · 기준 {THRESHOLD_SECONDS:g}초")
    for s, n in slow[:10]:
        print(f"  {s:8.2f}초  {n}")
    if len(slow) > 10:
        print(f"  … 그 밖 {len(slow) - 10}건은 {LIST_FILE} 에 있다")
    print(f"거르면 {remain}건이 남는다 — 받는 쪽이 이 수로 '정규식이 다 걸러 버렸다' 를 잡는다")
    return 0


def main(argv: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if len(args) == 3 and args[0] == "measure":
        return cmd_measure(args[1], args[2])
    print(__doc__)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
