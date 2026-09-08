"""AI 리뷰의 판정 합치기와 실패 표시 (S15P21B202-335).

실행: python scripts/ci/tests/test_ai_review_verdict.py
네트워크·모델 없이 돈다 — `merge`·`render`·`with_fail_reasons` 는 순수 함수다.

지키려는 것
  · **리뷰어가 전부 실패해도 코멘트가 남는다.** 예전에는 그 경우 `return 1` 로 빠져나가
    아무것도 남지 않았다. `❓ 판정 불가` 경로는 다 구현돼 있었는데 그 앞에서 반환했다.
    하필 모델 이름·자격증명·CLI 경로처럼 **모든 리뷰어에 공통으로 걸리는 원인**은 항상
    전부 실패로 나타나므로, 사람이 실제로 겪는 실패가 대부분 이 조용한 쪽이었다.
  · 잡이 `allow_failure: true` 라 파이프라인도 초록이다. 그래서 "AI 코멘트가 없는 MR" 이
    ① 문서만 바뀌어 건너뛴 것과 ② 리뷰가 죽은 것을 동시에 뜻하면 안 된다.
  · **리뷰어 실패가 섞이면 ✅ 를 내지 않는다** — 일부만 성공한 판정을 "머지 가능" 으로
    올리면 안 본 부분이 통과로 보인다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import ai_review as A

ROLES = list(A.ROLES)[:2]          # 실제 역할 이름을 쓴다(각주가 label 을 찾는다)


def render_all_failed(defaults=None):
    merged = A.merge({}, ROLES)
    body = A.render("메타", ROLES, {}, {}, merged, None, 0, ROLES, defaults or {})
    return merged, A.with_fail_reasons(body, {r: "claude -p 종료 코드 1 category=unknown" for r in ROLES})


# ── 전부 실패 ────────────────────────────────────────────────────────────
merged, body = render_all_failed({"scene": ("ok", "씬·프리팹·에셋 파일 변경 없음")})

assert merged["verdict"] == "unknown", merged["verdict"]
print("  전부 실패 → 판정 unknown ✅")

assert A.VERDICT_LINE["unknown"] in body, "❓ 판정 불가 줄이 없다"
print("  코멘트에 ❓ 판정 불가 ✅")

assert "실행 실패 리뷰어" in body, "각주에 실패 리뷰어가 없다"
assert "category=unknown" in body, "실패 이유가 코멘트에 없다"
print("  실패 리뷰어 각주 + 이유 ✅")

# 판정표는 ❓ 로 채워지되, **파일 목록으로 정해지는 것은 사실대로** 남는다.
# 리뷰어가 죽어도 "씬 변경이 없다" 는 것은 diff 로 아는 사실이다.
assert body.count("| ❓ |") >= 3, body.count("| ❓ |")
assert "씬·프리팹·에셋 파일 변경 없음" in body, "확정 가능한 항목까지 ❓ 로 덮었다"
print("  판정표는 ❓ · 파일로 정해지는 항목은 사실 유지 ✅")

# ── 일부만 실패하면 ✅ 를 내지 않는다 ────────────────────────────────────
one_ok = {ROLES[0]: {"verdict": "ok", "checks": {"scope": {"status": "ok", "why": "범위 안"}},
                     "findings": [], "questions": []}}
assert A.merge(one_ok, [ROLES[1]])["verdict"] != "ok", "실패가 섞였는데 ✅ 를 냈다"
assert A.merge(one_ok, [])["verdict"] == "ok", "전부 성공인데 ✅ 가 아니다"
print("  일부 실패 → ✅ 아님 · 전부 성공 → ✅ ✅")

# ── 이유가 없으면 줄을 붙이지 않는다 ────────────────────────────────────
assert A.with_fail_reasons("본문", {}) == "본문"
print("  실패가 없으면 이유 줄 없음 ✅")

print("TEST335 OK")
