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
import urllib.parse
import urllib.request

PARTS = ["Plan", "Art", "Sound", "UI", "Character", "Physics", "Puzzle", "Level",
         "Network", "Backend", "Infra", "Data", "QA"]
TYPES = ["feat", "fix", "asset", "scene", "refactor", "docs", "test", "perf", "build", "chore"]
PROJECT_KEY = os.environ.get("JIRA_PROJECT_KEY", "S15P21B202")

TITLE_RE = re.compile(
    rf"^\[({'|'.join(PARTS)})\] ({'|'.join(TYPES)})(\([^)]+\))?: (.+?) \(({PROJECT_KEY}-\d+)\)$")
BRANCH_RE = re.compile(rf"^(hotfix/)?({PROJECT_KEY}-\d+)(-[A-Za-z0-9._-]+)?$")
KEY_RE = re.compile(rf"{PROJECT_KEY}-\d+")
DRAFT_RE = re.compile(r"^(\[?draft\]?:?\s*|\(draft\)\s*|wip:\s*)", re.IGNORECASE)


def env(name: str, default: str | None = None) -> str | None:
    v = os.environ.get(name)
    return v if v not in (None, "") else default


def strip_draft(title: str) -> tuple[str, bool]:
    m = DRAFT_RE.match(title)
    return (title[m.end():].strip(), True) if m else (title.strip(), False)


def key_from_branch(branch: str) -> str | None:
    m = BRANCH_RE.match(branch or "")
    return m.group(2) if m else None


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

    def merged_mrs(self, days: int = 7, max_pages: int = 5) -> tuple[list[dict], bool]:
        """최근 N일 안에 **머지된** MR (merged_at 기준). updated_after 로 좁혀 페이지를 순회한 뒤 merged_at 으로 다시 거른다
        (GitLab MR API 에는 merged_after 필터가 없음). 반환 (mrs, complete) — max_pages 안에 끝나지 않으면 complete=False."""
        from datetime import datetime, timedelta, timezone
        since_dt = datetime.now(timezone.utc) - timedelta(days=days)
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

    def open_mrs(self) -> list[dict]:
        st, d = http("GET", f"{self.api}/projects/{self.project}/merge_requests?state=opened&per_page=100",
                     self._h())
        return d if st == 200 and isinstance(d, list) else []


class Jira:
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
