r"""apply_rules17 — R6·R7 의 소스 브랜치 정규식에 release/ 를 넣는다 (S15P21B202-325).

    ^(?:hotfix/)?   →   ^(?:hotfix/|release/)?      (규칙 JSON 안의 모든 문자열에서)

왜: 릴리스 브랜치 `release/<키>-vX.Y.Z` 를 master 에 머지하면 R6(머지→완료)·R7(닫힘 알림)이 소스 브랜치에서
이슈 키를 못 뽑아 릴리스 이슈가 자동 완료되지 않는다. docs/git-convention.md §8 · jira-gitlab-automation.md 부록 A.

실행 (PowerShell) — 예전 apply_rules14 와 같은 방식:
    cd "$env:LOCALAPPDATA\Temp\claude\c--Users-SSAFY-Desktop-S15P21B202\bbf49f1d-4830-4338-94ea-d69f8e3f1133\scratchpad\pw"
    .\.venv\Scripts\python.exe apply_rules17.py            # dry-run — 바뀔 곳만 보여 준다. 아무것도 쓰지 않는다
    .\.venv\Scripts\python.exe apply_rules17.py --apply     # 보여 준 뒤 Enter 를 누르면 PUT 한다

흐름 — **Enter 를 누를 일이 없다.** 창이 열리고, 로그인이 끝나면 스크립트가 알아서 이어서 한다.
  1. 크롬 창이 열린다(프로필은 ../chrome-profile 재사용) → 로그인돼 있지 않으면 **그 창에서 직접 로그인**한다
     (자격증명은 이 스크립트가 만지지 않는다. 한 번 로그인하면 그 프로필에 남아 다음 실행부터 바로 진행된다)
  2. 규칙 조회가 200 이 될 때까지 1초마다 확인한다(최대 5분) — 그게 곧 로그인 완료 신호다
  3. R6·R7 상세를 받아 ../rules/before-<id>.json 저장 → 바뀔 문자열을 경로와 함께 찍는다
  4. --apply 면 그대로 PUT → 다시 GET 해 새/옛 정규식 개수를 확인하고 after-<id>.json 저장

규칙은 **id 로** 못박는다(R6 5793966 · R7 5800235). 다른 규칙은 읽지도 쓰지도 않는다.
내부 API 는 API 토큰이 403 이고 브라우저 로그인 세션만 통한다(부록 A, 2026-08-27 실측).
"""
import json
import os
import sys

CLOUD_ID = "15a21def-11f5-4358-ab48-074194685963"
PROJECT_ID = "22557"
SITE = "https://ssafy.atlassian.net"
BASE = f"{SITE}/gateway/api/automation/internal-api/jira/{CLOUD_ID}/pro/rest"

HERE = os.path.dirname(os.path.abspath(__file__))
SCRATCH = os.path.dirname(HERE)                       # …/scratchpad
OUT = os.path.join(SCRATCH, "rules")
PROFILE = os.path.join(SCRATCH, "chrome-profile")     # dump_rules.py 가 쓰던 프로필을 그대로 재사용

RULES = {5793966: "R6 MR 병합 → 완료", 5800235: "R7 MR 닫힘 알림"}
OLD = "^(?:hotfix/)?"
NEW = "^(?:hotfix/|release/)?"


def walk_replace(node, path="$"):
    """모든 문자열에서 OLD → NEW. (바뀐 위치 목록, 새 노드)."""
    hits = []
    if isinstance(node, str):
        if OLD in node:
            hits.append((path, node))
            return hits, node.replace(OLD, NEW)
        return hits, node
    if isinstance(node, list):
        out = []
        for i, v in enumerate(node):
            h, nv = walk_replace(v, f"{path}[{i}]")
            hits += h
            out.append(nv)
        return hits, out
    if isinstance(node, dict):
        out = {}
        for k, v in node.items():
            h, nv = walk_replace(v, f"{path}.{k}")
            hits += h
            out[k] = nv
        return hits, out
    return hits, node


def count(node, needle):
    if isinstance(node, str):
        return node.count(needle)
    if isinstance(node, list):
        return sum(count(v, needle) for v in node)
    if isinstance(node, dict):
        return sum(count(v, needle) for v in node.values())
    return 0


def component_types(node, acc=None):
    """규칙 안 컴포넌트 타입 — 어떤 규칙인지 눈으로 확인하는 용도."""
    acc = acc if acc is not None else []
    if isinstance(node, dict):
        t = node.get("type")
        if isinstance(t, str) and "." in t:
            acc.append(t)
        for v in node.values():
            component_types(v, acc)
    elif isinstance(node, list):
        for v in node:
            component_types(v, acc)
    return acc


def find_uuid(cfg):
    for k in ("ruleUuid", "uuid", "ruleUUID"):
        if isinstance(cfg.get(k), str):
            return cfg[k]
    for k, v in cfg.items():
        if "uuid" in k.lower() and isinstance(v, str):
            return v
    return None


def wait_login(page, probe_id, timeout_s=300):
    """규칙 조회가 200 이 될 때까지 기다린다 = 로그인 완료 감지.

    예전 스크립트처럼 Enter 를 요구하지 않는다. `input()` 으로 Playwright 동기 API 를 붙잡고 있으면
    그 뒤 요청이 막히는 경우가 있어(2026-09-07 apply 단계가 진행되지 않았다) 폴링으로 바꿨다."""
    said = False
    for i in range(timeout_s):
        r = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{probe_id}")
        if r.ok:
            if said:
                print("로그인 확인됨 — 이어서 진행합니다.")
            return True
        if not said:
            print(f"열린 크롬 창에서 로그인하세요 (규칙 조회 {r.status}). 끝나면 자동으로 진행합니다 — 최대 {timeout_s}초 대기.")
            said = True
        page.wait_for_timeout(1000)
    print(f"❌ {timeout_s}초 안에 로그인이 확인되지 않았습니다.")
    return False


def save(name, data):
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, name)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    return p


def main() -> int:
    apply = "--apply" in sys.argv
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("playwright 가 없습니다:  .\\.venv\\Scripts\\python.exe -m pip install playwright")
        return 2

    with sync_playwright() as pw:
        ctx = pw.chromium.launch_persistent_context(PROFILE, channel="chrome", headless=False)
        page = ctx.pages[0] if ctx.pages else ctx.new_page()
        page.goto(f"{SITE}/jira/software/c/projects/S15P21B202/settings/automate")
        if not wait_login(page, next(iter(RULES))):
            ctx.close()
            return 1

        plan = []
        for rid, label in RULES.items():
            r = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{rid}")
            if not r.ok:
                print(f"❌ {label}({rid}) 조회 실패 {r.status} — 로그인이 안 됐거나 권한이 없습니다.\n{r.text()[:300]}")
                ctx.close()
                return 1
            raw = r.json()
            cfg = raw.get("ruleConfigBean") if isinstance(raw, dict) and isinstance(raw.get("ruleConfigBean"), dict) else raw
            before = save(f"before-{rid}.json", raw)
            name = cfg.get("name") or label
            uuid = find_uuid(cfg)
            hits, new_cfg = walk_replace(cfg)
            print(f"\n■ {label} — 규칙 '{name}' · id {rid} · uuid {uuid or '(못 찾음)'}")
            print(f"  컴포넌트: {', '.join(sorted(set(component_types(cfg))))}")
            print(f"  저장: {before}")
            if not hits:
                print(f"  바꿀 곳 없음 — '{OLD}' 없음 (release/ 포함 문자열 {count(cfg, 'release/')}개). "
                      f"이미 적용됐거나 조건 문구가 다릅니다")
                continue
            print(f"  바꿀 곳 {len(hits)}개:")
            for path, s in hits:
                print(f"    - {path}\n        {s[:160]}")
            if not uuid:
                print("  ❌ uuid 를 못 찾아 PUT 불가 — before 파일에서 uuid 키 이름을 확인해 find_uuid 를 고치세요")
                continue
            plan.append((rid, name, uuid, new_cfg, len(hits)))

        if not plan:
            print("\n적용할 것이 없습니다.")
            ctx.close()
            return 0
        if not apply:
            print(f"\n(dry-run) 위 {sum(n for *_, n in plan)}곳을 바꾸려면 --apply 로 다시 실행하세요. 아무것도 쓰지 않았습니다.")
            ctx.close()
            return 0

        print(f"\n위 {len(plan)}개 규칙에 PUT 합니다.")
        rc = 0
        for rid, name, uuid, new_cfg, n_hits in plan:
            body = {"ruleConfigBean": new_cfg, "newConnections": []}
            print(f"  PUT {BASE}/{PROJECT_ID}/rule/{uuid}  ({len(json.dumps(body))}바이트)")
            r = page.request.put(f"{BASE}/{PROJECT_ID}/rule/{uuid}",
                                 data=json.dumps(body, ensure_ascii=False),
                                 headers={"Content-Type": "application/json"})
            if not r.ok:
                print(f"❌ {name}: PUT {r.status}\n{r.text()[:1200]}")
                rc = 1
                continue
            page.wait_for_timeout(1500)
            v = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{rid}")
            after = v.json() if v.ok else {}
            save(f"after-{rid}.json", after)
            acfg = after.get("ruleConfigBean") if isinstance(after, dict) and isinstance(after.get("ruleConfigBean"), dict) else after
            n_new, n_old = count(acfg, NEW), count(acfg, OLD)
            ok = n_new >= n_hits and n_old == 0
            print(f"{'✅' if ok else '⚠'} {name}: PUT {r.status} · 다시 읽음 — 새 정규식 {n_new}곳 · 옛 정규식 {n_old}곳"
                  + ("" if ok else " — 규칙 화면에서 직접 확인하세요"))
            if not ok:
                rc = 1
        print("\n끝. 자동화 목록에서 두 규칙이 '사용 중' 인지 확인하세요(PUT 이 상태를 바꾸지는 않습니다).")
        print("첫 실전 확인: release/ 브랜치 MR 이 머지될 때 R6 가 그 이슈를 완료시키는지 — 감사 로그.")
        ctx.close()
        return rc


if __name__ == "__main__":
    raise SystemExit(main())
