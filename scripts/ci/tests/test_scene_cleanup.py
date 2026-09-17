"""씬을 연 검증은 반드시 닫는가 (S15P21B202-604).

실행: python scripts/ci/tests/test_scene_cleanup.py
Unity 없이 테스트 소스만 읽는다.

**왜 이 검사가 있나 — 같은 사고가 두 번 났다.**

EditMode 검증이 `EditorSceneManager.OpenScene` 으로 씬을 열고 닫지 않으면, 그 씬이
**열린 채로 다음 검증들에게 넘어간다.** 다음 검증이 만든 오브젝트는 그 씬의 콜라이더
위에 놓이고, 물리가 전혀 다른 결과를 낸다.

  · 1차 — `Stage1SkyCoverageTests` 를 만들 때 빠뜨려 **빔·운반체 검증 10개**가 무너졌다.
    그 파일에 `TearDown` 을 달아 막았다
  · 2차 — `-590` 의 `CompanionTests`·`CompanionToolTests` 가 `AIPracticeLobby.unity` 를
    8번 열고 한 번도 닫지 않았다. 머지 직후 develop 의 `unity-compile` 이 **새 실패
    10건**으로 빨개졌고 그중 9건이 `CraftMeteorImpactTests` 였다 — 기체가 바닥까지
    떨어지지 않고 **그 방의 바닥(4m)** 에 걸렸다

1차 때 규칙을 **그 파일 안에만** 두었기 때문에 2차가 났다. 사람의 기억이 아니라 검사가
막아야 한다 — 그것이 이 파일이다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
ASSETS = os.path.join(ROOT, "METAGAME", "Assets")

OPENS = "EditorSceneManager.OpenScene"
#: 되돌리는 관례. 빈 씬으로 갈아 끼우면 앞서 연 씬이 닫힌다.
RESETS = "NewScene(NewSceneSetup.EmptyScene"
#: 정리가 **테스트마다**(`TearDown`·`UnityTearDown`) 든 **클래스 끝에**
#: (`OneTimeTearDown`) 든 상관없다. 지키려는 것은 "이 클래스가 끝난 뒤 남의 씬이 열려
#: 있지 않다" 하나다. `UnityTearDown` 은 코루틴 정리용이고 실제로 쓰이고 있다
#: (`PracticeLobbyTests`) — 빠뜨리면 멀쩡한 파일을 빨갛게 만든다.
TEARDOWNS = ("[TearDown]", "[OneTimeTearDown]", "[UnityTearDown]")
#: **덧씌워 여는 것(Additive)은 닫는 방법이 다르다.** 현재 씬을 갈아치우지 않으므로
#: 빈 씬으로 되돌릴 이유가 없고, `CloseScene` 으로 그 씬만 떼면 된다
#: (`CheatArenaTests`·`CustomizationTests` 가 `finally` 에서 그렇게 한다).
ADDITIVE = "OpenSceneMode.Additive"
CLOSES = "EditorSceneManager.CloseScene"
#: 근거를 적고 빠져나가는 길. 이유 없이 붙이면 리뷰에서 걸린다.
ESCAPE = "scene-cleanup-ok"

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def test_files():
    """`Tests/Editor` 아래의 C# 파일 전부. 모듈마다 자기 폴더를 가질 수 있어 훑는다."""
    for dirpath, _dirs, names in os.walk(ASSETS):
        norm = dirpath.replace("\\", "/")
        if not norm.endswith("/Tests/Editor"):
            continue
        for n in sorted(names):
            if n.endswith(".cs"):
                yield os.path.join(dirpath, n)


print()
print("== 씬 정리 ==")

opened, guarded, excused = 0, 0, 0
for path in test_files():
    text = io.open(path, encoding="utf-8", errors="replace").read()
    if OPENS not in text:
        continue
    opened += 1
    rel = os.path.relpath(path, ROOT).replace("\\", "/")

    if ESCAPE in text:
        excused += 1
        print(f"  ⏭ 예외 표시됨 — {os.path.basename(path)}")
        continue

    # 여는 방식을 가른다 — `OpenScene(path)` 는 기본이 Single(현재 씬을 갈아치운다),
    # `OpenScene(path, OpenSceneMode.Additive)` 는 덧씌운다.
    calls = re.findall(re.escape(OPENS) + r"\s*\(([^;]*?)\)\s*;", text, re.S)
    single = [c for c in calls if ADDITIVE not in c]

    if not single:
        # 전부 덧씌워 열었다 — 그 씬만 떼면 된다.
        if CLOSES in text:
            guarded += 1
            continue
        check(False,
              f"{rel} — 씬을 덧씌워 여는데 `{CLOSES}` 로 떼지 않는다.\n"
              f"      뒤이어 도는 검증이 그 씬의 오브젝트를 함께 보게 된다")
        continue

    has_reset = RESETS in text
    has_teardown = any(t in text for t in TEARDOWNS)
    if has_reset and has_teardown:
        guarded += 1
        continue

    missing = []
    if not has_teardown:
        missing.append("정리 훅(TearDown·OneTimeTearDown·UnityTearDown)이 없다")
    if not has_reset:
        missing.append(f"`{RESETS}…)` 로 되돌리지 않는다")
    check(False,
          f"{rel} — 씬을 여는데 {' · '.join(missing)}.\n"
          f"      열어 둔 채 끝나면 **뒤이어 도는 검증들이 이 씬의 콜라이더를 만난다** "
          f"(-604 에서 9건이 그렇게 무너졌다).\n"
          f"      정리를 넣거나, 정말 필요하면 근거와 함께 `{ESCAPE}` 를 적는다")

print(f"  씬을 여는 파일 {opened}개 · 정리 있음 {guarded}개 · 예외 {excused}개")

# **검사가 아무것도 안 보고 초록인 상태를 막는다.** 경로 규칙이 바뀌어 파일을 하나도
# 못 찾으면 이 파일은 조용히 통과한다 — 그것이 가장 나쁜 모양이다.
check(opened >= 2, f"씬을 여는 테스트 파일을 {opened}개밖에 못 찾았다 — 경로 규칙을 확인하라")

if fails:
    print()
    for f in fails:
        print("❌ " + f)
    raise SystemExit(1)

print("  씬을 여는 검증은 전부 닫는다 ✅")
