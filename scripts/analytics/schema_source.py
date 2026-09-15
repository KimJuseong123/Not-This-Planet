"""이벤트가 기대는 정의를 **게임 코드에서** 뽑는다 (S15P21B202-552).

실행: python scripts/analytics/schema_source.py        지금 값을 계약 모양으로 찍는다

수집할 이벤트는 게임 코드의 값에 얹혀 있다. `floors: [2,1,0,0]` 에서 2가 `Welded`
라는 것은 **`FloorState` 의 순서**가 정한다. 그 순서가 바뀌면 어제까지 쌓인 데이터의
뜻이 조용히 달라진다 — 숫자는 그대로 들어오고 질의도 그대로 돌아가며, 틀린 답만 나온다.

**지문이 바뀌는 것 자체는 잘못이 아니다** — 기획이 바뀌면 당연히 바뀐다.
**조용히 바뀌는 것**이 잘못이다.
"""
import hashlib
import io
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

ASSEMBLY = "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/ShipAssembly.cs"
PARTSPEC = "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartSpec.cs"

# **지문에 들어가는 것 = 데이터의 뜻을 정하는 것** (`!461` AI 리뷰).
#
# `blocker_messages` 는 뺀다. 사람이 읽는 한글 문장이라 오타를 고치기만 해도 값이
# 달라지는데 뜻은 그대로다 — 그때마다 지문이 갈리면 **옛 데이터와 새 데이터가 비교
# 불가능해진다.** 계약 파일에는 실어 두고 검사가 따로 대조한다.
#
# **대신 `blocker_codes` 가 순서까지 담는다.** 전에는 그 목록을 손으로 적어 두고
# `Evaluate()` 의 순서에 맞췄는데, 그러면 **막는 자리의 순서만 바뀌어도 뜻이 달라지는데
# 지문은 그대로**였다 — `tilted` 로 센 것이 사실은 `off_pad` 가 된 채로 옛 데이터와
# 섞인다. 지금은 코드 이름을 소스에서 뽑으므로 순서가 바뀌면 지문도 바뀐다.
MEANING = ("floor_state", "parts", "blocker_codes")


def read(rel):
    return io.open(os.path.join(ROOT, rel), encoding="utf-8").read()


def strip_comments(s):
    """주석을 지운다 — **문자열 안의 `//` 는 건드리지 않는다**(`!461` AI 리뷰).

    단순히 `//.*` 를 지우면 `\"https://…\"` 같은 문자열이 잘린다. 지금 코드에는 없지만
    한 줄만 들어와도 계약이 조용히 틀리게 만들어지므로 처음부터 막아 둔다.
    """
    s = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
    out = []
    for line in s.split(chr(10)):
        out.append(line[:_code_len(line)])
    return chr(10).join(out)


def _code_len(line):
    """그 줄에서 주석이 시작되기 전까지의 길이. 문자열 안은 세지 않는다."""
    in_str = False
    i = 0
    while i < len(line):
        c = line[i]
        if in_str:
            if c == chr(92):      # 이스케이프 — 다음 글자는 통째로 건너뛴다
                i += 2
                continue
            if c == chr(34):
                in_str = False
        elif c == chr(34):
            in_str = True
        elif c == "/" and i + 1 < len(line) and line[i + 1] == "/":
            return i
        i += 1
    return len(line)


def braced_body(text, start_marker):
    """`start_marker` 뒤 첫 `{` 부터 짝이 맞는 `}` 까지.

    **주석 위치에 기대지 않는다**. 전에는 `Evaluate()` 뒤에 XML 주석이 나온다는 가정으로
    잘랐는데, 그 주석이 없어지거나 자리가 바뀌면 죽거나 목록이 잘린 채 계약이
    만들어졌다 — 계약이 조용히 틀리는 것이 이 파일에서 가장 나쁜 고장이다.

    **주석을 먼저 걷어 낸다** (`!461` AI 리뷰). 주석 안의 중괄호가 깊이를 흔들면
    엉뚱한 자리에서 잘리고, 주석 처리된 `Blocker = \"…\"` 줄이 남으면 사유 목록에
    섞여 코드 매핑이 한 칸 밀린다.
    """
    text = strip_comments(text)
    i = text.index(start_marker)
    j = text.index("{", i)
    depth = 0
    in_str = False
    k = j
    while k < len(text):
        c = text[k]
        if in_str:
            if c == chr(92):
                k += 2
                continue
            if c == chr(34):
                in_str = False
        elif c == chr(34):
            in_str = True
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return text[j + 1:k]
        k += 1
    raise RuntimeError(f"{start_marker} 의 끝을 찾지 못했다 — 계약을 만들 수 없다")


def enum_members(body):
    """enum 본문에서 이름을 순서대로. **검사가 부를 수 있게 갈라 둔다**(`!461` 리뷰).

    전에는 검사가 이 로직을 자기 안에 베껴 두고 있어서, 진짜 파서가 줄 단위 방식으로
    되돌아가도 검사는 계속 통과했다 — 회귀를 못 막는 검사였다.

    **쉼표로 자른다.** `…,` 로 끝나는 줄만 잡으면 C# 관행대로 맨 끝에 쉼표 없이 값을
    더했을 때 그 값이 통째로 빠지고, 지문이 그대로라 검사가 통과한다.
    """
    out = []
    for chunk in body.split(","):
        m = re.match(r"\s*(\w+)", chunk)
        if m:
            out.append(m.group(1))
    return out


def floor_states():
    """조립 층의 상태 목록. **순서가 곧 이벤트의 숫자**다."""
    out = enum_members(braced_body(read(ASSEMBLY), "enum FloorState"))
    if not out:
        raise RuntimeError("FloorState 를 한 개도 못 읽었다 — 계약을 만들 수 없다")
    return out

def parts():
    """조립 순서. 인덱스가 곧 층이고, 길이가 곧 `floors` 배열의 길이다.

    **`All` 배열 안에서만 찾는다** (`!461` AI 리뷰). 파일 전체를 훑으면 그 배열 밖에
    같은 모양의 선언(시험용·안 쓰는 상수·다른 배열)이 하나만 생겨도 실제 `All` 과
    **다른 목록·순서**가 계약에 들어간다. 이 값이 `floors` 인덱스의 뜻을 정하므로
    조용히 틀리면 데이터 전체가 틀린다.
    """
    body = braced_body(read(PARTSPEC), "PartSpec[] All =")
    out = [m.group(1) for m in re.finditer(r'new\("(Part_\d+)",', body)]
    if not out:
        raise RuntimeError("PartSpec.All 을 한 개도 못 읽었다 — 계약을 만들 수 없다")
    return out

def blockers():
    """`Evaluate()` 가 막는 사유를 **(이름, 문구)** 쌍으로. 빈 문자열은 뺀다.

    **이름으로 짝짓는다** (`!461` AI 리뷰). 전에는 문구를 순서대로 뽑아 손으로 적어 둔
    이름 목록에 위치로 맞췄다. 그러면 막는 자리의 **순서만 바뀌어도** 집계의 뜻이
    통째로 밀리는데(`tilted` 로 센 것이 `off_pad` 가 된다) 겉으로는 아무 일도 안
    일어난 것처럼 보인다.

    이제 각 대입 옆의 `// code: …` 주석이 짝을 정한다. 순서를 바꿔도 짝이 함께
    움직이고, 이름이 빠지면 여기서 막는다.
    """
    body = braced_body(read(ASSEMBLY), "public void Evaluate()")
    raw = read(ASSEMBLY)
    out = []
    for line in raw.split(chr(10)):
        m = re.search(r'Blocker\s*=\s*[$]?"([^"]*)"', line)
        if not m:
            continue
        msg = " ".join(m.group(1).split())
        if not msg:
            continue          # `Blocker = ""` — 막히지 않은 경우
        if line[:_code_len(line)].find(m.group(0)) < 0:
            continue          # 주석 처리된 줄
        c = re.search(r"//\s*code:\s*(\w+)", line)
        if not c:
            raise RuntimeError(
                "막는 사유에 `// code: 이름` 이 없다 — 집계가 이름으로 짝지어진다:"
                + chr(10) + "    " + line.strip())
        out.append((c.group(1), msg))
    if not out:
        raise RuntimeError("Evaluate() 에서 막는 사유를 못 읽었다 — 계약을 만들 수 없다")
    if len(out) != len({k for k, _ in out}):
        raise RuntimeError(f"막는 사유의 이름이 겹친다: {[k for k, _ in out]}")
    return out

def snapshot():
    """계약에 실리는 값 전부. 지문에 드는 것은 `MEANING` 뿐이다."""
    pairs = blockers()
    return {
        "floor_state": floor_states(),
        "parts": parts(),
        "blocker_codes": [k for k, _ in pairs],
        "blocker_messages": [v for _, v in pairs],
    }

def fingerprint(snap):
    """데이터의 뜻을 정하는 값들의 지문. 키 순서·공백에 안 흔들린다."""
    canon = json.dumps({k: snap[k] for k in MEANING},
                       ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canon.encode("utf-8")).hexdigest()[:12]


def contract():
    snap = snapshot()
    return {
        "fingerprint": fingerprint(snap),
        "note": "이벤트가 기대는 게임 코드의 정의 (S15P21B202-552). "
                "손으로 고치지 말 것 — `python scripts/analytics/schema_source.py` 가 찍어 준다. "
                "`fingerprint` 는 데이터의 뜻을 정하는 값(floor_state·parts·blocker_codes)만 "
                "묶은 것이라, 한글 문구만 고치면 지문은 그대로다.",
        **snap,
    }


if __name__ == "__main__":
    import sys
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(contract(), ensure_ascii=False, indent=2))
