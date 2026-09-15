"""보호 브랜치(develop)가 빨갈 때의 알림 (S15P21B202-344).

실행: python scripts/ci/tests/test_notify_branch_red.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

왜 이 테스트가 있는가 — 2026-09-08 14:11 에 `develop` 의 `unity-compile` 이 두 커밋
연속 실패했는데 **Mattermost 카드가 0건**이었다. 알림 잡이 `merge_request_event` 로
한정돼 있어 push 파이프라인에서는 아예 만들어지지 않았고, `needs` 에 `unity-compile`
이 없어 MR 에서도 그 잡만 실패하면 `on_failure` 조건을 못 만났다.

MR 실패와 성격이 다르다: `develop` 이 빨간 순간부터 **모든 사람의 다음 브랜치가 빨간
기준선 위에서 시작한다.** 그래서 카드가 그 사실을 말해야 하고, 풀렸을 때도 알려야 한다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({
    "CI_PROJECT_ID": "1",
    "CI_PROJECT_URL": "https://lab/g/p",
    "GITLAB_TOKEN": "x",
    "CI_PIPELINE_ID": "500",
    "CI_PIPELINE_URL": "https://lab/g/p/-/pipelines/500",
    "CI_COMMIT_BRANCH": "develop",
    "CI_COMMIT_SHA": "abcdef1234567890",
    "CI_COMMIT_SHORT_SHA": "abcdef12",
    "CI_COMMIT_TITLE": "Merge branch 'x' into 'develop'",
    "GITLAB_USER_LOGIN": "rmawnsdud",
    "MM_WEBHOOK_URL": "https://mm.example/h",
    # **직접 정해야 한다** (S15P21B202-533). 안 정하면 `ci-tests` 잡 자신의
    # `CI_PIPELINE_SOURCE=merge_request_event` 가 새어 들어온다 — 로컬에서는 그 변수가
    # 없어 통과하고 CI 에서만 깨진다. 실제로 그렇게 깨졌다.
    "CI_PIPELINE_SOURCE": "push",
})
os.environ.pop("CI_MERGE_REQUEST_IID", None)

import ci_common, mm_notify as N

TRACE = "  ❌ 새로 생긴 실패 2건\n  ❌ 컴파일 오류 1건\n"


def fake(pipelines):
    sent = []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            sent.append(body["text"]); return 200, {}
        if "/pipelines/500/jobs" in url:
            return 200, [{"id": 7, "name": "unity-compile", "status": "failed",
                          "allow_failure": False, "web_url": "https://lab/j/7"}]
        if url.endswith("/jobs/7/trace"):
            return 200, TRACE
        if "/pipelines?ref=develop" in url:
            return 200, pipelines
        return 200, {}

    return sent, http


# ── ① develop 이 빨가면 카드가 나가고, 팀 전체 영향을 말한다
sent, http = fake([])
ci_common.http = http
N.http = http
rc = N.branch_failed(ci_common.GitLab())
assert rc == 0, rc
assert len(sent) == 1, sent
card = sent[0]
assert "develop 가 빨갛다" in card, card
assert "unity-compile" in card, card
assert "빨간 기준선 위에서 시작한다" in card, "팀 전체 영향을 말해야 한다: " + card
assert "abcdef12" in card, card
print("  develop 빨강 → 카드 + 팀 영향 문구 ✅")

# ── ② 실패 이유(❌ 줄)를 싣는다 — 사람이 잡 로그를 찾아 들어가지 않게
assert "새로 생긴 실패 2건" in card, card
assert "<br>" not in card, "MM 표 셀은 <br> 을 렌더하지 않는다: " + card
print("  실패 이유를 카드에 싣는다 ✅")

# ── ③ MR 이 없으므로 **밀어 넣은 사람 + Infra** 를 부른다
#     원인이 두 MR 의 조합이면 저자가 하나로 정해지지 않는다 — 되돌릴 수 있는 사람을 부른다
row = [l for l in card.split("\n") if l.startswith("| 담당")][0]
assert "@" in row, row
print("  담당 멘션 있음 ✅")

# ── ④ main() 이 iid 없이도 이 경로를 태운다 (예전에는 "MR 파이프라인이 아님" 으로 나갔다)
sent2, http2 = fake([])
ci_common.http = http2
N.http = http2
sys.argv = ["mm_notify.py", "ci-failed"]
rc = N.main()
assert rc == 0, rc
assert len(sent2) == 1, "iid 가 없어도 브랜치 경로를 타야 한다"
print("  main 라우팅 (iid 없음 → 브랜치 경로) ✅")

# ── ⑤ 초록으로 돌아오면 알린다 — **직전이 실패였을 때만**
sent3, http3 = fake([{"id": 500, "status": "success", "source": "push", "web_url": "https://lab/p/500"},
                     {"id": 499, "status": "failed", "source": "push", "web_url": "https://lab/p/499"}])
ci_common.http = http3
N.http = http3
rc = N.branch_resolved(ci_common.GitLab())
assert rc == 0, rc
assert len(sent3) == 1, sent3
assert "초록으로 돌아왔다" in sent3[0], sent3[0]
assert "브랜치를 따도 된다" in sent3[0], sent3[0]
assert "https://lab/p/499" in sent3[0], "직전 실패 파이프라인을 링크해야 한다: " + sent3[0]
print("  초록 복귀 → 카드 (직전이 실패였음) ✅")

# ── ⑥ 직전이 이미 초록이면 **보내지 않는다** — 매 커밋마다 카드가 나가면 채널이 시끄럽다
sent4, http4 = fake([{"id": 500, "status": "success", "source": "push", "web_url": "https://lab/p/500"},
                     {"id": 499, "status": "success", "source": "push", "web_url": "https://lab/p/499"}])
ci_common.http = http4
N.http = http4
rc = N.branch_resolved(ci_common.GitLab())
assert rc == 0, rc
assert len(sent4) == 0, "직전이 초록이면 알릴 것이 없다: " + str(sent4)
print("  직전이 초록 → 보내지 않는다 ✅")

# ── ⑦ running·canceled 는 판단 근거가 아니다 — 그 뒤의 결론난 파이프라인을 본다
sent5, http5 = fake([{"id": 500, "status": "success", "source": "push", "web_url": "https://lab/p/500"},
                     {"id": 499, "status": "running", "source": "push", "web_url": "https://lab/p/499"},
                     {"id": 498, "status": "failed", "source": "push", "web_url": "https://lab/p/498"}])
ci_common.http = http5
N.http = http5
rc = N.branch_resolved(ci_common.GitLab())
assert len(sent5) == 1, "running 을 건너뛰고 498(실패)을 봐야 한다: " + str(sent5)
assert "https://lab/p/498" in sent5[0], sent5[0]
print("  running 은 건너뛰고 결론난 것을 본다 ✅")

# ── ⑧ 직전을 못 찾으면 조용히 넘어간다 (거짓 ✅ 를 만들지 않는다)
sent6, http6 = fake([{"id": 500, "status": "success", "source": "push", "web_url": "https://lab/p/500"}])
ci_common.http = http6
N.http = http6
rc = N.branch_resolved(ci_common.GitLab())
assert rc == 0 and len(sent6) == 0, sent6
print("  직전 없음 → 조용히 넘어간다 ✅")

# ── ⑨ **종류가 다른 파이프라인은 견줄 대상이 아니다** (S15P21B202-533)
# 2026-09-15 에 실제로 이렇게 됐다: 04:03 새벽 배포 실패를 직전으로 보고, 08:31 머지
# 푸시가 ✅ 회복 카드를 보냈다. 그 푸시는 `scripts/ci/**` 만 바꿔 실질 잡이 하나뿐이라
# **아무것도 검사하지 않고** "이제 브랜치를 따도 된다" 고 말한 것이다.
sent7, http7 = fake([{"id": 500, "status": "success", "source": "push", "web_url": "https://lab/p/500"},
                     {"id": 499, "status": "failed", "source": "schedule", "web_url": "https://lab/p/499"}])
ci_common.http = http7
N.http = http7
rc = N.branch_resolved(ci_common.GitLab())
assert rc == 0 and len(sent7) == 0, ("예약 실행의 실패를 머지 푸시가 해소했다고 말하면 안 된다: "
                                     + str(sent7))
print("  종류가 다르면 견주지 않는다 ✅")

print("TEST344 OK")
