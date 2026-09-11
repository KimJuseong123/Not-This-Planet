"""publish-release — master 머지 = 릴리스. Windows 빌드를 zip 으로 묶어 패키지 레지스트리에 올리고,
GitLab Release(태그 vX.Y.Z)를 만들고, MM 에 알린다 (S15P21B202-326).

잡 `release-windows` 가 두 단계로 부른다 — Unity 빌드의 앞과 뒤:

    python3 scripts/ci/publish_release.py version            # 버전 확정 → release-version.txt. 못 정하면 exit 1 (빌드 전에 멈춘다)
    python3 scripts/ci/publish_release.py publish <unity-rc> # zip → 패키지 → Release → MM. unity-rc≠0 이면 🔴 만 보내고 exit 1

버전의 출처(순서):
  1) 머지 커밋 메시지의 `release/<키>-vX.Y.Z` — 팀장이 릴리스 브랜치를 머지한 경우(git-convention §8)
  2) `hotfix/` 머지 — 마지막 `v*` 태그의 PATCH + 1
  3) 없으면 실패. master 에 릴리스·핫픽스가 아닌 것이 들어온 것이다 — 릴리스 브랜치로 다시.

왜 패키지 레지스트리인가: Release 첨부 상한(기본 100MB)은 인스턴스 설정이라 우리가 못 늘리고, 팀장 실측 빌드가
이미 넘었다. generic 패키지는 상한이 따로(기본 5GB)다. Release 의 에셋 링크가 그 파일을 가리키므로 받는 사람은
Release 페이지의 링크 하나만 본다 — 분할 없음.

인증: `CI_JOB_TOKEN`(JOB-TOKEN 헤더). 패키지 업로드·Release 생성(태그는 ref 로 자동 생성)은 잡 토큰으로 된다 —
비밀 변수를 다루지 않는다. 없으면 GITLAB_TOKEN 으로 대신한다(로컬 시험용).

같은 버전을 다시 돌려도 안전하다: 패키지 파일은 덮어쓰고, Release 는 있으면 설명을 갱신하고 링크는 없을 때만 더한다.
파이썬 표준 라이브러리만 쓴다 — GameCI 이미지의 python3(3.10)에서 돈다.
"""
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile
from datetime import datetime, timedelta, timezone
from urllib.parse import quote

from ci_common import GitLab, KEY_RE, PROJECT_KEY, banner, env, http, load_team, role_member

# 릴리스 게이트가 `unity-compile` 의 기준선 비교를 그대로 쓴다 (S15P21B202-440) —
# 기존 실패를 세는 규칙이 두 곳에서 갈리면 한쪽이 조용히 낡는다.
import unity_report

VERSION_FILE = "release-version.txt"
NOTES_FILE = "release-notes.md"
# **다음 잡에 zip 이름을 넘기는 자리** (S15P21B202-405). zip 자체는 아티팩트로 못
# 넘긴다(160 MB · 상한 100 MB)라 패키지 레지스트리로 보내는데, 이름에 분 단위
# 시각이 들어가 있어 **받는 쪽이 다시 계산할 수 없다.** 한 줄로 적어 둔다.
PACKAGE_FILE = "release-package.txt"
PACKAGE_NAME = "metagame"
RELEASE_RE = re.compile(rf"release/{PROJECT_KEY}-\d+-v(\d+\.\d+\.\d+)")
# 태그를 **읽을 때는** `v` 를 선택으로 본다 (S15P21B202-407 ②).
#
# 2026-09-10 실측: 저장소의 태그는 `1.0.1` 하나뿐이고 `v` 접두가 없다(GitLab UI 에서
# 손으로 만든 것이다). 예전 정규식은 `^v…$` 라 그것을 못 봤고, `all_tags()` 도
# `git tag --list v*` 로 걸렀다. 그래서 **핫픽스를 먼저 하면** `latest_tag()` 가 None 을
# 돌려주고 → "이전 태그가 없어 버전을 정할 수 없다" → exit 1 → 그리고 그때는 알림도
# 없었다(문제 ①). 읽기를 넓혀 그 길을 막는다.
#
# **만들 때는 항상 `v` 를 붙인다**(`bump_patch`·`parse_version`). 두 형식이 섞여 있으면
# 다음 사람이 또 헷갈리므로, 새로 나가는 것은 하나로 모은다.
TAG_RE = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)$")
MERGE_RE = re.compile(r"^Merge branch '([^']+)' into '([^']+)'")
MR_REF_RE = re.compile(r"See merge request [^!\s]*!(\d+)")
KST = timezone(timedelta(hours=9))
MAX_NOTE_LINES = 150          # 첫 릴리스는 develop 전체(수백 건)라 잘라야 읽힌다
# 배포용 폴더에 넣으면 안 되는 것 — Unity 가 만드는 디버그 심볼·백업 폴더
SHIP_EXCLUDE = ("_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame")


def dry_run() -> bool:
    """리허설인가 (S15P21B202-407 ④).

    트리거가 `push && master` 하나뿐이라 **시험 실행이 곧 실제 릴리스**였다. 90분짜리 잡을
    미검증 상태로 처음 돌리게 되므로, 빌드·zip 까지는 진짜로 하고 밖으로 나가는 것
    (패키지 업로드 · Release · 🟢 카드 · Jira 웹훅)만 건너뛰는 길을 만든다.

    **변수만 믿으면 안 된다** (`!336` AI 리뷰). `rules:` 의 `variables:` 는 Run pipeline
    화면에서 사람이 넣은 같은 이름 변수보다 **우선순위가 낮다.** 그래서 리허설 화면에서
    `RELEASE_DRY_RUN=0` 을 주면 `.gitlab-ci.yml` 이 준 1 이 덮이고, **아무 브랜치에서
    진짜 패키지·Release·태그가 나간다.** 안전 장치로 만든 것이 반대로 새는 길이 된다.

    그래서 여기서 **트리거로 잠근다**: 실제 릴리스는 `master` 로의 push 하나뿐이고,
    그 밖의 모든 트리거는 변수와 무관하게 리허설이다. 변수는 *리허설을 켜는* 데만
    쓸 수 있고 끄는 데는 쓸 수 없다 — 한 방향으로만 작동하는 스위치다.

    이 규칙은 `git-convention.md` §8 의 "릴리스는 release/… 브랜치를 master 에 머지하는
    것" 과 같은 말이다. 다른 경로로 릴리스가 나가면 그 절차가 뜻을 잃는다.
    """
    if env("CI_PIPELINE_SOURCE", "") != "push" or env("CI_COMMIT_BRANCH", "") != "master":
        return True
    return (env("RELEASE_DRY_RUN", "") or "").strip() not in ("", "0", "false", "no")


# ── 버전 ─────────────────────────────────────────────────────────────────────
def parse_version(message: str) -> str | None:
    """머지 커밋 메시지에서 릴리스 브랜치의 버전. `Merge branch 'release/KEY-318-v0.1.0' into 'master'` → v0.1.0"""
    m = RELEASE_RE.search(message or "")
    return f"v{m.group(1)}" if m else None


def parse_tag(tag: str) -> tuple[int, int, int] | None:
    m = TAG_RE.match((tag or "").strip())
    return tuple(int(x) for x in m.groups()) if m else None  # type: ignore[return-value]


def latest_tag(tags: list[str]) -> str | None:
    parsed = sorted((parse_tag(t), t) for t in tags if parse_tag(t))
    return parsed[-1][1] if parsed else None


def bump_patch(tag: str) -> str:
    a, b, c = parse_tag(tag)  # type: ignore[misc]
    return f"v{a}.{b}.{c + 1}"


def decide_version(message: str, tags: list[str]) -> tuple[str | None, str]:
    """(버전, 이유). 릴리스 브랜치 머지 → 그 버전. hotfix 머지 → 마지막 태그 PATCH+1. 그 외 → None."""
    v = parse_version(message)
    if v:
        return v, "릴리스 브랜치 이름의 버전"
    if "hotfix/" in (message or ""):
        last = latest_tag(tags)
        if last:
            return bump_patch(last), f"hotfix 머지 — 마지막 태그 {last} 의 PATCH+1"
        return None, "hotfix 머지인데 이전 태그가 없어 버전을 정할 수 없다"
    return None, ("머지 커밋에 release/…-vX.Y.Z 도 hotfix/ 도 없다 — master 에는 릴리스 브랜치(git-convention §8)나 "
                  "핫픽스만 들어와야 한다")


# ── git ──────────────────────────────────────────────────────────────────────
def git(*args: str) -> str:
    r = subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} 실패({r.returncode}): {r.stderr.strip()[:300]}")
    return r.stdout


def all_tags() -> list[str]:
    """버전으로 읽히는 태그 전부. `v` 없는 옛 태그(`1.0.1`)도 포함한다 — 위 TAG_RE 주석 참고.

    **태그가 워크스페이스에 실제로 있는지 확인한다** (S15P21B202-407 ②). `GIT_DEPTH: "0"`
    이라 전체 이력을 받지만, 태그가 함께 오는지는 첫 실행에서 확인해야 한다. 없으면
    `latest_tag` 가 늘 None 이고 핫픽스 경로가 조용히 막힌다. 그래서 개수를 찍는다.
    """
    try:
        raw = [t for t in git("tag", "--list").split() if t]
    except RuntimeError:
        print("   git tag 실패 — 태그를 0개로 본다")
        return []
    usable = [t for t in raw if parse_tag(t)]
    print(f"   태그 {len(raw)}개 중 버전으로 읽히는 것 {len(usable)}개"
          + (f": {' '.join(sorted(usable)[-5:])}" if usable else " (없음)"))
    return usable


def previous_tag(current: str) -> str | None:
    """이번 버전보다 낮은 태그 중 최신 — 변경 목록의 시작점."""
    cur = parse_tag(current)
    lower = [t for t in all_tags() if parse_tag(t) and parse_tag(t) < cur]  # type: ignore[operator]
    return latest_tag(lower)


# ── 변경 목록 ─────────────────────────────────────────────────────────────────
def parse_merges(log_text: str) -> tuple[list[dict], int]:
    """`git log --merges` 출력(레코드 구분 \\x1e, 제목/본문 구분 \\x1f) → develop 으로 들어온 MR 목록, 그 밖(스택 머지 등) 수.

    GitLab 머지 커밋은 제목 `Merge branch 'X' into 'develop'` 와 본문 `See merge request grp/proj!NNN` 을 갖는다.
    그래서 API 를 읽지 않고도 MR 번호·브랜치·이슈 키를 다 알 수 있다."""
    entries, others = [], 0
    for rec in (log_text or "").split("\x1e"):
        rec = rec.strip()
        if not rec:
            continue
        subject, _, body = rec.partition("\x1f")
        m = MERGE_RE.match(subject.strip())
        if not m:
            others += 1
            continue
        branch, into = m.group(1), m.group(2)
        if into != "develop":
            others += 1
            continue
        mr = MR_REF_RE.search(body or "")
        key = KEY_RE.search(branch)
        entries.append({"branch": branch, "iid": int(mr.group(1)) if mr else None, "key": key.group(0) if key else None})
    return entries, others


def changelog(prev: str | None, head: str = "HEAD") -> tuple[list[dict], int]:
    rng = f"{prev}..{head}" if prev else head
    out = git("log", "--merges", "--format=%s%x1f%b%x1e", rng)
    return parse_merges(out)


def render_notes(version: str, entries: list[dict], others: int, prev: str | None, zip_name: str, zip_url: str,
                 size_bytes: int, project_url: str, jira_browse: str) -> str:
    lines = [f"# METAGAME {version}", "",
             f"**내려받기**: [{zip_name}]({zip_url}) ({size_bytes / 1048576:.1f} MB) — 압축을 풀고 `NotThisPlanet.exe` 실행",
             f"이전 릴리스: {prev or '없음(첫 릴리스)'} · 포함 MR {len(entries)}건" + (f" · 그 밖 머지 {others}건" if others else ""),
             "", "## 들어간 것", ""]
    shown = entries[:MAX_NOTE_LINES]
    for e in shown:
        mr = f"[!{e['iid']}]({project_url}/-/merge_requests/{e['iid']})" if e["iid"] else "(MR 번호 없음)"
        key = f" · [{e['key']}]({jira_browse}{e['key']})" if e["key"] else ""
        lines.append(f"- {mr} `{e['branch']}`{key}")
    if len(entries) > len(shown):
        lines.append(f"- … 외 {len(entries) - len(shown)}건")
    lines += ["", "_자동 생성(release-windows) · 빌드는 git 에 없고 위 패키지에만 있다 — git-convention §8_"]
    return "\n".join(lines)


# ── zip ──────────────────────────────────────────────────────────────────────
def stamp_kst(now: datetime | None = None) -> str:
    return (now or datetime.now(KST)).strftime("%Y%m%d-%H%M")


def zip_name(version: str, now: datetime | None = None) -> str:
    """`-269` 규칙(날짜·시각, 한글 없음) + 버전. 이름순 = 시간순."""
    return f"{PACKAGE_NAME}-{version}-{stamp_kst(now)}.zip"


def zip_dir(src_dir: str, dest_zip: str) -> int:
    """빌드 폴더를 zip 으로. 배포 금지 폴더는 뺀다. 반환: zip 크기(바이트)."""
    n = 0
    with zipfile.ZipFile(dest_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for root, dirs, files in os.walk(src_dir):
            dirs[:] = [d for d in dirs if not any(x in d for x in SHIP_EXCLUDE)]
            for f in files:
                p = os.path.join(root, f)
                z.write(p, os.path.relpath(p, src_dir))
                n += 1
    if n == 0:
        raise RuntimeError(f"빌드 폴더가 비어 있다: {src_dir}")
    return os.path.getsize(dest_zip)


# ── GitLab API ───────────────────────────────────────────────────────────────
def auth_headers() -> dict:
    if env("CI_JOB_TOKEN"):
        return {"JOB-TOKEN": env("CI_JOB_TOKEN")}
    if env("GITLAB_TOKEN"):
        return {"PRIVATE-TOKEN": env("GITLAB_TOKEN")}
    return {}


def api_base() -> str:
    return (env("CI_API_V4_URL", "https://lab.ssafy.com/api/v4") or "").rstrip("/")


def project_api() -> str:
    return f"{api_base()}/projects/{env('CI_PROJECT_ID', '')}"


def package_url(version: str, file_name: str) -> str:
    return f"{project_api()}/packages/generic/{PACKAGE_NAME}/{quote(version, safe='')}/{quote(file_name, safe='')}"


def upload_package(path: str, version: str, file_name: str) -> int:
    """generic 패키지 업로드. 파일을 스트리밍한다(메모리에 다 올리지 않는다). 반환: HTTP 상태."""
    size = os.path.getsize(path)
    hdrs = {**auth_headers(), "Content-Type": "application/octet-stream", "Content-Length": str(size)}
    with open(path, "rb") as f:
        req = urllib.request.Request(package_url(version, file_name), data=f, headers=hdrs, method="PUT")
        try:
            with urllib.request.urlopen(req, timeout=1800) as r:
                return r.status
        except urllib.error.HTTPError as e:
            print(f"   업로드 응답 {e.code}: {e.read().decode('utf-8', 'replace')[:300]}")
            return e.code


def ensure_release(version: str, sha: str, notes: str, link_name: str, link_url: str) -> tuple[str, int]:
    """Release 를 만들거나 갱신한다. 반환: (한 일, 상태). 태그가 없으면 ref 로 GitLab 이 만든다."""
    base = f"{project_api()}/releases"
    hdrs = auth_headers()
    st, cur = http("GET", f"{base}/{quote(version, safe='')}", hdrs)
    link = {"name": link_name, "url": link_url, "link_type": "package"}
    if st == 404:
        st2, d = http("POST", base, hdrs, {"tag_name": version, "ref": sha, "name": f"METAGAME {version}",
                                          "description": notes, "assets": {"links": [link]}})
        return ("created", st2)
    if st != 200:
        return ("lookup-failed", st)
    st2, _ = http("PUT", f"{base}/{quote(version, safe='')}", hdrs, {"description": notes, "name": f"METAGAME {version}"})
    have = {l.get("url") for l in ((cur or {}).get("assets") or {}).get("links") or []} if isinstance(cur, dict) else set()
    if link_url not in have:
        http("POST", f"{base}/{quote(version, safe='')}/assets/links", hdrs, link)
    return ("updated", st2)


# ── 알림 ─────────────────────────────────────────────────────────────────────
def notify(ok: bool, version: str, rows: list[tuple[str, str]]) -> None:
    """MM 카드. 성공은 팀장(머지한 사람)에게, 실패는 Infra 에게."""
    try:
        import mm_notify
    except Exception as e:  # noqa: BLE001
        print(f"   mm_notify 임포트 실패({type(e).__name__}) — 알림 생략"); return
    team = load_team()
    who = (role_member(team, "infra") if not ok else role_member(team, "lead")) or {}
    mention = f" @{who['mm']}" if who.get("mm") else ""
    title = f"🟢 **릴리스 {version}**{mention}" if ok else f"🔴 **릴리스 실패 {version}**{mention}"
    mm_notify.send(mm_notify.card(title, rows))


def webhook_shape_problem() -> str | None:
    """설정된 웹훅 주소가 **보낼 수 있는 모양인지**만 본다. 값은 절대 찍지 않는다.

    첫 실전 릴리스(2026-09-11, v1.0.0)에서 이것이 필요해졌다. 빌드·zip·패키지·
    Release·MM 이 전부 끝난 **뒤** 마지막 한 줄에서 `urllib` 이
    `ValueError: unknown url type` 으로 터졌다 — 변수에 스킴(`https://`)이 없었다.
    58분을 쓴 잡이 빨간불이 됐고, 다시 돌리면 이미 낸 릴리스를 또 내려 한다.

    그래서 **0분에** 본다. 잘못됐으면 그때 말해 주고 릴리스는 계속 진행한다 —
    이것은 알림이고, 알림 설정이 틀린 것이 배포를 막을 이유가 못 된다.
    """
    url = env("JIRA_RELEASE_WEBHOOK")
    if not url:
        return None            # 설정하지 않은 것은 문제가 아니다(선택 기능이다)
    if url != url.strip():
        return "앞뒤에 공백이 붙어 있다"
    if not url.startswith(("https://", "http://")):
        return "http(s):// 로 시작하지 않는다"
    return None


def jira_release_webhook(version: str, keys: list[str], release_url: str) -> str | None:
    """(선택, S15P21B202-327) Jira 자동화 수신 웹훅 — Jira 가 버전을 만들고 fixVersion 을 단다. CI 는 Jira 를 직접 쓰지 않는다(-242).

    **절대 예외를 올려보내지 않는다.** 이 함수가 불릴 때 Release 는 이미 났다.
    여기서 터지면 성공한 릴리스가 빨간불로 남고, 재실행이 재발행을 시도한다.
    돌려주는 값은 MM 카드에 실을 한 줄이고, 문제가 없으면 `None` 이다.
    """
    url = env("JIRA_RELEASE_WEBHOOK")
    if not url:
        return None
    bad = webhook_shape_problem()
    if bad:
        # **값을 찍지 않는다.** masked 변수는 잡 로그에서는 가려지지만 MM 카드는
        # GitLab 이 가려 주지 않는다 — 예외 문구를 그대로 실으면 주소가 채널로 나간다.
        print(f"   ⚠️ CI 변수 JIRA_RELEASE_WEBHOOK: {bad} — 보내지 않는다")
        return f"⚠️ CI 변수 `JIRA_RELEASE_WEBHOOK` 이 {bad} (값은 여기 적지 않는다)"
    # Jira 수신 웹훅 형식: `issues` = 규칙이 다룰 이슈 키, `data` = 규칙 안에서 {{webhookData.version}} 처럼 읽는 값
    body = {"issues": sorted(set(keys)),
            "data": {"version": version, "releaseUrl": release_url, "date": datetime.now(KST).strftime("%Y-%m-%d")}}
    try:
        st, _ = http("POST", url, {}, body)
    except Exception as e:  # noqa: BLE001
        # **종류만** 싣는다(-335 와 같은 규칙). urllib 의 예외 문구에는 주소가 들어 있다.
        print(f"   ⚠️ Jira 릴리스 웹훅 호출이 예외로 끝났다: {type(e).__name__} (Release 는 이미 났다, 비차단)")
        return f"⚠️ Jira 연동 실패 ({type(e).__name__}) — Release 는 이미 났다"
    ok = 200 <= (st or 0) < 300
    print(f"   Jira 릴리스 웹훅 → {st}" + ("" if ok else " (실패 — Release 는 이미 났다, 비차단)"))
    return None if ok else f"⚠️ Jira 연동 실패 (HTTP {st}) — Release 는 이미 났다"


# ── 단계 ─────────────────────────────────────────────────────────────────────
def phase_version() -> int:
    message = env("CI_COMMIT_MESSAGE", "") or ""
    version, why = decide_version(message, all_tags())
    print(f"머지 커밋: {message.strip().splitlines()[0] if message.strip() else '(없음)'}")
    if not version and dry_run():
        # 리허설은 아무 브랜치에서나 돌린다 — 릴리스 브랜치 머지 커밋이 없는 것이 정상이다.
        # 여기서 멈추면 정작 시험하려던 빌드·zip 경로를 한 번도 못 지나간다.
        version, why = "v0.0.0-rehearsal", f"리허설(원래 사유: {why})"
    if not version:
        print(f"❌ 버전을 정할 수 없다 — {why}")
        return 1
    with open(VERSION_FILE, "w", encoding="utf-8") as f:
        f.write(version)
    print(f"✅ 버전 {version} ({why}) → {VERSION_FILE}")

    # **알림 설정을 지금 본다.** 58분 뒤에 알게 되면 고칠 기회가 그 릴리스에는 없다.
    bad = webhook_shape_problem()
    if bad:
        print(f"⚠️ CI 변수 JIRA_RELEASE_WEBHOOK: {bad} — Jira 연동만 건너뛴다(릴리스는 계속한다)")
    return 0


def phase_gate(test_rc: int, xml_path: str, log_path: str = "") -> int:
    """EditMode 테스트 결과로 빌드 여부를 가른다 (S15P21B202-407 ③).

    **왜 릴리스 잡 안인가.** master push 에서 생기는 잡은 `release-windows` 하나뿐이었다.
    `unity-compile` 의 rules 는 `merge_request_event`·`develop` 이라 여기 없고, MR
    파이프라인은 **소스 브랜치**를 검사하므로 머지 결과 트리는 어디서도 검사되지 않은
    상태로 릴리스됐다. master 에 `unity-compile` 을 따로 붙이면 `resource_group: unity`
    로 직렬화되어 총 시간이 두 배가 되므로, 같은 임포트를 재사용하는 이 자리에서 돈다.

    **컴파일 오류도 여기서 드러난다.** 컴파일이 깨지면 Unity 는 테스트를 돌리지 못하고
    종료 코드가 0이 아니며 결과 XML 이 없다. 그 경우를 "테스트 0건 통과" 로 읽으면
    안 된다 — 없는 것을 통과로 읽는 것이 `-134` 에서 고친 실패 방식이다.

    `❌` 줄을 남기는 것이 계약이다. `mm-ci-failed` 가 그 줄을 카드의 '이유' 로 싣는다
    (`mm_notify.REASON_MARKERS`). 남기지 않으면 카드가 "로그에서 이유를 찾지 못함" 이 된다.
    """
    if not os.path.exists(xml_path):
        print(f"❌ EditMode 테스트 결과가 없다 (종료 코드 {test_rc}) — 컴파일이 깨졌을 수 있다. 빌드하지 않는다")
        for line in compile_error_lines(log_path):
            print(f"   {line}")
        return 1

    try:
        data = unity_report.parse_nunit(open(xml_path, encoding="utf-8", errors="replace").read())
    except Exception as e:  # noqa: BLE001
        print(f"❌ 테스트 결과를 읽지 못했다: {type(e).__name__}: {e} — 빌드하지 않는다")
        return 1

    print(f"EditMode 테스트 — 전체 {data['total']} · 통과 {data['passed']} · "
          f"실패 {data['failed']} · 건너뜀 {data['skipped']} ({data['duration']:.0f}초)")

    # 파이프라인 화면에서 실패한 테스트를 바로 보게 한다 (unity-compile 과 같은 결).
    try:
        with open(os.path.join(env("CI_PROJECT_DIR", ".") or ".", "unity-junit.xml"),
                     "w", encoding="utf-8", newline="\n") as f:
            f.write(unity_report.to_junit(data))
    except Exception as e:  # noqa: BLE001
        print(f"   JUnit 변환 실패(비차단): {type(e).__name__}: {e}")

    # **기존 실패로 릴리스를 막지 않는다** (S15P21B202-440).
    #
    # 처음에는 실패가 하나라도 있으면 막았다. 그런데 develop 에는 기존 실패가 **33건**
    # 있다(`-346` 27건 + `-343` 6건, 2026-09-11 실측 1,130개 중). 그래서 그 게이트는
    # 무조건 걸려 **빌드 자체를 하지 않았고, 실제 릴리스도 나갈 수 없었다.**
    #
    # `unity-compile` 은 이 문제를 이미 풀어 두었다 — 대상 브랜치 기준선과 비교해
    # **이 변경이 만든 실패만** 막는다(`-309`: "기존 실패가 남의 MR 을 막으면 검사가
    # 형식으로만 통과된다"). 그 규칙을 릴리스 경로에도 쓴다. 기준선은 develop 이다 —
    # 릴리스 브랜치는 develop 의 스냅샷이므로 그 마지막 결과가 곧 기준선이고,
    # master 에서는 이 잡이 돈 적이 없어 기준선이 없다(`baseline_ref` 가 그 판단을 한다).
    now_failing = unity_report.failed_names(data)

    # **읽다가 죽으면 안 된다** (`!350` AI 리뷰). `GitLab()` 생성이나 조회에서 예외가
    # 나면 `phase_gate` 가 통째로 죽고 잡이 실패한다 — 아래에 적어 둔 "못 읽으면 막지
    # 않는다" 와 **정반대로** 릴리스가 멈춘다. 토큰 미설정·네트워크 오류가 그 길이다.
    try:
        base = unity_report.baseline_failures(GitLab(), unity_report.baseline_ref("master"))
    except Exception as e:  # noqa: BLE001
        print(f"   기준선 조회가 예외로 끝났다: {type(e).__name__}: {e}")
        base = None

    if base is None:
        # **기준선을 못 읽었으면 막지 않는다.**
        #
        # 릴리스는 이미 두 번 검사를 지났다 — 릴리스 MR 의 `unity-compile` 과 그 전
        # develop 머지의 `unity-compile`. 이 게이트는 **머지 결과 트리**를 보는 두 번째
        # 그물이고, 두 번째 그물이 API 딸꾹질로 릴리스를 막는 것은 그물이 없는 것보다
        # 나쁘다. 대신 크게 적어 잡 로그에 남긴다.
        print(f"🔴 기준선을 읽지 못했다 — 기존 실패와 새 실패를 가릴 수 없다"
              f" (지금 실패 {len(now_failing)}건). 막지 않고 넘어간다")
    else:
        fresh = sorted(now_failing - base)
        if fresh:
            print(f"❌ 이 트리가 만든 EditMode 실패 {len(fresh)}건 — 릴리스를 빌드하지 않는다"
                  f" (기존 실패 {len(base & now_failing)}건은 세지 않았다)")
            by_name = {f"{c['cls']}.{c['name']}": c for c in data["cases"]}
            for name in fresh[:10]:
                hit = by_name.get(name)
                print(f"   {name}" + (f" — {hit['msg'][:200]}" if hit and hit.get("msg") else ""))
            if len(fresh) > 10:
                print(f"   … 외 {len(fresh) - 10}건 (전체는 unity-results.xml)")
            return 1

        if now_failing:
            print(f"✅ 기존 실패 {len(now_failing)}건뿐 — 이 트리가 만든 실패는 없다")

    # **종료 코드만으로 막으면 이 함수의 목적이 무너진다** (`!350` AI 리뷰).
    #
    # 기준선 비교를 통과해도(기존 실패뿐) 그 아래에서 종료 코드로 다시 막으면
    # 릴리스는 여전히 나가지 못한다. Unity 는 **테스트가 하나라도 실패하면** 0이 아닌
    # 코드로 끝나므로, 기존 실패 33건이 있는 한 이 분기가 늘 걸린다. 즉 위에서
    # 기존 실패를 세지 않기로 한 판단이 여기서 그대로 뒤집힌다.
    #
    # 그래서 **실패한 테스트가 있으면 종료 코드는 그것으로 설명된 것**으로 본다.
    # 이 분기가 잡으려던 것은 "테스트는 전부 통과인데 그 밖에서 깨진 경우" 이고,
    # 그 조건이 `not now_failing` 이다.
    if test_rc != 0 and not now_failing:
        print(f"❌ 테스트는 전부 통과인데 Unity 종료 코드가 {test_rc} 다 — 빌드하지 않는다")
        for line in compile_error_lines(log_path):
            print(f"   {line}")
        return 1

    print(f"✅ EditMode 테스트 {data['passed']}건 통과 — 빌드로 넘어간다")
    return 0


def compile_error_lines(log_path: str, cap: int = 10) -> list[str]:
    """로그에서 `error CS` 줄만. 로그는 수만 줄이라 사람이 못 읽는다."""
    if not log_path or not os.path.exists(log_path):
        return ["(로그 파일 없음)"]
    try:
        errs = unity_report.compile_errors(
            open(log_path, encoding="utf-8", errors="replace").read(), cap=cap)
    except Exception:  # noqa: BLE001
        return ["(로그를 읽지 못했다)"]
    return errs or ["(로그에 `error CS` 줄 없음 — 잡 로그를 직접 보세요)"]


def phase_publish(unity_rc: int) -> int:
    version = open(VERSION_FILE, encoding="utf-8").read().strip() if os.path.exists(VERSION_FILE) else "?"
    sha = env("CI_COMMIT_SHA", "") or ""
    project_url = env("CI_PROJECT_URL", "") or ""
    links = " · ".join(f"[{n}]({u})" for n, u in (("파이프라인", env("CI_PIPELINE_URL")), ("잡 로그", env("CI_JOB_URL"))) if u)
    if unity_rc != 0:
        print(f"❌ Unity 빌드 실패(exit {unity_rc}) — Release 를 만들지 않는다")
        # **리허설 실패도 알린다** (`!336` AI 리뷰). 리허설의 목적이 "릴리스 전에
        # 안다" 인데 그 결과가 조용하면 화면을 직접 봐야 한다. 다만 제목에 리허설임을
        # 적어, 실제 릴리스가 깨진 것으로 읽히지 않게 한다.
        rehearsal = " (리허설)" if dry_run() else ""
        notify(False, version + rehearsal,
               [("단계", f"Unity 빌드 (exit {unity_rc})"), ("커밋", f"`{sha[:8]}`"),
                ("볼 곳", "잡 아티팩트 `unity-build.log` 의 `error` 줄"), ("링크", links)])
        return 1

    build_dir = env("SPACE_BUILD_DIR", "release/NotThisPlanet") or "release/NotThisPlanet"
    name = zip_name(version)
    zip_path = os.path.join(os.path.dirname(build_dir.rstrip("/\\")) or ".", name)
    t0 = time.time()
    size = zip_dir(build_dir, zip_path)
    print(f"zip {name} — {size / 1048576:.1f} MB ({time.time() - t0:.0f}초)")

    if dry_run():
        # 여기까지가 리허설의 목적이다 — 라이선스·임포트·빌드·삭제 검사·zip 이 실제로
        # 지나갔다. 밖으로 나가는 것(패키지·Release·카드·Jira)은 하지 않는다.
        print(f"✅ 리허설 완료 — 빌드와 zip 까지 지나갔다 ({name} · {size / 1048576:.1f} MB)")
        print("   건너뛴 것: 패키지 업로드 · GitLab Release · 🟢 MM 카드 · Jira 릴리스 웹훅")
        print("   실제 릴리스는 release/<키>-vX.Y.Z 브랜치를 master 에 머지할 때 난다")
        return 0

    t0 = time.time()
    # 이름을 먼저 적는다 — 업로드가 실패해도 무엇을 올리려 했는지 남는다.
    with open(PACKAGE_FILE, "w", encoding="utf-8", newline="\n") as f:
        f.write(name + "\n")

    st = upload_package(zip_path, version, name)
    if st not in (200, 201):
        print(f"❌ 패키지 업로드 실패 {st}")
        notify(False, version, [("단계", f"패키지 업로드 (HTTP {st})"), ("파일", f"{name} · {size / 1048576:.1f} MB"),
                                 ("확인", "인스턴스의 generic 패키지 상한 · 잡 토큰 권한"), ("링크", links)])
        return 1
    pkg_url = package_url(version, name)
    print(f"패키지 업로드 {st} ({time.time() - t0:.0f}초) → {pkg_url}")

    prev = previous_tag(version)
    entries, others = changelog(prev)
    jira_browse = (env("JIRA_URL", "https://ssafy.atlassian.net") or "").rstrip("/") + "/browse/"
    notes = render_notes(version, entries, others, prev, name, pkg_url, size, project_url, jira_browse)
    with open(NOTES_FILE, "w", encoding="utf-8") as f:
        f.write(notes)
    action, st = ensure_release(version, sha, notes, name, pkg_url)
    release_url = f"{project_url}/-/releases/{quote(version, safe='')}"
    if st not in (200, 201):
        print(f"❌ Release {action} 실패 {st}")
        notify(False, version, [("단계", f"Release {action} (HTTP {st})"), ("패키지", f"[{name}]({pkg_url}) 는 올라갔다"), ("링크", links)])
        return 1
    print(f"✅ Release {action} {st} → {release_url}")

    # **카드보다 먼저 부른다.** 완료 조건이 "실패하면 MM 카드에 한 줄" 이므로
    # 카드를 먼저 보내면 그 한 줄을 실을 수가 없다(-327).
    jira_line = jira_release_webhook(version, [e["key"] for e in entries if e.get("key")], release_url)

    who = env("GITLAB_USER_LOGIN", "") or ""
    rows = [("Release", f"[{version}]({release_url})"),
            ("내려받기", f"[{name}]({pkg_url}) — {size / 1048576:.1f} MB, 압축을 풀고 `NotThisPlanet.exe`"),
            ("들어간 것", f"MR {len(entries)}건" + (f" (이전 {prev} 이후)" if prev else " (첫 릴리스)")),
            ("머지한 사람", who or "?")]
    if jira_line:
        rows.append(("Jira", jira_line))
    rows.append(("링크", links))
    notify(True, version, rows)
    # 여기서 0 을 돌려준다. Jira 연동이 안 됐어도 **릴리스는 났다** — 빨간불로
    # 남기면 사람이 재실행을 눌러 이미 낸 릴리스를 또 내려 한다.
    return 0


def main(argv: list[str] | None = None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    banner("publish-release")
    phase = argv[0] if argv else "version"
    if phase == "version":
        return phase_version()
    if phase == "gate":
        try:
            trc = int(argv[1]) if len(argv) > 1 and str(argv[1]).strip() else 0
        except ValueError:
            trc = 1
        return phase_gate(trc, argv[2] if len(argv) > 2 else "",
                          argv[3] if len(argv) > 3 else "")
    if phase == "publish":
        try:
            rc = int(argv[1]) if len(argv) > 1 and str(argv[1]).strip() else 0
        except ValueError:
            rc = 1
        return phase_publish(rc)
    print(f"모르는 단계: {phase!r} (version | gate <test-rc> <xml> [log] | publish <unity-rc>)")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
