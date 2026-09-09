"""CI 스크립트 공통 — GitLab/Jira API(표준 라이브러리만), 컨벤션 상수, 리포트.

컨벤션 출처: docs/parts.md(파트 13종), docs/git-convention.md(§4 커밋 type, §5 MR 제목).
"""
from __future__ import annotations

import base64
import json
import os
import re
import sys
import urllib.error
from urllib.parse import quote
import urllib.parse
import urllib.request

PARTS = ["Plan", "Art", "Sound", "UI", "Character", "Physics", "Puzzle", "Level",
         "Network", "Backend", "Infra", "Data", "QA"]
TYPES = ["feat", "fix", "asset", "scene", "refactor", "docs", "test", "perf", "build", "chore"]
PROJECT_KEY = os.environ.get("JIRA_PROJECT_KEY", "S15P21B202")

TITLE_RE = re.compile(
    rf"^\[({'|'.join(PARTS)})\] ({'|'.join(TYPES)})(\([^)]+\))?: (.+?) \(({PROJECT_KEY}-\d+)\)$")
BRANCH_RE = re.compile(rf"^((?:hotfix|release)/)?({PROJECT_KEY}-\d+)(-[A-Za-z0-9._-]+)?$")
# 릴리스 브랜치 — develop 의 스냅샷을 master 로 가져가는 브랜치. 버전이 이름에 있어야 CI 가 머지 뒤
# 태그·Release 를 만들 수 있다 (S15P21B202-325). 예: release/S15P21B202-318-v0.1.0
RELEASE_BRANCH_RE = re.compile(rf"^release/({PROJECT_KEY}-\d+)-v(\d+\.\d+\.\d+)$")
# 대상 브랜치가 "이슈 브랜치처럼 보이는가" — Jira 규칙 R6 의 조건과 같은 정규식(시작 고정, 끝은 열림).
# `release/` 도 포함한다 (S15P21B202-338). 릴리스 브랜치를 대상으로 머지하는 것은 develop 에
# 넣는 것이 아니므로 이슈가 완료되지 않는다 — **Jira R6 가 정확히 그렇게 판단한다**(대상 정규식에
# release/ 가 들어가 키가 잡히면 조건이 실패해 완료 전환을 하지 않는다). 여기에 빠져 있던 동안
# CI 는 "완료된다" 로 보아 Jira 와 어긋났다: stack-order 가 머지 순서 게이트를 건너뛰고,
# 아침 점검의 🧩·🔗 분류가 틀린다. -325 가 BRANCH_RE·R6·R7 에는 넣고 여기만 빠뜨렸다(!246 AI 리뷰).
STACK_TARGET_RE = re.compile(rf"^(?:hotfix/|release/)?{PROJECT_KEY}-\d+")
KEY_RE = re.compile(rf"{PROJECT_KEY}-\d+")
DRAFT_RE = re.compile(r"^(\[?draft\]?:?\s*|\(draft\)\s*|wip:\s*)", re.IGNORECASE)


def env(name: str, default: str | None = None) -> str | None:
    v = os.environ.get(name)
    return v if v not in (None, "") else default


def env_int(name: str, default: int) -> int:
    """CI 변수를 정수로 읽는다 — 숫자가 아니어도 잡을 죽이지 않고 기본값으로 돌아간다.

    임포트 시점에 int(env(...)) 를 계산하면 변수 하나가 잘못 들어간 순간 스크립트가 통째로
    죽는다: 차단 잡이면 머지가 막히고, 알림 잡이면 알림이 조용히 사라진다
    (2026-08-28 DIGEST_PENDING_HOURS 에서 실제로 확인, S15P21B202-134)."""
    raw = env(name)
    if raw is None or raw == "":
        return default
    try:
        return int(raw)
    except ValueError:
        print(f"[warn] {name} 값이 정수가 아님({raw!r}) → 기본값 {default} 사용")
        return default


def load_team() -> dict:
    """scripts/ci/team.json — 파일 부재·JSON 파손이 검사·알림 전체를 죽이지 않게 감싼다.

    읽지 못하면 빈 팀으로 계속한다: 멘션과 담당자 대조만 생략되고, 그 밖의 검사는 그대로 돈다."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "team.json")
    try:
        with open(path, encoding="utf-8") as f:
            team = json.load(f)
        if not isinstance(team, dict):
            raise ValueError("team.json 최상위가 객체가 아님")
        return normalize_team(team)
    except Exception as e:
        print(f"[warn] team.json 을 읽지 못했습니다({type(e).__name__}: {e}) → 멘션·담당자 대조를 생략합니다")
        return {"members": [], "roles": {}}


def normalize_team(team: dict) -> dict:
    """팀원마다 gitlab/jira/mm/name 키가 반드시 있는 형태로 맞춘다.

    호출부가 `{m["gitlab"]: m["jira"] for m in members}` 처럼 쓰는데, 한 사람의 한 칸이 비어 있으면
    임포트 시점 KeyError 로 스크립트가 통째로 죽는다 — 폴백을 넣어도 여기서 막지 않으면 의미가 없다
    (MR !78 AI 리뷰 지적, S15P21B202-134)."""
    members = []
    for m in (team.get("members") or []):
        if not isinstance(m, dict) or not m.get("gitlab"):
            continue                      # gitlab 아이디가 없으면 어떤 매핑에도 쓸 수 없다
        members.append({"gitlab": m.get("gitlab", ""), "jira": m.get("jira", ""),
                        "mm": m.get("mm", ""), "name": m.get("name", "") or m.get("gitlab", "")})
    roles = team.get("roles") if isinstance(team.get("roles"), dict) else {}
    return {"members": members, "roles": roles}


def role_member(team: dict, role: str) -> dict | None:
    """역할(lead/infra 등)에 해당하는 팀원. roles 와 members 가 어긋나도 예외를 던지지 않는다.

    이전에는 next(...) 가 StopIteration 을 던져 임포트만으로 알림 스크립트가 죽었다(-134)."""
    gitlab_id = (team.get("roles") or {}).get(role)
    if not gitlab_id:
        return None
    found = next((m for m in team.get("members", []) if m.get("gitlab") == gitlab_id), None)
    if found is None:
        print(f"[warn] team.json roles.{role}={gitlab_id!r} 인데 members 에 그 사람이 없습니다 → 멘션 생략")
    return found


def strip_draft(title: str) -> tuple[str, bool]:
    """Draft 접두어를 **모두** 떼어 낸다(겹쳐 붙은 경우 포함).
    한 번만 떼면 남은 'Draft:' 가 요약의 일부로 취급되어 제목 보정이 중복된다
    (실측 2026-08-28: 'Draft: [Infra] docs: Draft: [Infra] docs: …', S15P21B202-105)."""
    title, found = title.strip(), False
    while True:
        m = DRAFT_RE.match(title)
        if not m:
            return title.strip(), found
        title, found = title[m.end():].strip(), True


def key_from_branch(branch: str) -> str | None:
    m = BRANCH_RE.match(branch or "")
    return m.group(2) if m else None


def release_version(branch: str) -> str | None:
    """`release/S15P21B202-318-v0.1.0` → `v0.1.0`. 릴리스 브랜치 형식이 아니면 None."""
    m = RELEASE_BRANCH_RE.match(branch or "")
    return f"v{m.group(2)}" if m else None


def completes_on_merge(target: str) -> bool:
    """이 대상 브랜치로 머지되면 Jira 자동 완료(R6)가 걸리는가.

    R6 은 **대상 브랜치가 이슈 브랜치로 보이지 않을 때만** 이슈를 완료로 옮긴다
    (조건: `{{pullRequest.destinationBranch.name.match("^(?:hotfix/)?KEY-\\d+")}}` 가 빈 문자열).
    그래서 스택 자식 MR(대상 = 부모의 이슈 브랜치)은 머지돼도 완료되지 않는다 — 설계대로다.
    develop·master 로 가는 MR 만 True.

    아침 점검이 이 둘을 구분하지 못해 스택 자식을 "R6 가 빠졌다" 로 보고했다
    (2026-08-31 실측: !82 오탐, S15P21B202-159).
    """
    return not STACK_TARGET_RE.match(target or "")


def is_release_mr(source: str, target: str) -> bool:
    """릴리즈 MR — develop 의 내용을 master 로 가져가는 MR. 두 형태:
      · `develop → master` (처음 형태, master 정렬)
      · `release/<키>-vX.Y.Z → master` (2026-09-07 채택 — develop 에서 딴 스냅샷 브랜치, S15P21B202-325)

    이 MR 의 내용은 develop 으로 들어올 때 이미 MR 단위로 검사를 통과했고, 비교 기준이 master 라
    develop 의 기존 파일까지 전부 '추가됨' 으로 잡혀 오탐만 나온다
    (2026-08-28 MR !68 실측: .gitkeep 의 .meta 누락 14건 · 2026-09-07 !240: LFS 폰트·씬 선언 등 4건).
    그래서 내용 검사를 건너뛴다. Draft 금지·대상 브랜치 규칙·릴리스 규칙(squash 해제, 자기 커밋 없음)은 적용된다.

    형식이 틀린 `release/…`(버전 없음)는 릴리즈 MR 이 아니다 — 일반 검사로 가서 대상 규칙에 걸린다.
    hotfix/* → master 는 **예외가 아니다** — develop 을 거치지 않은 새 코드라
    이슈 키·템플릿·위생 검사를 모두 받아야 한다 (S15P21B202-122)."""
    if (target or "") != "master":
        return False
    return (source or "") == "develop" or bool(RELEASE_BRANCH_RE.match(source or ""))


def http(method: str, url: str, headers: dict | None = None, body: dict | str | None = None,
         timeout: int = 60):
    """(status, parsed-json-or-text). 4xx/5xx 도 예외 대신 튜플로 반환."""
    data = None
    hdrs = {"Accept": "application/json"}
    if headers:
        hdrs.update(headers)
    if body is not None:
        data = (body if isinstance(body, str) else json.dumps(body, ensure_ascii=False)).encode("utf-8")
        hdrs.setdefault("Content-Type", "application/json; charset=utf-8")
    req = urllib.request.Request(url, data=data, headers=hdrs, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode("utf-8", "replace")
            status = r.status
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", "replace")
        status = e.code
    try:
        return status, json.loads(raw) if raw else None
    except json.JSONDecodeError:
        return status, raw


class GitLab:
    """GITLAB_TOKEN(프로젝트 액세스 토큰) 이 있으면 API, 없으면 CI 환경변수로 읽기만."""

    def __init__(self):
        self.api = env("CI_API_V4_URL", "https://lab.ssafy.com/api/v4")
        self.project = env("CI_PROJECT_ID")
        self.iid = env("CI_MERGE_REQUEST_IID")
        self.token = env("GITLAB_TOKEN")

    @property
    def can_write(self) -> bool:
        return bool(self.token and self.project and self.iid)

    def _h(self):
        return {"PRIVATE-TOKEN": self.token}

    def headers(self) -> dict:
        """다른 모듈에서 쓰는 공개 헬퍼(사설 _h 직접 호출 금지)."""
        return self._h()

    def _mr_url(self, suffix: str = "") -> str:
        return f"{self.api}/projects/{self.project}/merge_requests/{self.iid}{suffix}"

    def get_mr(self) -> dict:
        if self.token:
            st, d = http("GET", self._mr_url(), self._h())
            if st == 200:
                return d
            print(f"[warn] MR 조회 실패 {st} → 환경변수로 대체")
        return {
            "title": env("CI_MERGE_REQUEST_TITLE", ""),
            "description": env("CI_MERGE_REQUEST_DESCRIPTION", "") or "",
            "source_branch": env("CI_MERGE_REQUEST_SOURCE_BRANCH_NAME", ""),
            "target_branch": env("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", ""),
            "draft": (env("CI_MERGE_REQUEST_TITLE", "") or "").lower().startswith("draft:"),
            "web_url": f"{env('CI_PROJECT_URL', '')}/-/merge_requests/{self.iid}",
            "author": {"username": env("GITLAB_USER_LOGIN", "")},
        }

    def update_mr(self, **fields) -> tuple[int, object]:
        return http("PUT", self._mr_url(), self._h(), fields)

    def changes(self) -> list[dict]:
        if not self.token:
            return []
        st, d = http("GET", self._mr_url("/changes?access_raw_diffs=true"), self._h())
        return d.get("changes", []) if st == 200 and isinstance(d, dict) else []

    def notes(self) -> list[dict]:
        st, d = http("GET", self._mr_url("/notes?per_page=100&sort=desc"), self._h())
        return d if st == 200 and isinstance(d, list) else []

    def upsert_note(self, marker: str, body: str) -> tuple[int, object]:
        """marker(HTML 주석)가 들어 있는 기존 노트가 있으면 수정, 없으면 생성."""
        for n in self.notes():
            if marker in (n.get("body") or ""):
                return http("PUT", self._mr_url(f"/notes/{n['id']}"), self._h(), {"body": body})
        return http("POST", self._mr_url("/notes"), self._h(), {"body": body})

    def discussions(self) -> list[dict] | None:
        """이 MR 의 스레드 목록. 조회 실패는 None — '스레드가 없다' 와 섞으면 잘못 만든다."""
        st, d = http("GET", self._mr_url("/discussions?per_page=100"), self._h())
        return d if st == 200 and isinstance(d, list) else None

    def project_flag(self, name: str) -> bool | None:
        """프로젝트 설정 한 개. 조회 실패는 None."""
        st, d = http("GET", f"{self.api}/projects/{self.project}", self._h())
        return d.get(name) if st == 200 and isinstance(d, dict) else None

    def upsert_thread(self, marker: str, body: str, resolved: bool) -> tuple[int, str]:
        """마커가 든 **스레드**를 만들거나 갱신하고 **해소 상태까지 맞춘다.** 반환 (HTTP, 한 일).

        평범한 노트(`upsert_note`)와 다른 점: 스레드는 해소 대상이고,
        `only_allow_merge_if_all_discussions_are_resolved` 가 켜져 있으면 **미해소 스레드 하나가
        머지 버튼을 잠근다**(2026-09-04 실측: `resolvable=True` · `blocking_discussions_resolved=False`).
        `Pipelines must succeed` 는 마지막 파이프라인만 보므로 재대상 직후의 **낡은 초록**을 막지
        못한다 — 그 창을 이 스레드가 닫는다(S15P21B202-297).

        `resolved=False` 로 부르면 **사람이 손으로 해소해 둔 것도 되돌린다.** 아직 순서가 아니라는
        사실은 사람의 클릭으로 바뀌지 않는다."""
        ds = self.discussions()
        if ds is None:
            return 0, "스레드 조회 실패"
        found = next((d for d in ds
                      if any(marker in (nt.get("body") or "") for nt in (d.get("notes") or []))), None)

        # 예전 방식(POST /notes)으로 남은 개별 노트는 해소 대상이 아니다 → 지우고 스레드로 다시 만든다
        if found is not None and found.get("individual_note"):
            for nt in found.get("notes") or []:
                http("DELETE", self._mr_url(f"/notes/{nt['id']}"), self._h())
            found = None

        if found is None:
            st, d = http("POST", self._mr_url("/discussions"), self._h(), {"body": body})
            if st not in (200, 201):
                return st, "스레드 생성 실패"
            if resolved:
                did = (d or {}).get("id")
                http("PUT", self._mr_url(f"/discussions/{did}"), self._h(), {"resolved": True})
            return st, "스레드 생성" + (" + 해소" if resolved else " (미해소 — 머지 잠김)")

        did = found.get("id")
        note = (found.get("notes") or [{}])[0]
        did_what = []
        if (note.get("body") or "") != body:
            http("PUT", self._mr_url(f"/discussions/{did}/notes/{note['id']}"), self._h(), {"body": body})
            did_what.append("본문 갱신")
        if bool(note.get("resolved")) != resolved:
            http("PUT", self._mr_url(f"/discussions/{did}"), self._h(), {"resolved": resolved})
            did_what.append("해소" if resolved else "**미해소로 되돌림**")
        return 200, " + ".join(did_what) or "변경 없음"

    def merged_mrs(self, days: int = 7, max_pages: int = 5, now=None) -> tuple[list[dict], bool]:
        """최근 N일 안에 **머지된** MR (merged_at 기준). updated_after 로 좁혀 페이지를 순회한 뒤 merged_at 으로 다시 거른다
        (GitLab MR API 에는 merged_after 필터가 없음). 반환 (mrs, complete) — max_pages 안에 끝나지 않으면 complete=False.

        `now` 는 테스트가 주입한다. 고정 날짜 픽스처를 실제 현재 시각으로 자르면 테스트가 **시간이 지나면 깨진다** —
        2026-09-03 17:06 KST 에 픽스처(08-27)가 7일 창 밖으로 나가 ci-tests 가 develop 그대로 실패했다(-281)."""
        from datetime import datetime, timedelta, timezone
        since_dt = (now or datetime.now(timezone.utc)) - timedelta(days=days)
        since = since_dt.strftime("%Y-%m-%dT%H:%M:%SZ")
        out: list[dict] = []
        for page in range(1, max_pages + 1):
            st, d = http("GET", f"{self.api}/projects/{self.project}/merge_requests?state=merged&updated_after={since}"
                                f"&per_page=100&page={page}&order_by=updated_at", self._h())
            if st != 200 or not isinstance(d, list):
                raise RuntimeError(f"GitLab merged MR 조회 {st}")
            for mr in d:
                merged_at = (mr.get("merged_at") or "")[:19]          # 'YYYY-MM-DDTHH:MM:SS' 만 사용 (밀리초·Z 제거, 3.10 이하 호환)
                if merged_at and datetime.strptime(merged_at, "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc) >= since_dt:
                    out.append(mr)
            if len(d) < 100:
                return out, True
        return out, False
    def mrs_for_commit(self, sha: str) -> tuple[list[dict], bool]:
        """이 커밋에 연결된 MR 들. develop push 파이프라인에서 "방금 무엇이 머지됐나" 를 아는 유일한 길이다.
        머지 커밋과 그 안의 개별 커밋 모두 같은 MR 을 돌려준다(2026-09-01 실측). 반환 (mrs, ok)."""
        if not sha:
            return [], True
        st, d = http("GET", f"{self.api}/projects/{self.project}/repository/commits/"
                            f"{quote(sha, safe='')}/merge_requests?per_page=20", self._h())
        if st != 200 or not isinstance(d, list):
            return [], False
        return d, True

    def merged_mrs_for_target(self, branch: str) -> tuple[list[dict], bool]:
        """이 브랜치를 **대상으로** 머지된 MR 들 = 스택 자식. 반환 (mrs, ok)."""
        if not branch:
            return [], True
        st, d = http("GET", f"{self.api}/projects/{self.project}/merge_requests"
                            f"?state=merged&target_branch={quote(branch, safe='')}&per_page=50", self._h())
        if st != 200 or not isinstance(d, list):
            return [], False
        return d, True

    def merged_mr_for_source(self, branch: str) -> tuple[dict | None, bool]:
        """이 브랜치를 **소스로** 머지된 MR (가장 최근 1건). 반환 (mr|None, ok).

        스택에서 부모가 어디로 들어갔는지 따라 올라가는 데 쓴다. 커밋 포함 여부(refs)로는 판정할 수 없다 —
        이 프로젝트는 squash 가 기본이라 부모가 머지될 때 자식 커밋이 사라진다
        (2026-08-31 실측: !114 의 머지 커밋을 담은 브랜치가 0개). ok=False 는 조회 실패이며 '머지 안 됨' 과 다르다.
        """
        if not branch:
            return None, True
        st, d = http("GET", f"{self.api}/projects/{self.project}/merge_requests"
                            f"?state=merged&source_branch={quote(branch, safe='')}&per_page=5&order_by=updated_at",
                     self._h())
        if st != 200 or not isinstance(d, list):
            return None, False
        return (d[0] if d else None), True

    def merge_base(self, ref_a: str, ref_b: str) -> str | None:
        """두 ref 의 공통 조상 sha. master 는 develop 의 조상이므로,
        (develop 기준 merge-base) == (master 기준 merge-base) 이면 그 브랜치는 master 시점 이전에서 갈라진 것."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/repository/merge_base"
                            f"?refs[]={quote(ref_a, safe='')}&refs[]={quote(ref_b, safe='')}", self._h())
        return d.get("id") if st == 200 and isinstance(d, dict) else None

    def branch_head(self, name: str = "develop") -> str | None:
        st, d = http("GET", f"{self.api}/projects/{self.project}/repository/branches/{quote(name, safe='')}", self._h())
        return ((d.get("commit") or {}).get("id")) if st == 200 and isinstance(d, dict) else None

    def behind_count(self, base_sha: str, ref: str = "develop") -> int | None:
        """base_sha 이후 ref 에 쌓인 커밋 수 = 그 시작점이 ref 보다 얼마나 뒤처졌는지."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/repository/compare"
                            f"?from={quote(base_sha, safe='')}&to={quote(ref, safe='')}&straight=true", self._h())
        return len(d.get("commits") or []) if st == 200 and isinstance(d, dict) else None

    def ahead_count(self, branch: str, base: str = "develop") -> int | None:
        """branch 에만 있고 base 에는 없는 커밋 수. 조회 실패는 None(0 과 구분한다).

        릴리스 브랜치는 develop 의 스냅샷이어야 하므로 이 값이 0 이어야 한다 — 자기 커밋이 있으면
        그 변경은 develop 에 없어 다음 릴리스에서 사라지고, develop 재머지라는 절차가 하나 더 생긴다(S15P21B202-325)."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/repository/compare"
                            f"?from={quote(base, safe='')}&to={quote(branch, safe='')}&straight=true", self._h())
        return len(d.get("commits") or []) if st == 200 and isinstance(d, dict) else None

    def compare_paths(self, from_sha: str, to_ref: str = "develop") -> list[str] | None:
        """from_sha..to_ref 사이에 바뀐 파일 경로. 못 읽으면 None — 빈 목록과 구분한다.

        빈 목록("바뀐 것이 없다")과 None("확인하지 못했다")을 섞으면 배포 표류 경고가
        조용히 사라진다. 배포는 안 하고도 초록으로 보이는 것이 제일 위험한 상태다(-309)."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/repository/compare"
                            f"?from={quote(from_sha, safe='')}&to={quote(to_ref, safe='')}&straight=true", self._h())
        if st != 200 or not isinstance(d, dict):
            return None
        out: list[str] = []
        for f in (d.get("diffs") or []):
            for k in ("new_path", "old_path"):
                if f.get(k):
                    out.append(f[k])
        return sorted(set(out))

    def mr_commits(self, max_pages: int = 5) -> list[dict]:
        """MR 의 커밋 전체. 페이징을 돌지 않으면 커밋 100개가 넘는 MR 에서 뒤쪽 커밋에 섞인
        다른 이슈 키를 놓친다 — 오래 묵은 브랜치일수록 커밋이 많고, 그게 검사가 필요한 경우다(-134)."""
        out: list[dict] = []
        for page in range(1, max_pages + 1):
            st, d = http("GET", self._mr_url(f"/commits?per_page=100&page={page}"), self._h())
            if st != 200 or not isinstance(d, list):
                break
            out.extend(d)
            if len(d) < 100:
                break
        return out

    def mr_pipelines(self, iid: int | str) -> list[dict]:
        """해당 MR 의 파이프라인 목록(최신 우선). 조회에 실패하면 빈 목록이 아니라 None —
        '파이프라인이 없다' 와 '못 물어봤다' 를 섞으면 없는 경보를 만들어 낸다."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/merge_requests/{iid}/pipelines", self._h())
        return d if st == 200 and isinstance(d, list) else None

    def mr_notes(self, iid: int | str) -> list[dict] | None:
        """다른 MR 의 노트(최신 우선). `notes()` 는 이 파이프라인의 MR 만 본다.
        조회 실패는 None — '노트가 없다' 와 '못 물어봤다' 를 섞으면 없는 경보를 만들어 낸다."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/merge_requests/{iid}"
                            f"/notes?per_page=100&sort=desc", self._h())
        return d if st == 200 and isinstance(d, list) else None

    def create_mr_pipeline(self, iid: int | str) -> tuple[int, object]:
        """그 MR 에 파이프라인을 새로 만든다(merge request 파이프라인).

        GitLab 은 부모 브랜치 삭제로 자식 MR 을 자동 재대상할 때 **파이프라인을 만들지 않는다**
        (2026-09-02~04 실측 16건 중 14건). 그러면 자식에 남은 마지막 결과가 **부모 위에서 돈 것**인데
        초록으로 보인다 — S15P21B202-295."""
        return http("POST", f"{self.api}/projects/{self.project}/merge_requests/{iid}/pipelines", self._h())

    def branch_pipelines(self, ref: str = "develop", per_page: int = 20) -> list[dict]:
        """브랜치의 파이프라인 목록(최신 우선). 조회 실패는 빈 목록 — 부르는 쪽이 '못 물어봤다' 를
        구분해야 하면 완전탐색 플래그로 받는다(deploy_history 가 그렇게 한다)."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/pipelines"
                            f"?ref={quote(ref, safe='')}&per_page={per_page}&order_by=id&sort=desc", self._h())
        return d if st == 200 and isinstance(d, list) else []

    def project_jobs(self, max_pages: int = 8, per_page: int = 100) -> list[dict]:
        """프로젝트의 잡 목록(최신 우선). 한 번 호출로 100건이 오고 `ref`·`status`·`artifacts` 가 함께 온다.

        **왜 이것이 필요한가** (S15P21B202-342). Unity 기준선을 찾을 때 파이프라인을 하나씩 훑으면
        파이프라인당 잡 목록 호출이 한 번씩 든다. 그래서 창을 15개로 잡아 뒀는데, 문서·스크립트만
        머지되는 날이 이어지면 그 창 안에 `unity-compile` 이 아예 없다(그 잡은 `changes: METAGAME/**`
        게이트를 쓴다). 2026-09-08 실측: 가장 최근 것이 7번째 뒤, 그 전은 14번째 뒤 — 창 경계였다.
        이 API 는 잡을 바로 주므로 같은 호출 수로 훨씬 멀리 되짚는다.

        조회 실패는 **빈 목록이 아니라 그때까지 모은 것**을 준다 — 부르는 쪽이 폴백을 판단한다.
        """
        out: list[dict] = []
        for page in range(1, max_pages + 1):
            st, d = http("GET", f"{self.api}/projects/{self.project}/jobs"
                                f"?per_page={per_page}&page={page}", self._h())
            if st != 200 or not isinstance(d, list) or not d:
                break
            out.extend(d)
            if len(d) < per_page:
                break
        return out

    def pipeline_jobs(self, pipeline_id: int | str) -> list[dict]:
        """파이프라인 한 건의 잡 목록.

        프로젝트 전체 잡 목록(`/jobs`)으로 특정 잡을 찾지 않는 이유 — 그 API 는 **이름으로 걸러 주지
        않는다.** 잡이 수천 건인 리포에서 하루 한 번 도는 배포 잡을 찾으려면 수십 페이지를 넘겨야 한다.
        파이프라인을 최신부터 훑으면 대개 첫 몇 건에서 찾는다."""
        st, d = http("GET", f"{self.api}/projects/{self.project}/pipelines/{pipeline_id}/jobs?per_page=100",
                     self._h())
        return d if st == 200 and isinstance(d, list) else []

    def job_artifact_text(self, job_id: int | str, path: str) -> str | None:
        """잡 아티팩트 한 개를 텍스트로. 없거나 못 읽으면 None.

        http() 는 JSON 파싱에 실패하면 본문 문자열을 그대로 돌려주므로 XML 도 이걸로 읽힌다.
        (-271: 대상 브랜치의 마지막 Unity 결과를 기준선으로 쓰는 데 필요하다)"""
        st, d = http("GET", f"{self.api}/projects/{self.project}/jobs/{job_id}/artifacts/{path}",
                     self._h())
        return d if st == 200 and isinstance(d, str) and d.strip() else None

    def open_mrs(self) -> list[dict]:
        st, d = http("GET", f"{self.api}/projects/{self.project}/merge_requests?state=opened&per_page=100",
                     self._h())
        return d if st == 200 and isinstance(d, list) else []


class Jira:
    # **읽기 전용이다.** 이슈 상태를 바꾸는 것은 Jira 자동화 규칙(R5·R6·R9)만 한다 — CI 는 검사·알림만
    # 맡는다(S15P21B202-242). 그래서 CI 변수 JIRA_API_TOKEN 은 읽기 스코프로 충분하고, 팀 공용
    # 변수에 쓰기 권한을 두지 않는다. 상태 전이 규칙이 Jira 한 곳에만 있어 두 곳이 어긋나지도 않는다.
    """REST 호출은 api.atlassian.com/ex/jira/{cloudId} 경로를 쓴다 — 스코프(읽기 전용) API 토큰은 사이트 URL 로는
    익명 취급되어 404 가 나고(2026-08-27 실측), 이 경로는 클래식·스코프 토큰 모두 동작한다. 링크(browse)는 사이트 URL."""

    def __init__(self):
        self.url = (env("JIRA_URL", "https://ssafy.atlassian.net") or "").rstrip("/")
        cloud = env("JIRA_CLOUD_ID", "15a21def-11f5-4358-ab48-074194685963")
        self.api = f"https://api.atlassian.com/ex/jira/{cloud}" if cloud else self.url
        self.user = env("JIRA_USER") or env("JIRA_USERNAME")
        self.token = env("JIRA_API_TOKEN")

    @property
    def available(self) -> bool:
        return bool(self.user and self.token)

    def _h(self):
        tok = base64.b64encode(f"{self.user}:{self.token}".encode()).decode()
        return {"Authorization": f"Basic {tok}"}

    def get_issue(self, key: str) -> dict | None:
        """이슈 dict, 없으면 None. 그 외 오류는 예외."""
        st, d = http("GET", f"{self.api}/rest/api/3/issue/{key}"
                            "?fields=summary,status,labels,issuetype,assignee,parent,duedate,description",
                     self._h())
        if st == 404:
            return None
        if st != 200:
            raise RuntimeError(f"Jira GET {key} → {st}: {str(d)[:200]}")
        return d

    def search(self, jql: str, fields: list[str], max_results: int = 50) -> list[dict]:
        st, d = http("POST", f"{self.api}/rest/api/3/search/jql", self._h(),
                     {"jql": jql, "maxResults": max_results, "fields": fields})
        if st != 200:
            raise RuntimeError(f"Jira search → {st}: {str(d)[:200]}")
        return d.get("issues", [])

    def search_all(self, jql: str, fields: list[str], page_size: int = 100, max_pages: int = 20) -> tuple[list[dict], bool]:
        """전량 수집(/search/jql 의 nextPageToken 순회). 반환 (issues, complete) — max_pages 안에 끝나지 않으면 complete=False.
        Jira 가 요청보다 작은 페이지를 돌려줘도 isLast/nextPageToken 으로 판단하므로 잘림을 놓치지 않는다."""
        out: list[dict] = []
        token = None
        for _ in range(max_pages):
            body: dict = {"jql": jql, "maxResults": page_size, "fields": fields}
            if token:
                body["nextPageToken"] = token
            st, d = http("POST", f"{self.api}/rest/api/3/search/jql", self._h(), body)
            if st != 200:
                raise RuntimeError(f"Jira search → {st}: {str(d)[:200]}")
            out += d.get("issues", [])
            token = d.get("nextPageToken")
            if d.get("isLast", token is None) or not token:
                return out, True
        return out, False


def part_of_issue(issue: dict) -> str | None:
    """레이블 중 파트 태그 우선, 없으면 제목의 [Part]."""
    f = issue.get("fields", {})
    for lb in f.get("labels", []) or []:
        if lb in PARTS:
            return lb
    m = re.match(r"^\s*\[(\w+)\]", f.get("summary", "") or "")
    return m.group(1) if m and m.group(1) in PARTS else None


def summary_body(issue: dict) -> str:
    return re.sub(r"^\s*\[\w+\]\s*", "", issue.get("fields", {}).get("summary", "") or "").strip()


def sections(desc: str) -> dict[str, str]:
    """'## 제목' 단위로 본문을 나눈다. 키는 '## ' 뒤 텍스트."""
    out, cur, buf = {}, None, []
    for line in (desc or "").splitlines():
        if line.startswith("## "):
            if cur is not None:
                out[cur] = "\n".join(buf)
            cur, buf = line[3:].strip(), []
        elif cur is not None:
            buf.append(line)
    if cur is not None:
        out[cur] = "\n".join(buf)
    return out


def find_section(secs: dict[str, str], prefix: str) -> str | None:
    for k, v in secs.items():
        if k.startswith(prefix):
            return v
    return None


class Report:
    def __init__(self, name: str):
        self.name, self.fails, self.warns, self.oks = name, [], [], []

    def ok(self, msg): self.oks.append(msg); print(f"  ✅ {msg}")
    def warn(self, msg): self.warns.append(msg); print(f"  ⚠  {msg}")
    def fail(self, msg): self.fails.append(msg); print(f"  ❌ {msg}")

    def finish(self) -> int:
        print(f"\n== {self.name}: 통과 {len(self.oks)} · 경고 {len(self.warns)} · 실패 {len(self.fails)} ==")
        for f in self.fails:
            print(f"   FAIL: {f}")
        return 1 if self.fails else 0


def banner(title: str):
    print(f"\n===== {title} =====")
    sys.stdout.flush()
