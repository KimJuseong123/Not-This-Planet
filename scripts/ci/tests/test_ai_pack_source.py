"""AI 추가팩 주소가 빌드에 제대로 심기는가 (S15P21B202-690).

실행: python scripts/ci/tests/test_ai_pack_source.py

**무엇을 막나.** 이 값이 틀리면 게임의 "AI 추가팩 다운로드" 버튼이 **조용히 안
먹는다** — 설치기가 `AiPackValidation.Source` 에서 거부하고 "다운로드 배포 주소가
아직 설정되지 않았어요" 를 내는데, 그것은 *주소를 안 넣은 경우와 같은 문구*다.
QA 는 그 화면만 보고 주소 오타인지 기능 결함인지 가릴 수 없다. 그래서 빌드가
먼저 실패해야 한다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import os
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
CI = os.path.dirname(HERE)
ROOT = os.path.dirname(os.path.dirname(CI))
SCRIPT = os.path.join(CI, "write_ai_pack_source.py")
TARGET = os.path.join(ROOT, "METAGAME", "Assets", "StreamingAssets", "ai-pack-source.json")

GOOD_URL = "https://example.invalid/ai-packs/0.0.0/pack-manifest.json"
GOOD_HASH = "0" * 64

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def run(url=None, digest=None, args=()):
    """스크립트를 돌리고 (종료코드, 출력, 파일내용) 을 준다. 실제 파일은 되돌린다."""
    saved = None
    if os.path.exists(TARGET):
        with io.open(TARGET, "rb") as f:
            saved = f.read()
        os.remove(TARGET)
    env = dict(os.environ)
    env.pop("AI_PACK_MANIFEST_URL", None)
    env.pop("AI_PACK_MANIFEST_SHA256", None)
    if url is not None:
        env["AI_PACK_MANIFEST_URL"] = url
    if digest is not None:
        env["AI_PACK_MANIFEST_SHA256"] = digest
    env["PYTHONIOENCODING"] = "utf-8"
    r = subprocess.run([sys.executable, SCRIPT] + list(args), capture_output=True, text=True,
                       encoding="utf-8", errors="replace", env=env)
    body = None
    if os.path.exists(TARGET):
        with io.open(TARGET, encoding="utf-8") as f:
            body = f.read()
        os.remove(TARGET)
    # **원래 파일을 되돌린다.** 이 검사가 사람의 로컬 설정을 지우면 안 된다.
    if saved is not None:
        os.makedirs(os.path.dirname(TARGET), exist_ok=True)
        with io.open(TARGET, "wb") as f:
            f.write(saved)
    return r.returncode, (r.stdout or "") + (r.stderr or ""), body


print()
print("== AI 추가팩 주소 심기 (S15P21B202-690) ==")

# ── ① 값이 없으면 아무것도 쓰지 않고 **그 사실을 말한다**
code, out, body = run()
check(code == 0, "변수가 없을 때 빌드를 실패시킨다 — AI 없이 빌드하는 것이 정상 경로다")
check(body is None, "변수가 없는데 파일을 썼다")
check("설정되지 않았다" in out,
      "변수가 없다는 것을 말하지 않는다 — 'AI 설치가 안 된다' 의 원인이 변수 미설정인지 "
      "기능 결함인지 빌드 로그로 가릴 수 없다")
print("  없으면 안 쓰고, 안 썼다고 말한다 ✅")

# ── ② 한쪽만 있으면 실패
for url, digest, what in [(GOOD_URL, None, "주소만"), (None, GOOD_HASH, "해시만")]:
    code, out, body = run(url, digest)
    check(code != 0, f"{what} 있는데 통과했다 — 반쪽 설정으로 빌드가 나간다")
    check(body is None, f"{what} 있는데 파일을 썼다")
print("  한쪽만 있으면 실패한다 ✅")

# ── ③ 게임이 거부할 주소는 **빌드에서** 거부한다
#     `AiPackValidation.Source` 와 같은 조건이다. 설치기가 서명 URL 을 지원하지 않으므로
#     질의가 붙은 주소를 넣으면 게임에서 조용히 안 먹는다.
for bad, what in [("http://example.invalid/m.json", "http"),
                  ("https://u:p@example.invalid/m.json", "사용자정보"),
                  ("https://example.invalid/m.json?t=1", "질의"),
                  ("https://example.invalid/m.json#a", "조각"),
                  ("example.invalid/m.json", "스킴 없음")]:
    code, out, body = run(bad, GOOD_HASH)
    check(code != 0, f"주소에 {what} 가 있는데 통과했다 — 설치 버튼이 조용히 안 먹는다")
    check(body is None, f"주소에 {what} 가 있는데 파일을 썼다")
print("  게임이 거부할 주소를 빌드가 먼저 거부한다 ✅")

# ── ④ 해시가 64자리 16진수가 아니면 실패
for bad, what in [("abc", "짧음"), ("z" * 64, "16진수 아님"), ("0" * 63, "63자리")]:
    code, out, body = run(GOOD_URL, bad)
    check(code != 0, f"해시가 {what} 인데 통과했다")
print("  해시 형식을 본다 ✅")

# ── ⑤ 제대로 주면 **게임이 읽는 그 두 키**를 쓴다
code, out, body = run(GOOD_URL, GOOD_HASH)
check(code == 0, "올바른 값인데 실패했다: " + out.strip()[:200])
check(body is not None, "올바른 값인데 파일을 쓰지 않았다")
if body:
    import json
    data = json.loads(body)
    check(set(data) == {"manifest_url", "manifest_sha256"},
          f"게임이 읽는 두 키가 아니다: {sorted(data)} (AiPackSource 와 어긋나면 조용히 null 이 된다)")
    check(data.get("manifest_url") == GOOD_URL, "주소가 그대로 들어가지 않았다")
    check(data.get("manifest_sha256") == GOOD_HASH, "해시가 그대로 들어가지 않았다")
# 값을 로그에 흘리지 않는다 — 같은 자리에 비밀이 오는 날이 온다.
check(GOOD_HASH not in out, "해시 전체를 로그에 찍는다")
print("  올바른 값은 게임이 읽는 두 키로 들어간다 ✅")

# ── ⑥ **두 빌드 잡 모두** Unity 보다 먼저 이것을 부른다
#
#     한쪽만 부르면 "시험은 되는데 사용자 빌드에서만 설치가 안 되는" 상태가 된다 —
#     그 차이는 릴리스를 실제로 내보내기 전까지 아무도 모른다. 이 저장소가 반복해서
#     겪은 모양이라(한 문제를 두 곳이 다르게 푼다) 두 잡을 함께 본다.
with io.open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8") as f:
    ci = f.read()
jobs = {}
for name, end, version in [("build-devbuild", "publish-devbuild", "SPACE_BUILD_ID"),
                           ("release-windows", "release-summary", "SPACE_RELEASE_VERSION")]:
    job = ci[ci.index(chr(10) + name + ":"):]
    job = job[:job.index(chr(10) + end + ":")]
    jobs[name] = job
    call = job.find("write_ai_pack_source.py")
    unity = job.find(version)
    check(call >= 0, f"{name} 이 write_ai_pack_source.py 를 부르지 않는다 — "
                     "그 빌드에는 추가팩 주소가 안 들어간다")
    check(unity >= 0 and call > unity,
          f"{name} 이 버전을 정하기 전에 주소를 쓴다 — 순서가 바뀌면 읽기 어렵다")
print("  두 빌드 잡이 Unity 앞에서 부른다 ✅")

# ── ⑦ 주소가 없을 때: 개발 빌드는 넘어가고 **릴리스는 멈춘다** (S15P21B202-709)
#
#     개발 빌드는 주소 없이도 쓸모가 있다 — AI 말고 다른 것을 시험한다. 릴리스는 다르다.
#     주소 없이 나간 릴리스는 사용자가 "AI 동료 추가" 를 눌러도 **영영 받을 수 없는
#     빌드**이고, 그때 뜨는 문구는 변수를 안 넣은 경우와 글자 하나까지 같다. 즉
#     **내보낸 뒤에는 원인을 화면으로 가릴 수 없다.**
code, out, body = run(args=["--required"])
check(code != 0, "주소가 없는데 릴리스가 통과했다 — 게임 안에서 받을 길이 없는 빌드가 나간다")
check(body is None, "주소가 없는데 파일을 썼다")
code, out, body = run(GOOD_URL, GOOD_HASH, args=["--required"])
check(code == 0, "올바른 값인데 --required 가 실패했다: " + out.strip()[:200])
check(body is not None, "--required 로 올바른 값을 줬는데 파일을 쓰지 않았다")
print("  주소가 없으면 릴리스만 멈춘다 ✅")

# ── ⑧ 그 엄격함이 **릴리스 잡에만** 걸려 있다
#
#     개발 빌드까지 막으면 AI 변수가 없는 브랜치의 QA 빌드가 통째로 죽는다 — 그러면
#     사람들이 이 검사를 끄려 든다. 빨간 것이 늘 있으면 없는 것과 같아진다.
check("write_ai_pack_source.py --required" in jobs["release-windows"],
      "release-windows 가 --required 없이 부른다 — 주소 없는 릴리스가 조용히 나간다")
check("write_ai_pack_source.py --required" not in jobs["build-devbuild"],
      "build-devbuild 까지 --required 다 — AI 변수 없는 브랜치의 QA 빌드가 통째로 죽는다")
print("  엄격함은 릴리스에만 걸려 있다 ✅")

if fails:
    print(f"\n❌ 실패 {len(fails)}건")
    for f in fails:
        print(f"  · {f}")
    raise SystemExit(1)
print("TEST690_AI_PACK_SOURCE OK")
