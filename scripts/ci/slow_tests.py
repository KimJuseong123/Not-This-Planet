"""느린 테스트를 **재서 남기고**, MR 에서 **그 목록으로 뺀다** (S15P21B202-617 · -701).

실행:
    python scripts/ci/slow_tests.py measure <unity-results.xml> <내보낼 폴더>
    python scripts/ci/slow_tests.py apply   <내보낼 폴더>
    python scripts/ci/slow_tests.py verify  <unity-results.xml> <폴더>

**왜 재는가.** `-602` 가 느린 21건에 `[Category("Heavy")]` 를 손으로 붙여 MR 에서
뺐다. **하루 만에 낡았다** — 다음 날 5초 이상이 59건이 되어 MR 검사가 6분 → 13분
32초가 됐고, `-611` 이 59건을 다시 붙여 급한 불을 껐다. 그리고 2026-09-21 실측에서
같은 일이 또 있었다:

    테스트 1988건 · 합계 197.1초
    5초 이상 8건 · 119.9초 = 전체의 60.8%   ← 그 8건 **전부** Heavy 표시가 없다

표시를 붙이는 것이 사람 일인 한 이 상태는 반복된다. **그래서 사람에게 묻지 않고
매 전체 실행이 스스로 재게 한다.**

`-617` 은 **재는 쪽**만 했다. 읽는 쪽이 없어 그 뒤로도 사람이 두 번 더 손으로 붙였다
(`-611` · `-731`). `-701` 이 읽는 쪽을 붙인다 — `measure` 가 내놓고 `apply` 가 받아
`-testFilter` 로 빼고 `verify` 가 **정말 돌았는지** 본다.

**틀리는 방향이 둘이고 위험이 다르다.**

  · 빼면 안 되는 것을 뺀다 → **검사 안 한 코드가 초록으로 지나간다** (치명적)
  · 빼도 되는 것을 안 뺀다 → MR 이 느리다 (아깝지만 안전하다)

그래서 목록을 못 받으면 **아무것도 쓰지 않고 전부 돌린다**(안전한 쪽). 대신 그
사실을 로그에 남긴다 — 안 남기면 "왜 13분이지" 를 가릴 수 없다. 그리고 거른 뒤에는
`verify` 가 남은 수를 견주어 **0건 실행에 초록**을 잡는다.

**왜 `-testFilter` 인가.** Unity 에는 이름 목록으로
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


#: 하룻밤에 **새로 더할 수 있는 수**. 집합이 한 번에 뛰지 않게 한다.
#:
#: 없으면 하루 사이에 수십 건이 통째로 MR 에서 사라질 수 있다 — `-602` 다음 날
#: 5초 이상이 21 → 59건이 된 적이 있다. 그때 무엇이 왜 빠졌는지 아무도 못 봤다.
#: 넘치면 **그 사실을 남긴다**(조용히 자르지 않는다).
MAX_NEW_PER_RUN = 10

#: 목록 파일의 첫 줄. 사람이 열었을 때 **언제 잰 것인지** 알아야 한다.
HEADER = "# 전체 실행에서 잰 느린 테스트 (S15P21B202-617 · -701)" + chr(10)


def names_from_list(text: str) -> list[str]:
    """`unity-slow.txt` 를 이름 목록으로 되돌린다. 머리말(`#`)과 빈 줄은 건너뛴다."""
    out = []
    for line in (text or "").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        out.append(line.split(chr(9))[-1].strip())
    return [n for n in out if n]


def merge_monotone(previous, measured, cap: int = MAX_NEW_PER_RUN):
    """**집합은 한 방향으로만 자란다** (S15P21B202-701).

    매일 그날 느린 것만 빼면, 어제 느렸던 테스트가 오늘 4.9초라고 MR 에 돌아온다 —
    시간이 들쭉날쭉해지고, 더 나쁘게는 **한 번 빠진 테스트가 왜 빠졌는지 아무도
    모르게 된다.** 그래서 새 집합이 반드시 이전 집합을 포함한다.

    `measured` 는 (초, 이름) 이고 느린 것부터다. 새로 더하는 것은 `cap` 까지만
    고르고, 못 고른 것은 **돌려준다** — 부르는 쪽이 그것을 로그에 남긴다.

    순수 함수다. 검사가 여기를 본다.
    """
    kept = list(dict.fromkeys(previous or []))
    known = set(kept)
    fresh = [n for _s, n in measured if n not in known]
    added, left = fresh[:max(0, cap)], fresh[max(0, cap):]
    return sorted(known | set(added)), left


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

    # **이전 집합을 포함한다** (S15P21B202-701). 그날 느린 것만 빼면 어제 느렸던
    # 테스트가 오늘 4.9초라고 MR 로 돌아온다 — 시간이 들쭉날쭉해지고, 무엇이 왜
    # 빠졌는지 아무도 모르게 된다. 이전 목록을 못 읽으면 **이번 것만** 쓴다(줄어드는
    # 쪽이지만 조용하지는 않다 — 아래 로그가 그 사실을 말한다).
    gl = _gitlab()
    job = find_published(gl) if gl is not None else None
    before = names_from_list(gl.job_artifact_text(job.get("id"), LIST_FILE) or "") if job else []
    if job is None:
        print("이전 목록을 못 찾았다 — 이번에 잰 것만 쓴다")
    names, left = merge_monotone(before, slow)
    if left:
        print(f"상한({MAX_NEW_PER_RUN}건)에 걸려 이번에 안 더한 것 {len(left)}건 — 다음 실행에서 더한다")
        for n in left[:5]:
            print("  " + n)

    seconds = {n: s for s, n in slow}
    remain = total - sum(1 for n in names if n in seconds)

    write(os.path.join(out_dir, FILTER_FILE), exclude_regex(names))
    write(os.path.join(out_dir, EXPECT_FILE), f"{remain}\n")
    write(os.path.join(out_dir, LIST_FILE),
          HEADER + "".join(f"{seconds.get(n, 0.0):8.2f}\t{n}\n" for n in names))

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


def _gitlab():
    """API 를 쓸 수 있으면 핸들, 아니면 None. **없다고 실패시키지 않는다.**"""
    try:
        from ci_common import GitLab
    except Exception:  # noqa: BLE001
        return None
    gl = GitLab()
    return gl if (gl.token and gl.project) else None


#: 목록을 찾으러 거슬러 볼 잡의 수. `unity_reuse.MAX_KEY_LOOKUPS` 와 같은 결이다.
MAX_LIST_LOOKUPS = 40


def find_published(gl, job_name: str = "unity-compile"):
    """목록을 가진 **가장 최근** 잡. 없으면 None.

    전체 실행(새벽)이 만들고, MR 실행은 그것을 그대로 실어 나른다 — 어느 쪽을
    집어도 내용이 같으므로 출처를 따지지 않는다. **비어 있는 목록은 싣지 않으므로**
    (`cmd_apply` 가 못 찾으면 아무 파일도 안 쓴다) 빈 것이 최신으로 남지 않는다.
    """
    try:
        jobs = gl.project_jobs()
    except Exception as e:  # noqa: BLE001
        print("[warn] 잡 목록 조회 실패(%s: %s)" % (type(e).__name__, e))
        return None
    seen = 0
    for j in sorted(jobs, key=lambda x: -(x.get("id") or 0)):
        if j.get("name") != job_name or j.get("status") not in ("success", "failed"):
            continue
        seen += 1
        if seen > MAX_LIST_LOOKUPS:
            break
        text = gl.job_artifact_text(j.get("id"), FILTER_FILE)
        if text and text.strip():
            return j
    return None


def cmd_apply(out_dir: str) -> int:
    """**전체 실행이 남긴 목록을 가져온다.** 못 가져오면 아무것도 쓰지 않는다.

    쓰지 않는 것이 곧 "전부 돌린다" 다 — 받는 쪽(`.gitlab-ci.yml`)이 파일이 없으면
    거르지 않는다. **빼는 것을 실패로 만들지 않는다**: 목록이 없다고 검사를 멈추면
    그날 아무도 머지하지 못한다. 다만 **그 사실은 남긴다** — 안 남기면 "왜 13분이지"
    를 가릴 수 없다.

    **빈 목록을 쓰지 않는 것도 중요하다.** 쓰면 그것이 아티팩트로 실려 다음 실행의
    "가장 최근 목록" 이 되고, 새벽이 한 번 더 돌 때까지 아무도 거르지 못한다.
    """
    gl = _gitlab()
    if gl is None:
        print("목록을 받지 못했다: API 토큰/프로젝트가 없다 — 전부 돌린다")
        return 0
    job = find_published(gl)
    if job is None:
        print("목록을 받지 못했다: 목록을 가진 잡을 최근 %d개에서 못 찾았다 — 전부 돌린다"
              % MAX_LIST_LOOKUPS)
        return 0
    got = {}
    for name in (FILTER_FILE, LIST_FILE, EXPECT_FILE):
        text = gl.job_artifact_text(job.get("id"), name)
        if text is not None:
            got[name] = text
    if FILTER_FILE not in got or not got[FILTER_FILE].strip():
        print("목록을 받지 못했다: 잡 %s 의 %s 가 비었다 — 전부 돌린다"
              % (job.get("id"), FILTER_FILE))
        return 0
    for name, text in got.items():
        write(os.path.join(out_dir, name), text)
    names = names_from_list(got.get(LIST_FILE, ""))
    print("잡 %s 의 목록을 쓴다 — %d건을 MR 에서 뺀다 (남을 것 %s건)"
          % (job.get("id"), len(names), (got.get(EXPECT_FILE) or "?").strip() or "?"))
    for n in names[:10]:
        print("  " + n)
    if len(names) > 10:
        print("  … 그 밖 %d건은 %s 에 있다" % (len(names) - 10, LIST_FILE))
    return 0


#: 실제로 돈 수가 예상의 이 비율보다 낮으면 **정규식이 넘치게 걸렀다** 로 본다.
#:
#: 정확히 같을 수는 없다 — 잰 뒤로 테스트가 늘고 줄기 때문이다. 그래서 "몇 건
#: 다르다" 가 아니라 **"절반도 안 돈다"** 를 잡는다. 이 방식에서 가장 위험한 실패는
#: 0건 실행에 초록이 뜨는 것이고, 그것은 이 선에 확실히 걸린다.
MIN_RATIO = 0.5


def enough(ran: int, expect: int, ratio: float = MIN_RATIO) -> bool:
    """돈 수가 말이 되는가. **순수 함수** — 검사가 여기를 본다."""
    if expect <= 0:
        return True
    return ran >= int(expect * ratio)


def cmd_verify(results_path: str, out_dir: str) -> int:
    """거르고 나서 **정말 돌았는지** 본다. 안 돌았으면 1 을 준다.

    거르는 방식에서 가장 위험한 실패는 **정규식이 다 걸러 0건이 돌고 초록이 뜨는
    것**이다. 잰 쪽이 남겨 둔 예상 수와 견주어 그것을 잡는다.
    """
    expect_path = os.path.join(out_dir, EXPECT_FILE)
    if not os.path.exists(expect_path):
        print("거르지 않았다 — 확인할 것이 없다")
        return 0
    try:
        with open(expect_path, encoding="utf-8") as f:
            expect = int((f.read() or "0").strip() or 0)
    except (OSError, ValueError):
        print("[warn] 예상 수를 읽지 못했다 — 확인을 건너뛴다")
        return 0
    try:
        with open(results_path, encoding="utf-8", errors="replace") as f:
            ran = len((parse_nunit(f.read()).get("cases") or []))
    except Exception as e:  # noqa: BLE001
        print("[warn] 결과를 읽지 못했다(%s) — 확인을 건너뛴다" % type(e).__name__)
        return 0
    if enough(ran, expect):
        print("거른 뒤 %d건이 돌았다 (예상 %d건 이상) ✅" % (ran, expect))
        return 0
    print("❌ 거른 뒤 %d건만 돌았다 — 예상은 %d건이다. 정규식이 넘치게 걸렀을 수 있다."
          % (ran, expect))
    print("   %s 를 지우고 다시 돌리면 전부 돈다." % FILTER_FILE)
    return 1


def main(argv: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if len(args) == 3 and args[0] == "measure":
        return cmd_measure(args[1], args[2])
    if len(args) == 2 and args[0] == "apply":
        return cmd_apply(args[1])
    if len(args) == 3 and args[0] == "verify":
        return cmd_verify(args[1], args[2])
    print(__doc__)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
