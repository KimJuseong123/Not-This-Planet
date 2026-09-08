r"""apply_rules18 — R10(릴리스 발행 → fixVersion·버전 릴리스)을 우리가 정한 형태로 맞춘다 (S15P21B202-327).

CI(`scripts/ci/publish_release.py`)가 GitLab Release 를 낸 뒤 이 규칙의 수신 웹훅으로 보내는 본문:

    {"issues": ["S15P21B202-313", "S15P21B202-315"],
     "data": {"version": "v0.1.0", "releaseUrl": "https://…/-/releases/v0.1.0", "date": "2026-09-07"}}

맞추는 것 — 액션 셋을 **이 순서**로:
  1) jira.version.create   versionName = {{webhookData.version}}                 (없으면 버전을 만든다)
  2) jira.issue.edit       advancedFields 로 fixVersions **add**                 (기존 값을 지우지 않는다)
  3) jira.version.release  versionName = {{webhookData.version}} · 날짜 = {{webhookData.date}}

왜 편집을 `operations` 가 아니라 `advancedFields` 로 하나: `operations` 의 값 항목 타입(스마트값을 어떤
`type` 으로 싣는지)이 문서화돼 있지 않다. `advancedFields` 는 Jira REST 편집 JSON 문자열이고 **add 로 기존
값을 유지하는 형태가 부록 A 에 실측으로 남아 있다**(labels 예시). 추측 대신 아는 길을 쓴다.

실행 (PowerShell):
    cd "$env:LOCALAPPDATA\Temp\claude\c--Users-SSAFY-Desktop-S15P21B202\<세션>\scratchpad\pw"
    .\.venv\Scripts\python.exe apply_rules18.py            # dry-run — 바뀔 것만 보여 준다
    .\.venv\Scripts\python.exe apply_rules18.py --apply    # PUT 하고 다시 읽어 검증

트리거는 손대지 않는다. 다만 GET 이 `webhookToken` 을 마스킹해서 주므로, 그대로 PUT 하면 토큰이
별표로 덮여 **웹훅 URL 이 깨질 수 있다** → `tags` 의 `WEBHOOK_TOKEN` 실제 값으로 복원해서 보낸다(값은 찍지 않는다).
"""
import argparse
import json
import os
import sys
import uuid as uuidlib

CLOUD_ID = "15a21def-11f5-4358-ab48-074194685963"
PROJECT_ID = "22557"
SITE = "https://ssafy.atlassian.net"
BASE = f"{SITE}/gateway/api/automation/internal-api/jira/{CLOUD_ID}/pro/rest"

HERE = os.path.dirname(os.path.abspath(__file__))
SCRATCH = os.path.dirname(HERE)
OUT = os.path.join(SCRATCH, "rules")
PROFILE = os.path.join(SCRATCH, "chrome-profile")

RULE_ID = "01a07c26-15a4-7b2d-b828-e0866f942f1b"     # R10 (uuid — 새 UI 로 만든 규칙은 주소창에 이것만 나온다)
RULE_NAME = "R10 릴리스 발행 → fixVersion·버전 릴리스"
VERSION_SV = "{{webhookData.version}}"
DATE_SV = "{{webhookData.date}}"
PROJECT_REF = {"type": "ID", "value": PROJECT_ID, "additional": "software"}
# fixVersions 를 **더한다**(SET 이 아니다). 기존 fixVersion 이 있는 이슈의 값을 지우면 안 된다.
ADVANCED_FIELDS = json.dumps({"update": {"fixVersions": [{"add": {"name": VERSION_SV}}]}}, ensure_ascii=False)

WANT_TYPES = ["jira.version.create", "jira.issue.edit", "jira.version.release"]


def new_id() -> str:
    """새 컴포넌트 id — 이 형식이어야 한다. null·임의 UUID 는 '컴포넌트 ID 불일치/중복' 400 (부록 A)."""
    return f"__NEW__COMPONENT__{uuidlib.uuid4()}"


def wait_login(page, rule_id, timeout_s=300):
    said = False
    for _ in range(timeout_s):
        r = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{rule_id}")
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


def save(name, data):
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, name)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    return p


def shell(cfg, type_name):
    """기존 컴포넌트가 있으면 그 id·부속 필드를 물려받는다(없으면 새로 만든다) — 최소 변경."""
    for c in cfg.get("components") or []:
        if c.get("type") == type_name:
            out = dict(c)
            out["value"] = {}
            return out, False
    return ({"id": new_id(), "parentId": None, "conditionParentId": None, "component": "ACTION",
             "type": type_name, "conditions": [], "children": [], "schemaVersion": 1,
             "connectionId": None, "checksum": None}, True)


def build(cfg):
    """새 ruleConfigBean 과 사람이 읽을 변경 목록."""
    notes = []
    new = json.loads(json.dumps(cfg))            # 깊은 복사

    if new.get("name") != RULE_NAME:
        notes.append(f"이름: {new.get('name')!r} → {RULE_NAME!r}")
        new["name"] = RULE_NAME

    # 트리거 — 타입만 확인하고 값은 유지. 마스킹된 토큰은 tags 의 실제 값으로 복원한다
    trg = new.get("trigger") or {}
    if trg.get("type") != "jira.incoming.webhook":
        return None, [f"❌ 트리거가 수신 웹훅이 아닙니다: {trg.get('type')!r} — UI 에서 먼저 바꿔야 합니다"]
    tval = trg.setdefault("value", {})
    if tval.get("searchOrProvide") != "provided":
        notes.append(f"트리거 업무 항목 기준: {tval.get('searchOrProvide')!r} → 'provided'(본문에서 제공)")
        tval["searchOrProvide"] = "provided"
    tok = next((t.get("tagValue") for t in (new.get("tags") or []) if t.get("tagType") == "WEBHOOK_TOKEN"), None)
    if isinstance(tval.get("webhookToken"), str) and set(tval["webhookToken"]) <= {"*"}:
        if tok:
            tval["webhookToken"] = tok
            notes.append("트리거 webhookToken: 마스킹된 값을 tags 의 실제 값으로 복원(값은 표시하지 않음)")
        else:
            return None, ["❌ webhookToken 이 마스킹돼 있고 tags 에 실제 값도 없습니다 — 그대로 PUT 하면 웹훅이 깨집니다"]

    create, is_new_c = shell(new, "jira.version.create")
    create["value"] = {"versionName": VERSION_SV, "releaseDate": None, "overrideReleaseDate": "SET_ON_EMPTY",
                       "project": PROJECT_REF, "startDate": None, "description": None}
    notes.append(("액션 추가" if is_new_c else "액션 갱신") + f" 1) jira.version.create · versionName = {VERSION_SV}")

    edit, is_new_e = shell(new, "jira.issue.edit")
    old_ops = next((c.get("value", {}).get("operations") for c in (cfg.get("components") or [])
                    if c.get("type") == "jira.issue.edit"), None)
    edit["value"] = {"operations": [], "advancedFields": ADVANCED_FIELDS, "sendNotifications": False}
    if old_ops:
        notes.append(f"   ↳ 기존 operations {len(old_ops)}개(SET 모드)를 비우고 advancedFields 로 대체 — fixVersions **add**")
    notes.append(("액션 추가" if is_new_e else "액션 갱신") + f" 2) jira.issue.edit · advancedFields = {ADVANCED_FIELDS}")

    rel, is_new_r = shell(new, "jira.version.release")
    rel["value"] = {"versionName": VERSION_SV, "releaseDate": DATE_SV,
                    "overrideReleaseDate": "SET_ON_EMPTY", "project": PROJECT_REF}
    notes.append(("액션 추가" if is_new_r else "액션 갱신")
                 + f" 3) jira.version.release · versionName = {VERSION_SV} · releaseDate = {DATE_SV}")

    before_types = [c.get("type") for c in (cfg.get("components") or [])]
    new["components"] = [create, edit, rel]
    if before_types != WANT_TYPES:
        notes.append(f"액션 순서: {before_types} → {WANT_TYPES}")
    return new, notes


def verify(cfg):
    """PUT 뒤 다시 읽은 것이 의도대로인지. (성공?, 사람이 읽을 줄들)"""
    lines, ok = [], True
    types = [c.get("type") for c in (cfg.get("components") or [])]
    good = types == WANT_TYPES
    ok &= good
    lines.append(f"{'✅' if good else '❌'} 액션 순서·타입: {types}")
    by = {c.get("type"): (c.get("value") or {}) for c in (cfg.get("components") or [])}
    for t, key, want in (("jira.version.create", "versionName", VERSION_SV),
                         ("jira.version.release", "versionName", VERSION_SV),
                         ("jira.version.release", "releaseDate", DATE_SV)):
        got = by.get(t, {}).get(key)
        good = got == want
        ok &= good
        lines.append(f"{'✅' if good else '❌'} {t}.{key} = {got!r}")
    af = by.get("jira.issue.edit", {}).get("advancedFields") or ""
    good = "webhookData.version" in af and '"add"' in af and "fixVersions" in af
    ok &= good
    lines.append(f"{'✅' if good else '❌'} jira.issue.edit.advancedFields = {af[:120]}")
    ops = by.get("jira.issue.edit", {}).get("operations")
    good = not ops
    ok &= good
    lines.append(f"{'✅' if good else '❌'} jira.issue.edit.operations 비었음(SET 모드 잔재 없음): {ops!r}")
    trg = (cfg.get("trigger") or {})
    good = trg.get("type") == "jira.incoming.webhook" and (trg.get("value") or {}).get("searchOrProvide") == "provided"
    ok &= good
    lines.append(f"{'✅' if good else '❌'} 트리거: {trg.get('type')} · 기준 {(trg.get('value') or {}).get('searchOrProvide')!r}")
    lines.append(f"   이름: {cfg.get('name')!r} · 상태 {cfg.get('state')}")
    return ok, lines


def main() -> int:
    ap = argparse.ArgumentParser(description="R10 을 정한 형태로 맞춘다")
    ap.add_argument("--apply", action="store_true", help="실제로 PUT 한다(없으면 dry-run)")
    ap.add_argument("--id", default=RULE_ID, help="규칙 id(uuid 또는 숫자)")
    a = ap.parse_args()
    rule_id = a.id

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("playwright 가 없습니다:  .\\.venv\\Scripts\\python.exe -m pip install playwright")
        return 2

    with sync_playwright() as pw:
        ctx = pw.chromium.launch_persistent_context(PROFILE, channel="chrome", headless=False)
        page = ctx.pages[0] if ctx.pages else ctx.new_page()
        page.goto(f"{SITE}/jira/software/c/projects/S15P21B202/settings/automate")
        if not wait_login(page, rule_id):
            ctx.close()
            return 1

        r = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{rule_id}")
        if not r.ok:
            print(f"❌ 규칙 조회 실패 {r.status}\n{r.text()[:300]}")
            ctx.close()
            return 1
        raw = r.json()
        cfg = raw.get("ruleConfigBean") if isinstance(raw, dict) and isinstance(raw.get("ruleConfigBean"), dict) else raw
        print(f"저장: {save('r10-before.json', raw)}")
        print(f"\n■ 지금: 이름 {cfg.get('name')!r} · 트리거 {(cfg.get('trigger') or {}).get('type')} · "
              f"액션 {[c.get('type') for c in (cfg.get('components') or [])]}")

        new_cfg, notes = build(cfg)
        print("\n■ 바꿀 것:")
        for n in notes:
            print(f"  {n}")
        if new_cfg is None:
            ctx.close()
            return 1
        uuid_for_put = cfg.get("idUuid") or cfg.get("ruleUuid") or rule_id

        if not a.apply:
            print(f"\n(dry-run) --apply 로 다시 실행하면 PUT 합니다. 아무것도 쓰지 않았습니다.")
            ctx.close()
            return 0

        body = {"ruleConfigBean": new_cfg, "newConnections": []}
        print(f"\nPUT {BASE}/{PROJECT_ID}/rule/{uuid_for_put}  ({len(json.dumps(body))}바이트)")
        p = page.request.put(f"{BASE}/{PROJECT_ID}/rule/{uuid_for_put}",
                             data=json.dumps(body, ensure_ascii=False),
                             headers={"Content-Type": "application/json"})
        if not p.ok:
            print(f"❌ PUT {p.status}\n{p.text()[:1200]}")
            ctx.close()
            return 1
        page.wait_for_timeout(1500)
        v = page.request.get(f"{BASE}/{PROJECT_ID}/rule/{rule_id}")
        after = v.json() if v.ok else {}
        print(f"저장: {save('r10-after.json', after)}")
        acfg = after.get("ruleConfigBean") if isinstance(after, dict) and isinstance(after.get("ruleConfigBean"), dict) else after
        ok, lines = verify(acfg)
        print(f"\nPUT {p.status} · 다시 읽어 검증:")
        for line in lines:
            print(f"  {line}")
        print("\n" + ("✅ R10 이 의도한 형태입니다." if ok else "⚠ 어긋난 항목이 있습니다 — 규칙 화면에서 확인하세요."))
        print("규칙 화면에서 ❗(유효하지 않은 값)이 없는지, '사용' 상태인지 한 번 보세요.")
        print("첫 실전 확인: 릴리스가 나면 Jira 릴리스 화면에 버전·이슈가 보이는지.")
        ctx.close()
        return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
