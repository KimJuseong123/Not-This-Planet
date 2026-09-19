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


def code_only(text: str) -> str:
    """주석과 문자열 **내용**을 공백으로 지운 사본. 길이는 그대로 둔다.

    두 가지를 막는다. 둘 다 **조용히 틀리는** 모양이라 없는 검사보다 나쁘다(`!539` 리뷰).

      · 설명 주석에 적어 둔 `[UnityTearDown]` 이 진짜 훅으로 잡히는 것
      · **문자열 안의 `//` 를 주석으로 보는 것.** `"wss://"`·`IndexOf("//")` 가 이
        저장소에 실제로 있다(`BackendEndpointsTests` · `PlayEventsTests` 등 5곳).
        줄 끝까지 지우면 그 줄의 닫는 중괄호까지 사라져 **훅 본문이 파일 뒤쪽을
        삼키고**, 거기 있는 `NewScene(…)` 때문에 그냥 통과한다

    C# 파서가 아니라 **글자 상태기**다. 가르는 것은 주석·문자열·문자 리터럴뿐이다.
    """
    out = list(text)
    i, n = 0, len(text)

    def blank(k):
        if out[k] != "\n":
            out[k] = " "

    while i < n:
        two = text[i:i + 2]
        if two == "//":
            while i < n and text[i] != "\n":
                blank(i)
                i += 1
        elif two == "/*":
            while i < n and text[i:i + 2] != "*/":
                blank(i)
                i += 1
            for _ in range(2):
                if i < n:
                    blank(i)
                    i += 1
        elif two == '@"' or text[i:i + 3] in ('@$"', '$@"'):
            # 축자 문자열 — `\` 는 이스케이프가 아니고 `""` 가 따옴표다.
            # 보간이 붙은 `@$"`·`$@"` 도 같은 규칙이다. 그것을 일반 문자열로 읽으면
            # 경로 리터럴의 `\` 를 이스케이프로 보고 **문자열 끝을 놓친다**(`!539` 리뷰).
            width = 2 if two == '@"' else 3
            for k in range(width):
                blank(i + k)
            i += width
            while i < n:
                if text[i] == '"':
                    if text[i:i + 2] == '""':
                        blank(i)
                        blank(i + 1)
                        i += 2
                        continue
                    blank(i)
                    i += 1
                    break
                blank(i)
                i += 1
        elif text[i] in ('"', "'"):
            quote = text[i]
            blank(i)
            i += 1
            while i < n:
                if text[i] == chr(92) and i + 1 < n:   # 역슬래시 이스케이프
                    blank(i)
                    blank(i + 1)
                    i += 2
                    continue
                if text[i] == quote:
                    blank(i)
                    i += 1
                    break
                blank(i)
                i += 1
        else:
            i += 1
    return "".join(out)


#: 메서드 호출로 볼 이름. C# 관례상 메서드는 대문자로 시작하므로, `if (`·`while (`
#: 같은 키워드가 걸리지 않는다.
CALL_RE = re.compile(r"\b([A-Z][A-Za-z0-9_]*)\s*\(")


def body_of_method(code: str, name: str) -> str:
    """같은 파일에서 `name(…) { … }` 로 **선언된** 메서드의 본문. 없으면 빈 문자열.

    호출부(`ResetScene();`)는 `)` 뒤가 `;` 라 걸리지 않는다 — 선언만 본다.

    **제어 블록을 선언으로 읽지 않는다** (`!539` 4차 리뷰). `if (Foo()) {` 는 이름 뒤가
    `(…) {` 라 예전 정규식에 걸렸고, 그 블록 안에 `NewScene` 이 있으면 정리 훅이 실제로
    아무것도 안 해도 통과했다 — 이 MR 이 막으려던 바로 그 모양이다. 선언에는 이름 앞에
    **반환형**이 온다(`void ResetScene()`)는 점으로 가른다. `if`·`using`·`catch` 는
    이름 앞이 `(` 라 걸리지 않는다.
    """
    #                반환형의 마지막 글자 ─┐  (`List<int>`·`int[]` 도 받는다)
    m = re.search(r"[\w>\]]\s+" + re.escape(name) + r"\s*\([^;{]*\)\s*\{", code)
    if not m:
        return ""
    open_at = code.find("{", m.start())
    depth = 0
    for j in range(open_at, len(code)):
        if code[j] == "{":
            depth += 1
        elif code[j] == "}":
            depth -= 1
            if depth == 0:
                return code[open_at:j + 1]
    return ""


def resets_scene(body: str, code: str) -> bool:
    """이 정리 훅이 씬을 되돌리는가. **같은 파일의 헬퍼 한 단계까지** 따라간다.

    `[TearDown] void Cleanup() { ResetScene(); }` 처럼 공용 헬퍼로 정리하는 것은
    올바른 모양인데, 본문의 글자만 세면 **가짜로 실패한다**(`!539` AI 리뷰). 가짜
    실패는 다음 사람이 가드를 약하게 만드는 쪽으로 민다.

    한 단계만 편다. 그 이상은 이 파일이 C# 파서가 되어 가고, 그러면 파서가 틀리는
    자리마다 이 검사가 조용히 틀린다 — 그게 `-632` 의 교훈이다.
    """
    if RESETS in body or CLOSES in body:
        return True
    for name in set(CALL_RE.findall(body)):
        inner = body_of_method(code, name)
        if inner and (RESETS in inner or CLOSES in inner):
            return True
    return False


def teardown_bodies(code: str, rel: str):
    """정리 훅이 붙은 메서드의 본문들 (S15P21B202-632).

    C# 파서가 아니다 — 우리 검증 파일에 실제로 나오는 모양만 다룬다. 속성이 메서드와
    같은 줄인 것(`[UnityTearDown] public IEnumerator Cleanup(){…}`)과 다른 줄인 것,
    그리고 **식 본문**(`[TearDown] public void Cleanup() => Foo();`) 셋이다.

    식 본문을 따로 다루는 이유: 그때 뒤따라 나오는 첫 `{` 는 **다음 메서드의 본문**이다.
    그것을 이 훅의 본문으로 읽으면 엉뚱한 코드를 보게 된다 — 이 저장소에 그 모양이
    4곳 있다(`AuthoredMapSettingsTests` · `TinyFlowerPatchBuilderTests` 등).

    **짝을 못 찾으면 조용히 넘어가지 않는다.** 그냥 넘어가면 "훅이 없다" 는 엉뚱한
    메시지가 나가거나, 더 나쁘게는 통과한다.

    반환: `(본문 목록, 못 읽은 것이 있었나)`. 둘째 값이 참이면 호출한 쪽은 "훅이 없다"
    메시지를 겹쳐 내지 않는다 — 원인과 무관한 말이 섞이면 읽는 사람이 헤맨다(`!539` 리뷰).
    """
    out, unreadable = [], False
    for marker in TEARDOWNS:
        at = 0
        while True:
            i = code.find(marker, at)
            if i < 0:
                break
            at = i + len(marker)
            open_at = code.find("{", at)
            arrow = code.find("=>", at)

            if arrow >= 0 and (open_at < 0 or arrow < open_at):
                end = code.find(";", arrow)
                if end < 0:
                    check(False, f"{rel} — `{marker}` 의 식 본문이 `;` 로 끝나지 않는다 "
                                 f"(이 검사가 그 훅을 못 읽는다)")
                    unreadable = True
                    break
                out.append(code[arrow:end + 1])
                continue

            if open_at < 0:
                check(False, f"{rel} — `{marker}` 뒤에 본문(`{{`)도 식 본문(`=>`)도 없다 "
                             f"(이 검사가 그 훅을 못 읽는다)")
                unreadable = True
                break

            depth, closed = 0, False
            for j in range(open_at, len(code)):
                if code[j] == "{":
                    depth += 1
                elif code[j] == "}":
                    depth -= 1
                    if depth == 0:
                        out.append(code[open_at:j + 1])
                        closed = True
                        break
            if not closed:
                check(False, f"{rel} — `{marker}` 의 본문 중괄호 짝을 못 찾았다 "
                             f"(이 검사가 그 훅을 못 읽는다)")
                unreadable = True
                break
        if unreadable:
            # 다른 속성까지 마저 훑어 봐야 같은 파일에 대한 말만 늘어난다 (`!539` 3차 리뷰).
            break
    return out, unreadable


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
    code = code_only(text)
    bodies, unreadable = teardown_bodies(code, rel)
    if unreadable:
        # 못 읽은 것은 이미 실패로 적었다. 여기서 "훅이 없다" 를 또 내면 원인이 흐려진다.
        continue
    has_teardown = bool(bodies)
    has_reset = any(resets_scene(b, code) for b in bodies)
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
        missing.append(f"정리 훅 안에서 `{RESETS}…)` 나 `{CLOSES}(…)` 로 되돌리는 것이 {where}"
                       f"\n      (훅이 같은 파일의 헬퍼를 부르는 것도 된다 — 한 단계까지 따라간다)")
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
