"""재대상된 자식 MR 에 파이프라인을 만드는지 (S15P21B202-295).

실행: python scripts/ci/tests/test_retarget_pipeline.py
네트워크·토큰 없이 `ci_common.http` 를 가짜로 바꿔 돌린다. 현재 시각에 의존하지 않는다.

실측(09-02~04, 재대상 16건): 파이프라인 **안 생김 14 · 생김 2**. 생긴 둘은 `!204`(우리가 API 로
재대상)와 `!210`(직후 커밋 push)이라, **브랜치 삭제로 인한 자동 재대상만** 파이프라인을 안 만든다.

여기서 지키는 것 넷.
  ① 재대상 뒤 파이프라인이 없으면 만든다
  ② 재대상 뒤에 이미 돌았으면 만들지 않는다(멱등 — 만들고 나면 그것이 노트보다 나중이 된다)
  ③ 재대상이 아예 없었던 MR 은 건드리지 않는다
  ④ **조회에 실패하면 만들지도 넘어가지도 않고 경고한다** — 없는 것과 못 물어본 것을 섞지 않는다
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"
os.environ["GITLAB_TOKEN"] = "x"

import ci_common  # noqa: E402
import retarget_pipeline as R  # noqa: E402

RETARGET = ("deleted the `S15P21B202-281-time-independent-tests` branch. "
            "This merge request now targets the `develop` branch")

OPEN_MRS = [{"iid": 210, "title": "자식 — 재대상 뒤 안 돌았다"},
            {"iid": 204, "title": "자식 — 재대상 뒤 돌았다"},
            {"iid": 196, "title": "보통 MR — 재대상 없음"},
            {"iid": 999, "title": "노트 조회가 깨진 MR"}]

NOTES = {
    210: [{"system": True, "created_at": "2026-09-04T12:07:34Z", "body": RETARGET},
          {"system": False, "created_at": "2026-09-04T12:09:00Z", "body": "사람 코멘트"}],
    204: [{"system": True, "created_at": "2026-09-03T14:18:01Z", "body": RETARGET}],
    196: [{"system": True, "created_at": "2026-09-03T22:57:40Z", "body": "mentioned in merge request !196"}],
}
PIPES = {
    210: [{"id": 178569, "created_at": "2026-09-03T22:57:15Z"}],       # 재대상 **전**
    204: [{"id": 178001, "created_at": "2026-09-03T14:18:42Z"}],       # 재대상 **후**
    196: [{"id": 177000, "created_at": "2026-09-04T01:00:00Z"}],
}
created: list[int] = []
BROKEN_NOTES = {999}
BROKEN_PIPES: set[int] = set()


def fake_http(method, url, headers=None, body=None, timeout=30):
    if url.endswith("/merge_requests?state=opened&per_page=100&order_by=updated_at"):
        return 200, OPEN_MRS
    if "/merge_requests?" in url and "state=opened" in url:
        return 200, OPEN_MRS
    if "/notes?" in url:
        iid = int(url.split("/merge_requests/")[1].split("/")[0])
        return (500, {"message": "boom"}) if iid in BROKEN_NOTES else (200, NOTES.get(iid, []))
    if url.endswith("/pipelines") and method == "GET":
        iid = int(url.split("/merge_requests/")[1].split("/")[0])
        return (500, {"message": "boom"}) if iid in BROKEN_PIPES else (200, PIPES.get(iid, []))
    if url.endswith("/pipelines") and method == "POST":
        iid = int(url.split("/merge_requests/")[1].split("/")[0])
        created.append(iid)
        return 201, {"id": 900000 + iid}
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http
R.http = getattr(R, "http", fake_http)
gl = ci_common.GitLab()

# ── 판정 함수 단위
assert R.newest_retarget(NOTES[210]) == "2026-09-04T12:07:34Z"
assert R.newest_retarget(NOTES[196]) is None, "재대상이 아닌 시스템 노트를 재대상으로 봤다"
assert R.newest_pipeline(PIPES[204]) == "2026-09-03T14:18:42Z"
assert R.newest_pipeline([]) is None
print("  판정 함수 ✅")

# ── ①②③④ 를 한 번에
need, broken = R.stale_mrs(gl, OPEN_MRS)
iids = [m["iid"] for m, _ in need]
assert iids == [210], f"파이프라인이 필요한 MR 판정이 틀렸다: {iids}"
assert any("!999" in b for b in broken), broken
assert len(broken) == 1, broken
print("  ① 재대상 뒤 안 돈 MR 만 고름 ✅")
print("  ② 재대상 뒤 돈 MR(!204)은 제외 ✅")
print("  ③ 재대상 없는 MR(!196)은 제외 ✅")
print("  ④ 조회 실패(!999)는 경고만, 만들지 않음 ✅")

# ── 실제 실행 — 하나만 만든다
created.clear()
rc = R.main()
assert created == [210], f"만든 파이프라인: {created}"
assert rc == 0, f"조회 실패는 경고라 실패로 끝나면 안 된다: rc={rc}"
print("  실행: !210 하나만 생성, 종료코드 0 ✅")

# ── 멱등: 만들고 나면 그것이 노트보다 나중이라 다시 안 만든다
PIPES[210] = [{"id": 900210, "created_at": "2026-09-04T12:10:00Z"}] + PIPES[210]
created.clear()
R.main()
assert created == [], f"두 번째 실행에서 또 만들었다: {created}"
print("  멱등: 두 번째 실행은 아무 것도 안 만듦 ✅")

# ── 파이프라인 조회가 깨진 MR 도 건드리지 않는다
PIPES[210] = [{"id": 178569, "created_at": "2026-09-03T22:57:15Z"}]
BROKEN_PIPES.add(210)
created.clear()
R.main()
assert created == [], f"파이프라인 조회가 깨졌는데 만들었다: {created}"
BROKEN_PIPES.clear()
print("  파이프라인 조회 실패도 건드리지 않음 ✅")

# ── dry-run 은 아무 것도 만들지 않는다
os.environ["RETARGET_DRY_RUN"] = "1"
created.clear()
R.main()
assert created == [], f"dry-run 인데 만들었다: {created}"
del os.environ["RETARGET_DRY_RUN"]
print("  dry-run ✅")

print("TEST295 OK")
