r"""dump_rule — Jira Automation 규칙 하나를 떠서 구조를 보여 준다 (읽기 전용).

바꾸는 것은 없다. 규칙 JSON 을 저장하고, 컴포넌트 트리(타입·값 키·스마트값)를 사람이 읽을 형태로 찍는다.
규칙을 고치기 **전에** 실제 스키마를 확인하는 용도다 (교정 스크립트는 리포에 두지 않는다 — 부록 A) —
컴포넌트 타입 문자열을 추측하면 400 이거나 조용히 깨진 규칙이 된다(docs/jira-gitlab-automation.md 부록 A).

실행 (PowerShell):
    cd "$env:LOCALAPPDATA\Temp\claude\c--Users-SSAFY-Desktop-S15P21B202\<세션>\scratchpad\pw"
    .\.venv\Scripts\python.exe dump_rule.py --name R10          # 이름에 R10 이 든 규칙
    .\.venv\Scripts\python.exe dump_rule.py --id 5812345        # 숫자 id 를 알면
    .\.venv\Scripts\python.exe dump_rule.py --list              # 규칙 목록만

저장 위치: ../rules/rule-<id>.json  (전체 JSON — 이걸 보고 교정 스크립트를 쓴다)
로그인: 크롬 창이 열린다. 로그인돼 있지 않으면 그 창에서 직접 로그인하면 자동으로 이어진다(Enter 불필요).
"""
import argparse
import json
import os
import sys

CLOUD_ID = "15a21def-11f5-4358-ab48-074194685963"
PROJECT_ID = "22557"
SITE = "https://ssafy.atlassian.net"
BASE = f"{SITE}/gateway/api/automation/internal-api/jira/{CLOUD_ID}/pro/rest"

HERE = os.path.dirname(os.path.abspath(__file__))
SCRATCH = os.path.dirname(HERE)
OUT = os.path.join(SCRATCH, "rules")
PROFILE = os.path.join(SCRATCH, "chrome-profile")

# 목록 요청 본문 — **여러 형태를 시도한다.** `systemLabelId: "CONTAINER"` 만 쓰면 새로 만든 규칙이
# 그 범주에 안 잡혀 목록에서 빠진다(2026-09-07: UI 로 만든 R10 이 보이지 않았다). 결과는 id 로 합친다.
LIST_BODIES = [
    {"offset": 0, "limit": 100, "filter": "", "actionTypes": [], "triggerTypes": [],
     "orchestrationTypes": [], "sortKey": "NAME", "sortOrder": "ASC"},
    {"offset": 0, "limit": 100, "filter": "", "actionTypes": [], "triggerTypes": [],
     "orchestrationTypes": [], "systemLabelId": "CONTAINER", "sortKey": "NAME", "sortOrder": "ASC"},
    {"offset": 0, "limit": 100, "filter": ""},
]
PROBE_RULE = 5793966          # R6 — 로그인 확인용(읽기만)


def wait_login(page, timeout_s=300):
    """규칙 조회가 200 이 될 때까지 폴링 = 로그인 완료 감지. input() 을 쓰면 이후 요청이 막힌다(부록 A)."""
    said = False
    for _ in range(timeout_s):
        r = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{PROBE_RULE}")
        if r.ok:
            if said:
                print("로그인 확인됨 — 이어서 진행합니다.")
            return True
        if not said:
            print(f"열린 크롬 창에서 로그인하세요 (조회 {r.status}). 끝나면 자동으로 진행합니다 — 최대 {timeout_s}초.")
            said = True
        page.wait_for_timeout(1000)
    print(f"❌ {timeout_s}초 안에 로그인이 확인되지 않았습니다.")
    return False


def rules_list(page):
    """규칙 목록. 본문 형태를 여러 개 시도해 id 로 합친다 — 필터 하나에 기대면 새 규칙이 빠진다."""
    found: dict = {}
    for i, body in enumerate(LIST_BODIES):
        r = page.request.post(f"{BASE}/{PROJECT_ID}/rules", data=json.dumps(body),
                              headers={"Content-Type": "application/json"})
        if not r.ok:
            print(f"  목록 조회 {i + 1}/{len(LIST_BODIES)}: 실패 {r.status} {r.text()[:120]}")
            continue
        d = r.json()
        # 항목은 **values** 에 온다 (2026-09-08 실측: 최상위 키 limit·links·offset·total·values).
        # data/rules 로만 찾다가 total=10 인데 0건으로 읽혔다 — `--name` 으로 규칙을 못 찾던 원인이
        # 이것이었다(전에는 systemLabelId 필터 탓으로 짐작했는데 그쪽이 아니었다).
        rules = d.get("values") or d.get("data") or d.get("rules") or d
        if isinstance(rules, dict):
            rules = rules.get("rules", [])
        rules = rules if isinstance(rules, list) else []
        new = 0
        for rule in rules:
            rid = rule.get("id")
            if rid is not None and rid not in found:
                found[rid] = rule
                new += 1
        label = "systemLabelId 없음" if "systemLabelId" not in body else ("CONTAINER" if body.get("systemLabelId") else "최소")
        print(f"  목록 조회 {i + 1}/{len(LIST_BODIES)} ({label}): {len(rules)}개 · 새로 {new}개"
              + (f" · total={d.get('total')}" if isinstance(d, dict) and d.get("total") is not None else ""))
    return list(found.values())


def brief(value, cap=200):
    """값을 한 줄로. 스마트값·정규식이 그대로 보이는 게 목적이라 자르되 원문 유지."""
    if isinstance(value, str):
        s = value.replace("\n", "⏎")
        return s if len(s) <= cap else s[:cap] + f"…({len(value)}자)"
    if isinstance(value, (int, float, bool)) or value is None:
        return repr(value)
    if isinstance(value, list):
        return f"[{len(value)}개]"
    if isinstance(value, dict):
        return "{" + ", ".join(sorted(value)[:8]) + ("…}" if len(value) > 8 else "}")
    return type(value).__name__


def show(node, depth=0, label="", index=None):
    """컴포넌트 트리를 찍는다. type 이 있는 dict 를 컴포넌트로 본다."""
    pad = "  " * depth
    if isinstance(node, dict):
        t = node.get("type")
        if isinstance(t, str) and "." in t:
            tag = f"[{index}] " if index is not None else ""
            print(f"{pad}{tag}▸ {t}")
            val = node.get("value")
            if isinstance(val, dict):
                for k in sorted(val):
                    print(f"{pad}    {k} = {brief(val[k])}")
            elif val is not None:
                print(f"{pad}    value = {brief(val)}")
            for key in ("components", "children", "conditions", "actions"):
                sub = node.get(key)
                if isinstance(sub, list) and sub:
                    print(f"{pad}    └ {key}:")
                    for i, c in enumerate(sub):
                        show(c, depth + 3, index=i)
            return
        for k, v in node.items():
            if k in ("components", "children", "conditions", "actions") and isinstance(v, list):
                print(f"{pad}{k}:")
                for i, c in enumerate(v):
                    show(c, depth + 1, index=i)
    elif isinstance(node, list):
        for i, c in enumerate(node):
            show(c, depth, index=i)


def main() -> int:
    ap = argparse.ArgumentParser(description="Jira Automation 규칙 하나를 떠서 구조를 본다(읽기 전용)")
    # 숫자 id 와 uuid 를 모두 받는다. 새 UI 로 만든 규칙은 주소창에 uuid 만 나온다
    # (2026-09-07: R10 이 그랬다). 부록 A 는 GET=숫자 id · PUT=uuid 로 적었지만 GET 도 uuid 를 받는다.
    ap.add_argument("--id", help="규칙 id — 숫자 또는 uuid(주소창의 …/rule/<이 값>)")
    ap.add_argument("--name", help="규칙 이름에 이 문자열이 들어간 것을 찾는다 (예: R10)")
    ap.add_argument("--list", action="store_true", help="규칙 목록만 찍는다")
    a = ap.parse_args()
    if not (a.id or a.name or a.list):
        ap.error("--id 또는 --name 또는 --list 중 하나가 필요합니다")

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("playwright 가 없습니다:  .\\.venv\\Scripts\\python.exe -m pip install playwright")
        return 2

    os.makedirs(OUT, exist_ok=True)
    with sync_playwright() as pw:
        ctx = pw.chromium.launch_persistent_context(PROFILE, channel="chrome", headless=False)
        page = ctx.pages[0] if ctx.pages else ctx.new_page()
        page.goto(f"{SITE}/jira/software/c/projects/S15P21B202/settings/automate")
        if not wait_login(page):
            ctx.close()
            return 1

        rid = a.id
        if a.list or (a.name and not rid):
            rules = rules_list(page)
            print(f"\n규칙 {len(rules)}개:")
            hits = []
            for r in rules:
                name = r.get("name") or ""
                state = r.get("state") or r.get("ruleState") or "?"
                mark = " "
                if a.name and a.name.lower() in name.lower():
                    mark, _ = "▶", hits.append((r.get("id"), name))
                print(f"  {mark} {r.get('id')}  [{state}]  {name}")
            if a.list and not a.name:
                ctx.close()
                return 0
            if not hits:
                print(f"\n❌ 이름에 '{a.name}' 이 든 규칙이 목록에 없습니다. 목록 API 가 새 규칙을 빠뜨리는 경우가 있으니"
                      f" **id 로 직접** 지정하세요:")
                print(f"   1) 브라우저에서 그 규칙을 클릭해 엽니다")
                print(f"   2) 주소창 끝의 숫자를 읽습니다 — …/settings/automate#/rule/<숫자>")
                print(f"   3) .\\.venv\\Scripts\\python.exe dump_rule.py --id <숫자>")
                ctx.close()
                return 1
            if len(hits) > 1:
                print(f"\n❌ '{a.name}' 이 든 규칙이 {len(hits)}개입니다 — --id 로 하나만 지정하세요: "
                      + ", ".join(str(i) for i, _ in hits))
                ctx.close()
                return 1
            rid, picked = hits[0]
            print(f"\n선택: {rid}  {picked}")

        r = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{rid}")
        if not r.ok:
            print(f"❌ 규칙 {rid} 조회 실패 {r.status}\n{r.text()[:300]}")
            print("   404 면: 규칙이 저장됐는지(초안 아님) 확인하세요. 400 이면 id 형식이 다른 것이니 주소창 값을 그대로 넘겨 주세요.")
            ctx.close()
            return 1
        raw = r.json()
        safe = "".join(c if c.isalnum() or c in "-_" else "_" for c in str(rid))[:60]
        path = os.path.join(OUT, f"rule-{safe}.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(raw, f, ensure_ascii=False, indent=2)

        cfg = raw.get("ruleConfigBean") if isinstance(raw, dict) and isinstance(raw.get("ruleConfigBean"), dict) else raw
        print(f"\n■ 규칙 {rid} — 이름 '{cfg.get('name')}' · 상태 {cfg.get('state')}")
        for k in ("ruleUuid", "uuid", "ruleUUID"):
            if cfg.get(k):
                print(f"  uuid: {cfg[k]}")
                break
        print(f"  저장: {path}  ({os.path.getsize(path)}바이트)")
        print(f"  최상위 키: {', '.join(sorted(cfg))}\n")
        show(cfg)
        print("\n이 파일을 그대로 두면 교정 스크립트를 쓸 수 있습니다.")
        ctx.close()
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
