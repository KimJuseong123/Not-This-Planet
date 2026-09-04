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

왜 "새 실패" 를 굳이 구분하나 — 매번 실패 17개를 나열하면 사람이 읽지 않게 된다. mr-lint 의
스택 경고가 그렇게 무시됐다(실측 26 대 0). 그 MR 이 바꾼 것만 보여야 읽힌다.

로컬 실행: python3 scripts/ci/unity_report.py <nunit.xml> [unity.log]
           (GITLAB_TOKEN·CI_MERGE_REQUEST_IID 가 있으면 MR 코멘트까지)
"""
import os
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape, quoteattr

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import GitLab, banner, env  # noqa: E402

MARKER = "<!-- unity-compile:metagame -->"
JOB_NAME = "unity-compile"
ARTIFACT = "unity-results.xml"
MAX_LISTED = 15          # 코멘트에 나열할 실패 개수 상한
MAX_COMPILE_ERRORS = 20


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
def baseline_failures(gl: GitLab, ref: str, max_pipelines: int = 15) -> set | None:
    """대상 브랜치에서 마지막으로 돈 unity-compile 의 실패 집합. 못 구하면 None.

    None 과 빈 집합을 구분한다 — '기준선을 못 봤다' 와 '기준선에 실패가 없다' 는 다르다.
    전자에서 새 실패를 단정하면 없는 회귀를 만들어 낸다."""
    if not (gl.token and gl.project):
        return None
    try:
        for p in gl.branch_pipelines(ref, per_page=max_pipelines):
            jobs = [j for j in gl.pipeline_jobs(p.get("id")) if j.get("name") == JOB_NAME]
            for j in sorted(jobs, key=lambda x: x.get("id") or 0, reverse=True):
                if j.get("status") not in ("success", "failed"):
                    continue
                txt = gl.job_artifact_text(j["id"], ARTIFACT)
                if not txt:
                    continue
                d = parse_nunit(txt)
                return {f'{c["cls"]}.{c["name"]}' for c in d["cases"] if c["result"] == "Failed"}
    except Exception as e:  # noqa: BLE001 — 기준선 실패가 보고 자체를 막지 않는다
        print(f"[warn] 기준선 조회 실패({type(e).__name__}: {e}) — 새/기존 구분 없이 보고한다")
    return None


# ── 코멘트 ──────────────────────────────────────────────────────────────────
def body(data: dict, errs: list[str], base: set | None, ref: str) -> str:
    now_fail = {f'{c["cls"]}.{c["name"]}': c for c in data["cases"] if c["result"] == "Failed"}
    if errs:
        head = "🔴 **Unity C# 컴파일 실패** — 테스트는 돌지 못했습니다"
        parts = [head, "", "```"] + errs + ["```", "",
                 "이 MR 을 머지하면 develop 이 컴파일되지 않습니다. "
                 "(09-02 에 같은 일로 develop 이 2시간 37분 멈췄습니다.)"]
        return MARKER + "\n" + "\n".join(parts)

    total, passed, failed = data["total"], data["passed"], data["failed"]
    if base is None:
        head = ("✅ **Unity 컴파일 통과**" if not failed else "⚠ **Unity 컴파일 통과 · EditMode 실패 있음**")
        parts = [head, "",
                 f"| 총 | 통과 | 실패 |", "|---|---|---|", f"| {total} | {passed} | {failed} |", "",
                 f"`{ref}` 의 기준 결과를 찾지 못해 **새로 생긴 실패인지 가릴 수 없었습니다**"
                 f"(그 브랜치에서 이 잡이 아직 돌지 않았을 수 있습니다)."]
        return MARKER + "\n" + "\n".join(parts)

    new = [k for k in now_fail if k not in base]
    fixed = [k for k in base if k not in now_fail]
    if not new:
        head = "✅ **Unity 컴파일 통과 · 이 MR 이 만든 테스트 실패 없음**"
    else:
        head = f"🔴 **이 MR 에서 새로 실패하는 테스트 {len(new)}개**"
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
    if fixed:
        parts += ["", f"**되살린 것** {len(fixed)}건: " + ", ".join(f"`{k.split('.')[-1]}`" for k in sorted(fixed)[:8])]
    parts += ["", "<sub>기존 실패는 담당자가 정리 중일 수 있어 나열하지 않습니다. 전체 목록은 "
              "MR 의 테스트 리포트 탭 · 잡 아티팩트 `unity-results.xml` · 자동 생성(unity-compile)</sub>"]
    return MARKER + "\n" + "\n".join(parts)


def main(argv: list[str] | None = None) -> int:
    banner("unity-report")
    argv = argv if argv is not None else sys.argv[1:]
    nunit_path = argv[0] if argv else "unity-results.xml"
    log_path = argv[1] if len(argv) > 1 else "unity.log"

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
            print("컴파일 오류도 없다 — 잡이 결과를 남기기 전에 죽은 것이다")
            return 0
    else:
        with open(nunit_path, encoding="utf-8", errors="replace") as f:
            data = parse_nunit(f.read())
        out = os.path.join(os.path.dirname(nunit_path) or ".", "unity-junit.xml")
        with open(out, "w", encoding="utf-8") as f:
            f.write(to_junit(data))
        print(f"JUnit 변환: {out} (총 {data['total']} · 실패 {data['failed']})")

    gl = GitLab()
    ref = env("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", "develop") or "develop"
    base = baseline_failures(gl, ref)
    if base is None:
        print(f"기준선 없음 — `{ref}` 에서 이 잡이 돈 결과를 찾지 못했다")
    else:
        print(f"기준선: `{ref}` 실패 {len(base)}건")

    text = body(data, errs, base, ref)
    if gl.can_write:
        st, _ = gl.upsert_note(MARKER, text)
        print(f"MR 코멘트 {'갱신' if st == 200 else '작성' if st == 201 else f'실패({st})'}")
    else:
        print("GITLAB_TOKEN·MR iid 없음 → 코멘트 생략. 아래는 남겼을 내용:")
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
