"""릴리스 발행 — 버전 결정 · 변경 목록 · Release 멱등 · 실패 알림 (S15P21B202-326).

실행: python scripts/ci/tests/test_publish_release.py
네트워크·git·Unity 없이: ci_common.http 와 git()·zip_dir()·upload_package()·mm_notify.send 를 가짜로 바꿔 돈다.
"""
import os
import sys
from datetime import datetime, timezone, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "CI_JOB_TOKEN": "job-x", "CI_PROJECT_URL": "https://lab.example/g/p",
                   "CI_API_V4_URL": "https://lab.example/api/v4", "CI_COMMIT_SHA": "abcdef1234567890",
                   "MM_WEBHOOK_URL": "https://mm.example/hooks/t", "GITLAB_USER_LOGIN": "suho6347"})
os.environ.pop("JIRA_RELEASE_WEBHOOK", None)

import ci_common  # noqa: E402

CALLS: list = []
RELEASE_EXISTS: dict = {}     # tag → release dict (GET 200), 없으면 404


def http(method, url, headers=None, body=None, timeout=30):
    CALLS.append((method, url, body))
    if "/releases/" in url and method == "GET" and not url.endswith("/assets/links"):
        tag = url.rsplit("/", 1)[1]
        return (200, RELEASE_EXISTS[tag]) if tag in RELEASE_EXISTS else (404, {"message": "404 Not Found"})
    if "/releases" in url and method == "POST":
        return 201, {"tag_name": (body or {}).get("tag_name")}
    if method == "PUT":
        return 200, {}
    return 200, {}


ci_common.http = http
import publish_release as P  # noqa: E402
P.http = http

SENT: list = []
import mm_notify  # noqa: E402
mm_notify.send = lambda text: (SENT.append(text), 0)[1]

# ── 1) 버전 결정 ──
assert P.parse_version("Merge branch 'release/S15P21B202-318-v0.1.0' into 'master'\n\nSee merge request g/p!247") == "v0.1.0"
assert P.parse_version("Merge branch 'S15P21B202-318-x' into 'develop'") is None
assert P.decide_version("Merge branch 'release/S15P21B202-318-v0.1.0' into 'master'", []) == ("v0.1.0", "릴리스 브랜치 이름의 버전", P.R_OK)
v, why, rc = P.decide_version("Merge branch 'hotfix/S15P21B202-40-crash' into 'master'", ["v0.1.0", "v0.2.3", "v0.2.10", "junk"])
assert v == "v0.2.11" and "PATCH+1" in why, (v, why)      # 문자열 정렬이 아니라 숫자 정렬(10 > 3)
v, why, rc = P.decide_version("Merge branch 'hotfix/S15P21B202-40-crash' into 'master'", [])
assert v is None and "이전 태그" in why
v, why, rc = P.decide_version("fix: 직접 push", ["v0.1.0"])
assert v is None and "release/" in why
assert P.latest_tag([]) is None and P.bump_patch("v1.2.3") == "v1.2.4"

# **왜 못 정했는가를 문구가 아니라 값으로 돌려준다** (S15P21B202-467). 예전에는 부르는
# 쪽이 `"이미 붙어 있다"` 가 문구에 들었는지를 봤다 — 문구를 한 글자 다듬으면 리허설의
# "진짜였으면 여기서 멈췄다" 경고가 조용히 꺼진다. 그것이 `-446` 이 막으려던 상태다.
_v, _why, _rc = P.decide_version("Merge branch 'release/S15P21B202-9-v1.0.0' into 'master'", ["v1.0.0"])
assert _v is None and _rc == P.R_TAG_TAKEN, (_v, _rc)
_v, _why, _rc = P.decide_version("Merge branch 'hotfix/S15P21B202-9-x' into 'master'", [])
assert _rc == P.R_NO_TAG, _rc
_v, _why, _rc = P.decide_version("fix: 직접 push", ["v0.1.0"])
assert _rc == P.R_NOT_RELEASE, _rc

# 리허설 경고가 **문구가 아니라 그 값**을 본다.
_src = open("scripts/ci/publish_release.py", encoding="utf-8").read()
_pv = _src.split("def phase_version(", 1)[1].split(chr(10) + "def ", 1)[0]
assert "reason == R_TAG_TAKEN" in _pv, "리허설 경고가 아직 이유 문구를 본다"
assert '"이미 붙어 있다" in why' not in _pv, "이유 문구로 가르는 코드가 남아 있다"
print("  1) 버전: 릴리스 브랜치 → 이름의 버전 · hotfix → PATCH+1(숫자 정렬) · 그 외 → 실패 ✅")

# ── 2) 변경 목록 파싱 — develop 머지만, MR 번호·이슈 키 ──
LOG = ("Merge branch 'S15P21B202-313-broadcast-loss' into 'develop'\x1f\nfix\n\nSee merge request s15/S15P21B202!235\n\x1e"
       "Merge branch 'S15P21B202-9-stack' into 'S15P21B202-1-work'\x1f\nSee merge request s15/S15P21B202!99\n\x1e"
       "Merge branch 'S15P21B202-315-worldsync-gate' into 'develop'\x1f\n\x1e"
       "fix: 팀장 직접 push\x1f\n\x1e")
entries, others = P.parse_merges(LOG)
assert [e["iid"] for e in entries] == [235, None], entries
assert [e["key"] for e in entries] == ["S15P21B202-313", "S15P21B202-315"], entries
assert others == 2, others                     # 스택 머지 1 + 머지 아닌 커밋 1
notes = P.render_notes("v0.1.0", entries, others, None, "metagame-v0.1.0-20260907-2130.zip", "https://pkg/x.zip", 123 * 1048576,
                       "https://lab.example/g/p", "https://jira/browse/")
assert "[!235](https://lab.example/g/p/-/merge_requests/235)" in notes and "[S15P21B202-315](https://jira/browse/S15P21B202-315)" in notes
assert "첫 릴리스" in notes and "123.0 MB" in notes and "(MR 번호 없음)" in notes
many = [{"branch": f"S15P21B202-{i}-x", "iid": i, "key": f"S15P21B202-{i}"} for i in range(400)]
assert "외 250건" in P.render_notes("v0.1.0", many, 0, None, "z.zip", "u", 1, "p", "j")
print("  2) 변경 목록: develop 머지만 · !번호·이슈 키 링크 · 150건 넘으면 접음 ✅")

# ── 3) zip 이름 — 버전 + 날짜·시각(-269), 한글 없음 ──
kst = timezone(timedelta(hours=9))
assert P.zip_name("v0.1.0", datetime(2026, 9, 7, 21, 30, tzinfo=kst)) == "metagame-v0.1.0-20260907-2130.zip"
print("  3) zip 이름 규칙 ✅")

# ── 4) Release 멱등: 없으면 POST(태그는 ref 로) · 있으면 PUT + 없는 링크만 POST ──
CALLS.clear(); RELEASE_EXISTS.clear()
act, st = P.ensure_release("v0.1.0", "abc", "notes", "z.zip", "https://pkg/z.zip")
assert (act, st) == ("created", 201), (act, st)
post = [c for c in CALLS if c[0] == "POST"][0]
assert post[2]["tag_name"] == "v0.1.0" and post[2]["ref"] == "abc" and post[2]["assets"]["links"][0]["url"] == "https://pkg/z.zip", post
CALLS.clear(); RELEASE_EXISTS["v0.1.0"] = {"assets": {"links": [{"url": "https://pkg/other.zip"}]}}
act, st = P.ensure_release("v0.1.0", "abc", "notes2", "z.zip", "https://pkg/z.zip")
assert (act, st) == ("updated", 200), (act, st)
assert any(c[0] == "PUT" and c[2]["description"] == "notes2" for c in CALLS), CALLS
assert any(c[0] == "POST" and c[1].endswith("/assets/links") and c[2]["url"] == "https://pkg/z.zip" for c in CALLS), CALLS
CALLS.clear(); RELEASE_EXISTS["v0.1.0"] = {"assets": {"links": [{"url": "https://pkg/z.zip"}]}}
P.ensure_release("v0.1.0", "abc", "n", "z.zip", "https://pkg/z.zip")
assert not any(c[1].endswith("/assets/links") for c in CALLS), "이미 있는 링크는 다시 만들지 않는다"
print("  4) Release: 없으면 생성(ref 로 태그) · 있으면 갱신 · 링크는 없을 때만 ✅")

# ── 4b) Jira 웹훅 본문은 Jira 수신 웹훅 형식 — issues 배열 + data 객체(-327) ──
CALLS.clear(); os.environ["JIRA_RELEASE_WEBHOOK"] = "https://automation.example/hook"
P.jira_release_webhook("v0.1.0", ["S15P21B202-2", "S15P21B202-1", "S15P21B202-2"], "https://lab.example/g/p/-/releases/v0.1.0")
hook = [c for c in CALLS if c[1] == "https://automation.example/hook"][0]
assert hook[2]["issues"] == ["S15P21B202-1", "S15P21B202-2"] and hook[2]["data"]["version"] == "v0.1.0", hook
assert "releaseUrl" in hook[2]["data"] and "date" in hook[2]["data"]
assert P.jira_release_webhook("v0.1.0", [], "https://l/r") is None, (
    "정상 응답인데 카드에 실을 줄을 돌려준다")

# ── 4c) 웹훅은 **릴리스를 죽이지 못한다**, 그리고 주소를 밖으로 내지 않는다 (-327)
#
# 2026-09-11 첫 실전 릴리스(v1.0.0)에서 실제로 일어난 일: 빌드·zip·패키지·
# Release·MM 이 전부 끝난 **뒤** 이 한 줄이 `ValueError: unknown url type` 으로
# 터져 58분 걸린 잡이 빨간불이 됐다. 이 함수가 불릴 때 Release 는 이미 났으므로
# 여기서 터지면 성공한 릴리스가 실패로 남고, 재실행이 재발행을 시도한다.
SECRET = "https://automation.example/hook/s3cr3t-token-value"


def boom(method, url, headers=None, body=None, timeout=30):
    # urllib 이 실제로 그랬듯이 **예외 문구에 주소가 들어 있다**.
    raise ValueError(f"unknown url type: {url!r}")


saved_http = P.http
P.http = boom
os.environ["JIRA_RELEASE_WEBHOOK"] = SECRET
line = P.jira_release_webhook("v0.1.0", ["S15P21B202-1"], "https://l/r")
P.http = saved_http
assert line and "Jira 연동 실패" in line, f"실패를 카드 한 줄로 알리지 않는다: {line!r}"
assert "ValueError" in line, f"무엇이 터졌는지 종류를 적지 않는다: {line!r}"
assert SECRET not in line and "s3cr3t" not in line, (
    "웹훅 주소가 MM 카드로 나간다 — 잡 로그는 GitLab 이 가려 주지만 카드는 "
    f"가려 주지 않는다(-335 와 같은 규칙): {line!r}")

# 모양이 틀리면 **보내지도 않는다.** 스킴이 없던 것이 그 사고의 원인이었다.
for bad_value, why in [("automation.example/hook", "스킴 없음"),
                       ("  https://automation.example/hook  ", "공백")]:
    os.environ["JIRA_RELEASE_WEBHOOK"] = bad_value
    CALLS.clear()
    line = P.jira_release_webhook("v0.1.0", ["S15P21B202-1"], "https://l/r")
    assert not CALLS, f"{why} 인데 보냈다"
    assert line and "JIRA_RELEASE_WEBHOOK" in line, f"{why} 를 카드가 말하지 않는다: {line!r}"
    assert bad_value.strip() not in line, f"{why}: 값이 카드에 실렸다"
    assert P.webhook_shape_problem(), f"{why} 를 모양 검사가 못 잡는다"

# 설정하지 않은 것은 문제가 아니다 — 선택 기능이다.
os.environ.pop("JIRA_RELEASE_WEBHOOK")
assert P.webhook_shape_problem() is None
assert P.jira_release_webhook("v0.1.0", ["S15P21B202-1"], "https://l/r") is None
print("  4b) Jira 웹훅: issues 중복 제거·정렬 + data.version ✅")
print("  4c) 웹훅 실패는 비차단 · 주소가 카드로 새지 않음 · 모양 미리 검사 ✅")

# ── 5) 인증 헤더: 잡 토큰 우선 ──
assert P.auth_headers() == {"JOB-TOKEN": "job-x"}
assert P.package_url("v0.1.0", "a b.zip").endswith("/packages/generic/metagame/v0.1.0/a%20b.zip")
print("  5) 잡 토큰 헤더 · 패키지 URL 인코딩 ✅")

# ── 6) publish 단계 흐름 — 빌드 실패면 🔴 만, 성공이면 zip→업로드→Release→🟢 ──
import tempfile
tmp = tempfile.mkdtemp()
os.chdir(tmp)
open(P.VERSION_FILE, "w", encoding="utf-8").write("v0.1.0")
SENT.clear()
assert P.phase_publish(1) == 1
assert SENT and "🔴" in SENT[0] and "릴리스 실패 v0.1.0" in SENT[0] and "Unity 빌드" in SENT[0], SENT
# 성공 경로 — zip·업로드·git 은 가짜로
#
# **트리거를 세운다** (S15P21B202-407). `dry_run()` 이 "실제 릴리스는 master push
# 하나뿐" 으로 잠겨 있어(변수로 끌 수 없다), 트리거가 없으면 이 아래가 리허설로 돌아
# 패키지·Release·카드가 전부 생략된다 — 그러면 이 절이 검사할 것이 없어진다.
os.environ["CI_PIPELINE_SOURCE"] = "push"
os.environ["CI_COMMIT_BRANCH"] = "master"
os.makedirs(os.path.join(tmp, "release", "NotThisPlanet"), exist_ok=True)
open(os.path.join(tmp, "release", "NotThisPlanet", "NotThisPlanet.exe"), "wb").write(b"x" * 10)
os.environ["SPACE_BUILD_DIR"] = os.path.join(tmp, "release", "NotThisPlanet")
P.upload_package = lambda path, version, file_name: 201
P.git = lambda *a: "Merge branch 'S15P21B202-1-x' into 'develop'\x1f\nSee merge request g/p!1\n\x1e" if a[0] == "log" else ""
SENT.clear(); CALLS.clear(); RELEASE_EXISTS.clear()
assert P.phase_publish(0) == 0
assert SENT and "🟢" in SENT[0] and "릴리스 v0.1.0" in SENT[0] and "MR 1건" in SENT[0] and "suho6347" in SENT[0], SENT
assert any(c[0] == "POST" and "/releases" in c[1] and c[2].get("tag_name") == "v0.1.0" for c in CALLS), CALLS
assert os.path.exists(os.path.join(tmp, P.NOTES_FILE))
# 배포 금지 폴더는 zip 에서 빠진다
os.makedirs(os.path.join(tmp, "release", "NotThisPlanet", "NotThisPlanet_BurstDebugInformation_DoNotShip"), exist_ok=True)
open(os.path.join(tmp, "release", "NotThisPlanet", "NotThisPlanet_BurstDebugInformation_DoNotShip", "a.txt"), "w").write("x")
import zipfile
zp = os.path.join(tmp, "t.zip"); P.zip_dir(os.path.join(tmp, "release", "NotThisPlanet"), zp)
assert not any("DoNotShip" in n for n in zipfile.ZipFile(zp).namelist())
print("  6) publish: 빌드 실패 → 🔴 만 · 성공 → zip·업로드·Release·🟢 · DoNotShip 제외 ✅")

print("TEST326 OK")
