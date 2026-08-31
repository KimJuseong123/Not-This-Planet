"""mr-overlap 오프라인 테스트 — 실제 git 저장소(임시)에 브랜치를 만들어 비교하고, GitLab 은 가짜로 (S15P21B202-138).

실행: python scripts/ci/tests/test_mr_overlap.py
경우: 다른 MR 없음 · 나중 MR 이 텍스트 충돌(❌ + 양쪽 코멘트) · 먼저 MR 이 충돌(⚠만) ·
     씬 파일 겹침(⚠ risky) · 스택(대상 자동 전환) · 아침 점검 쌍 계산 ·
     물려받은 develop 변경 오귀속 회귀(-188: 상대 MR 이 옛 지점에서 갈라진 경우)
"""
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "2",
                   "MR_OVERLAP_AI": "0"})   # AI 는 여기서 안 부른다

import ci_common  # noqa: E402
import mr_overlap as M  # noqa: E402


def sh(*args, cwd):
    r = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    assert r.returncode == 0, f"{args} → {r.stderr}"
    return r.stdout.strip()


def write(cwd, path, text):
    full = os.path.join(cwd, path)
    os.makedirs(os.path.dirname(full) or cwd, exist_ok=True)
    with open(full, "w", encoding="utf-8") as f:
        f.write(text)


def build_repo(root):
    """bare 원격 + 로컬. develop 에서 A·B·C 가 갈라진다: A·B 는 f.cs 를 다르게 바꿈(충돌), C 는 무관, S 는 A 위에 얹힘."""
    remote, work = os.path.join(root, "remote.git"), os.path.join(root, "work")
    sh("git", "init", "-q", "--bare", remote, cwd=root)
    sh("git", "init", "-q", work, cwd=root)
    g = lambda *a: sh("git", *a, cwd=work)
    g("config", "user.email", "t@e.st"); g("config", "user.name", "t")
    g("remote", "add", "origin", remote)
    write(work, "f.cs", "int x = 1;\n"); write(work, "Main.unity", "scene: v1\n"); write(work, "other.txt", "o\n")
    g("add", "-A"); g("commit", "-qm", "base"); g("branch", "-M", "develop"); g("push", "-q", "-u", "origin", "develop")
    def branch(name, edits, base="develop"):
        g("switch", "-q", "-c", name, base)
        for p, t in edits.items():
            write(work, p, t)
        g("add", "-A"); g("commit", "-qm", name); g("push", "-q", "-u", "origin", name); g("switch", "-q", "develop")
    branch("S15P21B202-1-a", {"f.cs": "int x = 2; // A\n"})
    branch("S15P21B202-2-b", {"f.cs": "int x = 3; // B\n"})
    branch("S15P21B202-3-c", {"other.txt": "c\n"})
    branch("S15P21B202-4-d", {"Main.unity": "scene: v1\nadded_by_d: 1\n"})
    branch("S15P21B202-5-e", {"Main.unity": "scene: v1\nadded_by_e: 1\n"})   # 같은 씬, 다른 줄 추가 → 텍스트 충돌 (같은 위치)
    branch("S15P21B202-6-s", {"s.txt": "s\n"}, base="S15P21B202-1-a")        # A 위에 얹힘
    branch("S15P21B202-7-s2", {"other.txt": "s2\n"}, base="S15P21B202-1-a")  # A 위에 얹혔지만 C 와 other.txt 충돌
    branch("S15P21B202-8-s3", {"s3.txt": "s3\n"}, base="S15P21B202-6-s")     # S(중간) 위에 다시 얹힘 — 가장 가까운 부모 검증용

    # 오귀속 회귀용(-188): OLD 는 옛 develop 에서 갈라져 inherit.txt 를 고쳤고, 그 뒤 develop 이 같은
    # 파일을 다르게 고쳤다. 쌍별 공통 조상을 기준으로 재면 그 develop 변경이 나중 MR 의 변경으로 잡혀 오탐이 난다.
    def develop_commit(path, text, msg):
        g("switch", "-q", "develop")
        write(work, path, text)
        g("add", "-A"); g("commit", "-qm", msg); g("push", "-q", "origin", "develop")

    develop_commit("inherit.txt", "v1\n", "inherit v1")
    branch("S15P21B202-20-old", {"inherit.txt": "old-mr\n"})                 # 옛 지점에서 갈라짐
    develop_commit("inherit.txt", "v2-from-develop\n", "inherit v2 (develop)")
    branch("S15P21B202-21-newbase", {"newbase.txt": "n\n"})                  # 새 develop 기준, inherit.txt 안 건드림
    branch("S15P21B202-22-real", {"inherit.txt": "real-conflict\n"})         # 새 develop 기준, 같은 파일을 고침
    return work


def mr(iid, branch, created, target="develop"):
    return {"iid": iid, "title": f"MR {iid}", "source_branch": branch, "target_branch": target,
            "created_at": created, "web_url": f"https://lab/mr/{iid}", "author": {"username": "u"}}


class Fake:
    """GitLab API 흉내 — 열린 MR 목록·자기 MR·노트·대상 변경을 기록한다."""
    def __init__(self, me, others):
        self.me, self.others, self.puts, self.notes = me, others, [], {}

    def http(self, method, url, headers=None, body=None, timeout=30):
        if "merge_requests?state=opened" in url:
            return 200, [self.me] + self.others
        if url.endswith(f"/merge_requests/{self.me['iid']}") and method == "GET":
            return 200, self.me
        if url.endswith(f"/merge_requests/{self.me['iid']}") and method == "PUT":
            self.puts.append(body); return 200, {**self.me, **body}
        if "/notes" in url:
            iid = int(url.split("/merge_requests/")[1].split("/")[0])
            if method == "GET":
                return 200, [{"id": 1, "body": b} for b in self.notes.get(iid, [])]
            self.notes.setdefault(iid, []).append(body["body"]); return 201, {}
        return 404, {}


def run(work, me, others, head_branch):
    os.chdir(work)
    sh("git", "switch", "-q", head_branch, cwd=work)
    os.environ["CI_MERGE_REQUEST_IID"] = str(me["iid"])
    os.environ["CI_COMMIT_SHA"] = sh("git", "rev-parse", "HEAD", cwd=work)
    fake = Fake(me, others)
    ci_common.http = fake.http
    M.http = fake.http
    rc = M.main()
    return rc, fake


def main() -> int:
    with tempfile.TemporaryDirectory() as root:
        try:
            return _main(root)
        finally:
            os.chdir(HERE)   # 임시 폴더 안에 cwd 를 둔 채로는 Windows 가 폴더를 지우지 못한다


def _main(root: str) -> int:
    if True:
        work = build_repo(root)
        A = mr(1, "S15P21B202-1-a", "2026-08-28T10:00:00Z")
        B = mr(2, "S15P21B202-2-b", "2026-08-28T11:00:00Z")
        C = mr(3, "S15P21B202-3-c", "2026-08-28T12:00:00Z")
        D = mr(4, "S15P21B202-4-d", "2026-08-28T13:00:00Z")
        E = mr(5, "S15P21B202-5-e", "2026-08-28T14:00:00Z")
        S = mr(6, "S15P21B202-6-s", "2026-08-28T15:00:00Z")

        # 1) 다른 MR 없음
        rc, f = run(work, C, [], "S15P21B202-3-c")
        assert rc == 0 and any("겹치는 파일이 없습니다" in n for n in f.notes[3]), f.notes
        print("  다른 MR 없음 → 통과 + 코멘트 1개")

        # 2) B(나중) vs A(먼저) 텍스트 충돌 → B 는 ❌, 양쪽 코멘트, 대상 전환 없음
        rc, f = run(work, B, [A, C], "S15P21B202-2-b")
        assert rc == 1, rc
        mine = f.notes[2][-1]
        assert "텍스트 충돌" in mine and "`f.cs`" in mine and "!1 → !2" in mine and "git rebase origin/S15P21B202-1-a" in mine, mine
        assert "!3" not in mine, "무관한 C 가 끼어 있다"
        theirs = f.notes[1][-1]
        assert "mr-overlap:metagame:!2" in theirs and "이 MR 을 먼저 머지" in theirs, theirs
        assert not f.puts, "대상을 바꾸면 안 된다"
        print("  나중 MR 이 충돌 → ❌ + 리베이스 명령 + 상대 MR 에도 코멘트")

        # 3) A(먼저) 관점 → 경고만, 통과
        rc, f = run(work, A, [B, C], "S15P21B202-1-a")
        assert rc == 0, rc
        assert "먼저 열렸으니 그대로 진행" in f.notes[1][-1], f.notes[1][-1]
        print("  먼저 MR 관점 → ⚠ 정보만, 통과")

        # 4) 씬 파일 같은 줄 → 텍스트 충돌이므로 conflict 로 잡히고 merge 안내
        rc, f = run(work, E, [D], "S15P21B202-5-e")
        assert rc == 1 and "git merge origin/S15P21B202-4-d" in f.notes[5][-1], f.notes[5][-1]
        print("  씬 파일 충돌 → merge 로 풀도록 안내")

        # 5) 스택: S 는 A 위에 얹혀 있음 → 대상을 A 로 자동 전환, A 와는 충돌 판정 안 함
        rc, f = run(work, S, [A, B], "S15P21B202-6-s")
        assert rc == 0, rc
        assert f.puts and f.puts[0].get("target_branch") == "S15P21B202-1-a", f.puts
        assert "위에 얹혀 있습니다" in f.notes[6][-1], f.notes[6][-1]
        print("  A 위에 얹힌 S → 대상 A 로 자동 전환")

        # 6) 자동 전환 끄기
        os.environ["MR_OVERLAP_AUTORETARGET"] = "0"
        rc, f = run(work, S, [A], "S15P21B202-6-s")
        assert rc == 0 and not f.puts, f.puts
        del os.environ["MR_OVERLAP_AUTORETARGET"]
        print("  MR_OVERLAP_AUTORETARGET=0 → 전환하지 않고 안내만")

        # 7) 아침 점검: 전체 쌍 — A–B 충돌, D–E 충돌, S 는 A 위(제외), C 무관
        fake = Fake(C, [A, B, D, E, S])
        ci_common.http = fake.http; M.http = fake.http
        pairs = M.conflict_pairs(ci_common.GitLab())
        keys = sorted((p["first"]["iid"], p["second"]["iid"]) for p in pairs)
        assert keys == [(1, 2), (4, 5)], keys
        assert all(p["first"]["created_at"] <= p["second"]["created_at"] for p in pairs)
        print("  아침 점검 쌍 계산: (A,B) (D,E) 두 쌍, 먼저 열린 순")

        # 9) 이미 A 를 향하는 스택 MR 도 develop 을 향한 다른 MR 과의 충돌은 본다 — S2 는 자기 변경(other.txt)이 C 와 충돌
        S2 = mr(7, "S15P21B202-7-s2", "2026-08-28T16:00:00Z", target="S15P21B202-1-a")
        rc, f = run(work, S2, [A, B, C], "S15P21B202-7-s2")
        assert rc == 1, rc
        assert not f.puts, "이미 A 를 향하므로 대상을 또 바꾸면 안 된다"
        mine = f.notes[7][-1]
        assert "!3" in mine and "`other.txt`" in mine, mine
        assert "!2" not in mine, "A 에서 물려받은 f.cs 충돌을 S2 에게 물으면 안 된다"
        print("  A 를 향하는 스택 MR → develop 을 향한 C 와의 자기 충돌만 ❌ (물려받은 B 충돌은 제외)")

        # 10) 스택 중간 MR 이 있으면 **가장 가까운 부모**로 재대상한다
        #     (2026-08-28 MR !87·!88 실측: 중간 MR 이 후보에서 빠져 스택 바닥으로 재대상됐다)
        A2 = mr(11, "S15P21B202-1-a", "2026-08-28T10:00:00Z")                       # 바닥, develop 대상
        S1 = mr(12, "S15P21B202-6-s", "2026-08-28T15:00:00Z", target="S15P21B202-1-a")   # 중간, 작업 브랜치 대상
        NEW = mr(13, "S15P21B202-8-s3", "2026-08-28T16:00:00Z")                     # S1 위에 얹힌 새 MR, develop 대상
        rc, f = run(work, NEW, [A2, S1], "S15P21B202-8-s3")
        assert f.puts, "재대상이 일어나지 않았다"
        got = f.puts[0].get("target_branch")
        assert got == "S15P21B202-6-s", f"가장 가까운 부모가 아니라 {got} 로 재대상됐다"
        print("  스택 중간 MR 이 있어도 가장 가까운 부모로 재대상")

        # 11) 오귀속 회귀(-188): 상대 MR 이 옛 지점에서 갈라지고 그 사이 develop 이 같은 파일을 고친 경우 —
        #     물려받은 develop 변경을 내 변경으로 세면 애먼 MR 이 충돌로 지목된다(!97·!112·!75·!78 실측)
        OLD = mr(20, "S15P21B202-20-old", "2026-08-28T09:00:00Z")
        NB = mr(21, "S15P21B202-21-newbase", "2026-08-28T17:00:00Z")
        os.chdir(work)
        h_nb = sh("git", "rev-parse", "origin/S15P21B202-21-newbase", cwd=work)
        h_old = sh("git", "rev-parse", "origin/S15P21B202-20-old", cwd=work)
        pair_base = M.merge_base(h_nb, h_old)
        assert "inherit.txt" in M.changed_files(pair_base, h_nb), \
            "회귀 조건이 재현되지 않았다(쌍별 기준에서는 물려받은 변경이 보여야 한다)"
        assert "inherit.txt" not in M.changed_files(M.own_base(h_nb, "develop"), h_nb), \
            "대상 브랜치 기준에서는 내 변경이 아니어야 한다"
        rc, f = run(work, NB, [OLD], "S15P21B202-21-newbase")
        assert rc == 0, f"물려받은 변경으로 오탐: {f.notes.get(21)}"
        print("  옛 지점 상대 MR → 물려받은 develop 변경을 내 것으로 오귀속하지 않음")

        # 12) 같은 조건에서 진짜 충돌(내가 그 파일을 직접 고침)은 그대로 잡는다
        REAL = mr(22, "S15P21B202-22-real", "2026-08-28T18:00:00Z")
        rc, f = run(work, REAL, [OLD], "S15P21B202-22-real")
        assert rc == 1, rc
        assert "inherit.txt" in f.notes[22][-1], f.notes.get(22)
        print("  같은 조건에서 진짜 충돌은 실패 판정 유지")

        # 8) merge-tree 파서 직접
        assert M.merge_conflicts("origin/S15P21B202-1-a", "origin/S15P21B202-3-c") == []
        assert M.merge_conflicts("origin/S15P21B202-1-a", "origin/S15P21B202-2-b") == ["f.cs"]
        print("  merge_conflicts: 깨끗=[] · 충돌=[f.cs]")

    os.chdir(HERE)
    print("TEST_MR_OVERLAP OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
