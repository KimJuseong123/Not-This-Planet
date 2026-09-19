"""scripts/ci 오프라인 테스트 전체 실행 (S15P21B202-103).

실행: python scripts/ci/tests/run_all.py
각 테스트는 ci_common.http 를 가짜로 바꾸므로 서로 간섭하지 않도록 **별도 프로세스**로 돌린다.
네트워크·토큰·러너가 필요 없어 로컬에서도 그대로 돌아간다.
"""
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def shell() -> str | None:
    """셸 테스트를 돌릴 인터프리터. Windows 러너에는 sh 가 PATH 에 없을 수 있어 git 옆에서도 찾는다."""
    for name in ("sh", "bash"):
        found = shutil.which(name)
        if found:
            return found
    git = shutil.which("git")          # …/Git/cmd/git.exe → …/Git/bin/bash.exe
    if git:
        cand = os.path.join(os.path.dirname(os.path.dirname(git)), "bin", "bash.exe")
        if os.path.exists(cand):
            return cand
    return None


def main() -> int:
    files = sorted(f for f in os.listdir(HERE) if f.startswith("test_") and f.endswith(".py"))
    shells = sorted(f for f in os.listdir(HERE) if f.startswith("test_") and f.endswith(".sh"))
    if not files:
        print("테스트 파일이 없습니다"); return 1
    env = dict(os.environ, PYTHONUTF8="1", PYTHONIOENCODING="utf-8")
    # 테스트는 절대 밖으로 나가지 않는다. 2026-09-01 사고 — ci-tests 잡에 실제 MM_WEBHOOK_URL 이
    # 있어서, 배포 알림을 붙인 테스트가 팀 채널로 진짜 카드 4장을 보냈다. 개별 테스트가 http 를
    # 막는지에 의존하지 않고, 스위트가 값을 지운다(테스트마다 실수할 여지를 없앤다).
    # `DEVBUILD_TRIGGER_TOKEN` 도 같은 이유다 (S15P21B202-582) — 이것이 남아 있으면
    # `--dry-run` 을 빠뜨린 검사 하나가 **진짜 배치 파이프라인**을 만든다.
    for leak in ("MM_WEBHOOK_URL", "GITLAB_TOKEN", "JIRA_API_TOKEN", "ANTHROPIC_API_KEY",
                 "DEVBUILD_TRIGGER_TOKEN"):
        env.pop(leak, None)
    # **파이프라인 맥락도 지운다** (S15P21B202-648). 비밀값과 같은 이유지만 고장 모양이 다르다.
    #
    # 이 테스트들은 전부 **로컬에서 통과하도록** 쓰여 있다. 로컬에는 `CI_*` 가 하나도 없다.
    # 그런데 CI 에서는 러너가 넣어 준 값을 그대로 물려받아서, **같은 코드가 어디서 도느냐에
    # 따라 다른 답**을 냈다. 2026-09-19 04:03 예약 실행(잡 584017)에서 그렇게 깨졌다:
    # `-608` 이 "예약 실행이면 Infra 를 멘션하지 않는다" 를 넣자, `CI_PIPELINE_SOURCE=schedule`
    # 을 물려받은 `test_deploy_notify` 의 단정들이 멘션을 못 찾았다.
    #
    # **MR·push 에서는 초록이고 예약에서만 빨강**이라 알아채기 어렵다 — 고치기 전까지
    # 새벽·아침 카드가 매일 같은 실패를 달고 나간다.
    #
    # 값이 필요한 테스트는 자기가 정한다(그 파일들은 이미 그렇게 하고 있다).
    # `GITLAB_USER_LOGIN` 도 같이 지운다 — `CI_` 로 시작하지 않지만 러너가 넣는 값이다.
    for ci_var in [k for k in env if k.startswith("CI_")]:
        env.pop(ci_var, None)
    env.pop("GITLAB_USER_LOGIN", None)
    sh = shell()
    cmds = [(f, [sys.executable, os.path.join(HERE, f)]) for f in files]
    if shells and sh:
        cmds += [(f, [sh, os.path.join(HERE, f)]) for f in shells]
    elif shells:                       # 셸이 없으면 조용히 빠지지 않고 눈에 보이게 남긴다
        for f in shells:
            print(f"  ⏭ {f} — sh·bash 를 찾지 못해 건너뜀")
    failed = []
    for f, cmd in cmds:
        r = subprocess.run(cmd, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", env=env)
        if r.returncode == 0:
            print(f"  ✅ {f}")
        else:
            failed.append(f)
            print(f"  ❌ {f} — 실패")
            for line in (r.stdout or "").rstrip().split("\n")[-6:]:
                print(f"       {line}")
            for line in (r.stderr or "").rstrip().split("\n")[-6:]:
                print(f"       {line}")
    print(f"\n== 오프라인 테스트: 통과 {len(cmds) - len(failed)} · 실패 {len(failed)} ==")
    if failed:
        print("   실패: " + ", ".join(failed))
        print("   ❌ CI 스크립트 회귀 — 고친 뒤 다시 push 하세요")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
