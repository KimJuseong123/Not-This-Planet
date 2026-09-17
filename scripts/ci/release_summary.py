"""릴리스마다 사용자용 요약 **초안**을 만든다 (S15P21B202-462).

실행:
    python scripts/ci/release_summary.py <버전> [--prev <이전태그>] [--out release-summary.md]

**왜 필요한가.** 지금 릴리스 노트는 이렇게 시작한다.

    - !350 `S15P21B202-440-release-gate-baseline` · S15P21B202-440
    - !352 `S15P21B202-442-character` · S15P21B202-442
    - … 287줄

**개발자용이다.** 플레이어는 "이번에 뭐가 좋아졌나" 를 알 수 없고 브랜치 이름과 번호만 본다.

**재료는 이미 있다.** 릴리스 잡이 머지 커밋에서 MR 번호·브랜치·이슈 키를 뽑는다
(`publish_release.changelog`). 사람이 읽을 문장은 **Jira 이슈 요약**이고 — 우리 규약이
`[파트] 무엇을 했는지` 로 쓰게 하므로 이미 읽을 수 있다 — 그것을 묶기만 하면 된다.

**공개하지 않는다.** 이 스크립트는 **초안 파일 하나**를 만들 뿐이다. AI 가 쓴 문장이
그대로 사용자에게 나가면 이상한 한 줄이 게임의 첫인상이 된다. 사람이 보고 고친 뒤에
사이트·Release·Jira 에 싣는다 — `release-prod`(공개 버튼)와 같은 결이다.
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.stdout.reconfigure(encoding="utf-8")

import ai_review as AI          # noqa: E402  CLI 호출 경로를 그대로 쓴다
import ci_common as C           # noqa: E402
import publish_release as R     # noqa: E402

OUT_DEFAULT = "release-summary.md"

# 한 번에 물어볼 이슈 수. 첫 릴리스는 287건이었지만 그다음부터는 `v1.0.0..HEAD` 라 훨씬 적다.
MAX_ITEMS = int(C.env("RELEASE_SUMMARY_MAX_ITEMS", "400"))

# **사이트 문구에 이슈 번호가 섞이면 안 된다.** 플레이어에게 `S15P21B202-440` 은 뜻이 없다.
KEY_IN_TEXT = re.compile(r"\bS15P21B202[- ]?\d+\b|\B!\d{2,}\b")

SYSTEM = """당신은 게임 릴리스 노트를 쓰는 담당입니다. 한국어.
받는 것은 이번 릴리스에 들어간 작업 목록(파트 태그가 붙은 이슈 요약)입니다.
**번역이 아니라 선별입니다.** 대부분은 플레이어가 체감하지 못하는 내부 작업입니다 —
CI·테스트·문서·리팩터·빌드 설정은 **전부 빼세요.**

출력은 아래 JSON 하나만 (설명·마크다운 금지):
{"site": ["플레이어가 읽을 한 줄", "..."],
 "release": "팀이 읽을 한 단락(2~4문장)",
 "jira": "팀·평가가 읽을 한 단락(2~4문장)"}

site 규칙 — 이것이 가장 중요합니다:
 · **3~6줄.** 넘지 마세요
 · **이슈 번호·MR 번호·브랜치 이름을 쓰지 마세요.** 플레이어에게 뜻이 없습니다
 · 기능이 늘었으면 무엇을 할 수 있게 됐는지, 고쳤으면 무엇이 더 이상 안 일어나는지
 · 내부 용어(리팩터·리베이스·픽스처·파이프라인) 금지
 · 없는 것을 지어내지 마세요. 목록에 없으면 쓰지 않습니다"""


def keys_of(entries: list[dict]) -> list[str]:
    seen, out = set(), []
    for e in entries:
        k = e.get("key")
        if k and k not in seen:
            seen.add(k)
            out.append(k)
    return out


def summaries(keys: list[str]) -> tuple[list[str], list[str]]:
    """이슈 키 → `[파트] 요약` 목록. 돌려주는 것: (읽은 줄, 못 읽은 키).

    **못 읽은 것을 조용히 버리지 않는다.** Jira 가 안 되면 요약이 반쪽이 되는데,
    그것을 모른 채 사람이 그대로 올리면 빠진 것이 영영 안 보인다.
    """
    if not keys:
        return [], []
    jira = C.Jira()
    # **속성이지 함수가 아니다** (S15P21B202-579). `()` 를 붙이면 그 줄에서
    # `TypeError: 'bool' object is not callable` 이 난다. 저장소의 다른 다섯 곳
    # (`ai_review`·`mm_digest`×2·`mr_lint`·`mr_title`)은 전부 속성으로 읽는다.
    if not jira.available:
        return [], list(keys)

    found: dict[str, str] = {}
    for i in range(0, len(keys), 50):
        chunk = keys[i:i + 50]
        jql = "key in (%s)" % ", ".join(chunk)
        issues, _ = jira.search_all(jql, ["summary"], page_size=50, max_pages=3)
        for it in issues:
            s = ((it.get("fields") or {}).get("summary") or "").strip()
            if s:
                found[it.get("key", "")] = s
    return [found[k] for k in keys if k in found], [k for k in keys if k not in found]


def build_prompt(version: str, prev: str | None, lines: list[str], missing: list[str]) -> str:
    body = [f"버전: {version}", f"이전 릴리스: {prev or '없음(첫 릴리스)'}",
            f"작업 {len(lines)}건", ""]
    body += [f"- {s}" for s in lines[:MAX_ITEMS]]
    if len(lines) > MAX_ITEMS:
        body.append(f"- … 외 {len(lines) - MAX_ITEMS}건(생략)")
    if missing:
        body += ["", f"(요약을 읽지 못한 이슈 {len(missing)}건은 목록에 없습니다)"]
    return "\n".join(body)


def parse(raw: str) -> dict | None:
    """모델 출력 → dict. 못 읽으면 None.

    코드펜스로 감싸 주는 경우가 흔해서 **한 번은 벗겨 본다.** 그래도 안 되면 포기한다 —
    억지로 맞추면 무엇이 들어갔는지 아무도 모르게 된다.
    """
    t = (raw or "").strip()
    if t.startswith("```"):
        t = re.sub(r"^```[a-zA-Z]*\n?", "", t)
        t = re.sub(r"\n?```\s*$", "", t)
    try:
        d = json.loads(t)
    except ValueError:
        return None
    if not isinstance(d, dict):
        return None
    site = d.get("site")
    if isinstance(site, str):
        site = [x.strip("-• ").strip() for x in site.split("\n") if x.strip()]
    d["site"] = [str(x).strip() for x in (site or []) if str(x).strip()]
    d["release"] = str(d.get("release") or "").strip()
    d["jira"] = str(d.get("jira") or "").strip()
    return d


def leaks(site: list[str]) -> list[str]:
    """사이트 문구에 남은 이슈·MR 번호. 있으면 사람이 지우도록 눈에 띄게 알린다."""
    return [s for s in site if KEY_IN_TEXT.search(s)]


def render(version: str, prev: str | None, d: dict, count: int,
           missing: list[str], leaked: list[str]) -> str:
    out = [f"# {version} 사용자용 요약 — **초안**", "",
           "**그대로 올리지 마세요.** AI 가 쓴 문장이라 사람이 보고 고친 뒤에 싣습니다.",
           f"재료: 이슈 요약 {count}건 (이전 릴리스 {prev or '없음'})", ""]

    if leaked:
        out += ["> ⚠️ **사이트 문구에 이슈·MR 번호가 남아 있습니다** — 지우고 올리세요:", ""]
        out += [f"> - {s}" for s in leaked] + [""]
    if missing:
        out += [f"> ⚠️ Jira 요약을 읽지 못한 이슈 {len(missing)}건이 빠졌습니다: "
                + ", ".join(missing[:10]) + (" …" if len(missing) > 10 else ""), ""]

    out += ["## 사이트 `/play/` — 플레이어가 읽는다", ""]
    out += [f"- {s}" for s in d["site"]] or ["(없음)"]
    out += ["", "## GitLab Release 맨 앞 — 팀이 읽는다", "", d["release"] or "(없음)",
            "", "## Jira 릴리스 — 팀·평가가 읽는다", "", d["jira"] or "(없음)", ""]
    return "\n".join(out)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="릴리스 사용자용 요약 초안")
    ap.add_argument("version")
    ap.add_argument("--prev", default=None, help="이전 릴리스 태그 (없으면 자동)")
    ap.add_argument("--out", default=OUT_DEFAULT)
    a = ap.parse_args(argv)

    prev = a.prev if a.prev is not None else R.previous_tag(a.version)
    entries, _ = R.changelog(prev)
    keys = keys_of(entries)
    lines, missing = summaries(keys)

    print(f"=== 요약 초안 {a.version} ===")
    print(f"머지 {len(entries)}건 · 이슈 {len(keys)}건 · 요약을 읽은 것 {len(lines)}건")
    if missing:
        print(f"⚠ 요약을 읽지 못한 이슈 {len(missing)}건 — 초안에 표시한다")

    if not lines:
        # **빈 초안을 만들지 않는다.** 빈 파일이 아티팩트로 남으면 사람이 "요약이 없구나"
        # 로 읽고 넘어간다. 재료가 없으면 그렇다고 말하고 끝낸다.
        print("❌ 재료가 없다 — Jira 요약을 하나도 읽지 못했다(토큰·네트워크 확인)")
        return 1

    # **백엔드를 고른다** (S15P21B202-579). 예전에는 `call_cli` 를 바로 불렀다. 그래서
    # ① `ANTHROPIC_API_KEY` 가 있어도 API 로 못 갔고 ② `claude` 가 없는 이미지에서는
    # 그대로 죽었다 — 이 잡이 Unity 이미지 안에 있던 동안 초안이 한 번도 안 나온 이유다.
    # `ai-review` 와 **같은 고르기**를 쓴다: 두 곳이 갈리면 한쪽만 조용히 안 돈다.
    backend = AI.pick_backend()
    if not backend:
        print("❌ AI 백엔드가 없다 — ANTHROPIC_API_KEY 도, 러너의 claude CLI 도 없다")
        return 1
    model = C.env("AI_REVIEW_MODEL", AI.DEFAULT_MODEL)
    print(f"백엔드={backend} 모델={model}" + (f" claude={AI.find_claude()}" if backend == "cli" else ""))

    try:
        raw = AI.call(backend, model, SYSTEM, build_prompt(a.version, prev, lines, missing))
    except RuntimeError as e:
        # **스택 대신 한 줄로 말한다.** 이 잡은 릴리스를 막지 않으므로(allow_failure)
        # 사람이 읽는 것은 로그 한 줄뿐이다 — 거기서 원인이 보여야 한다.
        print(f"❌ 모델 호출 실패 — {e}")
        return 1

    d = parse(raw)
    if d is None:
        print("❌ 모델 출력을 읽지 못했다 — 초안을 만들지 않는다")
        return 1

    leaked = leaks(d["site"])
    if leaked:
        print(f"⚠ 사이트 문구 {len(leaked)}줄에 이슈·MR 번호가 남아 있다 — 초안에 표시한다")

    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(render(a.version, prev, d, len(lines), missing, leaked))
    print(f"초안 → {a.out} (사이트 {len(d['site'])}줄)")
    print("**공개하지 않는다** — 사람이 고친 뒤 사이트·Release·Jira 에 싣는다")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
