"""같은 씬을 한 픽스처가 몇 번이나 여는가 (S15P21B202-643).

실행: python scripts/ci/tests/test_scene_open_cost.py
Unity 없이 테스트 소스만 읽는다.

**무엇이 문제였나.** `Stage1_Astra.unity` 는 5.7MB 다. 한 번 여는 데 수 초 걸리는데,
`[TestCase]` 가 둘씩 달린 검증 두 개가 **같은 씬을 네 번** 열고 있었다. 잡 `585392`
실측에서 그 넷이 **60.8초** — EditMode 전체 219.8초의 **28%** 이고 픽스처별 합계 1위다.

    60.8초  ToolDisplayMountTests      ← 같은 씬 4번
    28.1초  FirstPersonHandViewTests
    23.3초  Stage2Tests
    21.1초  PracticeLobbyTests
    18.2초  Stage1SkyCoverageTests

**왜 이 방향인가.** 테스트 시간을 줄이는 방법 중 **커버리지를 하나도 잃지 않는** 쪽이다.
빼는 것이 없으므로 실행 집합이 안 바뀌고, 따라서 **새 플래키를 만들지 않는다** —
`-602`·`-611` 이 Heavy 태그로 55건을 뺀 뒤 처음 보는 플래키 3건이 생겼고(`-632`),
그것이 빼는 방식의 대가였다.

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

#: 씬 경로가 **글자로** 들어간 호출만 센다. 상수를 통하면 여기 안 걸리는데, 그것이
#: 바로 이 검사가 권하는 모양이라 괜찮다 — 한 곳으로 모으면 세어야 할 것이 없다.
OPEN_RE = re.compile(r'EditorSceneManager\.OpenScene\(\s*"([^"]+\.unity)"')

#: 고쳐 둔 자리. 되돌아가면 60.8초가 돌아온다.
FIXED = ("ToolDisplayMountTests.cs", "Assets/Scenes/Stage1_Astra.unity", 1)

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def test_files():
    for dirpath, _dirs, names in os.walk(ASSETS):
        if not dirpath.replace("\\", "/").endswith("/Tests/Editor"):
            continue
        for n in sorted(names):
            if n.endswith(".cs"):
                yield os.path.join(dirpath, n)


print()
print("== 씬 여는 비용 ==")

counts = {}
for path in test_files():
    text = io.open(path, encoding="utf-8", errors="replace").read()
    # 주석 줄은 뺀다 — 설명에 적어 둔 경로가 호출로 세어지면 안 된다.
    code = "\n".join(l for l in text.split("\n") if not l.lstrip().startswith("//"))
    name = os.path.basename(path)
    for scene in OPEN_RE.findall(code):
        counts[(name, scene)] = counts.get((name, scene), 0) + 1

# **아무것도 못 찾으면 조용히 통과한다** — 그것이 가장 나쁜 모양이다.
check(len(counts) >= 3,
      f"씬을 여는 호출을 {len(counts)}종밖에 못 찾았다 — 정규식이나 경로 규칙을 확인하라")

dupes = sorted(((n, s, c) for (n, s), c in counts.items() if c > 1),
               key=lambda x: -x[2])

f_name, f_scene, f_max = FIXED
got = counts.get((f_name, f_scene), 0)
check(got <= f_max,
      f"{f_name} 가 {f_scene} 를 {got}번 연다(최대 {f_max}) — `-643` 이 60.8초를 "
      f"줄인 자리다. 한 번만 열고 이미 열려 있으면 그대로 쓰는 도우미를 거치게 한다")

# **남은 것은 목록으로 남긴다** (`-643` 완료 조건 3). 막지는 않는다 — 어떤 픽스처는
# 다시 여는 것이 곧 격리 수단이라, 일률적으로 금지하면 `-604`·`-632` 가 고친 것을
# 되돌리게 된다. 무엇이 남았는지 보이게만 해 둔다.
if dupes:
    print("  같은 씬을 두 번 이상 여는 곳:")
    for n, s, c in dupes:
        print(f"    {c}회  {n}  ←  {s.rsplit('/', 1)[-1]}")
else:
    print("  같은 씬을 두 번 이상 여는 곳 없음")

if fails:
    print()
    for f in fails:
        print("❌ " + f)
    raise SystemExit(1)

print(f"  씬 여는 호출 {sum(counts.values())}건 · 파일·씬 조합 {len(counts)}종 ✅")
print("  가장 비쌌던 자리(ToolDisplayMountTests ← Stage1_Astra)는 한 번만 연다 ✅")
