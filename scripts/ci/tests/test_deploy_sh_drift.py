"""아침 점검이 **낡은 호스트 배포 스크립트**를 부르는지 (S15P21B202-485).

실행: python scripts/ci/tests/test_deploy_sh_drift.py
네트워크·마운트 없이 돈다 — 상태 dict 를 글자로 넣어 본다.

2026-09-14 발견: `/opt/metagame-deploy/deploy.sh` 가 **7일 동안 낡아 있었다.**
`-284` 가 넣은 dev 지원(`HEALTH_PORT`)이 호스트에 없어서 dev 배포가 **prod 포트로
헬스체크**했다 — dev 가 기동에 실패해도 `배포 완료` 로 끝나고 롤백도 안 한다.
같은 디렉터리의 다른 스크립트는 갱신돼 있었는데 이것만 빠졌고, **아무것도 빨갛지
않았다.**

**여기서 조용히 틀릴 수 있는 것은 "못 쟀다" 를 "같다" 로 읽는 것이다.** 호스트의
`ec2-status.sh` 가 옛 판이면 해시를 아예 안 보내는데, 그때 조용하면 낡은 상태가
영원히 안 보인다 — 이 이슈가 막으려는 바로 그 모양이다.
"""
import hashlib
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.dirname(HERE))

import ec2_status as E  # noqa: E402


def repo_hash() -> str:
    with open(os.path.join(ROOT, E.REPO_DEPLOY_SH), "rb") as f:
        return hashlib.sha256(f.read().replace(b"\r", b"")).hexdigest()


def main() -> int:
    os.chdir(ROOT)          # 저장소 상대 경로로 읽는다
    want = repo_hash()

    # ── ① 같으면 조용하다 ───────────────────────────────────────────────
    assert E.deploy_sh_drift({"deploy_sh_sha256": want}) is None
    print("  같으면 — 아무 말도 안 한다")

    # ── ② 다르면 **양쪽 해시와 무엇이 나쁜지**를 말한다 ─────────────────
    line = E.deploy_sh_drift({"deploy_sh_sha256": "0" * 64})
    assert line and "저장소본과 다르다" in line, line
    assert "00000000" in line and want[:8] in line, f"양쪽 해시가 안 적혔다: {line}"
    # 왜 나쁜지를 함께 말한다 — "다르다" 만으로는 사람이 우선순위를 못 정한다
    assert "성공한 척" in line, f"위험이 안 적혔다: {line}"
    assert "/opt/metagame-deploy/deploy.sh" in line, f"고칠 자리가 안 적혔다: {line}"
    print("  다르면 — 양쪽 해시 · 왜 나쁜지 · 고칠 자리")

    # ── ③ **못 쟀으면 조용하지 않다** ───────────────────────────────────
    #    호스트 스크립트가 옛 판이면 이 값을 아예 안 보낸다.
    for data, why in (({}, "키가 없음"),
                      ({"deploy_sh_sha256": None}, "None"),
                      ({"deploy_sh_sha256": ""}, "빈 문자열")):
        line = E.deploy_sh_drift(data)
        assert line is not None, f"{why}: 못 쟀는데 조용했다"
        assert "상태 파일에 없다" in line and "ec2-status.sh" in line, (why, line)
    print("  못 쟀으면 — '옛 ec2-status.sh 다' 라고 말한다")

    # ── ④ `problems()` 에 실제로 실린다 ─────────────────────────────────
    #    함수만 맞고 카드에 안 실리면 아무 소용이 없다(`-133` 에서 겪은 것).
    from datetime import datetime
    now = datetime.now(E.KST)
    good = {
        "at": now.isoformat(),
        "disk": {"used_pct": 20, "free_gb": 249},
        "mem": {"total_gb": 15, "available_gb": 12},
        "deploy_sh_sha256": want,
    }
    assert not any("배포 스크립트" in r for r in E.problems(good, now)), E.problems(good, now)
    stale = dict(good, deploy_sh_sha256="0" * 64)
    rows = E.problems(stale, now)
    assert any("저장소본과 다르다" in r for r in rows), rows
    assert "🖥 서버" in E.section(stale, now), E.section(stale, now)
    print("  problems()·section() 을 통해 카드에 실린다")

    # ── ⑤ 호스트가 그 값을 실제로 보내는가 ─────────────────────────────
    #    읽는 쪽만 고치고 재는 쪽을 잊으면 ③ 이 매일 울린다.
    sh = io.open(os.path.join(ROOT, "scripts/deploy/host/ec2-status.sh"),
                 encoding="utf-8").read()
    assert "DEPLOY_SH_SHA" in sh, "ec2-status.sh 가 해시를 재지 않는다"
    assert "deploy_sh_sha256" in sh, "ec2-status.sh 가 그 값을 JSON 에 안 넣는다"
    assert "DEPLOY_SH_SHA \\" in sh or "DEPLOY_SH_SHA " in sh, "export 목록에 없다"
    print("  재는 쪽(ec2-status.sh)도 같이 고쳐져 있다")

    print("TEST_DEPLOY_SH_DRIFT OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
