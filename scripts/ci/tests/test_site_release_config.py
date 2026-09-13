"""사이트가 실제로 무언가를 내려 주는지 (S15P21B202-452 · -406).

실행: python scripts/ci/tests/test_site_release_config.py
네트워크·토큰 없이 저장소의 파일을 글자로 읽는다.

**무엇을 막는 검증인가.** 2026-09-13 실측에서 **서버에는 값이 있는데 저장소는 비어
있었다**:

    서버   downloadUrl: "/builds/metagame-v1.0.0-20260911-1512.zip"
    저장소 downloadUrl: ""

`publish-site` 는 저장소를 서버로 복사한다. 그러니 다음 사이트 배포가 **다운로드
버튼을 조용히 꺼 버린다.** 사이트는 초록이고, 페이지도 열리고, 버튼만 "다운로드 준비 중"
으로 돌아간다 — 아무도 실패를 보지 못한다.

**그리고 세 곳이 같은 이름을 봐야 한다.** 런처를 내려 주는 자리는 이름이 고정이고,
그 이름을 **사이트·nginx·발행 스크립트** 셋이 각각 적고 있다. 하나만 바꾸면 404 가 되고,
그 404 는 사용자 쪽에서만 보인다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "launcher"))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

import publish_launcher as PL  # noqa: E402


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


CONFIG = read("website/release-config.js")
NGINX = read("scripts/deploy/server/nginx/metagame.conf")

print()
print("== 사이트 다운로드 ==")

# ── ① 다운로드 주소가 비어 있지 않다 ────────────────────────────────────
#
#     이것이 비면 버튼이 꺼진다. 그 상태로 배포되면 사이트는 멀쩡해 보이고
#     **받을 수만 없다.**
m = re.search(r'downloadUrl\s*:\s*"([^"]*)"', CONFIG)
assert m, "release-config.js 에서 downloadUrl 을 찾지 못했다"
url = m.group(1).strip()
assert url, ("downloadUrl 이 비어 있다 — 이대로 배포하면 다운로드 버튼이 꺼진다. "
             "2026-09-13 에 실제로 그 상태였고, 서버에만 값이 있었다")

#     상대 주소여야 한다. EC2 는 회수되므로 도메인을 박으면 그날 전부 고쳐야 한다.
assert url.startswith("/builds/"), f"다운로드 주소가 /builds/ 로 시작하지 않는다: {url!r}"

#     **토큰이 든 주소·만료 링크를 넣지 않는다** (사이트 README 규칙).
for bad in ("?token=", "private_token", "sig=", "Expires=", "X-Amz-"):
    assert bad not in url, f"다운로드 주소에 인증·만료 흔적이 있다: {bad}"
print("  다운로드 주소가 채워져 있고 상대 주소다 ✅")


# ── ② 세 곳이 같은 이름을 본다 ──────────────────────────────────────────
#
#     사이트가 가리키는 파일 · 발행 스크립트가 놓는 파일 · nginx 가 허용하는 파일.
#     하나만 어긋나면 사용자에게만 보이는 404 가 된다.
name = url.rsplit("/", 1)[-1]
assert name == PL.PUBLIC_NAME, \
    f"사이트가 가리키는 이름과 publish_launcher.PUBLIC_NAME 이 다르다: {name!r} vs {PL.PUBLIC_NAME!r}"

allow = next((l for l in NGINX.splitlines() if l.strip().startswith("location ~ ^/builds/")), "")
assert allow, "nginx 에 /builds/ 허용 목록(정규식 location)이 없다"
#     정규식이라 `.` 이 `\.` 로 이스케이프돼 있다. 역슬래시를 걷어 내고 이름만 본다.
flat = allow.replace("\\", "")
assert PL.PUBLIC_NAME in flat, \
    f"nginx 허용 목록에 {PL.PUBLIC_NAME} 이 없다 — 사이트 버튼이 404 가 된다: {allow}"
print(f"  사이트 · publish_launcher · nginx 가 모두 {PL.PUBLIC_NAME} ✅")


# ── ③ 그 밖의 것은 여전히 막혀 있다 ─────────────────────────────────────
#
#     `-465` 에서 저장소가 공개 폴더로 새어 나간 뒤 만든 방어선이다. exe 하나를
#     열면서 그것을 넓히지 않았는지 본다.
assert "[^/]+.exe" not in flat, \
    "아무 exe 나 열었다 — 이름을 그대로 적어야 한다(그 폴더에 exe 는 하나뿐이다)"
prefix = NGINX.split("location /builds/", 1)[1].split("}", 1)[0]
assert "return 404" in prefix and "alias" not in prefix, \
    "허용 목록에 안 걸린 요청이 여전히 서빙된다 — 여는 목록이 아무것도 막지 못한다"
print("  허용 목록 밖은 여전히 404 ✅")


# ── ④ 발행 스크립트가 실제로 그 자리에 놓는다 ───────────────────────────
#
#     이름만 맞고 파일을 안 놓으면 같은 404 다.
src = read("scripts/launcher/publish_launcher.py")
body = src.split("def main(", 1)[1]
assert "PUBLIC_NAME" in body, "발행 스크립트가 사람이 받는 자리에 아무것도 놓지 않는다"
assert "os.replace(" in body, \
    "원자적으로 바꾸지 않는다 — 받는 중인 사람이 반쯤 쓰인 파일을 받는다"

#     자기 갱신 경로(`files/<sha>.gz`)는 그대로 있어야 한다. 둘은 쓰임이 다르다.
assert '".gz"' in src or "'.gz'" in src, "런처 자기 갱신이 받는 자리가 사라졌다"
print("  발행 스크립트가 두 자리에 모두 놓는다 ✅")


# ── ⑤ 안내 문구가 zip 을 말하지 않는다 ──────────────────────────────────
#
#     주는 것이 런처인데 "압축을 푸세요" 라고 적혀 있으면, 받은 사람이 그 자리에서
#     멈춘다. 표시가 사실과 어긋나는 것은 이 저장소가 계속 고쳐 온 종류의 결함이다.
html = read("website/index.html")
js = read("website/release.js")
guide = html.split('id="guide"', 1)[1].split("</section>", 1)[0]
assert "압축" not in guide, "런처를 주는데 안내가 압축 해제를 말한다"
assert PL.PUBLIC_NAME in guide, f"안내에 {PL.PUBLIC_NAME} 이 없다"
assert "ZIP" not in js.split("downloadStatus", 1)[1][:200], \
    "버튼 아래 문구가 아직 ZIP 을 말한다"
print("  안내 문구가 런처를 말한다 ✅")

print("== 통과 ==")
