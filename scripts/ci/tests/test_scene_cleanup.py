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
#: 근거를 적고 빠져나가는 길. **주석 줄에, 이슈 키와 함께** 적어야 한다
#: (`!511` AI 리뷰). 파일 어디에 있든 면제하면 테스트 데이터 문자열 하나로 검사가
#: 꺼진다 — 빠져나가는 길은 있되 **왜 빠져나가는지가 남아야** 한다.
ESCAPE = "scene-cleanup-ok"
ESCAPE_RE = re.compile(r"^\s*//.*" + re.escape(ESCAPE) + r".*S15P21B202-\d+", re.M)

fails = []


def without_line_comments(text: str) -> str:
    """`//` 주석을 지운다. 설명에 적어 둔 `[UnityTearDown]` 이 훅으로 잡히면 안 된다."""
    return re.sub(r"//[^\n]*", "", text)


def teardown_bodies(text: str) -> list:
    """정리 훅이 붙은 메서드의 본문들. 중괄호 짝으로 자른다 (S15P21B202-632).

    C# 파서가 아니다 — 우리 검증 파일에 실제로 나오는 모양만 다룬다. 속성이 메서드와
    같은 줄인 것(`[UnityTearDown] public IEnumerator Cleanup(){…}`)과 다른 줄인 것 둘 다
    이 방식으로 잘린다. 주석은 미리 지워 두므로 그 안의 중괄호에 걸리지 않는다.
    """
    code = without_line_comments(text)
    out = []
    for marker in TEARDOWNS:
        at = 0
        while True:
            i = code.find(marker, at)
            if i < 0:
                break
            at = i + len(marker)
            open_at = code.find("{", at)
            if open_at < 0:
                break
            depth = 0
            for j in range(open_at, len(code)):
                if code[j] == "{":
                    depth += 1
                elif code[j] == "}":
                    depth -= 1
                    if depth == 0:
                        out.append(code[open_at:j + 1])
                        break
    return out


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

    if ESCAPE_RE.search(text):
        excused += 1
        print(f"  ⏭ 예외 표시됨(근거 있음) — {os.path.basename(path)}")
        continue
    if ESCAPE in text:
        check(False,
              f"{rel} — `{ESCAPE}` 가 있지만 **주석 줄에 이슈 키와 함께**가 아니다.\n"
              f"      예) `// {ESCAPE}: 이 씬은 다음 검증이 이어 쓴다 (S15P21B202-000)`")
        continue

    # 여는 방식을 가른다 — `OpenScene(path)` 는 기본이 Single(현재 씬을 갈아치운다),
    # `OpenScene(path, OpenSceneMode.Additive)` 는 덧씌운다.
    calls = re.findall(re.escape(OPENS) + r"\s*\(([^;]*?)\)\s*;", text, re.S)

    # **해석하지 못한 것을 '전부 Additive' 로 읽지 않는다** (`!511` AI 리뷰).
    # 호출은 있는데(`OPENS in text`) 정규식이 하나도 못 잡으면 `single` 이 비어
    # 아래 Additive 분기로 떨어진다 — 그러면 `CloseScene` 글자만 있어도 **정리 없이
    # 통과한다.** 조용히 통과하는 검사는 없는 검사와 같다.
    if not calls:
        check(False,
              f"{rel} — `{OPENS}` 가 있는데 호출 형태를 해석하지 못했다.\n"
              f"      이 검사가 그 파일을 못 보고 지나간다 — 정규식을 넓히거나 호출을 "
              f"한 줄로 쓴다")
        continue

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

    # **되돌리기가 정리 훅 *안에* 있어야 한다** (S15P21B202-632).
    #
    # 예전에는 둘이 **같은 파일에 있기만 하면** 통과했다. 그래서 `Stage2Tests.cs` 가
    # 새어 나갔다 — 정리 훅은 `ExitPlayMode` 만 하고, `NewScene(...EmptyScene...)` 은
    # 300줄 아래 **어느 테스트의 준비 과정**에 있었다. 파일 단위로 글자만 세면 그 둘을
    # 가릴 수 없고, 그 결과 뒤이어 도는 검증 3건이 흔들렸다.
    # **빈 씬으로 되돌리거나, 연 씬을 닫거나** — 둘 다 다음 픽스처에게 깨끗한 상태를 남긴다.
    # `Single` 로 연 씬을 닫으면 유니티가 빈 씬을 남기므로 되돌린 것과 같다.
    bodies = teardown_bodies(text)
    has_teardown = bool(bodies)
    has_reset = any(RESETS in b or CLOSES in b for b in bodies)
    if has_reset and has_teardown:
        guarded += 1
        continue

    missing = []
    if not has_teardown:
        missing.append("정리 훅(TearDown·OneTimeTearDown·UnityTearDown)이 없다")
    elif not has_reset:
        # 파일 어딘가에는 있는데 훅 밖인 경우를 따로 말한다 — 그게 이 검사가 놓쳤던 모양이다.
        where = ("**정리 훅 밖**에 있다 — 그 자리는 다음 픽스처를 지키지 못한다"
                 if (RESETS in text or CLOSES in text) else "없다")
        missing.append(f"정리 훅 안에서 `{RESETS}…)` 나 `{CLOSES}(…)` 로 되돌리는 것이 {where}")
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
