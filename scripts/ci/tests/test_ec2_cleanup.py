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
#
#     **묶음 플래그와 롱 옵션까지 본다.** 처음에는 `\s-a\b` 였는데, `-af` 는 `a` 뒤가
#     `f` 라 `\b` 가 성립하지 않아 **그냥 통과했다.** `--all` 도 마찬가지였다.
#     막으려던 바로 그 실수를 놓치는 검사는 없는 것보다 나쁘다 — 있다고 믿게 만든다.
FORBIDDEN = [
    (r"docker\s+image\s+prune[^\n]*(?:\s-\w*a|--all\b)",
     "`docker image prune -a`(`-af`·`--all` 포함) — 태그 붙은 Unity 이미지(33 GB)까지 "
     "지운다. 다시 받는 데 7분"),
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


# ── ①b 이 검사가 실제로 잡는지 — 가짜 스크립트로 확인한다 ────────────────
#
#     **"금지 패턴이 없다" 는 패턴이 틀렸을 때도 통과한다.** 그래서 잡아야 하는
#     문장을 넣어 보고 실제로 걸리는지 본다. 이것이 없으면 이 파일은 안전하다는
#     느낌만 주고 아무것도 막지 않는다.
MUST_CATCH = [
    "docker image prune -a",
    "docker image prune -af",
    "docker image prune --all --force",
    "  docker system prune -af --volumes",
    "docker volume prune -f",
    "docker rmi unityci/editor:x",
]
for bad in MUST_CATCH:
    assert any(re.search(pat, bad) for pat, _ in FORBIDDEN), \
        f"검사가 이것을 못 잡는다: {bad!r} — 막으려던 실수가 그대로 통과한다"

MUST_ALLOW = [
    "docker image prune -f",
    "docker builder prune -f --filter until=168h",
    "docker container prune -f --filter until=24h",
    "docker image inspect unityci/editor:x",
]
for good in MUST_ALLOW:
    assert not any(re.search(pat, good) for pat, _ in FORBIDDEN), \
        f"안전한 명령을 막는다: {good!r}"
print("  검사가 -a·-af·--all 을 실제로 잡고, 안전한 것은 통과시킨다 ✅")


# ── ② 하기로 한 것은 실제로 한다 ─────────────────────────────────────────
#
#     "아무것도 안 하는 스크립트" 도 위 검사를 통과한다. 그것은 이 이슈를 푼 것이
#     아니므로 네 가지가 다 들어 있는지 본다.
REQUIRED = [
    ("docker builder prune", "빌드 캐시 (2026-09-11 실측 1.94 GB)"),
    ("dangling=true", "떠돌이 이미지 — 백엔드를 새로 빌드할 때마다 남는다"),
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

#     **태그를 박아 두면 에디터 버전을 올리는 순간 매주 빨개진다.** 그러면 "늘 빨간
#     유닛" 이 되어 진짜 소실을 못 알아본다. 저장소에 몇 개라도 있는지를 본다.
assert "6000.5.4f1" not in code,     "Unity 이미지 태그가 박혀 있다 — 에디터를 올리면 정상인데도 매주 실패한다"
assert "image ls" in code, "이미지가 남았는지 실제로 확인하지 않는다"
assert "exit 1" in code, "지켜야 할 것이 사라졌는데 성공으로 끝난다"

#     **볼륨도 이미지와 같은 무게다.** 개수를 찍기만 하면 0 이어도 조용히 지나가는데,
#     셰이더 캐시가 사라진 것은 이미지가 사라진 것과 같다(릴리스 +40분).
vol_block = code[code.index("VOLS="):code.index("VOLS=") + 400]
assert "MISSING=1" in vol_block, "보호 볼륨이 0개여도 성공으로 끝난다"
print("  끝나고 Unity 이미지와 보호 볼륨이 남았는지 확인한다 ✅")

#     **zip 은 수정 시각으로 고른다.** 이름 규칙을 안 따르는 것이 하나라도 섞이면
#     문자열 정렬은 최신 릴리스를 지울 대상으로 만든다.
assert "ls -1t" in code, "zip 을 이름으로 정렬한다 — 규칙 밖 파일이 섞이면 최신을 지운다"
print("  zip 을 수정 시각으로 고른다 ✅")


# ── ④ 지우지 않고 볼 수 있어야 한다 ──────────────────────────────────────
#
#     처음 돌릴 때 무엇이 사라질지 모르면 아무도 못 돌린다.
assert "DRY_RUN" in code, "DRY_RUN 이 없다 — 무엇을 지울지 미리 볼 수 없다"
print("  DRY_RUN 으로 미리 볼 수 있다 ✅")


# ── ④b 실패를 삼키지 않는다 ──────────────────────────────────────────────
#
#     타이머로 도는 것이라 아무도 화면을 안 보고 있다. 실패가 조용하면 몇 주 동안
#     아무것도 안 치우면서 초록으로 남는다.
assert "FAILED=1" in code, "단계가 실패해도 기록하지 않는다"
assert "exit 2" in code, "실패했는데 성공으로 끝난다"
print("  한 단계라도 실패하면 비0 으로 끝낸다 ✅")


# ── ④c CI 잡과 겹치면 도커 정리를 건너뛴다 ───────────────────────────────
#
#     일요일 03:00 은 한가하지만 **비어 있는 것이 보장되지는 않는다** — 새벽 배포
#     스케줄(-455)이나 누가 돌린 릴리스가 겹칠 수 있다. 캐시를 치우면 그 잡의
#     빌드가 느려진다. 한 주 미루는 편이 싸다.
assert "^runner-" in code, "도는 CI 잡을 보지 않는다"

#     이름은 도커 executor 의 관례일 뿐이다. executor 를 바꾸면 조용히 0 이 되어
#     **빌드 도중에 캐시를 치운다.** 러너가 붙이는 라벨도 같이 본다.
assert "gitlab-runner.job.id" in code, "러너 라벨을 보지 않는다 — 이름 규칙에만 기댄다"
assert "건너뛴다" in code or "건너뜀" in code, "겹칠 때 건너뛰지 않는다"

#     **단계마다 다시 잰다.** 한 번만 재면 ①을 도는 사이에 잡이 시작돼도 ②·③ 을
#     그대로 밀어붙인다.
assert code.count("busy)") >= 2, "건너뛸지를 한 번만 재고 단계마다 다시 보지 않는다"

#     **계속 건너뛰기만 하는 것도 문제다.** 러너가 늘 떠 있는 구성이 되면 정리가
#     영원히 안 도는데 종료 코드는 0 이라 아무도 모른다.
assert "skip-streak" in code, "연속 건너뛰기를 세지 않는다"

#     **미리 보기가 그 셈을 지우면 안 된다.** 설치가 DRY_RUN 을 한 번 돌리므로,
#     상태를 쓰면 설치할 때마다 연속 건너뛰기가 리셋돼 경고가 영원히 안 뜬다.
streak = code[code.index("STREAK_FILE="):]
assert 'if [ "$DRY" != "1" ]' in streak, "미리 보기가 연속 건너뛰기 셈을 바꾼다"
print("  CI 잡이 돌면 건너뛰되, 계속 건너뛰면 알린다 (미리 보기는 상태를 안 바꾼다) ✅")


# ── ④d 설치가 미리 보기 결과로 죽지 않는다 ───────────────────────────────
#
#     정리 스크립트는 지켜야 할 이미지가 없으면 1, 단계 실패면 2 로 끝낸다.
#     `set -e` 아래에서 그대로 두면 **설치가 거기서 죽어** 타이머는 등록됐는데
#     마지막 안내가 안 나온다 — 절반만 끝난 것처럼 보인다.
inst = open("scripts/deploy/host/install-cleanup.sh", encoding="utf-8").read()
dry_line = [ln for ln in inst.splitlines() if "DRY_RUN=1" in ln and "ec2-cleanup.sh" in ln]
assert dry_line, "설치가 미리 보기를 돌리지 않는다"
assert "||" in inst.split("DRY_RUN=1", 1)[1][:300], \
    "미리 보기의 종료 코드를 흡수하지 않는다 — 설치가 거기서 죽는다"
print("  설치가 미리 보기 결과로 죽지 않는다 ✅")


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

# ── 떠돌이 이미지에도 시간 조건이 붙어 있다 (S15P21B202-467) ─────────────
#     `busy()` 는 **도커 컨테이너**로만 잡을 센다. 그 사이 `docker build` 가 남기는
#     태그 없는 중간 층을 지우면 빌드가 깨지고, 되돌리려고 남겨 둔 직전 백엔드
#     이미지도 같이 사라진다 — 배포가 잘못됐다는 걸 아는 데는 시간이 걸린다.
#     **`docker image prune` 자체를 쓰지 않는다** (`!378` 리뷰). `--filter until=` 은
#     이미지를 **만든 시각**을 보지 **태그를 잃은 시각**을 보지 않는다 — 몇 주 전에
#     받은 Unity 이미지는 방금 태그를 잃어도 그 조건을 그냥 지나가 삭제된다.
_cl = open("scripts/deploy/host/ec2-cleanup.sh", encoding="utf-8").read()
_live = [ln for ln in _cl.splitlines() if not ln.strip().startswith("#")]
_img = [ln for ln in _live if "docker image prune" in ln]
assert not _img, \
    "`docker image prune` 으로 지운다 — until 은 만든 시각이라 태그를 잃은 Unity 이미지를 못 지킨다: " + str(_img)

#     받아 온 이미지(RepoDigests 가 있는 것)는 남겨야 한다 — Unity 가 그것이다.
assert "RepoDigests" in _cl, \
    "받아 온 이미지와 우리가 만든 이미지를 가르지 않는다 — 태그를 잃은 Unity 이미지가 지워진다"
assert "dangling=true" in _cl, "떠돌이 이미지 정리가 사라졌다"
assert "DANGLING_HOURS" in _cl, "시간을 바꿀 자리가 없다"

#     시간 조건도 남아 있어야 한다 — 도는 `docker build` 의 중간 층을 지우면 빌드가 깨진다.
assert "docker image inspect" in _cl and "{{.Created}}" in _cl, \
    "만든 시각을 보지 않는다 — 도는 빌드의 중간 층까지 지운다"
#     **못 읽으면 남기는 쪽이다.** 만든 시각을 못 읽었을 때 `0` 으로 두고 비교하면
#     `0 -gt cutoff` 가 거짓이라 **지우는 쪽**으로 떨어진다 — 주석과 정반대다.
#     도커 출력 형식이 바뀌는 순간 지키려던 이미지가 그 길로 사라진다(`!378` 2차 리뷰).
_pd = _cl.split("prune_dangling()", 1)[1].split(chr(10) + "}", 1)[0]
assert '"$created" = "0"' in _pd, \
    "만든 시각을 못 읽은 경우를 가르지 않는다 — 그러면 지우는 쪽으로 떨어진다"
_zero = _pd.index('"$created" = "0"')
_gt = _pd.index('-gt "$cutoff"')
assert _zero < _gt, "못 읽은 경우를 시간 비교보다 뒤에서 본다 — 비교가 먼저 지운다"
assert "kept" in _pd[_zero:_gt], "못 읽었는데 남기지 않는다"
print("  받아 온 이미지·방금 만든 층·못 읽은 것을 남기고, prune 을 쓰지 않는다 ✅")

print("== 통과 ==")
