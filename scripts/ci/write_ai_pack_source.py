"""AI 추가팩의 다운로드 주소를 빌드에 심는다 (S15P21B202-690).

실행: python3 scripts/ci/write_ai_pack_source.py [--required]

`AI_PACK_MANIFEST_URL` 과 `AI_PACK_MANIFEST_SHA256` 이 **둘 다** 있을 때만
`METAGAME/Assets/StreamingAssets/ai-pack-source.json` 을 **덮어쓴다.** 없으면 아무것도
쓰지 않고 그 사실을 말한다 — 게임은 그 경우 "다운로드 배포 주소가 아직 설정되지
않았어요" 로 멈추고 나머지는 정상 동작한다(`AiPackInstaller.Begin`).

**`--required` 는 그 경우에 실패한다** (S15P21B202-709). 릴리스에 쓴다.

개발 빌드는 주소가 없어도 쓸모가 있다 — AI 말고 다른 것을 시험한다. 그런데 **릴리스는
다르다.** 주소 없이 나간 릴리스는 사용자가 "AI 동료 추가" 를 눌러도 **영영 받을 수 없는
빌드**이고, 그때 화면에 뜨는 문구는 *변수를 안 넣은 경우와 글자 하나까지 같다.* 즉
**내보낸 뒤에는 원인을 화면으로 가릴 수 없다.** 그래서 릴리스는 여기서 멈춘다.

**왜 저장소 파일을 그대로 쓰지 않나.** 저장소의 `ai-pack-source.json` 은 **값이 빈
껍데기**다(`manifest_url` · `manifest_sha256` 이 `""`). 배포 주소와 해시는 팩을 올리는
쪽이 정하는 값이고 팩 버전마다 달라지므로, 저장소에 박아 두면 **팩을 다시 올릴 때마다
게임 코드를 고쳐 커밋해야 한다.** 그래서 값은 CI 변수에 두고 빌드 때 덮어쓴다 —
`SPACE_BUILD_ID` 와 같은 자리, 같은 이유다.

**값이 틀리면 여기서 실패한다.** 게임 쪽 `AiPackValidation.Source` 와 같은 조건을
본다 — https 이고, 사용자정보·질의·조각이 없고, 해시가 64자리 16진수. 이걸 안 보면
**설치 버튼이 조용히 안 먹는 빌드**가 나가고, QA 는 그것이 주소 오타인지 기능 결함인지
구별할 수 없다.

`assert` 를 쓰지 않는다 (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import json
import os
import sys
from urllib.parse import urlsplit

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
TARGET = os.path.join(ROOT, "METAGAME", "Assets", "StreamingAssets", "ai-pack-source.json")

#: 게임 쪽 `AiPackValidation.HashString` 과 같은 조건.
HEX = "0123456789abcdefABCDEF"


def valid_hash(value):
    return isinstance(value, str) and len(value) == 64 and all(c in HEX for c in value)


def valid_url(value):
    """게임 쪽 `AiPackValidation.Source` 와 같은 조건.

    서명 URL 을 쓸 수 없다 — 질의가 비어 있어야 하므로. 그래서 이 주소는 무인증
    GET 이고, 그 사실이 여기 적혀 있어야 다음 사람이 토큰을 붙이려다 헤매지 않는다.
    """
    if not isinstance(value, str) or not value:
        return False
    parts = urlsplit(value)
    return (parts.scheme == "https" and bool(parts.netloc)
            and not parts.username and not parts.password
            and not parts.query and not parts.fragment)


def main(argv=None):
    required = "--required" in (argv if argv is not None else sys.argv[1:])
    url = (os.environ.get("AI_PACK_MANIFEST_URL") or "").strip()
    digest = (os.environ.get("AI_PACK_MANIFEST_SHA256") or "").strip()

    if not url and not digest:
        if required:
            # 릴리스는 여기서 멈춘다. 이대로 내보내면 사용자가 "AI 동료 추가" 를 눌러도
            # 영영 받을 수 없고, 그 화면은 변수를 안 넣은 경우와 글자 하나까지 같다.
            print("❌ 릴리스 빌드에 AI 추가팩 주소가 없다 —"
                  " AI_PACK_MANIFEST_URL · AI_PACK_MANIFEST_SHA256 을 CI 변수에 넣어야 한다")
            print("   (이대로 내보내면 게임 안에서 추가팩을 받을 길이 없는 빌드가 된다)")
            return 1
        # **조용히 넘기지 않는다.** 이 줄이 없으면 "AI 설치가 안 된다" 의 원인이
        # 변수 미설정인지 기능 결함인지 빌드 로그로 가릴 수 없다.
        print("AI 추가팩 주소가 설정되지 않았다 — ai-pack-source.json 을 쓰지 않는다.")
        print("  (게임은 '다운로드 배포 주소가 아직 설정되지 않았어요' 로 멈추고 나머지는 정상이다)")
        return 0

    if not url or not digest:
        print("❌ AI_PACK_MANIFEST_URL 과 AI_PACK_MANIFEST_SHA256 은 둘 다 있어야 한다"
              f" (url={'있음' if url else '없음'} · sha256={'있음' if digest else '없음'})")
        return 1

    if not valid_url(url):
        # 값 자체를 찍지 않는다 — 주소는 비밀이 아니지만, 로그에 값을 흘리는 습관을
        # 만들지 않는다(같은 자리에 비밀이 오는 날이 온다).
        print("❌ AI_PACK_MANIFEST_URL 이 게임의 검증을 통과하지 못한다 —"
              " https 여야 하고 사용자정보·질의·조각이 없어야 한다")
        print("   (설치기가 서명 URL 을 지원하지 않는다: AiPackValidation.Source)")
        return 1

    if not valid_hash(digest):
        print("❌ AI_PACK_MANIFEST_SHA256 이 64자리 16진수가 아니다")
        return 1

    os.makedirs(os.path.dirname(TARGET), exist_ok=True)
    body = json.dumps({"manifest_url": url, "manifest_sha256": digest}, indent=2) + "\n"
    with io.open(TARGET, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)

    # 주소의 호스트와 경로만 적는다. 해시는 앞 8자리만 — 어느 팩인지 가리는 데는
    # 충분하고, 로그를 옮길 때 값 전체를 흘리지 않는다.
    parts = urlsplit(url)
    print(f"ai-pack-source.json 을 썼다 — {parts.netloc}{parts.path} · sha256 {digest[:8]}…")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
