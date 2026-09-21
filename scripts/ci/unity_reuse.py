"""검사 대상이 지난 실행과 같으면 Unity 를 다시 돌리지 않는다 (S15P21B202-602).

실행: python3 scripts/ci/unity_reuse.py
`unity-compile` 잡이 Unity 를 부르기 **전에** 한 번 부른다.

**왜 필요한가.** GitLab 의 MR 파이프라인에서 `rules:changes` 는 *지난 push 와의 차이* 가
아니라 **대상 브랜치(develop)와의 차이** 를 본다. 그래서 브랜치에 Unity 변경이 한 번이라도
쌓이면, 그 뒤 push 가 문서만 고쳐도 Unity 전체가 다시 돈다. 2026-09-17 실측: 이어진
재실행 15건 중 7건이 Unity 무변경이었고, 그중 `565396` 이 7.8분, `565473`(같은 커밋
재시도)이 4.4분을 태웠다. `resource_group: unity` 로 직렬화돼 있어 그 시간이 **다른
사람의 MR 대기 시간**이 된다.

`rules:changes` 로는 못 고친다 — `compare_to` 는 이름 붙은 ref 만 받는데 "이 MR 에서
지난번에 돈 커밋" 은 매번 달라지는 값이라 YAML 에 쓸 수 없다. 그래서 여기서 판단한다.

**무엇을 같다고 보나.** 이 잡이 검사하는 것은 브랜치가 아니라 **브랜치 + develop 을 합친
결과** 다(`-342`). 그래서 조건이 둘이다.

  ① 지난 실행 이후 **이 브랜치**의 Unity 입력이 안 바뀌었다
  ② 지난 실행이 합쳤던 develop 과 지금 합칠 develop 사이에도 Unity 입력이 안 바뀌었다

둘 다 참이면 검사 대상이 **바이트 단위로 같으므로** 결과도 같다. 추정이 아니다.

**모르면 다시 돌린다.** 토큰이 없든, 지난 잡을 못 찾든, 아티팩트가 만료됐든, git 이
대답을 안 하든 — 판단하지 못한 경우는 전부 "다시 돌린다" 로 떨어진다. 잘못 건너뛰면
**검사하지 않은 코드가 초록으로 지나가고**, 그건 이 저장소가 계속 피해 온 모양이다.
"""
import hashlib
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import GitLab, banner, env  # noqa: E402

JOB_NAME = "unity-compile"

#: 이 중 하나라도 바뀌면 결과가 달라질 수 있다.
#:
#: `METAGAME/` 셋은 Unity 가 읽는 것이고(`.gitlab-ci.yml` 의 `changes:` 게이트와 같은
#: 범위다 — `-420`), 나머지 둘은 **검사하는 쪽**이다: 잡 정의가 바뀌면 무엇을 어떻게
#: 돌리는지가 달라지고, 판정 스크립트가 바뀌면 같은 결과에서 다른 판정이 나온다.
INPUTS = (
    "METAGAME/Assets",
    "METAGAME/Packages",
    "METAGAME/ProjectSettings",
    ".gitlab-ci.yml",
    "scripts/ci/unity_report.py",
)

#: 재사용할 때 지난 잡에서 받아 오는 것. **글롭을 쓰지 않는다** — 아티팩트 API 는 경로
#: 하나씩만 받으므로, 이름을 여기 적어 두고 없으면 없는 대로 넘어간다.
COPY = ("unity-results.xml", "unity-junit.xml", "unity.log", "unity-rc.txt", "unity-base.txt")

#: 합친 develop 의 sha 를 적어 두는 파일. 지난 실행이 **무엇과 합쳐서** 검사했는지
#: 알아야 ②를 판단할 수 있다. 이 파일이 없는 잡(이 이슈 이전 것)은 재사용하지 않는다.
BASE_FILE = "unity-base.txt"

#: 재사용했다는 표시. 잡 스크립트가 이 파일로 Unity 를 건너뛸지 정하고,
#: `unity_report.py` 가 코멘트에 한 줄을 싣는다.
MARK_FILE = "unity-reused.txt"

#: **합칠 대상이 없었다**는 표시 (S15P21B202-641). develop·master push 가 그 경우다.
#:
#: 빈 문자열로 두면 안 된다. 그러면 "합칠 것이 없었다"(정상)와 "이 기능 이전에 돈
#: 잡이라 파일이 없다"(모른다)가 **구별되지 않는다.** 실제로 그 둘이 뭉쳐 있어서
#: **develop 에서는 재사용이 한 번도 안 됐다** — 잡 `578864` 로그가 그것이다:
#:
#:     앞선 실행: 잡 576971 (success, …) · 합친 base 없음
#:     앞선 잡에 unity-base.txt 이 없다(이 이슈 이전 실행) — 다시 돌린다
#:
#: 메시지는 "이 이슈 이전" 이라고 말하지만 사실이 아니었다. develop push 는 **매번**
#: 이 길로 떨어졌다. `-641` 이 develop 의 `changes:` 를 없애려면 그 비용을 재사용이
#: 받아 줘야 하므로, 먼저 이것을 가른다.
NO_MERGE = "-"


#: 트리 지문을 적어 두는 파일. **ref 와 무관하게** 같은 대상을 찾으려면 이것이 있어야 한다.
KEY_FILE = "unity-key.txt"

#: 무엇을 돌렸는지. MR 은 `!Heavy` 로 빼고 그 밖은 전부 돈다(`-602`) — **둘은 서로를
#: 대신하지 못한다.** 적은 쪽 결과를 많은 쪽 자리에 쓰면 검사하지 않은 것이 초록이 된다.
CAT_FULL = "full"
CAT_NO_HEAVY = "no-heavy"


def category() -> str:
    """이 실행이 무엇을 돌리는가. `.gitlab-ci.yml` 의 `-testCategory` 분기와 같은 조건."""
    return CAT_NO_HEAVY if env("CI_PIPELINE_SOURCE") == "merge_request_event" else CAT_FULL


def tree_key(paths=INPUTS) -> str | None:
    """지금 체크아웃된 **검사 대상 트리**의 지문. 판단 못 하면 None (S15P21B202-642).

    **비용이 없다.** git 은 디렉터리마다 트리 OID 를, 파일마다 블롭 OID 를 이미 갖고
    있다 — `git rev-parse HEAD:<경로>` 는 계산이 아니라 조회다.

    **머지 뒤에 불러야 한다.** MR 잡은 `git merge --no-edit origin/<대상>` 으로 **머지
    커밋을 만들고** 검사하므로, 그 뒤의 `HEAD` 가 곧 검사 대상이다. 머지 전에 부르면
    소스 브랜치의 트리를 가리켜 **다른 것을 같다고 말하게 된다.**
    """
    parts = []
    for p in paths:
        try:
            # **인코딩을 준다.** 안 주면 Windows 기본(cp949)으로 읽다가 한글이 섞인 git
            # 오류에서 디코딩이 깨지고, `stderr` 가 `None` 이 되어 아래에서 터진다.
            r = subprocess.run(["git", "rev-parse", f"HEAD:{p}"], capture_output=True,
                               text=True, encoding="utf-8", errors="replace", timeout=30)
        except Exception as e:  # noqa: BLE001
            print(f"[warn] 트리 지문 조회 실패({type(e).__name__}: {e})")
            return None
        if r.returncode != 0:
            print(f"[warn] 트리 지문 조회 실패: {p} — {(r.stderr or '').strip()[:120]}")
            return None
        parts.append(f"{p}={r.stdout.strip()}")
    return hashlib.sha256("\n".join(parts).encode("utf-8")).hexdigest()[:16]


def git_changed(a: str, b: str, paths=INPUTS):
    """`a`..`b` 사이에 `paths` 아래가 바뀌었나. **판단 못 하면 None.**

    `None` 과 `False` 를 반드시 구분한다 — "안 바뀌었다" 와 "물어보지 못했다" 를 뭉치면
    git 이 한 번 삐끗할 때 검사를 통째로 건너뛴다.
    """
    if not (a and b):
        return None
    if a == b:
        return False
    try:
        r = subprocess.run(["git", "diff", "--name-only", a, b, "--", *paths],
                           capture_output=True, text=True, timeout=60)
    except Exception as e:  # noqa: BLE001
        print(f"[warn] git diff 실패({type(e).__name__}: {e}) — 다시 돌린다")
        return None
    if r.returncode != 0:
        print(f"[warn] git diff 실패(exit {r.returncode}) {r.stderr.strip()[:160]} — 다시 돌린다")
        return None
    return bool(r.stdout.strip())


def job_ref() -> str:
    """이 잡의 `ref`. MR 파이프라인은 브랜치 이름이 아니라 MR ref 로 돈다."""
    return env("CI_MERGE_REQUEST_REF_PATH") or env("CI_COMMIT_REF_NAME") or ""


def previous(gl: GitLab, ref: str, before_id) -> dict | None:
    """같은 ref 에서 **나보다 먼저** 끝난 가장 최근 `unity-compile`. 없으면 None.

    `success`·`failed` 만 본다 — 취소되거나 도는 중인 잡의 아티팩트는 반쪽이다.
    """
    if not (gl.token and gl.project and ref):
        return None
    try:
        jobs = gl.project_jobs()
    except Exception as e:  # noqa: BLE001
        print(f"[warn] 잡 목록 조회 실패({type(e).__name__}: {e}) — 다시 돌린다")
        return None
    try:
        mine = int(before_id)
    except (TypeError, ValueError):
        mine = None
    best = None
    for j in jobs:
        if j.get("name") != JOB_NAME or j.get("ref") != ref:
            continue
        if j.get("status") not in ("success", "failed"):
            continue
        jid = j.get("id") or 0
        if mine is not None and jid >= mine:
            continue                      # 나 자신과 나보다 뒤에 만들어진 잡은 기준이 아니다
        if best is None or jid > (best.get("id") or 0):
            best = j
    return best


def decide(branch_moved, base_moved) -> tuple[bool, str]:
    """두 판정을 받아 재사용 여부를 정한다. **순수 함수** — 검사가 여기를 본다.

    `None`(판단 못 함)은 `True`(바뀌었다)와 같이 취급하되 **이유를 다르게 적는다** —
    로그를 읽는 사람이 "바빠서 돌았다" 와 "몰라서 돌았다" 를 구분할 수 있어야 한다.
    """
    if branch_moved is None:
        return False, "이 브랜치가 바뀌었는지 판단하지 못했다"
    if base_moved is None:
        return False, "합칠 develop 이 바뀌었는지 판단하지 못했다"
    if branch_moved:
        return False, "이 브랜치의 Unity 입력이 바뀌었다"
    if base_moved:
        return False, "합칠 develop 의 Unity 입력이 바뀌었다"
    return True, "브랜치와 develop 모두 Unity 입력이 그대로다"


def fetch(gl: GitLab, job_id, out_dir: str) -> list[str]:
    """지난 잡의 산출물을 내려받는다. 받은 파일 이름을 준다.

    **플래키 재실행 결과(`unity-recheck-*.xml`)는 받지 않는다** (`-347`). 뒤따르는
    재실행 계획 단계가 그 파일들을 먼저 지우므로 받아 봐야 사라지고, 순서를 뒤집으면
    "이번에 재실행하지 않았는데 옛 통과 기록으로 면제된다" 는 구멍이 생긴다(`!272`
    AI 리뷰가 막아 둔 것이다). 재사용한 실행에 새 실패가 있으면 재실행이 그대로 한 번
    더 돈다 — 느리지만 **맞다**. 그 경우는 드물다(무변경 push 인데 새 실패가 있을 때).
    """
    got = []
    for name in COPY:
        text = gl.job_artifact_text(job_id, name)
        if text is None:
            continue
        with open(os.path.join(out_dir, name), "w", encoding="utf-8", newline="") as f:
            f.write(text)
        got.append(name)
    return got


#: 트리 지문을 확인해 볼 지난 잡의 수. 잡마다 아티팩트를 한 번 읽으므로 무한정 보지
#: 않는다 — `unity_report.MAX_BASELINE_CANDIDATES` 와 같은 결이다.
MAX_KEY_LOOKUPS = 12


def find_same_tree(gl: GitLab, want_key: str, want_cat: str, before_id=None) -> dict | None:
    """**ref 를 보지 않고** 같은 트리를 같은 범위로 검사한 잡을 찾는다 (S15P21B202-642).

    `previous()` 와 다른 점은 그 하나다. `previous()` 는 "이 브랜치가 그 뒤로 움직였나"
    를 묻고, 여기서는 **검사 대상 자체가 같은가**를 묻는다 — 같은 트리면 어느 ref 에서
    돌았든 결과가 같다.

    `want_cat` 를 반드시 맞춘다. MR 은 `!Heavy` 로 빼고 돌므로 그 결과를 전체 실행
    자리에 쓰면 **Heavy 가 검사되지 않은 채 초록**이 된다.
    """
    if not (gl.token and gl.project):
        print("토큰이 없어 지난 잡을 볼 수 없다 — 그대로 돌린다")
        return None
    try:
        jobs = gl.project_jobs()
    except Exception as e:  # noqa: BLE001
        print(f"[warn] 잡 목록 조회 실패({type(e).__name__}: {e}) — 그대로 돌린다")
        return None
    try:
        mine = int(before_id) if before_id else None
    except (TypeError, ValueError):
        mine = None
    cands = [j for j in jobs
             if j.get("name") == JOB_NAME and j.get("status") in ("success", "failed")]
    cands.sort(key=lambda j: j.get("id") or 0, reverse=True)
    looked = 0
    for j in cands:
        jid = j.get("id") or 0
        if mine is not None and jid >= mine:
            continue
        if looked >= MAX_KEY_LOOKUPS:
            break
        looked += 1
        line = (gl.job_artifact_text(jid, KEY_FILE) or "").strip()
        parts = line.split()
        if len(parts) < 2:
            continue                      # 이 이슈 이전 잡에는 이 파일이 없다
        if parts[0] == want_key and parts[1] == want_cat:
            return j
    print(f"같은 트리({want_key} · {want_cat})를 검사한 잡을 최근 {looked}개에서 "
          f"찾지 못했다 — 그대로 돌린다")
    return None


def mode_key(out_dir: str) -> int:
    """이 실행이 무엇을 검사하는지 적어 둔다. **머지 뒤에** 부른다."""
    key = tree_key()
    cat = category()
    if not key:
        print("트리 지문을 읽지 못했다 — 이 실행은 재사용 대상이 되지 않는다")
        return 0                          # 막지 않는다. 다음 잡이 그냥 다시 돌 뿐이다
    with open(os.path.join(out_dir, KEY_FILE), "w", encoding="utf-8", newline="\n") as f:
        f.write(f"{key} {cat}\n")
    print(f"검사 대상 지문 {key} · 범위 {cat}")
    return 0


def mode_borrow(out_dir: str) -> int:
    """같은 트리를 이미 전체로 검사했으면 그 결과를 빌려 온다 (S15P21B202-642).

    `release-windows` 가 빌드 전에 EditMode 를 스스로 돌리는 자리에서 부른다. 그 실행은
    2026-09-18 실측 **20분 20초**로 릴리스 시간의 82% 였고, 결과는 같은 트리를 검사한
    develop 의 `unity-compile` 과 **실패 수까지 같았다**(26 = 26).

    **약해지지 않는다.** 핫픽스로 master 가 갈라져 트리가 달라지면 지문이 달라져 그대로
    돈다 — 그때가 이 검사가 진짜 필요한 때다.
    """
    key = tree_key()
    if not key:
        print("트리 지문을 읽지 못했다 — 그대로 돌린다")
        return 0
    print(f"검사 대상 지문 {key} · 범위 {CAT_FULL}")
    gl = GitLab()
    j = find_same_tree(gl, key, CAT_FULL, env("CI_JOB_ID"))
    if not j:
        return 0
    got = fetch(gl, j["id"], out_dir)
    need = [n for n in ("unity-results.xml", "unity-rc.txt") if n not in got]
    if need:
        print(f"잡 {j['id']} 에서 {' '.join(need)} 를 받지 못했다(아티팩트 만료?) — 그대로 돌린다")
        return 0
    # 릴리스 잡은 로그 이름이 다르다. 없어도 게이트는 XML 로 판정하지만, 있으면 맞춰 둔다.
    src = os.path.join(out_dir, "unity.log")
    if os.path.exists(src):
        with open(src, encoding="utf-8", errors="replace") as f:
            body = f.read()
        with open(os.path.join(out_dir, "unity-tests.log"), "w",
                  encoding="utf-8", newline="\n") as f:
            f.write(body)
    with open(os.path.join(out_dir, MARK_FILE), "w", encoding="utf-8") as f:
        f.write(str(j["id"]))
    print(f"✅ 같은 트리를 검사한 잡 {j['id']}({j.get('status')}, ref {j.get('ref')})의 "
          f"결과를 그대로 쓴다 — {len(got)}개 받음: {' '.join(got)}")
    print("   EditMode 를 다시 돌리지 않는다. 트리가 바이트 단위로 같으므로 결과도 같다.")
    return 0


def main() -> int:
    banner("unity-reuse")
    out_dir = env("CI_PROJECT_DIR", os.getcwd())
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode == "key":
        return mode_key(out_dir)
    if mode == "borrow":
        return mode_borrow(out_dir)

    # 합친 결과를 검사하지 못했으면 **무엇을 검사할지가 이미 평소와 다르다.** 그 상태를
    # 재사용 판단에 끌어들이지 않는다.
    base = (env("UNITY_MERGED_BASE") or "").strip()
    if env("CI_MERGE_REQUEST_TARGET_BRANCH_NAME") and not base:
        print("합친 base 를 모른다(대상 브랜치를 못 합쳤다) — 다시 돌린다")
        return 0
    # 여기까지 왔는데 비어 있으면 **합칠 대상이 없는 것**이다(develop·master push).
    # 위 분기가 "MR 인데 못 합쳤다" 를 이미 걸렀으므로 남는 경우는 그것뿐이다.
    if not base:
        base = NO_MERGE

    gl = GitLab()
    ref = job_ref()
    prev = previous(gl, ref, env("CI_JOB_ID"))
    if not prev:
        print(f"이 ref({ref or '모름'})에서 앞선 {JOB_NAME} 을 찾지 못했다 — 다시 돌린다")
        return 0

    prev_sha = ((prev.get("commit") or {}).get("id") or "")
    prev_base = (gl.job_artifact_text(prev["id"], BASE_FILE) or "").strip()
    when = (prev.get("created_at") or "")[:16]
    print(f"앞선 실행: 잡 {prev['id']} ({prev.get('status')}, {when}, {prev_sha[:8] or 'sha미상'}) "
          f"· 합친 base {prev_base[:8] or '없음'}")

    if not prev_base:
        # **여기 오는 경우가 하나뿐이어야 한다** (S15P21B202-641). 예전에는 둘이었다 —
        # "이 기능 이전에 돈 잡" 과 "합칠 대상이 없던 잡(develop push)". 뒤의 것이
        # 매번 여기로 떨어져 **develop 에서는 재사용이 한 번도 안 됐다.**
        # 이제 합칠 대상이 없으면 `NO_MERGE` 를 적으므로, 빈 값은 옛 잡뿐이다.
        print(f"앞선 잡에 {BASE_FILE} 이 비어 있다(이 이슈 이전 실행) — 다시 돌린다")
        return 0

    branch_moved = git_changed(prev_sha, env("CI_COMMIT_SHA"))
    base_moved = git_changed(prev_base, base)
    reuse, why = decide(branch_moved, base_moved)
    print(f"브랜치 변경 {branch_moved} · base 변경 {base_moved} → {why}")

    if not reuse:
        return 0

    got = fetch(gl, prev["id"], out_dir)
    # **필요한 것이 다 오지 않으면 재사용할 것이 없다.** 아티팩트는 14일이면 만료되고,
    # 만료된 잡에서는 위 `job_artifact_text` 가 전부 None 을 준다.
    #
    # `unity-rc.txt` 를 함께 요구하는 이유: 뒤따르는 재실행 계획 단계가 그 파일을
    # `cat` 한다. 없으면 `set -e` 아래에서 **판정에 닿기도 전에 잡이 죽는다** —
    # 최적화 때문에 검사가 실패하는 것은 가장 나쁜 모양이다.
    need = [n for n in ("unity-results.xml", "unity-rc.txt") if n not in got]
    if need:
        print(f"앞선 잡에서 {' '.join(need)} 를 받지 못했다(아티팩트 만료?) — 다시 돌린다")
        return 0

    with open(os.path.join(out_dir, MARK_FILE), "w", encoding="utf-8") as f:
        f.write(str(prev["id"]))
    print(f"✅ 지난 결과를 그대로 쓴다 — 잡 {prev['id']} 에서 {len(got)}개 받음: {' '.join(got)}")
    print("   검사 대상이 바이트 단위로 같으므로 Unity 를 다시 돌리지 않는다.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
