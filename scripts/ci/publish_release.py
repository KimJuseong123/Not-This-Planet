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

from ci_common import KEY_RE, PROJECT_KEY, banner, env, http, load_team, role_member

VERSION_FILE = "release-version.txt"
NOTES_FILE = "release-notes.md"
PACKAGE_NAME = "metagame"
RELEASE_RE = re.compile(rf"release/{PROJECT_KEY}-\d+-v(\d+\.\d+\.\d+)")
TAG_RE = re.compile(r"^v(\d+)\.(\d+)\.(\d+)$")
MERGE_RE = re.compile(r"^Merge branch '([^']+)' into '([^']+)'")
MR_REF_RE = re.compile(r"See merge request [^!\s]*!(\d+)")
KST = timezone(timedelta(hours=9))
MAX_NOTE_LINES = 150          # 첫 릴리스는 develop 전체(수백 건)라 잘라야 읽힌다
# 배포용 폴더에 넣으면 안 되는 것 — Unity 가 만드는 디버그 심볼·백업 폴더
SHIP_EXCLUDE = ("_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame")


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
    try:
        return [t for t in git("tag", "--list", "v*").split() if t]
    except RuntimeError:
        return []


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


def jira_release_webhook(version: str, keys: list[str], release_url: str) -> None:
    """(선택, S15P21B202-327) Jira 자동화 수신 웹훅 — Jira 가 버전을 만들고 fixVersion 을 단다. CI 는 Jira 를 직접 쓰지 않는다(-242)."""
    url = env("JIRA_RELEASE_WEBHOOK")
    if not url:
        return
    # Jira 수신 웹훅 형식: `issues` = 규칙이 다룰 이슈 키, `data` = 규칙 안에서 {{webhookData.version}} 처럼 읽는 값
    body = {"issues": sorted(set(keys)),
            "data": {"version": version, "releaseUrl": release_url, "date": datetime.now(KST).strftime("%Y-%m-%d")}}
    st, _ = http("POST", url, {}, body)
    print(f"   Jira 릴리스 웹훅 → {st}" + ("" if 200 <= (st or 0) < 300 else " (실패 — Release 는 이미 났다, 비차단)"))


# ── 단계 ─────────────────────────────────────────────────────────────────────
def phase_version() -> int:
    message = env("CI_COMMIT_MESSAGE", "") or ""
    version, why = decide_version(message, all_tags())
    print(f"머지 커밋: {message.strip().splitlines()[0] if message.strip() else '(없음)'}")
    if not version:
        print(f"❌ 버전을 정할 수 없다 — {why}")
        return 1
    with open(VERSION_FILE, "w", encoding="utf-8") as f:
        f.write(version)
    print(f"✅ 버전 {version} ({why}) → {VERSION_FILE}")
    return 0


def phase_publish(unity_rc: int) -> int:
    version = open(VERSION_FILE, encoding="utf-8").read().strip() if os.path.exists(VERSION_FILE) else "?"
    sha = env("CI_COMMIT_SHA", "") or ""
    project_url = env("CI_PROJECT_URL", "") or ""
    links = " · ".join(f"[{n}]({u})" for n, u in (("파이프라인", env("CI_PIPELINE_URL")), ("잡 로그", env("CI_JOB_URL"))) if u)
    if unity_rc != 0:
        print(f"❌ Unity 빌드 실패(exit {unity_rc}) — Release 를 만들지 않는다")
        notify(False, version, [("단계", f"Unity 빌드 (exit {unity_rc})"), ("커밋", f"`{sha[:8]}`"), ("볼 곳", "잡 아티팩트 `unity-build.log` 의 `error` 줄"),
                                 ("링크", links)])
        return 1

    build_dir = env("SPACE_BUILD_DIR", "release/NotThisPlanet") or "release/NotThisPlanet"
    name = zip_name(version)
    zip_path = os.path.join(os.path.dirname(build_dir.rstrip("/\\")) or ".", name)
    t0 = time.time()
    size = zip_dir(build_dir, zip_path)
    print(f"zip {name} — {size / 1048576:.1f} MB ({time.time() - t0:.0f}초)")

    t0 = time.time()
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

    who = env("GITLAB_USER_LOGIN", "") or ""
    notify(True, version, [("Release", f"[{version}]({release_url})"),
                           ("내려받기", f"[{name}]({pkg_url}) — {size / 1048576:.1f} MB, 압축을 풀고 `NotThisPlanet.exe`"),
                           ("들어간 것", f"MR {len(entries)}건" + (f" (이전 {prev} 이후)" if prev else " (첫 릴리스)")),
                           ("머지한 사람", who or "?"), ("링크", links)])
    jira_release_webhook(version, [e["key"] for e in entries if e.get("key")], release_url)
    return 0


def main(argv: list[str] | None = None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    banner("publish-release")
    phase = argv[0] if argv else "version"
    if phase == "version":
        return phase_version()
    if phase == "publish":
        try:
            rc = int(argv[1]) if len(argv) > 1 and str(argv[1]).strip() else 0
        except ValueError:
            rc = 1
        return phase_publish(rc)
    print(f"모르는 단계: {phase!r} (version | publish <unity-rc>)")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
