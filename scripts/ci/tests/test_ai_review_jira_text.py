"""AI 리뷰가 보는 Jira 설명 (S15P21B202-384).

실행: python scripts/ci/tests/test_ai_review_jira_text.py
네트워크 없이 돈다 — `adf_to_text` 는 순수 함수이고 `jira_text` 는 가짜 Jira 를 받는다.

지키려는 것
  · **완료 조건 절이 들어간다.** 예전에는 ADF 를 `json.dumps` 한 JSON 문자열을 3000자에서
    잘랐다. 구조 잡음이 본문보다 많아 실제로 담긴 글자는 약 660자였고(표가 있으면 더
    나쁘다 — 아래 경우가 9배를 실측한다), 우리 이슈 틀은
    `현상 → 원인 → 완료 조건 → 참고` 라서 **완료 조건이 거의 항상 잘려 나갔다.**
  · **완료 조건은 5항목 중 하나다.** 팀장은 ⚠️/❓ 만 본다는 전제로 그 표를
    쓴다. 그 판정의 입력이 조용히 잘려 있으면 ✅ 가 "확인했다" 가 아니라 "못 봤다" 를
    뜻할 수 있다. `-335`·`-354`·`-361` 과 같은 계열이다.
  · **잘렸으면 잘렸다고 적고, 안 잘렸으면 적지 않는다.** 항상 붙이면 신호가 죽는다.
    리뷰어가 그 문구를 보고 **의도적으로** ❓ 를 낼 수 있어야 한다.
  · 글자를 잃지 않는다 — 모르는 노드는 자식만 따라 내려간다.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import ai_review as A


class FakeJira:
    """`jira_text` 가 쓰는 것만 흉내낸다 — `available` 과 `get_issue`."""

    def __init__(self, description, available=True, raise_with=None):
        self.description = description
        self.available = available
        self.raise_with = raise_with

    def get_issue(self, key):
        if self.raise_with:
            raise RuntimeError(self.raise_with)
        return {"fields": {
            "summary": "[Network] 참가자가 기절한 동료를 일으킬 수 없다",
            "status": {"name": "진행 중"},
            "assignee": {"displayName": "금준영"},
            "description": self.description,
        }}


def doc(*nodes):
    return {"type": "doc", "version": 1, "content": list(nodes)}


def para(text):
    return {"type": "paragraph", "content": [{"type": "text", "text": text}]}


def heading(text, level=2):
    return {"type": "heading", "attrs": {"level": level},
            "content": [{"type": "text", "text": text}]}


def bullets(*items):
    return {"type": "bulletList", "content": [
        {"type": "listItem", "content": [para(i)]} for i in items]}


def table(rows):
    return {"type": "table", "content": [
        {"type": "tableRow", "content": [
            {"type": "tableCell", "content": [para(c)]} for c in row]}
        for row in rows]}


# ── 평문화 ───────────────────────────────────────────────────────────────
adf = doc(
    heading("현상"),
    para("호스트가 기절했는데 참가자가 일으킬 수 없었다."),
    heading("완료 조건"),
    bullets("참가자가 호스트를 일으킬 수 있다", "일어난 뒤 후보에서 빠진다"),
    table([["downed", "복제됨"]]),
    {"type": "codeBlock", "content": [{"type": "text", "text": "public bool downed;"}]},
    {"type": "rule"},
)
text = A.adf_to_text(adf)

for want in ("현상", "호스트가 기절했는데", "완료 조건",
             "참가자가 호스트를 일으킬 수 있다", "일어난 뒤 후보에서 빠진다",
             "downed", "복제됨", "public bool downed;"):
    assert want in text, f"평문에서 사라졌다: {want}\n---\n{text}"
print("  문단·제목·목록·표·코드블록이 모두 남는다 ✅")

assert "## 현상" in text and "## 완료 조건" in text, text
assert "- 참가자가 호스트를 일으킬 수 있다" in text, text
print("  제목과 목록의 모양이 남는다 (읽는 쪽이 절 구분을 안다) ✅")

assert '"type"' not in text and "listItem" not in text, "구조 잡음이 남았다"
print("  구조 잡음이 사라진다 ✅")


# ── 예전 방식이 왜 완료 조건을 잘랐나 ──────────────────────────────────────
as_json = json.dumps(adf, ensure_ascii=False)
ratio = len(as_json) / len(text)

assert ratio > 3, f"부풀림이 {ratio:.1f}배뿐이다 — 이 경우가 문제를 못 보여 준다"
print(f"  ADF JSON 은 평문의 {ratio:.1f}배 ✅  (평문 {len(text)}자 · JSON {len(as_json)}자)")

# 예전 코드 그대로: json.dumps 한 뒤 3000자. 이 문서는 짧아서 안 잘리지만,
# 같은 예산이 담는 **실제 글자**가 몇 배 적다는 것이 요점이다.
assert len(as_json[:3000]) >= len(text), "이 경우로는 손실을 설명할 수 없다"
old_effective = 3000 / ratio
assert old_effective < 1200, f"예전 예산의 실효 글자가 {old_effective:.0f}자 — 가정이 틀렸다"
print(f"  예전 3000자 예산의 실효 글자 ≈ {old_effective:.0f}자 ✅")


# ── 모르는 노드에서 글자를 잃지 않는다 ─────────────────────────────────────
weird = doc({"type": "expand", "attrs": {"title": "접힌 것"},
             "content": [para("안에 든 완료 조건")]})
assert "안에 든 완료 조건" in A.adf_to_text(weird), A.adf_to_text(weird)
print("  모르는 노드는 자식만 따라 내려간다 ✅")


# ── 죽지 않는다 ──────────────────────────────────────────────────────────
assert A.adf_to_text(None) == ""
assert A.adf_to_text(doc()) .strip() == ""
assert A.adf_to_text({"type": "doc"}).strip() == ""
print("  빈 문서·None 에서 죽지 않는다 ✅")


# ── 잘리지 않았으면 그 말을 하지 않는다 ────────────────────────────────────
out = A.jira_text(FakeJira(adf), "S15P21B202-378")

assert "잘렸다" not in out, f"안 잘렸는데 잘렸다고 한다\n{out}"
assert "전문" in out, f"전문이라고 말하지 않는다\n{out}"
assert "완료 조건" in out and "일어난 뒤 후보에서 빠진다" in out
assert "S15P21B202-378" in out and "진행 중" in out and "금준영" in out
print("  안 잘렸으면 잘렸다는 말이 없고, 전문이라고 말한다 ✅")


# ── 잘렸으면 반드시 그 말을 한다 ──────────────────────────────────────────
long_doc = doc(*([para("가" * 200)] * 100), heading("완료 조건"), para("여기까지 와야 한다"))
out = A.jira_text(FakeJira(long_doc), "S15P21B202-378")

assert "잘렸다" in out, f"잘렸는데 말하지 않는다\n{out[-400:]}"
assert "❓" in out, "리뷰어가 무엇을 해야 하는지 적어야 한다 (완료 조건을 ❓ 로)"
assert str(A.MAX_JIRA_DESC_CHARS) in out, "어디서 잘렸는지 적어야 한다"
assert "전문" not in out.split("설명(")[1].split(":")[0], "잘렸는데 전문이라고 한다"
print("  잘렸으면 어디서 잘렸는지와 무엇을 할지 적는다 ✅")

# 예산 자체는 넉넉해야 한다 — 우리 이슈가 평범하게 안 잘려야 의미가 있다.
assert A.MAX_JIRA_DESC_CHARS >= 8000, f"예산이 {A.MAX_JIRA_DESC_CHARS}자뿐이다"
print(f"  예산 {A.MAX_JIRA_DESC_CHARS}자 ✅")


# ── ADF 가 아닌 설명도 그대로 돈다 ────────────────────────────────────────
out = A.jira_text(FakeJira("## 완료 조건\n- 그냥 문자열이다"), "S15P21B202-378")
assert "그냥 문자열이다" in out, out
print("  평문 설명(REST v2·문자열 필드)도 그대로 ✅")

out = A.jira_text(FakeJira(None), "S15P21B202-378")
assert "S15P21B202-378" in out, out
print("  설명이 없어도 죽지 않는다 ✅")


# ── Jira 를 못 쓰는 경우 ─────────────────────────────────────────────────
assert A.jira_text(FakeJira(adf, available=False), "S15P21B202-378") == "(Jira 이슈 정보 없음)"
assert A.jira_text(FakeJira(adf), None) == "(Jira 이슈 정보 없음)"
assert "조회 실패" in A.jira_text(FakeJira(adf, raise_with="401"), "S15P21B202-378")
print("  Jira 를 못 쓰면 그 사실을 말한다 ✅")


print("\ntest_ai_review_jira_text: 통과")
