"""EC2 정리가 지우면 안 되는 것을 지우지 않는지 (S15P21B202-457).

실행: python scripts/ci/tests/test_ec2_cleanup.py
네트워크·토큰·서버 없이 돈다. 저장소의 스크립트를 글자로 읽는다.

**왜 이 검사가 있나.** `docker system df` 는 2026-09-11 기준 **36.7 GB 중 32.5 GB
(88%)가 회수 가능**이라고 말한다. 그 말을 그대로 믿고 `-a` 를 붙이면 Unity 이미지
두 개(17.9 GB + 15.1 GB)가 사라진다 — 그것은 **컨테이너가 도는 동안에만** 쓰여서
도커가 보기에는 늘 놀고 있기 때문이다.

**지우는 순간은 공짜고 대가는 다음 릴리스에 온다.** 이미지 다시 받기 7분,
셰이더 캐시까지 날아가면 빌드가 40분 더(-406 실측). 그때는 이미 지운 뒤다.

그래서 "하면 안 되는 것" 을 검사로 잠근다. 사람이 나중에 `-a` 를 붙이는 것이
이 이슈가 막으려는 바로 그 실수다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
os.chdir(ROOT)

SCRIPT = "scripts/deploy/host/ec2-cleanup.sh"
src = open(SCRIPT, encoding="utf-8").read()

# 주석에서는 위험한 명령을 **설명해야** 한다. 실제로 도는 줄만 본다.
code = "\n".join(ln for ln in src.splitlines() if not ln.lstrip().startswith("#"))

print()
print("== EC2 정리 ==")


# ── ① 절대 하면 안 되는 것 ───────────────────────────────────────────────
FORBIDDEN = [
    (r"docker\s+image\s+prune[^\n]*\s-a\b",
     "`docker image prune -a` — 태그 붙은 Unity 이미지(33 GB)까지 지운다. 다시 받는 데 7분"),
    (r"docker\s+system\s+prune",
     "`docker system prune` — 이미지·볼륨·캐시를 한꺼번에 지운다. 러너 Library 가 거기 있다"),
    (r"docker\s+volume\s+prune",
     "`docker volume prune` — 러너 셰이더 캐시가 날아가 릴리스가 40분 길어진다(-406 실측)"),
    (r"docker\s+rmi\b",
     "`docker rmi` — 이름을 짚어 지우는 것은 이 스크립트가 할 일이 아니다"),
    (r"rm\s+-rf\s+/(?:var/lib/docker|opt/metagame\b|home/ubuntu)",
     "통째로 지우기 — 되돌릴 수 없다"),
]
for pat, why in FORBIDDEN:
    m = re.search(pat, code)
    assert not m, f"정리가 위험한 명령을 쓴다: {m.group(0) if m else ''}\n    {why}"
print("  Unity 이미지·러너 볼륨을 지우는 명령이 없다 ✅")


# ── ② 하기로 한 것은 실제로 한다 ─────────────────────────────────────────
#
#     "아무것도 안 하는 스크립트" 도 위 검사를 통과한다. 그것은 이 이슈를 푼 것이
#     아니므로 네 가지가 다 들어 있는지 본다.
REQUIRED = [
    ("docker builder prune", "빌드 캐시 (2026-09-11 실측 1.94 GB)"),
    ("docker image prune -f", "떠돌이 이미지 — 백엔드를 새로 빌드할 때마다 남는다"),
    ("docker container prune", "끝난 컨테이너 — 러너가 잡마다 만들고 버린다"),
    (".zip", "릴리스 zip — 런처 저장소와 달리 아무도 회수하지 않았다"),
]
for frag, why in REQUIRED:
    assert frag in code, f"{frag} 가 없다 — {why}"
print("  치우기로 한 넷을 모두 한다 ✅")


# ── ③ 지운 뒤 지켜야 할 것이 남았는지 스스로 확인한다 ────────────────────
#
#     위 ①은 **지금** 안전하다는 것만 말한다. 누가 나중에 `-a` 를 붙이면 이 검사가
#     막지만, 손으로 돌리는 경우까지는 못 막는다. 스크립트가 끝에서 직접 본다.
assert "unityci/editor" in code, "지켜야 할 이미지를 확인하지 않는다"
assert "image inspect" in code, "이미지가 남았는지 실제로 확인하지 않는다"
assert "exit 1" in code, "지켜야 할 것이 사라졌는데 성공으로 끝난다"
print("  끝나고 Unity 이미지가 남았는지 스스로 확인한다 ✅")


# ── ④ 지우지 않고 볼 수 있어야 한다 ──────────────────────────────────────
#
#     처음 돌릴 때 무엇이 사라질지 모르면 아무도 못 돌린다.
assert "DRY_RUN" in code, "DRY_RUN 이 없다 — 무엇을 지울지 미리 볼 수 없다"
print("  DRY_RUN 으로 미리 볼 수 있다 ✅")


# ── ⑤ 보관 개수가 되돌릴 여지를 남긴다 ───────────────────────────────────
m = re.search(r"KEEP_ZIPS:-(\d+)", code)
assert m, "zip 보관 개수가 없다"
assert 2 <= int(m.group(1)) <= 10, f"보관 {m.group(1)}개는 되돌릴 여지가 없거나 너무 많다"
print(f"  릴리스 zip 을 {m.group(1)}개 남긴다 ✅")


# ── ⑥ 실행되는 코드는 스풀 밖에 있어야 한다 (-324) ───────────────────────
#
#     스풀은 배포 러너에 rw 로 보인다. 안에 둔 스크립트는 잡이 덮어쓸 수 있고
#     호스트가 그것을 root 로 실행한다.
unit = open("scripts/deploy/host/metagame-cleanup.service", encoding="utf-8").read()
assert "/opt/metagame-deploy/" in unit, "유닛이 스풀 밖 코드를 부르지 않는다"
assert "/deploy/ec2-cleanup.sh" not in unit, "스풀 안의 스크립트를 root 로 실행한다(-324)"
print("  유닛이 스풀 밖 코드를 부른다 ✅")


# ── ⑦ 정리와 알림이 짝인지 ───────────────────────────────────────────────
#
#     `-458` 이 "차는 것을 알려 주고" 이쪽이 "치운다". 기록에 그 관계가 있어야
#     다음 사람이 한쪽만 보고 판단하지 않는다.
rec = open("docs/records/S15P21B202-457-ec2-retention.md", encoding="utf-8").read()
assert "-458" in rec, "정리와 알림의 관계가 기록에 없다"
assert "metagame.service" in rec, "정지된 게임 서버 유닛에 대한 결정이 기록에 없다"
print("  기록이 알림(-458)과 정지된 유닛 결정을 담고 있다 ✅")

print("== 통과 ==")
