#!/usr/bin/env python3
"""Unity 검사 결과를 MR 에서 보이게 한다 (S15P21B202-271).

세 가지를 한다.
  1. **NUnit3 → JUnit 변환.** Unity 는 `<test-run>` 형식을 내는데 GitLab 은 `<testsuite>` 만
     파싱한다. 그래서 `reports: junit:` 을 걸어도 **조용히 0건**으로 읽혔다(2026-09-02 실측:
     test_report_summary 가 total 0). 변환해 주면 MR 의 테스트 위젯이 살아나고, GitLab 이
     **대상 브랜치와 비교해 새로 생긴 실패만** 보여 준다 — 팀원이 지금 고치는 중인 기존 실패를
     묻어 주고 그 MR 이 만든 회귀만 드러낸다.
  2. **기존 실패와 새 실패를 갈라서** MR 코멘트로 남긴다. 위젯은 눌러야 보이니 코멘트로도 남긴다.
     기준선은 대상 브랜치의 마지막 unity-compile 아티팩트다.
  3. 컴파일이 깨졌으면 그것을 맨 위에 크게 적는다 — 테스트 목록보다 그게 급하다.
  4. **잡의 종료 코드를 정한다**(S15P21B202-309). 전에는 잡이 Unity 의 원시 종료 코드를 그대로
     뱉었다 — Unity 는 테스트가 하나라도 실패하면 2 로 끝나므로, 기존 실패 25건만 있는 MR 도
     빨간불이 됐다. 이 스크립트는 이미 새/기존을 가르고 있었는데 **그 판정이 종료 코드에
     반영되지 않았다.** 이제 `unity-verdict.txt` 에 적고 잡이 그 값으로 끝난다.

왜 "새 실패" 를 굳이 구분하나 — 매번 실패 17개를 나열하면 사람이 읽지 않게 된다. mr-lint 의
스택 경고가 그렇게 무시됐다(실측 26 대 0). 그 MR 이 바꾼 것만 보여야 읽힌다.

로컬 실행: python3 scripts/ci/unity_report.py <nunit.xml> [unity.log]
           (GITLAB_TOKEN·CI_MERGE_REQUEST_IID 가 있으면 MR 코멘트까지)
"""
import glob
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape, quoteattr

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import GitLab, banner, env  # noqa: E402

MARKER = "<!-- unity-compile:metagame -->"
JOB_NAME = "unity-compile"
# 기준선 후보를 몇 개까지 열어 볼지. 아티팩트가 만료된 옛 잡은 404 가 한 번씩 나가므로
# 상한이 없으면 잡 목록 800건 중 unity-compile 수십~수백 건에 요청이 그만큼 붙는다
# (14일 이전은 전부 만료). 넘으면 기준선을 못 찾은 것으로 보고 판정 2 로 떨어진다.
MAX_BASELINE_CANDIDATES = 10   # !259 3차 AI 리뷰 (S15P21B202-342)

# 기준선으로 **합집합**할 실행 수 (S15P21B202-347). 1 이면 예전과 같다.
#
# 왜 합집합인가: 판정은 "기준선에 없던 실패" 를 이 MR 탓으로 돌린다. 그 계산은 실행이
# 결정적이라는 것을 전제하는데, 물리 시뮬레이션 테스트 몇 개가 경계에 앉아 있어 같은
# 커밋으로도 결과가 갈린다. 기준선이 단 한 번의 실행이면 **그 한 번이 경계의 어느 쪽에
# 떨어졌는지가 이후 모든 MR 의 "정상" 을 정한다.** 2026-09-09 에 그래서 MR 3건이 동시에
# 막혔다 — develop 의 09:45 실행이 운 좋게 통과한 쪽이었다.
#
# 대가를 분명히 안다: 최근 develop 에서 한 번 실패한 적 있는 테스트는 **진짜 회귀여도
# 기준선에 들어가 넘어간다.** 그래서 3 으로 좁게 두고, 어느 잡이 무엇을 보탰는지 로그에
# 남긴다. 이것만으로는 부족해서 아래 재실행(RECHECK_RUNS)을 함께 쓴다.
BASELINE_RUNS = 3

# 재실행 **횟수와 상한은 여기 없다** — `.gitlab-ci.yml` 의 `RECHECK_RUNS`·`RECHECK_MAX_NEW`
# 하나에만 둔다 (S15P21B202-347, `!272` AI 리뷰). 이 스크립트는 Unity 를 돌리지 않으므로
# 그 값이 필요 없고, 두 곳에 두면 한쪽만 바뀌어 아티팩트 목록과 어긋난다.
#
# 여기서 하는 일은 하나다 — 잡이 준 재실행 결과를 읽어 **한 번이라도 통과했는지** 보고
# 플래키를 가린다. 뺀 것은 **조용히 빼지 않고 코멘트에 적는다.**
RECHECK_PLAN = "unity-recheck.txt"     # 재실행할 테스트 이름 (한 줄에 하나)

# 잡이 `-testFilter` 에 그대로 넣는 값. **여기서 만든다** (`!272` 3차 AI 리뷰).
#
# `-testFilter` 는 이름을 **정규식으로** 해석한다. 파라미터화 테스트 이름에는 `(`·`)`·`,`
# 가 들어가고(`Load(1,2)`), 그것을 그대로 넘기면 매칭에 실패한다 — 그러면 전원 '확인 못함'
# → 회귀가 되어 **이 장치가 무력화된다.** 셸에서 sed 로 이스케이프하면 백슬래시가 몇 겹
# 먹히는지가 애매해지므로, 파이썬에서 만들어 파일로 넘긴다. 테스트로 못 박을 수 있다.
RECHECK_FILTER = "unity-recheck-filter.txt"

# 계획 단계가 조회한 기준선을 최종 판정이 **그대로 다시 쓴다** (`!272` AI 리뷰).
#
# 두 단계가 각각 GitLab 에 물으면, 그 사이 develop 의 unity-compile 이 끝났을 때 합집합이
# 달라진다. 그러면 **재실행한 집합과 판정 대상이 어긋난다** — 실제 새 실패인데 재실행되지
# 않아 '확인 못함' 으로 회귀 처리되거나, 반대로 재실행한 이름이 판정 대상에서 사라진다.
# 파일로 못 박으면 그 창이 닫히고 API 호출도 절반이 된다.
BASELINE_FILE = "unity-baseline.json"
ARTIFACT = "unity-results.xml"
MAX_LISTED = 15          # 코멘트에 나열할 실패 개수 상한
MAX_COMPILE_ERRORS = 20
VERDICT_FILE = "unity-verdict.txt"      # 잡이 이 파일의 값으로 끝난다(.gitlab-ci.yml)

# 종료 코드 — .gitlab-ci.yml 의 `allow_failure: exit_codes: [2]` 와 짝이다
PASS, REGRESSION, UNKNOWN = 0, 1, 2


# ── NUnit3 읽기 ──────────────────────────────────────────────────────────────
def parse_nunit(text: str) -> dict:
    """{total, passed, failed, skipped, duration, cases:[{cls,name,msg,result}]} 로 만든다.

    NUnit3 는 test-suite 가 중첩되고 test-case 의 fullname 이 '네임스페이스.픽스처.테스트' 다.
    fullname 에서 마지막 조각을 떼면 클래스(픽스처)이고, 그 둘이 GitLab 의 테스트 신원이 된다 —
    파이프라인 사이에 안정적이어야 '새 실패' 판정이 성립한다."""
    root = ET.fromstring(text)
    out = {"total": int(root.get("total") or 0), "passed": int(root.get("passed") or 0),
           "failed": int(root.get("failed") or 0), "skipped": int(root.get("skipped") or 0),
           "duration": float(root.get("duration") or 0.0), "cases": []}
    for tc in root.iter("test-case"):
        full = tc.get("fullname") or tc.get("name") or ""
        cls, _, name = full.rpartition(".")
        msg = ""
        m = tc.find("failure/message")
        if m is not None and m.text:
            msg = " ".join(m.text.split())
        out["cases"].append({
            "cls": cls or "(클래스 없음)", "name": name or full,
            "result": tc.get("result") or "", "msg": msg,
            "time": tc.get("duration") or "0",
        })
    return out


def to_junit(data: dict) -> str:
    """GitLab 이 읽는 JUnit XML. classname+name 이 테스트 신원이므로 그대로 유지한다."""
    by_cls: dict[str, list[dict]] = {}
    for c in data["cases"]:
        by_cls.setdefault(c["cls"], []).append(c)
    lines = ['<?xml version="1.0" encoding="UTF-8"?>',
             f'<testsuites tests="{data["total"]}" failures="{data["failed"]}" '
             f'time="{data["duration"]:.3f}">']
    for cls, cases in sorted(by_cls.items()):
        fails = sum(1 for c in cases if c["result"] == "Failed")
        skips = sum(1 for c in cases if c["result"] in ("Skipped", "Inconclusive"))
        lines.append(f'  <testsuite name={quoteattr(cls)} tests="{len(cases)}" '
                     f'failures="{fails}" skipped="{skips}">')
        for c in cases:
            lines.append(f'    <testcase classname={quoteattr(cls)} name={quoteattr(c["name"])} '
                         f'time="{c["time"]}">')
            if c["result"] == "Failed":
                lines.append(f'      <failure message={quoteattr(c["msg"][:500])}>'
                             f'{escape(c["msg"][:2000])}</failure>')
            elif c["result"] in ("Skipped", "Inconclusive"):
                lines.append("      <skipped/>")
            lines.append("    </testcase>")
        lines.append("  </testsuite>")
    lines.append("</testsuites>")
    return "\n".join(lines) + "\n"


def compile_errors(log_text: str, cap: int = MAX_COMPILE_ERRORS) -> list[str]:
    """error CS 줄만 중복 제거해 뽑는다. 로그는 수만 줄이라 사람이 못 읽는다."""
    seen, out = set(), []
    for line in log_text.splitlines():
        if "error CS" not in line:
            continue
        s = line.strip()
        if s not in seen:
            seen.add(s)
            out.append(s)
        if len(out) >= cap:
            break
    return out


# ── 기준선(대상 브랜치의 마지막 결과) ────────────────────────────────────────
def baseline_candidates(gl: GitLab, ref: str, max_pipelines: int) -> list[dict]:
    """기준선으로 쓸 `unity-compile` 잡을 최신 순으로 준다 (S15P21B202-342).

    **먼저 잡 목록 API 로 찾는다.** 한 번 호출로 100건이 오고 `ref`·`status` 가 함께 오므로,
    파이프라인을 하나씩 훑는 것(파이프라인당 호출 1회)보다 같은 비용으로 훨씬 멀리 되짚는다.

    <para>왜 멀리 되짚어야 하나: `unity-compile` 은 `changes: METAGAME/**` 게이트를 쓰므로
    문서·스크립트만 머지되는 날에는 develop 에 그 잡이 아예 생기지 않는다. 파이프라인 15개만
    보던 동안 창이 자주 비었다 — 2026-09-08 실측으로 가장 최근 것이 7번째 뒤, 그 전은 14번째
    뒤였다(창 경계). 기준선을 못 찾으면 판정이 2(판단 불가)로 떨어지고 그 잡은 비차단이라
    **이 MR 이 만든 새 실패가 그대로 통과한다.**</para>

    잡 목록을 못 읽으면 **예전 방식(파이프라인 훑기)으로 되돌아간다** — 둘 중 하나는 되도록.
    """
    out: list[dict] = []
    try:
        for j in gl.project_jobs():
            if j.get("name") == JOB_NAME and j.get("ref") == ref and j.get("status") in ("success", "failed"):
                out.append(j)
    except Exception as e:  # noqa: BLE001
        print(f"[warn] 잡 목록 조회 실패({type(e).__name__}: {e}) — 파이프라인 훑기로 되돌아간다")
    if out:
        return out
    try:
        for p in gl.branch_pipelines(ref, per_page=max_pipelines):
            jobs = [j for j in gl.pipeline_jobs(p.get("id")) if j.get("name") == JOB_NAME]
            out += [j for j in sorted(jobs, key=lambda x: x.get("id") or 0, reverse=True)
                    if j.get("status") in ("success", "failed")]
    except Exception as e:  # noqa: BLE001
        print(f"[warn] 파이프라인 훑기도 실패({type(e).__name__}: {e})")
    return out


def baseline_failures(gl: GitLab, ref: str, max_pipelines: int = 15,
                      meta: dict | None = None, runs: int = BASELINE_RUNS) -> set | None:
    """대상 브랜치의 최근 `runs` 회 unity-compile 실패의 **합집합**. 못 구하면 None.

    None 과 빈 집합을 구분한다 — '기준선을 못 봤다' 와 '기준선에 실패가 없다' 는 다르다.
    전자에서 새 실패를 단정하면 없는 회귀를 만들어 낸다.

    <para>`runs` 회를 합치는 이유는 `BASELINE_RUNS` 주석에 있다. `meta` 의 `sha`·`when` 은
    **가장 최신 실행**의 것을 담는다 — 기준선 신선도 검사(`metagame_changed`)가 "기준선이
    합친 base 보다 낡았나" 를 묻기 때문이다. 합집합에 옛 실행이 섞여도 그 질문의 답은
    가장 최신 실행으로 정해진다.</para>
    """
    if not (gl.token and gl.project):
        return None
    union: set = set()
    used: list[str] = []
    try:
        cands = baseline_candidates(gl, ref, max_pipelines)
        if len(cands) > MAX_BASELINE_CANDIDATES:
            print(f"기준선 후보 {len(cands)}개 중 최신 {MAX_BASELINE_CANDIDATES}개만 본다")
        for j in cands[:MAX_BASELINE_CANDIDATES]:
            if len(used) >= max(1, runs):
                break
            txt = gl.job_artifact_text(j["id"], ARTIFACT)
            if not txt:
                continue          # 아티팩트 만료(expire_in) — 더 최신 것부터 계속 본다
            d = parse_nunit(txt)
            when = (j.get('created_at') or '시각미상')[:16]
            sha = ((j.get('commit') or {}).get('id') or '')[:8]
            fails = {f'{c["cls"]}.{c["name"]}' for c in d["cases"] if c["result"] == "Failed"}
            added = fails - union
            print(f"기준선: 잡 {j['id']} ({j.get('status')}, {when}, {sha or 'sha미상'}) "
                  f"실패 {len(fails)}건 · 합집합에 새로 보탠 것 {len(added)}건")
            for name in sorted(added):
                if union:                      # 첫 실행은 전부 '보탠 것' 이라 나열하지 않는다
                    print(f"  + {name}")
            union |= fails
            if meta is not None and not used:  # 최신 실행의 것만 남긴다
                meta.update(job=j['id'], sha=sha, when=when)
            used.append(str(j['id']))
    except Exception as e:  # noqa: BLE001 — 기준선 실패가 보고 자체를 막지 않는다
        print(f"[warn] 기준선 조회 실패({type(e).__name__}: {e}) — 새/기존 구분 없이 보고한다")
    if not used:
        return None
    if meta is not None:
        meta.update(runs=used)
    if len(used) > 1:
        print(f"기준선 = 최근 {len(used)}회 합집합 (잡 {', '.join(used)}) → 실패 {len(union)}건")
    return union


# ── 판정 ────────────────────────────────────────────────────────────────────
def failed_names(data: dict) -> set:
    return {f'{c["cls"]}.{c["name"]}' for c in data["cases"] if c["result"] == "Failed"}


def testfilter_value(names) -> str:
    """`-testFilter` 에 넣을 값. 이름을 정규식 이스케이프해 세미콜론으로 잇는다.

    `-testFilter` 는 정규식이므로 `Load(1,2)` 같은 파라미터화 이름을 그대로 넘기면
    매칭에 실패한다 (`!272` 3차 AI 리뷰). `re.escape` 는 필요한 문자만 감싸므로
    평범한 이름은 그대로 남는다.
    """
    return ";".join(re.escape(n) for n in sorted(names))


def classify_rechecks(new: set, paths: list[str]) -> tuple[set, set, set]:
    """새 실패를 재실행 결과로 세 갈래로 나눈다 (S15P21B202-347).

    돌려주는 것은 `(플래키, 계속실패, 확인못함)` 이다.

    - **플래키** — 재실행에서 **한 번이라도 통과**했다. 같은 코드로 결과가 갈렸으므로
      이 MR 이 만든 것이 아니다. 판정에서 뺀다
    - **계속실패** — 재실행 전부 실패. 회귀로 둔다
    - **확인못함** — 재실행 결과에 그 이름이 아예 없다(`-testFilter` 가 못 잡았거나 재실행이
      죽었다). **회귀로 둔다** — 확인하지 못한 것을 면제하면 진짜 회귀가 빠져나간다

    <para>재실행은 그 테스트만 골라 돌리므로 **거의 빈 테스트 세션**에서 돈다. 그래서
    여기서 통과한 것은 실패가 **실행 맥락에 달려 있다**는 뜻이고, 원인은 셋 중 하나다.
    ① 원래 있던 무작위성 ② 다른 테스트가 남긴 상태에 걸려 넘어진 것(테스트 격리 결함)
    ③ **이 MR 이 전역·정적 상태를 오염시켜** 다른 테스트를 깨뜨린 것.</para>

    <para>**③ 이면 이 MR 이 원인이다** — 그래서 "격리에서 통과했으니 무죄" 라고 단정하지
    않는다(`!272` 3차 AI 리뷰). 그래도 판정에서 빼는 쪽을 택한 이유는, ③ 은 이 MR 이
    `METAGAME/` 을 건드렸을 때만 가능하고 그 경우 **사람이 목록을 보면 몇 초에 가릴 수
    있는데**, ①②로 막는 쪽은 아무 잘못 없는 사람이 원인을 찾을 방법이 없기 때문이다.
    코멘트가 세 갈래를 그대로 적고 ③ 을 가리는 방법까지 알려 준다 — 조용히 빼면
    아무도 고치지 않는다.</para>
    """
    if not new:
        return set(), set(), set()
    if not paths:
        return set(), set(new), set()

    passed_any: set = set()
    seen: set = set()
    for p in paths:
        if not p or not os.path.isfile(p) or os.path.getsize(p) == 0:
            print(f"[warn] 재실행 결과가 없다: {p}")
            continue
        try:
            with open(p, encoding="utf-8", errors="replace") as f:
                d = parse_nunit(f.read())
        except Exception as e:  # noqa: BLE001 — 재실행 판독 실패가 판정을 뒤집지 않는다
            print(f"[warn] 재실행 결과를 읽지 못했다({type(e).__name__}: {e}): {p}")
            continue
        # **이 회차만** 센다. 누적값을 찍으면 2회차 로그가 1회차 통과분까지 포함해 보여
        # 로그를 근거로 판독하는 사람을 오도한다(`!272` AI 리뷰).
        ran: set = set()
        passed_here: set = set()
        other_here: set = set()
        for c in d["cases"]:
            name = f'{c["cls"]}.{c["name"]}'
            if name not in new:
                continue
            res = c["result"]
            if res == "Passed":
                ran.add(name)
                passed_here.add(name)
            elif res == "Failed":
                ran.add(name)
            else:
                # **Skipped·Inconclusive 를 통과로 세지 않는다** (`!272` AI 리뷰).
                # 재실행에서 스킵된 테스트를 면제하면 진짜 회귀가 판정에서 빠진다.
                # `ran` 에 넣지 않으므로 '확인 못함' 으로 남아 회귀로 처리된다.
                other_here.add(name)
        seen |= ran
        passed_any |= passed_here
        print(f"재실행 {os.path.basename(p)}: 대상 {len(ran)}/{len(new)}건 · "
              f"이 회차 통과 {len(passed_here)}건")
        if other_here:
            print(f"  이 회차에 통과도 실패도 아닌 결과 {len(other_here)}건 "
                  f"(Skipped·Inconclusive) — 확인한 것으로 세지 않는다: "
                  f"{', '.join(sorted(other_here)[:5])}")
        if not ran:
            # **필터가 아무것도 못 잡았다.** `-testFilter` 구분자가 이 Unity 버전에서
            # 안 통하면 이렇게 된다 — 그러면 전원 '확인 못함' → 회귀가 되어 이 장치가
            # 있으나 마나 해진다. 조용히 넘기지 않고 크게 적는다(`!272` AI 리뷰).
            print(f"[warn] {os.path.basename(p)} 에 재실행 대상이 하나도 없다 — "
                  f"-testFilter 가 이름을 못 잡은 것으로 보인다. "
                  f"구분자(;)가 이 Unity 버전에서 통하는지 확인해야 한다")

    flaky = passed_any & new
    unverified = new - seen
    still = new - flaky - unverified
    if new and not seen:
        print("[warn] 재실행이 돌았는데 대상 이름을 하나도 못 찾았다 — "
              "플래키 판별이 동작하지 않았다. 전부 회귀로 남는다")
    return flaky, still, unverified


def decide(data: dict, errs: list[str], base: set | None, unity_rc: int,
           flaky: set | None = None) -> tuple[int, str]:
    """잡의 종료 코드와 그 이유. (S15P21B202-309)

    원칙은 하나다 — **이 MR 이 만든 것만 막는다.** 나머지는 경고이거나 통과다.
    판단하지 못한 것을 회귀로 단정하면 아무 잘못 없는 사람의 머지가 막히고, 그러면
    사람들이 이 신호를 우회하는 법을 배운다(mr-lint 의 스택 경고가 실측 26 대 0 으로 무시됐다).
    """
    if errs:
        # **컴파일 오류는 기준선을 보지 않고 막는다.** 잡이 대상 브랜치를 합친 뒤 검사하므로
        # (S15P21B202-342 ⓞ) 대상 브랜치 자신이 깨져 있으면 그 오류도 여기로 온다 — 그래도
        # 막는 것이 맞다. **깨진 브랜치 위로는 아무도 머지하면 안 된다.**
        #
        # 한때 "오류 파일이 전부 이 MR 밖이면 경고로 낮춘다" 를 넣었다가 되돌렸다
        # (`!259` 리뷰). 파일 단위 귀속은 **한 방향으로만 성립한다** — A 를 바꿔 B 에서
        # 오류가 날 수 있다(시그니처 변경 → 호출부 깨짐). 그래서 "오류가 내 파일 밖" 은
        # 결백의 증거가 아닌데, 그것을 근거로 차단을 풀면 09-02 사고 경로(컴파일 깨진 채
        # 머지되어 develop 2시간 37분 정지)가 다시 열린다. 틀릴 때는 **막는 쪽으로** 틀린다.
        #
        # 대신 코멘트가 "대상 브랜치가 원인일 수도 있다" 를 말한다(`body` 참고).
        return REGRESSION, f"컴파일 오류 {len(errs)}건 — 합친 결과가 컴파일되지 않는다"
    if base is None:
        if unity_rc == 0:
            return PASS, "기준선을 못 봤지만 Unity 가 성공으로 끝났다"
        return UNKNOWN, (f"기준선을 못 봐서 새 실패인지 가릴 수 없다(Unity 종료 {unity_rc}) — "
                         f"경고로 둔다")
    new = failed_names(data) - base
    # 재실행에서 한 번이라도 통과한 것은 이 MR 이 만든 것이 아니다 (S15P21B202-347).
    # `classify_rechecks` 가 정한다 — 확인하지 못한 것은 여기 들어오지 않는다.
    excused = (flaky or set()) & new
    new = new - excused
    if new:
        why = f"이 MR 이 만든 테스트 실패 {len(new)}건"
        if excused:
            why += f" (재실행에서 통과한 {len(excused)}건은 플래키로 빼고 센 것)"
        return REGRESSION, why
    if excused:
        return PASS, (f"새 실패로 보였던 {len(excused)}건이 재실행에서 통과했다 — "
                      f"플래키다. 이 MR 탓이 아니다")
    if data["failed"] == 0 and unity_rc != 0:
        # 실패가 없는데 죽었다 = 테스트 얘기가 아니다(라이선스 만료·에디터 크래시·타임아웃).
        # 사람이 손댈 것은 인프라 쪽이므로 머지를 막지 않는다.
        return UNKNOWN, f"테스트 실패는 없는데 Unity 가 {unity_rc} 로 끝났다 — 환경 문제로 본다"
    if data["failed"]:
        return PASS, f"실패 {data['failed']}건이 전부 기준선에도 있다 — 이 MR 탓이 아니다"
    return PASS, "실패 없음"


def write_verdict(path_dir: str, code: int) -> str:
    out = os.path.join(path_dir or ".", VERDICT_FILE)
    with open(out, "w", encoding="utf-8") as f:
        f.write(str(code))
    return out


# ── 코멘트 ──────────────────────────────────────────────────────────────────
def merged_note() -> str:
    """무엇을 검사했는지 한 줄로 밝힌다 (S15P21B202-342).

    `.gitlab-ci.yml` 의 unity-compile 이 대상 브랜치를 합친 뒤 검사하고 그 sha 를
    `UNITY_MERGED_BASE` 로 넘긴다. 없으면 브랜치 끝만 검사한 것이므로 **그렇게 적는다** —
    검사 범위를 읽는 사람이 착각하면 초록의 의미를 잘못 읽는다."""
    base = (os.environ.get("UNITY_MERGED_BASE") or "").strip()
    if base:
        return f"`{base}` 를 합친 결과에서 검사했습니다"
    if os.environ.get("CI_MERGE_REQUEST_TARGET_BRANCH_NAME"):
        return "**대상 브랜치를 합치지 못해 브랜치 끝에서만 검사했습니다** — 합쳐서 깨지는 경우는 가리지 못합니다"
    return ""


def body(data: dict, errs: list[str], base: set | None, ref: str,
         stale_base: dict | None = None, flaky: set | None = None) -> str:
    now_fail = {f'{c["cls"]}.{c["name"]}': c for c in data["cases"] if c["result"] == "Failed"}
    if errs:
        head = "🔴 **Unity C# 컴파일 실패** — 테스트는 돌지 못했습니다"
        parts = [head, "", "```"] + errs + ["```", "",
                 "이 MR 을 머지하면 develop 이 컴파일되지 않습니다. "
                 "(09-02 에 같은 일로 develop 이 2시간 37분 멈췄습니다.)", "",
                 # 잡이 대상 브랜치를 합친 뒤 검사하므로(S15P21B202-342) 대상 브랜치
                 # 자신이 깨져 있으면 그 오류도 여기 실린다. 판정은 그대로 막지만
                 # (깨진 브랜치 위로는 머지하면 안 된다) **문구가 단정하지 않는다** —
                 # "이 MR 이 깼다" 는 그 경우 사실이 아니다.
                 # 합치지 못한 경우(충돌·fetch 실패)에는 이렇게 말하면 사람을 엉뚱한
                 # 브랜치로 보낸다. merged_note() 가 무엇을 검사했는지 이미 알고 있다.
                 (f"이 검사는 `{ref}` 를 합친 결과에서 돌았습니다. 그래서 `{ref}` 가 "
                  f"이미 컴파일되지 않는 상태였다면 그 오류도 위에 함께 실립니다 — "
                  f"`{ref}` 의 최신 `unity-compile` 이 초록인지 먼저 확인해 주세요."
                  if os.environ.get("UNITY_MERGED_BASE") else
                  f"이 검사는 **브랜치 끝에서만** 돌았습니다(대상 브랜치를 합치지 못했습니다) — "
                  f"`{ref}` 를 합치면 결과가 달라질 수 있습니다.")]
        return MARKER + "\n" + "\n".join(parts)

    total, passed, failed = data["total"], data["passed"], data["failed"]
    if base is None:
        head = ("✅ **Unity 컴파일 통과**" if not failed else "⚠ **Unity 컴파일 통과 · EditMode 실패 있음**")
        parts = [head, "",
                 f"| 총 | 통과 | 실패 |", "|---|---|---|", f"| {total} | {passed} | {failed} |", "",
                 f"`{ref}` 의 기준 결과를 찾지 못해 **새로 생긴 실패인지 가릴 수 없었습니다**"
                 f"(그 브랜치에서 이 잡이 아직 돌지 않았거나 아티팩트가 만료됐을 수 있습니다)."]
        if failed:
            # 판정 2 는 비차단(allow_failure: exit_codes: [2])이다. 그 사실을 적지 않으면
            # 초록 파이프라인이 '실패를 검사했고 문제없었다' 로 읽힌다 (S15P21B202-342).
            parts += ["",
                      f"⚠ **이 판정은 머지를 막지 않습니다.** 그래서 이 MR 이 만든 새 실패가 "
                      f"있어도 그대로 통과합니다 — 실패 {failed}건을 직접 확인해 주세요."]
        note = merged_note()
        if note:
            parts += ["", f"<sub>{note} · 자동 생성(unity-compile)</sub>"]
        return MARKER + "\n" + "\n".join(parts)

    # 재실행에서 통과한 것은 "새 실패" 에서 뺀다 — 다만 **아래에 반드시 적는다**
    # (S15P21B202-347). 조용히 빼면 테스트 격리 결함을 아무도 고치지 않는다.
    excused = sorted((flaky or set()) & set(now_fail))
    new = [k for k in now_fail if k not in base and k not in excused]
    fixed = [k for k in base if k not in now_fail]
    if not new:
        head = "✅ **Unity 컴파일 통과 · 이 MR 이 만든 테스트 실패 없음**"
        if excused:
            head = (f"✅ **이 MR 이 만든 테스트 실패 없음** — 새 실패로 보였던 "
                    f"{len(excused)}건은 재실행에서 통과했습니다(플래키)")
    else:
        head = f"🔴 **이 MR 에서 새로 실패하는 테스트 {len(new)}개**"
        if stale_base:
            # 기준선이 합친 base 보다 낡았다 — 그 차이에 develop 이 만든 실패가 섞여 있다.
            # 이 경우 판정은 main() 이 2(경고)로 낮춘다 (S15P21B202-342).
            head = f"⚠ **새로 실패하는 테스트 {len(new)}개 — 다만 이 MR 탓인지 가릴 수 없습니다**"
    parts = [head, "",
             "| 항목 | 수 |", "|---|---|",
             f"| 이 MR 의 실패 | {failed} |",
             f"| 그중 **새로 생긴 것** | **{len(new)}** |",
             f"| `{ref}` 에도 있던 것 | {failed - len(new)} |"]
    if fixed:
        parts.append(f"| 이 MR 이 되살린 것 | {len(fixed)} |")
    if new:
        parts += ["", "**새 실패**", ""]
        for k in sorted(new)[:MAX_LISTED]:
            c = now_fail[k]
            parts.append(f"- `{c['cls'].split('.')[-1]}.{c['name']}`" + (f" — {c['msg'][:160]}" if c["msg"] else ""))
        if len(new) > MAX_LISTED:
            parts.append(f"- … 외 {len(new) - MAX_LISTED}건")
    if excused:
        # **판정에서 뺀 것을 드러낸다** (S15P21B202-347). 그 테스트만 골라 다시 돌렸을 때
        # 통과했다는 뜻이고, 그러면 원인은 둘 중 하나다 — 진짜 무작위이거나, 전체 실행에서
        # 다른 테스트가 남긴 상태에 걸려 넘어진 것(테스트 격리 결함). 어느 쪽이든 이 MR 이
        # 만든 것은 아니지만 **둘 다 테스트 쪽 결함이므로 고쳐야 한다.**
        parts += ["", f"<details><summary>플래키로 판정에서 뺀 {len(excused)}건 "
                      f"(재실행에서 통과) — 이 MR 탓은 아니지만 테스트 결함입니다</summary>", ""]
        for k in excused[:MAX_LISTED]:
            c = now_fail[k]
            parts.append(f"- `{c['cls'].split('.')[-1]}.{c['name']}`"
                         + (f" — {c['msg'][:160]}" if c["msg"] else ""))
        if len(excused) > MAX_LISTED:
            parts.append(f"- … 외 {len(excused) - MAX_LISTED}건")
        parts += ["", "전체 실행에서는 실패했고 그 테스트만 골라 다시 돌리면 통과합니다. "
                      "즉 실패가 **실행 맥락에 달려 있다**는 뜻입니다.", "",
                  "- 원래 있던 무작위성이면 이 MR 탓이 아닙니다 (대부분 이 경우입니다)",
                  "- **이 MR 이 전역·정적 상태를 오염시켜** 다른 테스트를 깨뜨린 경우에도 "
                  "격리 재실행은 통과합니다. 그때는 이 MR 이 원인입니다",
                  "",
                  "**이 MR 이 `METAGAME/` 을 하나도 건드리지 않았다면 두 번째는 불가능하므로 "
                  "확정입니다.** 게임 코드를 바꿨다면 위 목록의 테스트가 그 변경과 닿는지 "
                  "한 번 보십시오.", "",
                  "테스트 자체를 결정적으로 만드는 것은 `S15P21B202-360` 에서 다룹니다.",
                  "</details>"]
    if fixed:
        parts += ["", f"**되살린 것** {len(fixed)}건: " + ", ".join(f"`{k.split('.')[-1]}`" for k in sorted(fixed)[:8])]
    if stale_base:
        job, sha, when = stale_base.get("job"), stale_base.get("sha"), stale_base.get("when")
        parts += ["",
                  f"기준 결과가 **합친 base 보다 낡았습니다**(잡 `{job}` · `{sha}` · {when}). "
                  f"그 사이 `{ref}` 의 `METAGAME/` 이 바뀌었으므로 위 **새로 생긴 것** 안에는 "
                  f"`{ref}` 가 만든 실패가 섞여 있을 수 있습니다 — "
                  "**이 MR 탓으로 단정하지 않고 경고로 낮췄습니다.**",
                  "",
                  f"가리려면 `{ref}` 에서 `unity-compile` 을 한 번 돌려 기준선을 최신으로 만든 뒤 "
                  "이 MR 의 파이프라인을 재실행하면 됩니다."]
    note = merged_note()
    parts += ["", "<sub>기존 실패는 담당자가 정리 중일 수 있어 나열하지 않습니다. 전체 목록은 "
              "MR 의 테스트 리포트 탭 · 잡 아티팩트 `unity-results.xml`"
              + (f" · {note}" if note else "") + " · 자동 생성(unity-compile)</sub>"]
    return MARKER + "\n" + "\n".join(parts)


def metagame_changed(a: str, b: str) -> bool | None:
    """`a`..`b` 사이에 `METAGAME/` 아래가 바뀌었는지. 판단 못 하면 None (S15P21B202-342).

    **왜 필요한가 — ⓞ 와 ① 이 서로 얽힌다.** 이제 MR 은 `develop` 을 합친 결과를 검사하므로
    develop 의 실패를 함께 물려받는다. 그런데 기준선은 develop 의 *마지막* `unity-compile`
    이고, 그 잡은 아티팩트 14일 보관 덕에 며칠 전 것일 수 있다. 그 사이 develop 에 들어온
    실패는 **기준선에 없고 내 결과에는 있다** → "이 MR 이 만든 새 실패" 로 오탐되어 남의
    실패로 내 MR 이 막힌다.

    2026-09-08 실측 반례: 기준선이 한 파이프라인만 더 옛것(`906fdfd2`, 실패 27건)이었다면
    `-339`(중력 조정)가 깨뜨린 실패가 전부 내 몫으로 계산됐다. 실제로는 `8b5978e6` 이
    잡혀 그 사이 METAGAME 변경이 0건이었고 판정 0 이 나왔다 — 운이 좋았을 뿐이다.

    `METAGAME/` 아래가 안 바뀌었다면 테스트 거동이 바뀔 수 없으므로 낡은 기준선도 유효하다.
    바뀌었다면 **회귀로 단정하지 않는다**(`-309`)."""
    if not (a and b):
        return None
    try:
        r = subprocess.run(["git", "diff", "--name-only", a, b, "--", "METAGAME/"],
                           capture_output=True, text=True, timeout=30)
    except Exception as e:  # noqa: BLE001 — git 이 없거나 sha 를 모르면 판단하지 않는다
        print(f"[warn] 기준선 신선도 확인 실패({type(e).__name__}: {e})")
        return None
    if r.returncode != 0:
        print(f"[warn] 기준선 신선도 확인 실패(git diff {r.returncode}) — {r.stderr.strip()[:120]}")
        return None
    return bool(r.stdout.strip())



def baseline_ref(target: str | None) -> str:
    """기준선을 읽을 브랜치. 릴리즈 MR(대상 master)은 develop 을 쓴다 — master 에서는 이 잡이 돈 적이 없어
    기준선이 없고(그래서 항상 '판단 불가' 였다, 2026-09-07 !240), 릴리스 브랜치의 내용은 develop 의
    스냅샷이라 develop 의 마지막 결과가 곧 기준선이다 (S15P21B202-325)."""
    t = (target or "").strip()
    return "develop" if t in ("", "master") else t


def main(argv: list[str] | None = None) -> int:
    banner("unity-report")
    argv = list(argv if argv is not None else sys.argv[1:])

    # 플래그를 먼저 떼어 낸다 — 위치 인자 셋(결과·로그·Unity 종료코드)은 그대로 둔다.
    #   --recheck-plan <파일>  새 실패 이름을 그 파일에 적고 **판정도 코멘트도 하지 않는다**
    #   --recheck <xml>        필터 재실행 결과(여러 번 줄 수 있다)
    #   --recheck-dir <디렉터리>  그 안의 unity-recheck-*.xml 을 스스로 모은다
    #   --baseline-from <파일>    계획 단계가 적어 둔 기준선을 그대로 쓴다(재조회 안 함)
    plan_path = ""
    recheck_paths: list[str] = []
    recheck_dir = ""
    baseline_from = ""
    rest: list[str] = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--recheck-plan" and i + 1 < len(argv):
            plan_path = argv[i + 1]; i += 2; continue
        if a == "--recheck" and i + 1 < len(argv):
            recheck_paths.append(argv[i + 1]); i += 2; continue
        if a == "--recheck-dir" and i + 1 < len(argv):
            recheck_dir = argv[i + 1]; i += 2; continue
        if a == "--baseline-from" and i + 1 < len(argv):
            baseline_from = argv[i + 1]; i += 2; continue
        rest.append(a); i += 1
    argv = rest

    # 디렉터리로 받으면 셸이 인자를 이어 붙이지 않아도 된다 — 경로에 공백이 섞여도
    # 조용히 깨지지 않는다(`!272` AI 리뷰). 회차 순서대로 읽는다.
    if recheck_dir:
        found = sorted(glob.glob(os.path.join(recheck_dir, "unity-recheck-*.xml")))
        if found:
            print(f"재실행 결과 {len(found)}개를 {recheck_dir} 에서 모았다")
        recheck_paths += found

    # 계획 단계는 판정을 하지 않으므로 재실행 결과를 받아도 쓸 곳이 없다. 두 옵션을 함께
    # 주면 **조용히 무시되어 통과처럼 보인다** — 인자 조합 오류를 말한다(`!272` AI 리뷰).
    if plan_path and recheck_paths:
        print(f"[warn] --recheck-plan 과 재실행 옵션을 함께 줬다 — 계획 단계는 판정하지 "
              f"않으므로 재실행 결과 {len(recheck_paths)}개를 쓰지 않는다. 잡 스크립트를 확인하라")

    nunit_path = argv[0] if argv else "unity-results.xml"
    log_path = argv[1] if len(argv) > 1 else "unity.log"
    # 세 번째 인자는 Unity 의 원시 종료 코드. 판정을 못 할 때만 쓴다(환경 문제 구분).
    try:
        unity_rc = int(argv[2]) if len(argv) > 2 and str(argv[2]).strip() else 0
    except ValueError:
        unity_rc = 0
    where = os.path.dirname(nunit_path) or "."

    # 검사를 아예 못 한 경우 — 잡이 그 사유를 `UNITY_ABORTED` 로 넘긴다.
    # **조용히 끝내지 않는다**: 판정 2 는 비차단이라 파이프라인이 초록인데 코멘트가 없으면
    # "문서만 바뀌어 잡이 안 돌았다" 와 구분되지 않는다 — `-335` 가 AI 리뷰에서 고친 것과
    # 같은 종류의 침묵이다 (S15P21B202-342 · `!259` 5차 AI 리뷰).
    aborted = (env("UNITY_ABORTED", "") or "").strip()
    if aborted:
        print(f"검사하지 못했다: {aborted}")
        note = "\n".join([
            MARKER,
            "⚠ **Unity 검사를 하지 못했습니다** — 결과가 없습니다",
            "",
            f"사유: {aborted}",
            "",
            "파이프라인을 재실행하면 새 워크스페이스에서 다시 시도합니다. 이 판정은 머지를 "
            "막지 않으므로, **이 MR 의 Unity 검사는 아직 이뤄지지 않았다는 뜻입니다.**",
        ])
        st0, _ = GitLab().upsert_note(MARKER, note)
        print(note)
        print("코멘트 " + ("게시" if st0 in (200, 201) else f"실패 {st0}"))
        print(f"판정 {UNKNOWN} → {write_verdict(where, UNKNOWN)}")
        return 0

    errs: list[str] = []
    if os.path.isfile(log_path):
        with open(log_path, encoding="utf-8", errors="replace") as f:
            errs = compile_errors(f.read())

    if not os.path.isfile(nunit_path) or os.path.getsize(nunit_path) == 0:
        print(f"결과 파일이 없다: {nunit_path}")
        if errs:
            print(f"컴파일 오류 {len(errs)}건 — 테스트가 돌지 못한 것이다")
            data = {"total": 0, "passed": 0, "failed": 0, "skipped": 0, "duration": 0.0, "cases": []}
        else:
            # 결과도 없고 컴파일 오류도 없다 = 판정 근거가 아무것도 없다. 회귀로 단정하지 않는다.
            print("컴파일 오류도 없다 — 잡이 결과를 남기기 전에 죽은 것이다")
            print(f"판정 {UNKNOWN} → {write_verdict(where, UNKNOWN)}")
            return 0
    else:
        with open(nunit_path, encoding="utf-8", errors="replace") as f:
            data = parse_nunit(f.read())
        out = os.path.join(os.path.dirname(nunit_path) or ".", "unity-junit.xml")
        with open(out, "w", encoding="utf-8") as f:
            f.write(to_junit(data))
        print(f"JUnit 변환: {out} (총 {data['total']} · 실패 {data['failed']})")

    gl = GitLab()
    tgt = (env("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", "") or "").strip()
    ref = baseline_ref(env("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", "develop"))
    bmeta: dict = {}
    base: set | None = None
    pinned = False
    if baseline_from and os.path.isfile(baseline_from):
        try:
            with open(baseline_from, encoding="utf-8") as f:
                saved = json.load(f)
            raw = saved.get("base")
            base = None if raw is None else set(raw)
            bmeta.update(saved.get("meta") or {})
            pinned = True
            print(f"기준선을 계획 단계 값으로 고정한다: {baseline_from} "
                  f"(실패 {'없음' if base is None else len(base)}건)")
        except (ValueError, TypeError, OSError) as e:
            # **조용히 넘기지 않는다.** 고정에 실패하면 다시 조회하는데, 그러면 계획과
            # 판정이 어긋날 창이 다시 열린다 — 그 사실이 로그에 있어야 한다.
            print(f"[warn] 기준선 고정 파일을 읽지 못했다({type(e).__name__}: {e}) — 다시 조회한다")
    elif baseline_from:
        print(f"[warn] 기준선 고정 파일이 없다: {baseline_from} — 다시 조회한다")
    if not pinned:
        base = baseline_failures(gl, ref, meta=bmeta)
    if base is None:
        print(f"기준선 없음 — `{ref}` 에서 이 잡이 돈 결과를 찾지 못했다")
    else:
        print(f"기준선: `{ref}` 실패 {len(base)}건")

    # 기준선에 없던 실패 — 재실행 대상이자 판정의 근거 (S15P21B202-347).
    new_now: set = set() if (base is None or errs) else (failed_names(data) - base)

    # ── 계획 단계: 재실행할 이름만 적고 끝낸다 ────────────────────────────
    # 판정도 코멘트도 하지 않는다 — 잡이 재실행을 돌린 뒤 이 스크립트를 다시 부른다.
    if plan_path:
        with open(plan_path, "w", encoding="utf-8") as f:
            for name in sorted(new_now):
                f.write(name + "\n")
        # `-testFilter` 값도 여기서 만든다 — 정규식 이스케이프가 필요하다.
        here = os.path.dirname(plan_path) or where
        filt_at = os.path.join(here, RECHECK_FILTER)
        try:
            with open(filt_at, "w", encoding="utf-8") as f:
                f.write(testfilter_value(new_now))
        except OSError as e:
            print(f"[warn] -testFilter 값을 쓰지 못했다({type(e).__name__}: {e}) — 재실행이 생략된다")

        # 최종 판정이 **같은 기준선**을 쓰도록 못 박는다 (`!272` AI 리뷰).
        saved_at = os.path.join(here, BASELINE_FILE)
        try:
            with open(saved_at, "w", encoding="utf-8") as f:
                json.dump({"base": None if base is None else sorted(base),
                           "meta": bmeta, "ref": ref}, f, ensure_ascii=False)
            print(f"기준선을 고정해 뒀다: {saved_at}")
        except OSError as e:
            print(f"[warn] 기준선을 고정하지 못했다({type(e).__name__}: {e}) — "
                  f"최종 판정이 다시 조회하므로 그 사이 develop 이 바뀌면 어긋날 수 있다")
        if not new_now:
            print("재실행 계획: 새 실패가 없다 — 재실행하지 않는다"
                  + ("(컴파일 오류가 있어 테스트 얘기가 아니다)" if errs else ""))
        else:
            print(f"재실행 계획: 새 실패 {len(new_now)}건 → {plan_path}")
            for name in sorted(new_now):
                print(f"  · {name}")
        return 0

    # ── 재실행 결과로 플래키를 가린다 ────────────────────────────────────
    flaky: set = set()
    unverified: set = set()
    if recheck_paths:
        flaky, still, unverified = classify_rechecks(new_now, recheck_paths)
        print(f"재실행 판별: 플래키 {len(flaky)}건 · 계속 실패 {len(still)}건 · "
              f"확인 못함 {len(unverified)}건")
        for name in sorted(flaky):
            print(f"  플래키(판정에서 뺀다): {name}")
        for name in sorted(unverified):
            print(f"  확인 못함(회귀로 둔다): {name}")

    code, why = decide(data, errs, base, unity_rc, flaky=flaky)
    # 낡은 기준선으로 회귀를 단정하지 않는다 (S15P21B202-342 · metagame_changed 참고).
    # 이제 develop 을 합친 결과를 검사하므로 develop 의 실패를 함께 물려받는데, 기준선이
    # 그보다 옛것이면 남의 실패가 내 몫으로 계산된다. 그 경우 차단(1) 대신 경고(2)로 낮춘다.
    stale = None
    # **컴파일 오류에는 이 완화를 걸지 않는다(R2).** `decide` 는 컴파일 오류에도
    # REGRESSION 을 주는데, 여기서 조건 없이 낮추면 기준선이 낡았을 때 **컴파일이 깨진
    # MR 이 판정 2(비차단)로 통과한다** — 문서가 확정한 R2 와 정면으로 어긋나고 09-02
    # 사고 경로(컴파일 깨진 채 머지되어 develop 2시간 37분 정지)가 다시 열린다.
    # `!259` 3차 AI 리뷰가 높음으로 잡았다. R1 은 **테스트 실패에만** 적용된다.
    # 그리고 **기준선 브랜치와 머지 대상이 같을 때만** 적용한다. 릴리스 MR(대상 master)은
    # baseline_ref 가 develop 을 쓰는데(-325) 합친 base 는 master 끝이라, 두 값이 서로 다른
    # 브랜치 위에 있다. 그 둘을 diff 하면 METAGAME 이 거의 항상 다르므로 R1 이 상시 발동해
    # **릴리스 MR 의 차단이 통째로 무력화된다**(`!259` 3차 AI 리뷰). 그때는 완화하지 않는다.
    if code == REGRESSION and not errs and ref == tgt:
        merged = (env("UNITY_MERGED_BASE", "") or "").strip()
        bsha = bmeta.get("sha", "")
        stale = metagame_changed(bsha, merged) if merged else None
        if stale:
            print(f"기준선({bsha})이 합친 base({merged})보다 낡았고 그 사이 METAGAME 이 "
                  f"바뀌었다 — 새 실패를 이 MR 탓으로 단정하지 않는다")
            code = UNKNOWN
            why = ("기준선이 합친 base 보다 낡았다 — 그 사이 develop 이 만든 실패와 "
                   "이 MR 이 만든 실패를 가릴 수 없다")
        elif stale is False:
            # **판단했다는 사실을 남긴다.** 아무 것도 안 찍으면 "검사했는데 신선했다" 와
            # "이 검사가 아예 안 돌았다" 를 로그로 가릴 수 없다 — 조용한 검사는 없는
            # 검사와 같다(S15P21B202-342 · -335 와 같은 교훈).
            print(f"기준선 신선도: 합친 base({merged})까지 METAGAME 변경 없음 — "
                  f"기준선({bsha})이 유효하다")
        else:
            print(f"기준선 신선도: 판단하지 않았다"
                  f"({'합친 base 없음' if not merged else '확인 실패'}) — 판정을 그대로 둔다")
    elif code == REGRESSION and not errs and ref != tgt:
        print(f"기준선 신선도: 적용하지 않는다 — 기준선 브랜치({ref})와 머지 대상({tgt})이 "
              f"다르다(릴리스 MR). 차단을 그대로 둔다")
    label = {PASS: "통과", REGRESSION: "이 MR 이 만든 문제 — 막는다", UNKNOWN: "판단 불가 — 경고"}[code]
    print(f"판정 {code} ({label}): {why}")
    write_verdict(where, code)

    text = body(data, errs, base, ref, stale_base=bmeta if stale else None, flaky=flaky)
    if gl.can_write:
        st, _ = gl.upsert_note(MARKER, text)
        print(f"MR 코멘트 {'갱신' if st == 200 else '작성' if st == 201 else f'실패({st})'}")
    else:
        print("GITLAB_TOKEN·MR iid 없음 → 코멘트 생략. 아래는 남겼을 내용:")
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
