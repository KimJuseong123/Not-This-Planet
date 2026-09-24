"""느린 테스트를 **재서 남기고**, MR 에서 **그 목록을 건너뛴다** (S15P21B202-617 · -701 · -739).

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
(`-611` · `-731`). `-701` 이 읽는 쪽을 붙였는데 **하나도 빼지 못했다**(`-739`).

**왜 `-testFilter` 가 아닌가** (S15P21B202-739). `-701` 은 부정 전방탐색 한 줄
(`^(?!(?:이름|…)$).*$`)을 `-testFilter` 로 넘겼다. Unity(UTF 1.7.0)는 그 값을 **포함**
필터로 쓰고, NUnit 의 통과 규칙은 `Match || MatchParent` — **조상 하나만 맞아도 돈다.**
픽스처·네임스페이스·어셈블리 이름과 루트의 빈 이름 `""` 은 목록에 없으니 전방탐색에
늘 맞았고, 그래서 모든 테스트가 조상 덕에 돌았다. 2026-09-23 에 그 필터를 쓴 잡 5건이
뺀 것은 **0건**이다 — 목록 100건 중 99건은 Heavy 로 이미 빠져 있었고, 남은 1건
`Stage2DemoTests.YardOptOutRetainsUnderlyingGameplayAndSessionInput` 은 5건 모두 돌았다.
그동안 `verify` 는 "절반은 돌았나" 만 봐서 **아무것도 안 빠진 것**을 잡지 못했다.

그래서 `-testSettingsFile` 의 `ignoreTests` 를 쓴다:

  · **잎 이름과 글자 그대로 같을 때만** 건너뛴다. 낡은 이름은 아무것도 못 뺄 뿐이고
    엉뚱한 하위 트리를 지우지 못한다(`!` 부정 필터는 조상 이름과 겹치면 통째로 지운다)
  · 건너뛴 것이 결과 XML 에 `Skipped` · 사유 `slow-list …` 로 **남는다** — `verify` 가
    "빠졌어야 할 것이 돌았다" 와 "목록에 없는 것이 빠졌다" 를 **이름으로** 가린다
  · 명령줄에는 경로만 간다 — 길이 제한도, `;` 로 쪼개지는 일도 없다
  · 도메인 리로드 뒤에도 유지된다(6000.5.4f1 프로브: 리로드 두 번 뒤의 테스트까지 건너뜀)

**틀리는 방향이 둘이고 위험이 다르다.**

  · 빼면 안 되는 것을 뺀다 → **검사 안 한 코드가 초록으로 지나간다** (치명적)
  · 빼도 되는 것을 안 뺀다 → MR 이 느리다 (아깝지만 안전하다)

그래서 목록을 못 받으면 **아무것도 쓰지 않고 전부 돌린다**(안전한 쪽). 대신 그
사실을 로그에 남긴다 — 안 남기면 "왜 13분이지" 를 가릴 수 없다.
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.stdout.reconfigure(encoding="utf-8")

#: 이 시간을 넘으면 "느리다" 로 본다(초).
#:
#: 2026-09-21 실측에서 5초 기준이 **8건으로 테스트 시간의 61%** 를 가져간다.
#: 2초로 내리면 14건 70%, 1초면 26건 77% 로 **얻는 것이 급히 줄면서 "MR 에서 안 도는
#: 테스트" 만 늘어난다.** 그래서 5초에서 시작하고, 실제 실행을 보고 옮긴다.
THRESHOLD_SECONDS = 5.0

#: 사람이 읽는 목록이자 **다음 실행이 받아 가는 유일한 파일.** 무엇이 왜 빠지는지가
#: 코드에 안 보이는 것이 이 방식의 대가라, 그 대가를 갚는 자리다.
#:
#: 남을 수(`expect`)와 이름 수(`listed`)를 **이 파일의 머리줄에 함께 싣는다.** 예전에는
#: 남을 수를 `unity-slow-expect.txt` 에 따로 두었는데 `2413` 같은 값이 `ci_common.http`
#: 의 `json.loads` 를 지나며 **숫자가 되어** `job_artifact_text` 가 None 을 줬다 — 그래서
#: 잡 로그가 "(남을 것 ?건)" 을 찍었다. 파일이 둘이면 둘 다 같은 잡에서 온전히 받아야
#: 하고, 하나만 받으면 "걸러 놓고 확인은 못 하는" 반쪽 상태가 된다. 한 파일이면 받거나
#: 못 받거나 둘뿐이다.
LIST_FILE = "unity-slow.txt"

#: `-testSettingsFile` 로 넘기는 파일. `apply` 가 쓰고 **되읽어 확인한 뒤에만** 남긴다.
SETTINGS_FILE = "unity-slow-settings.json"

#: `apply` 가 마지막에 쓰는 표시. 잡은 설정 파일과 **이것이 함께 있을 때만** 넘기고,
#: `verify` 는 이것으로 "이번 실행이 건너뛰었나" 를 안다.
APPLIED_FILE = "unity-slow-applied.txt"

#: 이 모듈이 남기는 파일 전부 — `.gitlab-ci.yml` 의 artifacts 에 다 있어야 한다.
PUBLISHED = (LIST_FILE, SETTINGS_FILE, APPLIED_FILE)

#: Unity 가 이 이름으로 로그를 쓴다(`-logFile`). `verify` 가 실행 설정 덤프를 읽는다.
LOG_FILE = "unity.log"

#: 이 파일이 있으면 Unity 를 돌리지 않고 **다른 잡의 결과를 그대로 썼다**(`unity_reuse`).
REUSED_FILE = "unity-reused.txt"

#: `ignoreComment` 의 머리. 결과 XML 의 `<reason><message>` 에 그대로 남으므로
#: `verify` 가 "우리가 건너뛴 것" 을 다른 이유(`[Ignore]`·`[Explicit]`)와 가를 수 있다.
MARK = "slow-list"

#: 하룻밤에 **새로 더할 수 있는 수**. 집합이 한 번에 뛰지 않게 한다.
#:
#: 없으면 하루 사이에 수십 건이 통째로 MR 에서 사라질 수 있다 — `-602` 다음 날
#: 5초 이상이 21 → 59건이 된 적이 있다. 그때 무엇이 왜 빠졌는지 아무도 못 봤다.
#: 넘치면 **그 사실을 남긴다**(조용히 자르지 않는다).
MAX_NEW_PER_RUN = 10

#: 한 실행에서 건너뛸 수 있는 **상한.** 넘치면 느린 것부터 이만큼만 건너뛰고 나머지는
#: 돌린다(안전한 쪽). 하루 10건씩 자라므로 40일은 걸려야 닿는 수라, 닿았다면 무언가
#: 이상한 것이다 — 로그에 남은 이름을 보고 기준을 다시 본다.
MAX_LISTED = 400

#: `-testCategory "!Heavy"` 가 이미 빼는 표시. Unity 는 카테고리를 **정규식으로**
#: 견주므로(`CategoryFilterExtended`, IsRegex) 이 글자를 **품은** 카테고리가 다 빠진다 —
#: 여기서도 같은 규칙으로 본다. 어긋나면 이미 빠지는 것을 목록에 넣거나, 그 반대가 된다.
HEAVY = "Heavy"

#: 실제로 **돈** 결과. `Skipped` 는 `[Ignore]`·`[Explicit]`·목록으로 건너뛴 것이다.
EXECUTED = ("Passed", "Failed", "Inconclusive")

#: 목록을 받아 올 브랜치. **검토를 거친 코드가 만든 목록만 쓴다.** MR 잡의 아티팩트는
#: 아직 리뷰 전인 그 브랜치의 스크립트가 쓴 것이라, 그것을 받으면 한 MR 이 **다른 MR
#: 들이 무엇을 건너뛸지**를 정할 수 있다. `-testFilter` 시절에는 아무것도 못 뺐으니 드러나지
#: 않았을 뿐이다. 새벽 스케줄과 develop 머지 직후 실행이 이 ref 에서 돈다.
SOURCE_REF = "develop"

#: 목록 파일의 머리줄. 사람이 열었을 때 **언제 잰 것인지** 알아야 한다.
HEADER = "# 전체 실행에서 잰 느린 테스트 (S15P21B202-617 · -701 · -739)\n"

#: 머리줄의 기계가 읽는 두 칸. 둘 다 있어야 "온전한 목록" 이다 — `-739` 이전 목록에는
#: 없으므로, 그 목록(99%가 Heavy 인 100건)은 받지 않고 새벽이 새로 재기를 기다린다.
EXPECT_KEY = "# expect-executed:"
COUNT_KEY = "# listed:"

#: 목록을 찾으러 거슬러 볼 잡의 수. `unity_reuse.MAX_KEY_LOOKUPS` 와 같은 결이다.
MAX_LIST_LOOKUPS = 40

#: 실제로 돈 수가 예상의 이 비율보다 낮으면 **넘치게 건너뛰었다** 로 본다.
#:
#: 정확히 같을 수는 없다 — 잰 뒤로 테스트가 늘고 줄기 때문이다. 그래서 "몇 건
#: 다르다" 가 아니라 **"절반도 안 돈다"** 를 잡는다. 가장 위험한 실패는 0건 실행에
#: 초록이 뜨는 것이고, 그것은 이 선에 확실히 걸린다.
MIN_RATIO = 0.5

#: Unity 가 실행을 시작할 때 설정을 통째로 찍는 줄(`ExecutionSettings.ToString`).
#: 그 아래에 `ignoreTests[i] = '이름': '사유'` 가 이름마다 한 줄씩 나온다.
SETTINGS_DUMP = "Running tests for ExecutionSettings"
IGNORE_LINE = re.compile(r"(?m)^ignoreTests\[(\d+)\] = ")


class LookupFailed(Exception):
    """이전 목록이 **없는지 있는지 모른다.** "없다" 와 섞으면 안 된다 — `measure` 가
    "없다" 로 읽으면 이번에 잰 것만 남겨 집합이 줄어든다."""


# ── 결과 XML 읽기 ────────────────────────────────────────────────────────────
def _has_category(node, category: str) -> bool:
    return any(p.get("name") == "Category" and category in (p.get("value") or "")
               for p in node.findall("properties/property"))


def leaves(xml_text: str) -> list[dict]:
    """NUnit3 결과의 테스트(잎)마다 {full, result, reason, heavy, time}.

    **Heavy 는 조상까지 본다.** `[Category("Heavy")]` 는 메서드에도, `[TestCase]` 들을
    묶는 ParameterizedMethod 에도, 픽스처에도 붙고 Unity 는 셋 다 빼 준다. 2026-09-24
    새벽 결과에서 ParameterizedMethod 에 붙은 것이 4곳이었다 — 잎만 보면 그 아래 케이스가
    "Heavy 아님" 으로 보여 목록에 들어간다.

    `full` 은 XML 의 `fullname` 그대로다 — Unity 의 `ignoreTests` 가 **이 글자와 정확히
    같은지**로 건너뛴다. `&quot;` 같은 엔티티는 ElementTree 가 풀어 준다.
    """
    root = ET.fromstring(xml_text)
    out: list[dict] = []

    def walk(node, heavy: bool) -> None:
        for child in node:
            if child.tag not in ("test-suite", "test-case"):
                continue
            marked = heavy or _has_category(child, HEAVY)
            if child.tag == "test-suite":
                walk(child, marked)
                continue
            m = child.find("reason/message")
            out.append({"full": child.get("fullname") or child.get("name") or "",
                        "result": child.get("result") or "",
                        "reason": ((m.text or "") if m is not None else "").strip(),
                        "heavy": marked,
                        "time": child.get("duration") or "0"})

    walk(root, False)
    return out


def _seconds(value) -> float | None:
    try:
        return float(value or 0.0)
    except (TypeError, ValueError):
        return None


def slow_cases(cases, threshold: float = THRESHOLD_SECONDS) -> list[tuple[float, str]]:
    """(초, 전체이름) 목록. 느린 것부터.

    이름은 `leaves()` 의 `full`(XML 의 fullname)을 쓰고, 없으면 `클래스.테스트` 로
    잇는다 — `unity_report.failed_names` 와 같은 모양이다.
    """
    out = []
    for c in cases:
        secs = _seconds(c.get("time"))
        if secs is None:
            # 시간을 못 읽은 것을 0 으로 보면 **느린데 안 빠진다**. 그래도 여기서는
            # 빼는 것이 아니라 재는 것이므로, 모르는 것은 빼지 않고 넘긴다.
            continue
        if secs >= threshold:
            out.append((secs, c.get("full") or f'{c["cls"]}.{c["name"]}'))
    out.sort(key=lambda t: -t[0])
    return out


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


def expected_executed(cases, names) -> int:
    """거르고 나면 몇 건이 남는가 = 전체 − |Heavy ∪ 목록|.

    **Heavy 를 뺀다.** `-701` 은 "전체 − 목록 중 그날 느렸던 것" 을 썼다(2515 − 102 =
    2413). MR 에서는 Heavy 도 빠지므로 실제로 남는 것은 2409 였다 — 절반 기준이라
    지금은 티가 안 나지만, 기준을 조이는 순간 오탐이 된다. 순수 함수다.
    """
    skip = set(names)
    return sum(1 for c in cases if not c["heavy"] and c["full"] not in skip)


# ── 목록 파일 ────────────────────────────────────────────────────────────────
def list_text(names, seconds: dict, expect: int) -> str:
    """`unity-slow.txt` 본문. 이름마다 `초<TAB>이름` 한 줄."""
    body = "".join(f"{seconds.get(n, 0.0):8.2f}\t{n}\n" for n in names)
    return f"{HEADER}{EXPECT_KEY} {int(expect)}\n{COUNT_KEY} {len(names)}\n{body}"


def _count(value: str) -> int | None:
    # `isdigit()` 만 보면 `²` 같은 글자가 통과하고 int() 에서 터진다.
    value = value.strip()
    return int(value) if value.isascii() and value.isdigit() else None


def read_list(text) -> dict | None:
    """온전한 목록이면 {expect, entries:[(초, 이름)], names}. 아니면 None.

    **온전하다 = 남을 수와 이름 수가 머리줄에 있고, 이름 수가 실제 줄 수와 같다.**
    하나라도 어긋나면 통째로 버린다 — 반쯤 읽은 목록으로 건너뛰면 무엇을 왜 건너뛰었는지
    아무도 말할 수 없다. 버리면 전부 돌 뿐이다(안전한 쪽). 순수 함수다.
    """
    if not isinstance(text, str) or not text.startswith("#"):
        return None
    expect = listed = None
    entries: list[tuple[float, str]] = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith(EXPECT_KEY):
            expect = _count(line[len(EXPECT_KEY):])
            if expect is None:
                return None
            continue
        if line.startswith(COUNT_KEY):
            listed = _count(line[len(COUNT_KEY):])
            if listed is None:
                return None
            continue
        if line.startswith("#"):
            continue
        # 이름 안에 탭이 있어도 앞의 초만 떼어 낸다 — 이름을 자르면 다른 이름이 된다.
        secs, tab, name = line.partition("\t")
        secs, name = _seconds(secs), name.strip()
        if not tab or secs is None or not name:
            return None
        entries.append((secs, name))
    names = [n for _s, n in entries]
    # 이름 수 줄이 없으면 listed 가 None 이라 아래 비교에서 걸린다.
    if expect is None or listed != len(names) or len(set(names)) != len(names):
        return None
    return {"expect": expect, "entries": entries, "names": names}


# ── Unity 설정 파일 ──────────────────────────────────────────────────────────
def settings_json(names, source_job) -> str:
    """`-testSettingsFile` 본문.

    **ASCII 로만 쓴다**(`ensure_ascii=True`). 한글 사유·따옴표·`;` 가 든 이름이
    `\\uXXXX`·`\\"` 로 나가고, Unity 의 Json 파서는 그것을 그대로 되돌린다(프로브
    107/107). 깨진 JSON 은 Unity 를 **멈추게** 한다 — 파서 예외가 SetupException 이 아니라서
    종료 콜백이 걸리기 전에 빠져나가고, 에디터는 90분 타임아웃까지 unity 슬롯을 문다
    (프로브: 631초에 강제 종료).
    """
    doc = {"ignoreTests": [{"test": n, "ignoreComment": f"{MARK} · 잡 {source_job}"}
                           for n in names]}
    return json.dumps(doc, ensure_ascii=True, indent=1) + "\n"


def read_settings(text) -> list[str] | None:
    """설정 파일에 든 이름들. 모양이 하나라도 다르면 None. 순수 함수다."""
    try:
        doc = json.loads(text)
    except (TypeError, ValueError):
        return None
    items = doc.get("ignoreTests") if isinstance(doc, dict) else None
    if not isinstance(items, list):
        return None
    names = []
    for item in items:
        if not isinstance(item, dict):
            return None
        name, why = item.get("test"), item.get("ignoreComment")
        if not isinstance(name, str) or not name or not isinstance(why, str) or not why.startswith(MARK):
            return None
        names.append(name)
    return names


def cap_entries(entries, cap: int = MAX_LISTED) -> tuple[list[str], list[str]]:
    """(건너뛸 이름, 상한에 걸려 돌릴 이름). **느린 것부터** 건너뛴다 — 같은 수를
    건너뛸 거라면 아끼는 시간이 큰 쪽이다. 순수 함수다."""
    order = sorted(entries, key=lambda e: (-e[0], e[1]))
    return [n for _s, n in order[:cap]], [n for _s, n in order[cap:]]


def marker_text(source_job, listed: int, expect: int, dropped: int) -> str:
    return f"source {source_job}\nlisted {listed}\nexpect {expect}\ndropped {dropped}\n"


def read_marker(text) -> dict | None:
    if not isinstance(text, str):
        return None
    got = {}
    for line in text.splitlines():
        key, _, value = line.strip().partition(" ")
        if key:
            got[key] = value.strip()
    if not got.get("source") or _count(got.get("listed", "")) is None or _count(got.get("expect", "")) is None:
        return None
    return {"source": got["source"], "listed": int(got["listed"]), "expect": int(got["expect"])}


# ── 확인 ─────────────────────────────────────────────────────────────────────
def enough(ran: int, expect: int, ratio: float = MIN_RATIO) -> bool:
    """돈 수가 말이 되는가. **순수 함수** — 검사가 여기를 본다."""
    if expect <= 0:
        return True
    return ran >= int(expect * ratio)


def audit(xml_text: str, listed) -> dict:
    """결과를 목록과 **이름으로** 견준다. 순수 함수다.

      · leaked  = 목록 ∩ 돈 것 — 빼는 장치가 안 돌았다(`-701` 이 5건 내내 이랬다)
      · stray   = 목록 사유로 건너뛴 것 − 목록 — 넘치게 뺐다
      · absent  = 목록 − 결과의 모든 이름 — 지워졌거나 이름이 바뀌었다(정보)

    `-701` 의 확인은 **개수**만 봤다(절반은 돌았나). 아무것도 안 빠져도 개수는 넉넉하므로
    그 검사로는 "안 뺐다" 를 영영 못 본다. 이름을 봐야 한다.
    """
    cases = leaves(xml_text)
    seen = {c["full"] for c in cases}
    ran = [c["full"] for c in cases if c["result"] in EXECUTED]
    ours = {c["full"] for c in cases if c["result"] == "Skipped" and c["reason"].startswith(MARK)}
    want = set(listed)
    return {"executed": len(ran),
            "skipped": sorted(ours & want),
            "leaked": sorted(want & set(ran)),
            "stray": sorted(ours - want),
            "absent": sorted(want - seen),
            "other": sorted((want & seen) - set(ran) - ours)}


def log_ignore_count(log_text) -> int | None:
    """Unity 가 받았다고 찍은 `ignoreTests` 의 수. 설정 덤프가 없으면 None.

    **번호를 센다**(줄이 아니라). 도메인 리로드 뒤에 덤프가 다시 찍혀도 같은 수가 나온다.
    """
    if not log_text or SETTINGS_DUMP not in log_text:
        return None
    return len({m.group(1) for m in IGNORE_LINE.finditer(log_text)})


# ── 파일 · API ───────────────────────────────────────────────────────────────
def write(path: str, text: str) -> None:
    """**한 번에 바꾼다**(임시 파일 → 이름 바꾸기). 쓰다 죽어도 반쯤 쓴 파일이 남지 않는다."""
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    os.replace(tmp, path)


def _read(path: str) -> str | None:
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return None


def _gitlab():
    """API 를 쓸 수 있으면 핸들, 아니면 None. **없다고 실패시키지 않는다.**"""
    try:
        from ci_common import GitLab
    except Exception:  # noqa: BLE001
        return None
    gl = GitLab()
    return gl if (gl.token and gl.project) else None


def find_published(gl, job_name: str = "unity-compile"):
    """`SOURCE_REF` 에서 **온전한 목록**을 가진 가장 최근 잡 → (잡, 원문, read_list 결과).

    없으면 None, **찾아볼 수 없었으면 LookupFailed** 를 던진다. 새벽이 만들고 develop
    머지 직후 실행이 원문 그대로 실어 나른다 — 어느 쪽을 집어도 내용이 같다.

    목록은 **원문으로** 받는다(`job_artifact_raw`). `job_artifact_text` 는 본문을
    `json.loads` 에 먼저 넣어서 숫자만 든 파일을 숫자로 바꾼다.
    """
    try:
        jobs = gl.project_jobs()
    except Exception as e:  # noqa: BLE001
        raise LookupFailed(f"잡 목록 조회 실패 {type(e).__name__}") from e
    if not jobs:
        # 이 저장소에 잡이 0건일 리 없다 — 조회가 실패한 것이다(project_jobs 는 실패를
        # 빈 목록으로 준다).
        raise LookupFailed("잡 목록이 비었다")
    seen = 0
    for j in sorted(jobs, key=lambda x: -(x.get("id") or 0)):
        if j.get("name") != job_name or j.get("status") not in ("success", "failed"):
            continue
        if j.get("ref") != SOURCE_REF:
            continue
        seen += 1
        if seen > MAX_LIST_LOOKUPS:
            break
        try:
            text = gl.job_artifact_raw(j.get("id"), LIST_FILE)
        except Exception as e:  # noqa: BLE001
            # 404 는 None 으로 온다 — 여기 오는 것은 **있는지 없는지 모르는** 경우다.
            raise LookupFailed(f"잡 {j.get('id')} 의 {LIST_FILE} 를 못 읽었다({type(e).__name__})") from e
        parsed = read_list(text)
        if parsed is not None:
            return j, text, parsed
    return None


# ── 명령 ─────────────────────────────────────────────────────────────────────
def cmd_measure(results_path: str, out_dir: str) -> int:
    """전체 실행의 결과에서 느린 것을 골라 `unity-slow.txt` 하나로 남긴다.

    **무엇이든 못 읽으면 아무것도 쓰지 않는다.** 결과든 이전 목록이든 — 반쯤 아는
    채로 쓰면 그것이 "가장 최근 목록" 이 되어 다음 실행들이 받아 간다. 안 쓰면 앞
    실행의 목록이 그대로 쓰인다.
    """
    try:
        with open(results_path, encoding="utf-8", errors="replace") as f:
            cases = leaves(f.read())
    except Exception as e:  # noqa: BLE001
        print(f"[warn] 결과를 읽지 못했다({type(e).__name__}: {e}) — 목록을 남기지 않는다")
        return 0
    if not cases:
        print("[warn] 결과에 테스트가 0건이다(컴파일 실패?) — 목록을 남기지 않는다")
        return 0

    heavy = {c["full"] for c in cases if c["heavy"]}
    slow_all = slow_cases(cases)
    # **Heavy 는 목록에 넣지 않는다** (S15P21B202-739). `-testCategory "!Heavy"` 가 이미
    # 빼므로 목록에 있어 봐야 하는 일이 없고, 그 탓에 `-701` 의 목록은 100건 중 99건이
    # Heavy 였다 — "목록이 돈다" 를 확인할 이름이 1건밖에 없었다.
    slow = [(s, n) for s, n in slow_all if n not in heavy]

    # **이전 집합을 포함한다** (S15P21B202-701). 그날 느린 것만 빼면 어제 느렸던
    # 테스트가 오늘 4.9초라고 MR 로 돌아온다.
    gl = _gitlab()
    if gl is None:
        print("[warn] 이전 목록을 읽을 수 없다(API 토큰/프로젝트 없음) — 목록을 남기지 않는다")
        return 0
    try:
        found = find_published(gl)
    except LookupFailed as e:
        # 여기서 "없다" 로 치고 쓰면 오늘 잰 것만 남아 **집합이 줄어든 목록이 최신이 된다.**
        print(f"[warn] 이전 목록을 확인하지 못했다({e}) — 목록을 남기지 않는다")
        return 0
    if found is None:
        print(f"이전 목록이 없다({SOURCE_REF} 의 최근 {MAX_LIST_LOOKUPS}개) — 이번에 잰 것부터 시작한다")
        before = []
    else:
        before = found[2]["names"]
        print(f"잡 {found[0].get('id')} 의 목록 {len(before)}건을 이어받는다")
    # 이제 Heavy 가 된 것은 목록에서 내린다 — 빠지는 집합(Heavy ∪ 목록)은 그대로다.
    moved = [n for n in before if n in heavy]
    before = [n for n in before if n not in heavy]
    if moved:
        print(f"Heavy 로 이미 빠지는 {len(moved)}건은 목록에서 내린다")

    names, left = merge_monotone(before, slow)
    if left:
        print(f"상한({MAX_NEW_PER_RUN}건)에 걸려 이번에 안 더한 것 {len(left)}건 — 다음 실행에서 더한다")
        for n in left[:5]:
            print("  " + n)

    seconds = {c["full"]: (_seconds(c["time"]) or 0.0) for c in cases}
    expect = expected_executed(cases, names)
    write(os.path.join(out_dir, LIST_FILE), list_text(names, seconds, expect))

    spent = sum(s for s, _n in slow_all)
    whole = sum(_seconds(c["time"]) or 0.0 for c in cases)
    share = (100.0 * spent / whole) if whole else 0.0
    print(f"느린 테스트 {len(slow_all)}건 · {spent:.1f}초 "
          f"(전체 {len(cases)}건 · {whole:.1f}초의 {share:.1f}%) · 기준 {THRESHOLD_SECONDS:g}초")
    print(f"  그중 Heavy {len(slow_all) - len(slow)}건은 이미 빠진다 · 목록 {len(names)}건")
    for s, n in slow[:10]:
        print(f"  {s:8.2f}초  {n}")
    if len(slow) > 10:
        print(f"  … 그 밖 {len(slow) - 10}건은 {LIST_FILE} 에 있다")
    print(f"Heavy 와 목록을 빼면 {expect}건이 남는다 — 받는 쪽이 이 수로 '넘치게 건너뛰었다' 를 잡는다")
    return 0


def cmd_apply(out_dir: str) -> int:
    """**전체 실행이 남긴 목록을 가져와 설정 파일로 만든다.** 못 하면 아무것도 쓰지 않는다.

    쓰지 않는 것이 곧 "전부 돌린다" 다 — 잡(`.gitlab-ci.yml`)이 설정 파일과 표시가
    함께 있을 때만 넘긴다. **빼는 것을 실패로 만들지 않는다**: 목록이 없다고 검사를
    멈추면 그날 아무도 머지하지 못한다. 다만 **그 사실은 남긴다.**

    쓰는 순서가 뜻을 가진다 — 설정 파일 → 되읽어 확인 → 목록 원문 → 표시. 표시가
    마지막이라, 중간에 무엇이 실패해도 잡은 설정 파일을 넘기지 않는다.
    """
    # 지난 실행이 남긴 것을 먼저 지운다 — 이번에 못 받았는데 옛 파일로 건너뛰면 안 된다.
    for name in PUBLISHED:
        try:
            os.remove(os.path.join(out_dir, name))
        except OSError:
            pass
    gl = _gitlab()
    if gl is None:
        print("목록을 받지 못했다: API 토큰/프로젝트가 없다 — 전부 돌린다")
        return 0
    try:
        found = find_published(gl)
    except LookupFailed as e:
        print(f"목록을 받지 못했다: {e} — 전부 돌린다")
        return 0
    if found is None:
        print(f"목록을 받지 못했다: {SOURCE_REF} 의 최근 {MAX_LIST_LOOKUPS}개에 온전한 목록이 없다 — 전부 돌린다")
        return 0
    job, text, parsed = found
    if not parsed["names"]:
        print(f"잡 {job.get('id')} 의 목록이 비었다 — 건너뛸 것이 없다")
        return 0

    names, dropped = cap_entries(parsed["entries"], MAX_LISTED)
    if dropped:
        print(f"❌ 목록 {len(parsed['names'])}건이 상한 {MAX_LISTED}건을 넘는다 — "
              f"느린 {len(names)}건만 건너뛰고 {len(dropped)}건은 돌린다")
        for n in dropped[:10]:
            print("   돌린다: " + n)
        if len(dropped) > 10:
            print(f"   … 그 밖 {len(dropped) - 10}건")

    path = os.path.join(out_dir, SETTINGS_FILE)
    write(path, settings_json(names, job.get("id")))
    if read_settings(_read(path)) != names:
        try:
            os.remove(path)
        except OSError:
            pass
        print("❌ 설정 파일을 되읽으니 이름이 다르다 — 넘기지 않는다(전부 돌린다)")
        return 0
    # 목록은 **원문 그대로** 싣는다 — 이 잡이 develop 이면 다음 실행이 이것을 받아 간다.
    # 상한으로 자른 것을 실으면 그 사슬에서 이름이 영영 빠진다.
    write(os.path.join(out_dir, LIST_FILE), text)
    write(os.path.join(out_dir, APPLIED_FILE),
          marker_text(job.get("id"), len(names), parsed["expect"], len(dropped)))
    print(f"잡 {job.get('id')} 의 목록 {len(names)}건을 건너뛴다 (남을 것 {parsed['expect']}건)")
    for n in names[:10]:
        print("  " + n)
    if len(names) > 10:
        print(f"  … 그 밖 {len(names) - 10}건은 {SETTINGS_FILE} 에 있다")
    return 0


def cmd_verify(results_path: str, out_dir: str) -> int:
    """건너뛰고 나서 **이름으로** 확인한다. 이상하면 1 을 준다(잡이 2 — 경고로 올린다).

    `-701` 의 확인은 "절반은 돌았나" 뿐이라 **아무것도 안 빠진 것**을 5건 내내 못 봤다.
    이제 셋을 본다 — 목록이 돌았나(leaked), 목록 밖이 빠졌나(stray), 그리고 Unity 가
    목록을 몇 건 받았다고 로그에 찍었나.
    """
    reused = (_read(os.path.join(out_dir, REUSED_FILE)) or "").strip()
    if reused:
        # 다른 잡의 결과다 — 이 잡은 목록을 넘기지 않았으니 견줄 목록이 없다. 그 잡이
        # 돌 때 이미 같은 확인을 했다.
        print(f"잡 {reused} 의 결과를 그대로 썼다 — 이름 확인은 그 잡이 했다")
        return 0

    marker_path = os.path.join(out_dir, APPLIED_FILE)
    applied = os.path.exists(marker_path)
    listed: list[str] = []
    expect = 0
    if applied:
        marker = read_marker(_read(marker_path))
        got = read_settings(_read(os.path.join(out_dir, SETTINGS_FILE)))
        if marker is None or got is None or len(got) != marker["listed"]:
            print("❌ 건너뛴다고 표시해 놓고 무엇을 건너뛰었는지 읽지 못한다")
            return 1
        listed, expect = got, marker["expect"]

    try:
        with open(results_path, encoding="utf-8", errors="replace") as f:
            a = audit(f.read(), listed)
    except Exception as e:  # noqa: BLE001
        print(("❌" if applied else "[warn]") + f" 결과를 읽지 못했다({type(e).__name__})"
              + (" — 건너뛰고 나서 확인하지 못했다" if applied else " — 확인을 건너뛴다"))
        return 1 if applied else 0

    bad = False
    if a["stray"]:
        print(f"❌ 목록에 없는데 '{MARK}' 로 건너뛴 것 {len(a['stray'])}건 — 넘치게 뺐다")
        for n in a["stray"][:10]:
            print("   " + n)
        bad = True
    if not applied:
        if not bad:
            print("건너뛰지 않았다 — 확인할 것이 없다")
        return 1 if bad else 0

    print(f"목록 {len(listed)}건: 건너뜀 {len(a['skipped'])} · 돌았다 {len(a['leaked'])} · "
          f"결과에 없음 {len(a['absent'])} · 다른 사유로 안 돎 {len(a['other'])} · 실행 {a['executed']}건")
    if a["leaked"]:
        print(f"❌ 목록에 있는데 돌았다 {len(a['leaked'])}건 — 건너뛰는 장치가 동작하지 않는다")
        for n in a["leaked"][:10]:
            print("   " + n)
        bad = True
    if a["absent"]:
        # 정보다 — 지워졌거나 이름이 바뀐 테스트다. `ignoreTests` 는 정확히 같을 때만
        # 건너뛰므로 낡은 이름이 다른 것을 빼지는 못한다.
        print(f"  결과에 없는 이름 {len(a['absent'])}건(지워졌거나 바뀌었다):")
        for n in a["absent"][:5]:
            print("   " + n)
    if not enough(a["executed"], expect):
        print(f"❌ 건너뛴 뒤 {a['executed']}건만 돌았다 — 예상은 {expect}건이다. 넘치게 건너뛰었다.")
        bad = True

    log_text = _read(os.path.join(out_dir, LOG_FILE))
    if not log_text:
        print(f"  {LOG_FILE} 가 없다 — Unity 가 받은 수는 확인하지 못했다")
    else:
        told = log_ignore_count(log_text)
        if told is None:
            print(f"❌ {LOG_FILE} 에 실행 설정('{SETTINGS_DUMP}')이 없다 — Unity 가 목록을 받았는지 모른다")
            bad = True
        elif told != len(listed):
            print(f"❌ Unity 가 받은 ignoreTests 는 {told}건 — 넘긴 것은 {len(listed)}건이다")
            bad = True
        else:
            print(f"  Unity 가 ignoreTests {told}건을 받았다고 찍었다")
    if not bad:
        print(f"목록 {len(listed)}건을 건너뛰고 {a['executed']}건이 돌았다 (예상 {expect}건) ✅")
    else:
        # 이 MR 이 만든 문제가 아니라 건너뛰는 장치의 문제다 — 잡이 2(경고)로 올린다.
        print("   판정 2(경고) — 머지를 막지 않는다. 고칠 곳은 scripts/ci/slow_tests.py 쪽이다.")
    return 1 if bad else 0


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
