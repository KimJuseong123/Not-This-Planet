"""AI 리뷰가 ⛔ 로 본 MR 은 여기서 막는다 (S15P21B202-341).

실행: python scripts/ci/ai_review_gate.py     # 종료코드 1 = 차단

**왜 별 잡인가.** `ai-review` 는 `allow_failure: true` 그대로 남긴다. 리뷰를 *돌리는* 것과
그 결과로 *막는* 것은 실패 이유가 다르다 — 모델 호출이 죽는 것과 판정이 ⛔ 인 것을 한
잡에 섞으면 "인프라 사고" 와 "코드에 문제" 가 같은 빨간불로 보인다. 그리고 게이트를
되돌리는 일이 잡 하나 지우기로 끝나야 한다.

**⛔ 만 막는다.** 2026-09-08 전수 조사에서 판정이 달린 MR 188건 중 ⚠️ 가 137건(73%)이었다.
⚠️ 를 막으면 아무것도 머지되지 않는다. ⛔ 는 34건(18%)이었고 **그 34건 전부 리뷰 후 코드가
한 줄도 바뀌지 않은 채 끝났다**(리뷰 코멘트의 커밋 sha 와 MR 최종 sha 가 34/34 동일). 그중
`!238` 은 실제 버그였고 그 자리가 이후 `-315`·`-328` 에서 다시 문제가 됐다. 즉 18% 를 새로
막는 것이 아니라 **원래 막혀야 했던 18% 가 그냥 지나간 것**이다.

**판정을 못 읽는 경우는 통과시킨다(fail-open).** 코멘트가 없거나, ❓ 이거나, 코멘트가 이
커밋의 것이 아니면 막지 않고 **크게 알린다**. fail-closed 로 하면 러너·모델·자격증명 사고
하나가 팀의 모든 MR 을 멈춘다 — `-335` 가 고친 "전부 실패" 는 바로 그 공통 원인들이었다.
대신 조용히 꺼지지 않게 잡 로그와 MM 카드에 남긴다. 이 fail-open 이 우회로가 되지는
않는다: 고쳐서 push 하면 리뷰가 다시 돌아 새 판정이 붙고, 살아 있는 리뷰가 ⛔ 를 내면 막힌다.

**릴리즈 MR(→ master)은 건너뛴다.** `ai_review.py` 가 같은 조건에서 리뷰를 생략하므로
코멘트가 아예 없고, 그러면 게이트가 "판정을 못 읽었다" 로 떨어져 **릴리스마다 헛된 🟠
카드**를 보낸다. 헛된 카드가 반복되면 사람이 카드를 무시하게 되어 fail-open 경고 자체가
무력해진다. 릴리스 내용은 develop 에 들어올 때 이미 리뷰됐다.

**탈출구를 두지 않는다.** 게이트를 끄는 변수는 없다. `ai-review` 만 다시 돌리는 길도 열지
않는다 — 그 우회가 있으면 수정 과정에서 `repo-hygiene`·`mr-lint` 를 새로 위반해도 안 걸린다.
리뷰가 틀렸다고 판단되면 **MR 본문에 근거를 적는다.** 다음 리뷰의 입력에 그 본문이 들어간다.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ai_review as AR                      # noqa: E402  마커·판정 문구를 한 곳에서 가져온다
import mm_notify as MM                      # noqa: E402
from ci_common import GitLab, env, is_release_mr   # noqa: E402

# 판정 문구 → 판정 키. `ai_review.VERDICT_LINE` 을 뒤집어 쓴다 — 문구가 바뀌면 여기도 같이
# 바뀌어야 하는데, 상수를 복사해 두면 조용히 어긋나 게이트가 모든 것을 ❓ 로 보게 된다.
_BY_LINE = {line: key for key, line in AR.VERDICT_LINE.items()}

BLOCKING = ("block",)


def verdict_of(body: str) -> str | None:
    """리뷰 코멘트 본문에서 판정 키를 뽑는다. 못 읽으면 None.

    첫 줄의 판정 문구로만 판단한다 — 본문 뒷부분에는 `<details>` 안 리뷰어별 판정과
    사람이 쓴 인용이 섞여 있어 문자열을 아무 데서나 찾으면 엉뚱한 것을 집는다.
    """
    for raw in body.splitlines():
        line = raw.strip()
        if not line.startswith("🤖"):
            continue
        for phrase, key in _BY_LINE.items():
            if phrase in line:
                return key
        return None                          # 요약 줄은 찾았는데 판정 문구가 없다
    return None


def commit_of(body: str) -> str | None:
    """리뷰 코멘트가 어느 커밋을 본 것인지. `ai_review` 가 요약 줄에 `커밋 \\`abc1234\\`` 로 싣는다."""
    for raw in body.splitlines():
        line = raw.strip()
        if not line.startswith("🤖") or "커밋 `" not in line:
            continue
        tail = line.split("커밋 `", 1)[1]
        return tail.split("`", 1)[0].strip() or None
    return None


def find_review(notes: list[dict]) -> str | None:
    """이 MR 의 AI 리뷰 코멘트 본문. `upsert_note` 가 마커로 한 개만 유지한다."""
    for n in notes:
        body = n.get("body") or ""
        if AR.MARKER in body:
            return body
    return None


def warn(reason: str, detail: str) -> int:
    """막지 않되 조용히 넘기지 않는다. 종료코드는 0."""
    print(f"⚠ 게이트가 판정을 확인하지 못했다 — {reason}")
    print(f"   {detail}")
    print("   막지 않는다(fail-open). 리뷰가 살아 있으면 다음 push 에서 판정이 붙는다.")
    url = env("MM_WEBHOOK_URL")
    if not url:
        return 0
    try:
        MM.send(MM.card("🟠 **AI 리뷰 게이트가 판정을 못 읽었다**", [
            ("MR", env("CI_MERGE_REQUEST_TITLE") or env("CI_MERGE_REQUEST_IID") or "-"),
            ("링크", env("CI_MERGE_REQUEST_PROJECT_URL", "") + "/-/merge_requests/"
                    + (env("CI_MERGE_REQUEST_IID") or "")),
            ("이유", reason),
            ("조치", "AI 리뷰 잡 로그를 확인한다. 막지 않았으므로 머지 자체는 열려 있다"),
        ]))
    except Exception as e:                   # 알림 실패가 게이트 판정을 바꾸면 안 된다
        print(f"   (MM 전송 실패: {type(e).__name__})")
    return 0


def main() -> int:
    gl = GitLab()
    mr = gl.get_mr() or {}
    if is_release_mr(mr.get("source_branch") or "", mr.get("target_branch") or ""):
        # `ai_review.py` 가 같은 조건에서 리뷰를 생략한다 — develop 에 들어올 때 이미 리뷰된
        # 내용이고 비교 기준이 master 라 오탐만 나온다. 그러면 코멘트가 없으니 게이트는
        # `warn()` 으로 떨어져 **릴리스마다 헛된 🟠 카드를 보낸다.** 헛된 카드가 반복되면
        # 사람이 카드를 무시하게 되고, 그것이 이 게이트의 fail-open 경고를 무력화한다.
        print("릴리즈 MR(→ master) — AI 리뷰를 생략하는 MR 이라 게이트도 건너뛴다")
        return 0

    body = find_review(gl.notes())
    if body is None:
        # `-335` 이후 리뷰는 전부 실패해도 ❓ 코멘트를 남긴다. 그래도 없다면 코멘트 게시
        # 자체가 실패했거나(GitLab 4xx/5xx) 잡이 아예 돌지 않은 것이다.
        return warn("리뷰 코멘트가 없다", "ai-review 잡이 돌지 않았거나 코멘트 게시가 실패했다")

    verdict = verdict_of(body)
    if verdict is None:
        return warn("코멘트에서 판정 문구를 찾지 못했다", f"첫 줄: {body.splitlines()[0][:120] if body else ''}")

    head = env("CI_COMMIT_SHORT_SHA")
    seen = commit_of(body)
    if head and seen and not (head.startswith(seen) or seen.startswith(head)):
        # 지난 push 의 코멘트가 남아 있는 상태다. 낡은 ⛔ 로 고쳐진 코드를 막아서도,
        # 낡은 ✅ 로 새 코드를 통과시켜서도 안 된다.
        return warn("코멘트가 이 커밋의 것이 아니다", f"코멘트 `{seen}` · 지금 `{head}`")

    if verdict in BLOCKING:
        # 실패 이유는 `❌` 줄로 남긴다 — `mm_notify.failure_reasons()` 가 잡 로그에서 이 줄을
        # 뽑아 🔴 카드와 MR 코멘트에 싣는다. 여기가 비면 "로그에서 이유를 찾지 못함" 이 된다.
        print(f"❌ AI 리뷰가 ⛔ 보류 권고로 판정했다 — MR 의 `🤖 AI 리뷰 요약` 코멘트에서 "
              f"'팀장이 확인할 것' 과 발견 사항을 보고 고친 뒤 다시 push 한다. "
              f"리뷰가 틀렸다고 판단되면 MR 본문에 근거를 적는다(다음 리뷰가 그 본문을 읽는다).")
        return 1

    print(f"게이트 통과 — 판정 {AR.STATUS_ICON.get(verdict, verdict)} ({verdict}) · 커밋 {seen or '-'}")
    if verdict == "unknown":
        return warn("판정이 ❓ 다", "리뷰어가 실패했거나 출력을 해석하지 못했다. ⛔ 가 아니므로 막지 않는다")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
