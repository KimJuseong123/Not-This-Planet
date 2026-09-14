"""mm-digest — 확인이 필요한 것들을 모아 Mattermost 채널로 보낸다 (스케줄 파이프라인).

입력: MM_WEBHOOK_URL(필수), JIRA_USER/JIRA_API_TOKEN(Jira 항목), GITLAB_TOKEN(열린 MR 항목)
항목: needs-fix 이슈 · 기한 지난 미완료 이슈 · 3일간 갱신 없는 진행 중 이슈 · 스프린트인데 담당자 없는 이슈 · 24시간 넘게 열린 MR
      · 검사가 돌지 않은 MR — 파이프라인 없음·오래 대기(S15P21B202-130)
      · 서로 충돌하는 열린 MR 쌍(실제 3-way 머지 시험 — 머지 순서 안내, S15P21B202-138)
      · 최근 7일 안에 머지된 MR(merged_at 기준) 의 브랜치 이슈가 아직 완료가 아닌 것(Jira 자동화 R6 누락 감지, S15P21B202-83)
아무 것도 없으면 보내지 않는다.
"""
import json
import os
from datetime import datetime, timedelta, timezone

from urllib.parse import quote

import ec2_status
from ci_common import (PROJECT_KEY, GitLab, Jira, banner, completes_on_merge, env, http,
                       key_from_branch, load_team, role_member)

JIRA_BROWSE = (env("JIRA_URL", "https://ssafy.atlassian.net") or "").rstrip("/") + "/browse/"
TEAM = load_team()
MM_BY_JIRA = {m["jira"]: m["mm"] for m in TEAM["members"] if m.get("jira")}
MM_BY_GITLAB = {m["gitlab"]: m["mm"] for m in TEAM["members"] if m.get("gitlab")}
_LEAD = role_member(TEAM, "lead")
LEAD_MM = (_LEAD or {}).get("mm", "")            # 못 찾으면 멘션 없이 보낸다 — 알림 자체가 사라지는 것보다 낫다
_INFRA = role_member(TEAM, "infra") or _LEAD
INFRA_MM = (_INFRA or {}).get("mm", "")          # 조회 실패 = Infra 가 볼 일


def who(assignee: dict | None) -> str:
    """담당자 → @멘션(매핑 있으면), 없으면 표시 이름, 미지정이면 팀장 멘션."""
    if not assignee:
        return f"@{LEAD_MM}(담당자 없음)"
    mm = MM_BY_JIRA.get(assignee.get("accountId", ""))
    return f"@{mm}" if mm else (assignee.get("displayName") or "?")


def fmt_issues(issues: list[dict]) -> str:
    """| 이슈 | 담당 | 표 한 덩어리 (MM Markdown)."""
    rows = "\n".join(f"| [{i['key']}]({JIRA_BROWSE}{i['key']}) {i['fields'].get('summary', '')} | {who(i['fields'].get('assignee'))} |"
                     for i in issues)
    return "| 이슈 | 담당 |\n|---|---|\n" + rows


KEYS_PER_QUERY = 50      # 이보다 많으면 키 조회를 포기하고 전량 조회로 간다

# 스토리 포인트의 커스텀 필드 번호. 인스턴스마다 다르므로 바뀌면 여기만 고친다 —
# `GET /rest/api/3/field` 에서 이름이 "Story Points" 인 것의 id 다(2026-09-14 확인: 10031).
# 번호가 틀리면 Jira 가 JQL 을 거부하고 **그 항목만** "조회 실패" 로 나간다(아래 try/except).
STORY_POINTS = "cf[10031]"


def not_done_issues(jira: Jira, keys: list[str]) -> tuple[list[dict], bool]:
    """완료가 아닌 이슈를 모은다 — 먼저 **키로 직접** 묻고, 안 되면 전량 조회로 폴백한다.

    전량 조회(statusCategory != Done)는 머지 MR 이 몇 건이든 프로젝트의 미완료 이슈를 전부 받아오므로
    스프린트가 쌓이면 페이지 상한에 걸려 누락되고 호출도 무겁다(-134).
    그렇다고 키 조회만 쓸 수도 없다: 브랜치 이름에 오타 키나 삭제된 테스트 이슈 키가 섞이면
    Jira 가 JQL 자체를 거부한다(그래서 원래 전량 조회를 골랐다). 그래서 **시도 후 폴백** 이다."""
    fields = ["summary", "assignee", "status"]
    if len(keys) <= KEYS_PER_QUERY:
        key_list = ", ".join(sorted(keys))
        try:
            return jira.search_all(f"project = {PROJECT_KEY} AND key in ({key_list}) "
                                   f"AND statusCategory != Done AND issuetype != Epic ORDER BY key", fields)
        except Exception as e:
            print(f"키 조회 실패({type(e).__name__}) → 미완료 이슈 전량 조회로 폴백: {str(e)[:120]}")
    return jira.search_all(f"project = {PROJECT_KEY} AND statusCategory != Done AND issuetype != Epic ORDER BY key", fields)


def landed_on_develop(gl: GitLab, target: str, depth: int = 5) -> bool | None:
    """스택 자식의 코드가 결국 develop 에 도달했는가. 부모 MR 을 따라 올라간다.

    - `True`  : 어느 단계에서 develop·master 로 머지됐다 → 자식 이슈는 **지금 완료로 옮길 수 있다**
    - `False` : 부모가 아직 머지되지 않았거나, 실험용·폐기 브랜치로 들어가 develop 에 없다 → **보고하지 않는다**
                (2026-08-29 오탐 !82 가 이 경우였다: -138 코드는 develop 에 없었다)
    - `None`  : 조회 실패 → 판단 불가. 일을 숨기는 쪽이 더 나쁘므로 표시하고 물음표를 붙인다
    """
    seen: set[str] = set()
    while target and depth > 0:
        if completes_on_merge(target):
            return True
        if target in seen:            # 있을 수 없는 순환 — 무한 루프만 막는다
            return False
        seen.add(target)
        mr, ok = gl.merged_mr_for_source(target)
        if not ok:
            return None
        if mr is None:                # 부모가 아직 머지 안 됨 / 폐기된 브랜치
            return False
        target = mr.get("target_branch") or ""
        depth -= 1
    return False


def merged_not_done(gl: GitLab, jira: Jira, days: int = 7, now=None) -> tuple[list[str], list[str], bool]:
    """최근 N일 안에 머지됐는데 Jira 상태가 완료가 아닌 이슈 → 표 행. **두 갈래로 나눠** 돌려준다.

    - `rows`  : 대상이 develop·master 인 MR — R6 가 걸려야 했는데 안 걸린 것. **이상 상황**
    - `stacks`: 대상이 다른 이슈 브랜치인 스택 자식 MR — R6 가 원래 걸리지 않는다. **정상이지만 손이 필요**

    나누지 않으면 스택 자식이 "자동 완료가 빠졌다" 로 보고돼 매일 오탐이 된다
    (2026-08-31 실측: !82 가 그렇게 잡혔다 — S15P21B202-159).
    자식 이슈를 CI 가 자동 완료하던 장치(-199)는 없앴다 — 순서를 CI 가 막게 됐고(-246), Jira 상태는
    Jira 자동화만 쓴다(-242). 그래서 이 표는 **사람이 옮겨야 하는 목록**이다.

    키로 직접 조회하고(가벼움), 존재하지 않는 키(오타·삭제된 테스트 이슈)가 섞여 JQL 이 거부되면 미완료 이슈 전량 조회로 폴백해 로컬에서 교차한다(-134)."""
    by_key: dict[str, dict] = {}
    mrs, mrs_complete = gl.merged_mrs(days, now=now)
    for mr in mrs:
        k = key_from_branch(mr.get("source_branch") or "")
        if k and k not in by_key:
            by_key[k] = mr
    if not by_key:
        # 반드시 3개를 돌려준다. 예전엔 2개였고, 7일 안에 이슈 키가 붙은 머지가 없으면 main() 이
        # ValueError 를 잡아 "조회 실패 → @Infra" 오탐 카드를 냈다(-281). 매일 머지가 있어 숨어 있던 경로다.
        return [], [], mrs_complete
    issues, complete = not_done_issues(jira, list(by_key))
    rows, stacks = [], []
    for i in issues:
        mr = by_key.get(i["key"])
        if not mr:
            continue
        target = mr.get("target_branch") or ""
        head = (f"| [{i['key']}]({JIRA_BROWSE}{i['key']}) {i['fields'].get('summary', '')} "
                f"| {i['fields'].get('status', {}).get('name', '')} | [!{mr['iid']}]({mr['web_url']}) ")
        if completes_on_merge(target):
            rows.append(head + f"| {who(i['fields'].get('assignee'))} |")
            continue
        landed = landed_on_develop(gl, target)
        if landed is False:      # 부모가 아직 develop 에 없다 → 지금은 할 일이 아니다
            continue
        mark = "" if landed else " ⁇"   # None = 조회 실패. 숨기지 않고 물음표로 남긴다
        stacks.append(head + f"| `{target}`{mark} | {who(i['fields'].get('assignee'))} |")
    return rows, stacks, complete and mrs_complete


def _hdrs(gl: GitLab) -> dict:
    """S15P21B202-109 의 공개 헬퍼가 머지되기 전후 모두 동작하도록."""
    return gl.headers() if hasattr(gl, "headers") else gl._h()


def ahead_of_develop(gl: GitLab, branch: str) -> int | None:
    """그 브랜치에만 있고 develop 에는 없는 커밋 수. 조회 실패는 None(0 과 구분한다).

    `0` = develop 에 이미 다 들어갔다(지우면 끝) · `>0` = 유실 후보. 이 구분이 없으면 둘을 같은
    문구로 경고하게 되고, 그러면 사람이 카드를 안 읽는다(-282)."""
    st, d = http("GET", f"{gl.api}/projects/{gl.project}/repository/compare"
                        f"?from=develop&to={quote(branch, safe='')}&straight=true", _hdrs(gl))
    if st != 200 or not isinstance(d, dict):
        return None
    return len(d.get("commits") or [])


def resurrected_branches(gl: GitLab, days: int = 7, now=None) -> list[str]:
    """최근 N일 안에 머지된 MR 의 소스 브랜치가 **다시 존재**하면 표 행으로. 머지 후 같은 브랜치에 push 한 경우를 잡는다
    (그렇게 push 하면 MR·파이프라인이 없어 조용히 유실된다 — S15P21B202-110).

    **develop 에 없는 커밋 수를 함께 센다**(-282). 0 이면 브랜치만 남은 것이라 지우면 끝이고,
    1 이상이면 그 커밋이 어디에도 없다 = 진짜 유실 후보다. 실측 예: `-126` 은 0, `-74` 는
    `Main.unity` 126줄을 포함해 >0 이었는데 카드가 둘을 똑같이 경고했다."""
    mrs, _ = gl.merged_mrs(days, now=now)
    rows = []
    for mr in mrs:
        br = mr.get("source_branch") or ""
        if not br or not mr.get("should_remove_source_branch", True):
            continue
        st, d = http("GET", f"{gl.api}/projects/{gl.project}/repository/branches/{quote(br, safe='')}", _hdrs(gl))
        if st != 200 or not isinstance(d, dict):
            continue
        author = ((d.get("commit") or {}).get("author_name")) or (mr.get("author") or {}).get("username", "?")
        mention = next((f"@{m['mm']}" for m in TEAM["members"] if m["name"] == author or m["gitlab"] == author), author)
        ahead = ahead_of_develop(gl, br)
        if ahead is None:
            what = "⁇ 확인 실패"
        elif ahead == 0:
            what = "0 — 지우면 끝"
        else:
            what = f"**{ahead}개 — develop 에 없음**"
        rows.append(f"| `{br}` | [!{mr['iid']}]({mr['web_url']}) | {what} | {mention} |")
    return rows


def pending_hours(raw: str | None = None) -> float:
    """대기 경보 임계값(시간). CI 변수에 숫자가 아닌 값이 들어와도 아침 점검 전체가 죽지 않게 기본값으로 돌아간다."""
    try:
        return float(raw if raw is not None else env("DIGEST_PENDING_HOURS", "2") or 2)
    except (TypeError, ValueError):
        print("[warn] DIGEST_PENDING_HOURS 값이 숫자가 아님 → 기본 2시간 사용")
        return 2.0


def unchecked_mrs(gl: GitLab, now: datetime) -> list[str]:
    """검사가 한 번도 돌지 않았거나 계속 대기 중인 열린 MR → 표 행.

    빨간불이 없는 MR 은 통과한 것처럼 보이지만, 다음 두 경우는 아무 검사도 받지 않은 것이다.
      · 파이프라인 없음 — 소스 브랜치에 .gitlab-ci.yml 이 없다(master 기반 브랜치. 2026-08-28 MR !74 실측:
        훅도 파이프라인도 알림도 없이 MR 이 열렸다)
      · 계속 대기 — 러너가 꺼져 있다(2026-08-28 F1 실측). 이 상태로도 머지는 막히지 않는다
    조회 실패(None)는 경보로 만들지 않는다 — 없는 문제를 매일 띄우는 쪽이 더 나쁘다."""
    rows = []
    for mr in gl.open_mrs():
        pipes = gl.mr_pipelines(mr["iid"])
        if pipes is None:
            continue
        author = (mr.get("author") or {}).get("username", "")
        mention = f"@{MM_BY_GITLAB.get(author, author)}"
        link = f"[!{mr['iid']}]({mr['web_url']}) {mr['title']}"
        if not pipes:
            rows.append(f"| {link} | 파이프라인 없음 — 브랜치에 CI 설정이 없다(master 기반?) | {mention} |")
            continue
        latest = max(pipes, key=lambda x: x.get("id", 0))   # 목록 순서에 기대지 않는다
        if latest.get("status") in ("pending", "created", "waiting_for_resource"):
            created = datetime.fromisoformat(latest["created_at"].replace("Z", "+00:00"))
            hours = (now - created).total_seconds() / 3600
            if hours >= pending_hours():
                rows.append(f"| {link} | {hours:.0f}시간째 대기 — 러너 확인 | {mention} @{INFRA_MM} |")
    return rows


# ── 배포 상태 (S15P21B202-144 · -464) ────────────────────────────────
#
# **이름이 어긋나면 이 줄은 조용히 거짓말을 한다** (S15P21B202-464). `-284` 가
# `deploy-backend` 를 `deploy-dev`·`deploy-prod` 로 가를 때 이 상수가 옛 이름으로
# 남았다. 그 뒤로 아침 카드의 배포 줄은 **늘 "CI 로 올린 이력 없음"** 이었다 —
# 2026-09-12 실측: develop 파이프라인 30건에 `deploy-backend` 는 하나도 없고
# `deploy-dev` 만 5건 있다. 아무도 못 알아챈 이유는 그 문구가 **있을 수 있는 답**
# 이기 때문이다. 그래서 이름·ref·라벨을 한 자리에 묶어 둔다.
DEV_JOB = "deploy-dev"
PROD_JOB = "deploy-prod"

# (라벨, 잡 이름, ref, 이력이 없을 때 덧붙일 말)
DEPLOY_ENVS = (
    ("개발", DEV_JOB, "develop",
     "머지는 버튼만 만듭니다 — 새벽 스케줄이나 사람이 눌러야 바뀝니다"),
    ("배포", PROD_JOB, "master",
     "**릴리스는 빌드까지입니다** — `master` 파이프라인의 `deploy-prod` 를 눌러야 서버가 바뀝니다"),
)

DEPLOY_SCAN_PIPELINES = 20      # 파이프라인 몇 건까지 거슬러 볼지


KST = timezone(timedelta(hours=9))


def now_kst() -> str:
    """카드 헤더용 현재 시각(KST). naive datetime.now() 를 쓰면 EC2 컨테이너 TZ 가 UTC 라
    09:33 발송 카드에 '00:33' 이 찍힌다 — 09-01·02·03 사흘 연속 그렇게 나갔다(-282)."""
    return datetime.now(KST).strftime("%m/%d %H:%M")


def kst(iso: str | None) -> str:
    """GitLab 이 주는 UTC ISO 문자열을 한국 시간 표시로. 팀이 읽는 시각이 KST 다."""
    if not iso:
        return "?"
    try:
        t = datetime.fromisoformat(iso.replace("Z", "+00:00")).astimezone(KST)
        return t.strftime("%m/%d %H:%M")
    except ValueError:
        return "?"


TEST_JOB = "ci-tests"


def test_health(gl: GitLab) -> str | None:
    """이 스케줄 파이프라인의 `ci-tests` 결과. **통과했으면 아무 말도 하지 않는다.**

    스케줄에서 테스트를 돌리는 이유는 코드가 안 바뀌어도 시간이 흘러 깨지는 것을 잡기 위해서다 —
    `-281` 이 그랬다(픽스처 08-27 고정 vs `now-7일` 창 → 09-03 17:06 KST 에 저절로 실패,
    그 뒤 `scripts/ci` 를 건드린 MR 이 없어 사흘 가까이 아무도 몰랐다). 돌기만 하고 아무도 안 보면
    의미가 없으므로 실패했을 때만 카드에 싣는다.

    아직 안 끝난 상태(running/pending)는 **아무 말도 하지 않는다** — 거짓 경고를 내지 않는 쪽이 낫다."""
    pid = env("CI_PIPELINE_ID")
    if not pid:
        return None
    try:
        jobs = [j for j in gl.pipeline_jobs(pid) if j.get("name") == TEST_JOB]
    except Exception as e:  # noqa: BLE001
        return f"**🧪 오프라인 테스트 결과를 못 읽었습니다** → @{INFRA_MM} ({type(e).__name__})"
    if not jobs:
        return None
    job = max(jobs, key=lambda j: j.get("id") or 0)
    st = job.get("status")
    # **실패만** 보고한다. 화이트리스트로 두면 `canceled` 가 실패로 새는데, GitLab 은 같은 ref 의
    # 새 파이프라인을 만들 때 이전 파이프라인의 대기 잡을 자동 취소한다 — 배포 카드가 정확히 그 이유로
    # 오탐을 냈다(-282 09-03 09:52 실측). 테스트가 취소된 것은 "테스트가 깨졌다" 는 정보가 아니다.
    if st != "failed":
        return None
    where = job.get("web_url") or ""
    return (f"**🧪 오프라인 테스트 {st}** → @{INFRA_MM}\n\n"
            f"코드가 안 바뀌어도 시간이 흐르면 깨질 수 있습니다(`-281`). "
            f"[{TEST_JOB} 로그]({where}) 를 보고 오늘 안에 고쳐 주세요.")


def deploy_jobs(gl: GitLab, ref: str = "develop", job_name: str = DEV_JOB,
                max_pipelines: int = DEPLOY_SCAN_PIPELINES) -> tuple[list[dict], bool]:
    """그 ref 의 파이프라인을 최신부터 훑어 배포 잡을 모은다(최신 우선). 반환 (잡 목록, 전량 확인 여부).

    프로젝트 전체 잡 목록으로 찾지 않는 이유는 그 API 가 **이름으로 걸러 주지 않아서**다
    (ci_common.pipeline_jobs 주석 참고). 배포 잡은 `changes: backend/**` 일 때만 만들어지므로
    파이프라인 20건을 봐도 배포 잡은 몇 건뿐이다."""
    pipes = gl.branch_pipelines(ref, per_page=max_pipelines)
    found: list[dict] = []
    for p in pipes:
        same = [j for j in gl.pipeline_jobs(p.get("id")) if j.get("name") == job_name]
        if same:
            found.append(max(same, key=lambda j: j.get("id") or 0))   # 재시도가 있으면 마지막 것
    # 파이프라인이 상한보다 적게 왔으면 그것이 전부다 = 배포 이력을 다 봤다
    return found, len(pipes) < max_pipelines


def deploy_state(gl: GitLab, ref: str = "develop", job_name: str = DEV_JOB) -> dict:
    """{'live': 성공 잡|None, 'attempt': 마지막 시도 잡|None, 'pending': 눌리지 않은 배포 수, 'complete': bool}

    `live` 와 `attempt` 를 따로 두는 이유 — 마지막 시도가 실패했어도 **서버는 그 전 성공분으로 돌고 있다.**
    둘을 섞으면 실패한 커밋이 배포된 것처럼 읽힌다."""
    jobs, complete = deploy_jobs(gl, ref, job_name)
    live = next((j for j in jobs if j.get("status") == "success"), None)
    # 시작조차 안 한 canceled 는 배포 시도가 아니다. GitLab 이 같은 ref 의 새 파이프라인을 만들 때
    # 이전 파이프라인의 대기 잡을 자동 취소하는데(09-03 09:52 실측: 177699 의 잡이 177702 생성
    # 0.2초 뒤 canceled, started_at=None), 그걸 시도로 세면 "배포가 취소로 남아 있다" 오탐이 난다.
    attempt = next((j for j in jobs
                    if j.get("status") != "manual"
                    and not (j.get("status") == "canceled" and not j.get("started_at"))), None)
    pending = 0
    for j in jobs:                      # 마지막 시도보다 새로운 '눌리지 않은' 배포 = 배포 대기
        if j.get("status") != "manual":
            break
        pending += 1
    return {"live": live, "attempt": attempt, "pending": pending, "complete": complete,
            "drift": backend_drift(gl, live, ref)}


def backend_drift(gl: GitLab, live: dict | None, ref: str = "develop") -> list[str] | None:
    """배포된 커밋 이후 `ref` 에 쌓인 `backend/` 변경 파일. 확인 못 하면 None.

    **`pending` 만으로는 부족하다.** pending 은 '눌리지 않은 배포 잡' 을 세는데, 배포 잡이
    아예 만들어지지 않는 경우가 있다 — 09-06 에 -303(릴레이)의 파이프라인이 1분 뒤 머지된
    -304 때문에 자동 취소됐고, -304 는 `backend/` 를 건드리지 않아 새 파이프라인에는 배포 잡이
    없었다(-309). 그래서 pending=0, 파이프라인 전부 초록, 그런데 서버는 이틀 전 코드였다.
    잡 목록으로는 없는 것을 볼 수 없으니 **커밋을 직접 비교**한다."""
    sha = ((live or {}).get("commit") or {}).get("id")
    if not sha:
        return None
    paths = gl.compare_paths(sha, ref)
    if paths is None:
        return None
    return [p for p in paths if p.startswith("backend/")]


def deploy_line(st: dict, label: str = "배포", hint: str = "") -> str:
    """아침 점검 꼬리에 붙는 한 줄. 조치할 것이 없어도 나온다 — '지금 뭐가 올라가 있나' 는 항상 궁금하다.

    **어느 서버인지 적는다** (S15P21B202-464). 개발(8081)과 배포(8080)는 서로 다른 jar
    로 돌고, 릴리스가 났다고 배포 서버가 바뀌지 않는다 — 그 둘을 한 줄로 뭉쳐 두면
    "릴리스했으니 서버도 새것" 으로 읽힌다."""
    live, pending = st["live"], st["pending"]
    if not live:
        head = f"🚀 {label} 서버: **CI 로 올린 이력 없음**" + ("" if st["complete"] else f" (최근 파이프라인 {DEPLOY_SCAN_PIPELINES}건 안에서)")
        if hint:
            head += f" — {hint}"
    else:
        c = live.get("commit") or {}
        sha = c.get("short_id") or c.get("id", "")[:8] or "?"
        # **해시를 누를 수 있게 한다.** 더 볼 것이 있으면 링크로 가면 되고,
        # 주소가 없으면 코드 조각으로 물러선다(줄이 깨지지 않는 것이 먼저다).
        url = c.get("web_url")
        mark = f"[`{sha}`]({url})" if url else f"`{sha}`"
        # **무엇을 배포했는지가 아니라 `백엔드` 라고 적는다** (S15P21B202-490).
        #
        # 한때 여기에 머지 커밋의 MR 제목을 붙이려 했다. 그런데 개발 서버는 새벽
        # 스케줄이 develop 머리를 **`changes` 를 보지 않고** 배포하므로, 그때 머리가
        # 씬·아트 MR 이면 `🚀 개발 서버: … [Plan] scene: v1.1.0` 이 된다 —
        # **백엔드 줄에 백엔드와 무관한 문구**가 실린다. 해시는 "그 jar 을 어느
        # 커밋에서 만들었나" 일 뿐이고, 사람이 알고 싶은 것은 그 다음 줄이다.
        head = f"🚀 {label} 서버 백엔드: {mark} ({kst(live.get('finished_at'))})"
    if pending:
        head += f" · 백엔드 변경 **{pending}건 배포 대기**(수동 잡을 눌러야 올라갑니다)"
    elif st.get("drift") == []:
        # **같을 때도 말한다** (S15P21B202-490). 아무 말이 없으면 "최신이라 조용한
        # 것" 과 "확인을 못 한 것" 이 구별되지 않는다 — `drift` 는 확인 실패 시
        # `None` 이고 같을 때 `[]` 다. 그 둘을 여기서 가른다.
        head += " · develop 과 같음"
    elif live and st.get("drift") is None:
        # **배포 이력이 아예 없을 때는 붙이지 않는다.** 올린 적이 없는데 "최신 여부를
        # 확인 못 했다" 고 적으면, 이미 "CI 로 올린 이력 없음" 이라고 말한 줄에
        # 쓸데없는 말이 하나 더 붙는다.
        head += " · (최신 여부 확인 못 함)"
    elif st.get("drift"):
        # 눌릴 잡이 없는데 코드가 다르다 = 배포 잡이 사라진 것이다. 이쪽이 훨씬 위험하다 —
        # 대기 잡은 보이지만 없는 잡은 안 보인다.
        head += (f" · ⚠ **배포 대기 잡이 없는데 소스의 backend 가 배포본과 다릅니다**"
                 f"(파일 {len(st['drift'])}개) — 배포 잡이 만들어지지 않은 것입니다"
                 f" → @{INFRA_MM} 확인")
    return head


def main() -> int:
    banner("mm-digest")
    url = env("MM_WEBHOOK_URL")
    if not url:
        print("MM_WEBHOOK_URL 없음 → 생략"); return 0
    jira, gl = Jira(), GitLab()
    blocks = []
    if jira.available:
        base = f"project = {PROJECT_KEY} AND statusCategory != Done"
        fields = ["summary", "assignee", "status"]
        q = [
            ("🏷 `needs-fix` — 제목 형식/상위 Epic 확인", f"{base} AND labels = needs-fix ORDER BY updated DESC"),
            ("⏰ 기한 지남", f"{base} AND duedate < startOfDay() ORDER BY duedate ASC"),
            ("🐢 3일 이상 갱신 없는 진행 중", f"{base} AND status = \"In Progress\" AND updated <= -3d ORDER BY updated ASC"),
            ("👤 스프린트에 있는데 담당자 없음", f"{base} AND sprint in openSprints() AND assignee is EMPTY AND issuetype != Epic"),
            # **포인트는 이슈를 만들 때 예측해 넣는 값인데 Jira 가 비어도 막지 않는다**
            # (S15P21B202-482). 생성 화면 스키마에서 `customfield_10031` 은 `required: false` 다 —
            # 이 프로젝트의 필수 필드는 project·issuetype·summary·reporter 넷뿐이라 조용히 빈 채로 들어간다.
            #
            # **필수로 바꾸지 않는다.** 만드는 순간에는 예측이 안 서는 이슈가 있고, 막으면
            # 아무 숫자나 넣게 된다. **틀린 값은 빈 값과 달리 눈에 띄지 않는다.**
            # 그래서 막는 대신 다음 날 아침에 목록으로 돌려준다.
            #
            # 범위를 **열린 스프린트의 미완료**로 좁힌다 — 백로그까지 보면 매일 수십 줄이 나오고,
            # 그런 알림은 읽히지 않는다(바로 위 항목이 같은 이유로 같은 범위를 쓴다).
            # 끝난 이슈는 뺀다: 지나간 일에 예측값을 소급해 넣는 것은 뜻이 없다.
            ("🔢 스토리 포인트 없음 — 만들 때 넣지 못했으면 지금 넣는다",
             f"{base} AND sprint in openSprints() AND {STORY_POINTS} is EMPTY AND issuetype != Epic"),
        ]
        for title, jql in q:
            try:
                issues = jira.search(jql, fields)
            except Exception as e:  # 조회 실패도 조치할 사람(Infra)을 붙여 보낸다 — 매일 조용히 빠지는 것보다 낫다
                blocks.append(f"**{title}** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
                continue
            if issues:
                blocks.append(f"**{title}** ({len(issues)})\n\n{fmt_issues(issues)}")   # 제목-표 사이 빈 줄 필수
    else:
        blocks.append("_(JIRA_USER/JIRA_API_TOKEN 없음 — Jira 항목 생략)_")
    if gl.token and gl.project:
        th = test_health(gl)
        if th:
            blocks.insert(0, th)      # 인프라가 깨졌으면 맨 위에 — 나머지 항목의 신뢰도가 여기에 달려 있다
        now = datetime.now(timezone.utc)
        old = []
        for mr in gl.open_mrs():
            created = datetime.fromisoformat(mr["created_at"].replace("Z", "+00:00"))
            age_h = (now - created).total_seconds() / 3600
            if age_h >= 24:
                revs = [r["username"] for r in mr.get("reviewers", [])] or [TEAM["roles"]["lead"]]
                rev = " ".join(f"@{MM_BY_GITLAB.get(r, r)}" for r in revs)
                old.append(f"| [!{mr['iid']}]({mr['web_url']}) {mr['title']}{' (Draft)' if mr.get('draft') else ''} | {age_h/24:.1f}일 | {rev} |")
        if old:
            blocks.append(f"**🔀 24시간 넘게 열린 MR** ({len(old)})\n\n| MR | 경과 | 리뷰어 |\n|---|---|---|\n" + "\n".join(old))
        try:
            unchecked = unchecked_mrs(gl, now)
        except Exception as e:   # 이 항목이 실패해도 나머지 항목은 보낸다
            unchecked = []
            blocks.append(f"**🚫 검사가 돌지 않은 MR** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
        if unchecked:
            blocks.append(f"**🚫 검사가 돌지 않은 MR** ({len(unchecked)}) — 빨간불이 없다고 통과한 것이 아니다\n\n"
                          f"| MR | 왜 | 확인 |\n|---|---|---|\n" + "\n".join(unchecked))
        try:
            from mr_overlap import conflict_pairs
            pairs = conflict_pairs(gl)
        except Exception as e:   # 이 항목이 실패해도 나머지는 보낸다
            pairs = []
            blocks.append(f"**\u2694 서로 충돌하는 열린 MR** — 계산 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
        if pairs:
            rows_p = []
            for pr in pairs:
                a, b, c = pr["first"], pr["second"], pr["cmp"]
                kind = "⛔ 텍스트 충돌" if c["kind"] == "conflict" else "⚠ 같은 씬·프리팹"
                files = " · ".join(f"`{x}`" for x in (c["conflicts"] or c["risky"])[:4])
                who = f"@{MM_BY_GITLAB.get((b.get('author') or {}).get('username', ''), (b.get('author') or {}).get('username', '?'))}"
                rows_p.append(f"| [!{a['iid']}]({a['web_url']}) → [!{b['iid']}]({b['web_url']}) | {kind} | {files} | {who} |")
            blocks.append(f"**\u2694 서로 충돌하는 열린 MR** ({len(pairs)}) — 앞의 것을 먼저 머지하고, 뒤의 것은 그 위에 얹은 뒤 머지\n\n"
                          f"| 머지 순서 | 종류 | 파일 | 뒤 MR 작성자 |\n|---|---|---|---|\n" + "\n".join(rows_p))
        if jira.available:
            try:
                rows, stacks, complete = merged_not_done(gl, jira)
            except Exception as e:  # 이 항목이 실패해도 다른 항목은 보낸다
                rows, stacks, complete = [], [], True; blocks.append(f"**🔗 머지됐는데 완료 안 된 이슈** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
            if not complete:
                print("경고: 머지 MR 또는 미완료 이슈 수집이 페이지 상한에서 끊김 — 일부 누락 가능")
            try:
                res = resurrected_branches(gl)
            except Exception as e:  # noqa: BLE001
                res = []; blocks.append(f"**🌿 머지 뒤 다시 생긴 브랜치** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
            if res:
                blocks.append(f"**🌿 머지 뒤 다시 생긴 브랜치** ({len(res)}) — 머지된 브랜치에 다시 push 하면 MR·파이프라인 없이 조용히 남습니다. "
                              f"후속 작업은 새 이슈·새 브랜치로, 남은 브랜치는 삭제하세요\n\n"
                              f"| 브랜치 | 머지된 MR | develop 에 없는 커밋 | 담당 |\n|---|---|---|---|\n" + "\n".join(res))
            note = f"\n_⚠ 목록이 페이지 상한에서 끊겨 일부가 빠졌을 수 있음 — @{INFRA_MM} 확인_" if not complete else ""
            if rows:   # 매칭이 없으면 블록 없음(잘림 경고도 각주로만) — "항목 없으면 보내지 않는다" 유지
                blocks.append(f"**🔗 머지됐는데 완료 안 된 이슈** ({len(rows)}) — develop 으로 머지됐으니 자동 완료(R6)가 걸려야 했다. 담당자가 완료로 옮기면 허용됨(머지된 MR 있음)\n\n"
                              f"| 이슈 | 상태 | MR | 담당 |\n|---|---|---|---|\n" + "\n".join(rows) + note)
            if stacks:  # 스택 자식 — R6 가 원래 걸리지 않는다(오탐이 아니라 별 항목, S15P21B202-159)
                blocks.append(f"**🧩 스택 자식 MR — 완료로 옮기면 됩니다** ({len(stacks)}) — "
                              f"대상이 develop 이 아니라 다른 이슈 브랜치여서 자동 완료(R6)가 걸리지 않습니다. "
                              f"**부모가 이미 develop 에 들어갔으니** 담당자가 완료로 옮기면 됩니다(`⁇` 는 확인 실패 — 눈으로 보세요)\n\n"
                              f"| 이슈 | 상태 | MR | 머지 대상 | 담당 |\n|---|---|---|---|---|\n" + "\n".join(stacks) + (note if not rows else ""))
    # **개발과 배포를 따로 본다** (S15P21B202-464). 둘은 서로 다른 jar 로 돌고,
    # 릴리스가 났다고 배포 서버가 바뀌지 않는다 — `deploy-prod` 는 사람이 누른다.
    # 한 줄로 뭉쳐 두면 "릴리스했으니 서버도 새것" 으로 읽힌다.
    tails: list[str] = []
    if gl.token and gl.project:
        for label, job_name, ref, hint in DEPLOY_ENVS:
            dep = {"live": None, "attempt": None, "pending": 0, "complete": True, "drift": None}
            try:
                dep = deploy_state(gl, ref, job_name)
            except Exception as e:  # noqa: BLE001 — 배포 조회 실패가 점검 전체를 막지 않는다
                blocks.append(f"**🚀 {label} 배포 상태** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
                continue
            tails.append(deploy_line(dep, label, hint))
            att = dep["attempt"]
            if att and att.get("status") in ("failed", "canceled"):
                # 마지막 시도가 깨진 채로 남아 있다 = 아무도 뒤처리를 안 했다. 이건 조치 항목이다.
                live = dep["live"]
                now_on = f"`{(live.get('commit') or {}).get('short_id', '?')}` ({kst(live.get('finished_at'))})" if live else "**확인 필요**"
                spool = "~/metagame/deploy/deploy.log" if job_name == PROD_JOB else "~/metagame-dev/deploy/deploy.log"
                blocks.append(f"**🚀 {label} 서버의 마지막 배포 시도가 {'취소' if att.get('status') == 'canceled' else '실패'}로 남아 있습니다** → @{INFRA_MM}\n\n"
                              f"| 항목 | 내용 |\n|---|---|\n"
                              f"| 시도 | [잡 #{att.get('id')}]({att.get('web_url', '')}) · {kst(att.get('finished_at'))} |\n"
                              f"| 지금 서버 | {now_on} |\n"
                              f"| 볼 곳 | 잡 로그 · 호스트 `{spool}` |")
    # ── 서버 (S15P21B202-458) ────────────────────────────────────────────────
    #
    # 호스트가 5분마다 재 둔 것을 여기서 **직접 읽는다** — 모든 러너에 `/ec2-status`
    # 로 읽기 전용 마운트돼 있다. 중간 잡을 두면 그 잡이 대기하는 동안 카드가 통째로
    # 늦어져서(`allow_failure` 도 `timeout` 도 대기에는 안 듣는다) 그 구조를 버렸다.
    # **정상이면 아무 줄도 붙지 않는다** — 이 카드의 규칙 그대로다.
    #
    # 파일이 아예 없으면(잡이 안 돌았거나 배포 러너가 꺼져 있으면) `ec2_status` 가
    # 그것을 하나의 항목으로 만든다. **조용한 것과 못 읽은 것을 구별해야 한다.**
    # **러너는 GitLab 에 묻지 않는다** (S15P21B202-466). `-458` 에서 "프로세스는
    # 살았는데 GitLab 이 offline 으로 본다" 를 잡으려고 넣었는데, 그 조회에는
    # **Maintainer 권한이 필요하고 우리 `GITLAB_TOKEN` 은 Developer 라 403** 이다.
    # 카드 한 줄을 위해 CI 토큰 권한을 넓히는 것은 값이 맞지 않는다.
    #
    # 대신 남는 것: 호스트의 `systemctl is-active gitlab-runner`(아래 상태 파일)와
    # `-130` 의 "검사가 돌지 않은 MR". 빠지는 것은 좁은 한 경우뿐이고 그것도
    # `-130` 이 몇 시간 뒤에 잡는다.
    try:
        srv_data = ec2_status.load()
        srv = ec2_status.section(srv_data)
        if srv:
            blocks.append(srv)
        # **정상일 때도 숫자를 남긴다** (S15P21B202-486). 위 블록은 문제가 있을
        # 때만 붙어서, 조용한 화면이 "멀쩡하다" 인지 "재는 쪽이 죽었다" 인지
        # 구별되지 않았다. 꼬리는 🚀 와 같은 자리다 — 이것만으로 카드를 보내진 않는다.
        srv_tail = ec2_status.tail(srv_data)
        if srv_tail:
            tails.append(srv_tail)
    except Exception as e:  # noqa: BLE001 — 서버 판정 실패가 점검 전체를 막지 않는다
        blocks.append(f"**🖥 서버** — 판정 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")

    if not blocks:
        print("보낼 항목 없음"); return 0
    tail = "\n".join(t for t in tails if t)
    text = f"📋 **아침 점검 — 확인 필요** ({now_kst()})\n" + "\n\n".join(blocks) + \
           (f"\n\n{tail}" if tail else "") + \
           f"\n\n_각 항목의 @담당자가 처리 · 항목 설명: docs/ci-cd-pipeline.md §6 · 자동 생성(mm-digest)_"
    st, d = http("POST", url, body={"text": text, "username": "METAGAME 봇"})
    print(text)
    print(f"\nMM 전송 → {st}")
    return 0 if st == 200 else 1


if __name__ == "__main__":
    raise SystemExit(main())
