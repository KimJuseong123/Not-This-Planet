"""재대상된 자식 MR 에 파이프라인을 만든다 — 결함 ① (S15P21B202-295).

스택 MR 의 부모가 develop 에 머지되면 GitLab 이 소스 브랜치를 지우고(`remove_source_branch_after_merge`)
자식 MR 의 대상을 develop 으로 **자동 재대상**한다. 그런데 **새 파이프라인을 만들지 않는다.**

실측(2026-09-02 ~ 09-04, 재대상이 일어난 MR 16건):

| | 건수 |
|---|---|
| 재대상 뒤 파이프라인 **안 생김** | **14** |
| 생김 | 2 — `!204`(우리가 API 로 재대상), `!210`(직후 커밋 push) |

즉 **브랜치 삭제로 인한 자동 재대상만** 파이프라인을 안 만든다. API 재대상은 같은 초에 만든다.
GitLab #381622 · #559008.

**왜 문제인가**: 자식에 남아 있는 마지막 파이프라인은 **부모 브랜치 위에서 돈 결과**다. develop
기준으로는 한 번도 검사받지 않았는데 초록으로 보이고, 팀장은 그 초록을 믿고 머지한다.
`Pipelines must succeed` 를 켜기 전에 이걸 고쳐야 한다 — 안 그러면 그 규칙이 거짓 초록 위에서 돈다.

**판정**: 재대상은 MR 의 시스템 노트로 남는다(`... now targets the \\`develop\\` branch`).
그래서 규칙이 한 줄이다 — **가장 최근 재대상 노트가 마지막 파이프라인보다 나중이면 만든다.**
파이프라인을 만들면 그것이 노트보다 나중이 되므로 다시 만들지 않는다(멱등).

**왜 다른 방법이 아닌가**
  - *현재 sha 에 파이프라인이 있는지* 로 판정 → 있다. 부모 위에서 돈 것이. 재대상은 sha 를 바꾸지 않는다
  - *부모 MR 에서 자식을 찾아* 트리거 → 재대상이 끝나면 자식이 부모를 가리키던 흔적이 MR 객체에 없다

실행: python scripts/ci/retarget_pipeline.py
언제: develop push 파이프라인(재대상이 일어나는 시점) · 매일 스케줄(안전망)
"""
import sys

from ci_common import GitLab, Report, banner, env

# GitLab 시스템 노트 문구. 영문 고정이다(UI 언어와 무관하게 API 는 영어로 준다 — 2026-09-04 실측:
# "deleted the `S15P21B202-281-...` branch. This merge request now targets the `develop` branch").
RETARGET_MARK = "now targets the"
MAX_MRS = int(env("RETARGET_MAX_MRS", "") or 50)


def newest_retarget(notes: list[dict]) -> str | None:
    """가장 최근 재대상 시각(ISO). 없으면 None."""
    times = [n.get("created_at") for n in notes
             if n.get("system") and RETARGET_MARK in (n.get("body") or "")]
    times = [t for t in times if t]
    return max(times) if times else None


def newest_pipeline(pipes: list[dict]) -> str | None:
    times = [p.get("created_at") for p in pipes if p.get("created_at")]
    return max(times) if times else None


def stale_mrs(gl: GitLab, mrs: list[dict]) -> tuple[list[tuple[dict, str]], list[str]]:
    """(파이프라인이 필요한 MR, 판정 이유), 조회 실패 목록.

    조회에 실패한 MR 은 **건드리지 않는다.** '노트가 없다' 로 오인해 넘어가면 조용히 빠지고,
    반대로 만들면 쓸데없는 파이프라인이 돈다. 실패는 실패로 보고한다."""
    need, broken = [], []
    for mr in mrs[:MAX_MRS]:
        iid = mr.get("iid")
        notes = gl.mr_notes(iid)
        if notes is None:
            broken.append(f"!{iid} 노트 조회 실패")
            continue
        rt = newest_retarget(notes)
        if not rt:
            continue
        pipes = gl.mr_pipelines(iid)
        if pipes is None:
            broken.append(f"!{iid} 파이프라인 조회 실패")
            continue
        last = newest_pipeline(pipes)
        if last and last > rt:
            continue                      # 재대상 뒤에 이미 돌았다
        need.append((mr, f"재대상 {rt[:19]} · 마지막 파이프라인 {last[:19] if last else '없음'}"))
    return need, broken


def main() -> int:
    banner("retarget-pipeline")
    rep = Report("retarget-pipeline")
    gl = GitLab()
    # can_write 를 쓰지 않는다 — 그것은 CI_MERGE_REQUEST_IID 까지 요구하는 **MR 전용** 판정이고,
    # 이 잡은 develop push·스케줄에서 돌아 MR 컨텍스트가 없다(mm_digest 도 같은 이유로 이 형태다).
    if not (gl.token and gl.project):
        rep.warn("GITLAB_TOKEN/CI_PROJECT_ID 없음 → 생략")
        return rep.finish()

    mrs = gl.open_mrs()
    if not mrs:
        rep.ok("열린 MR 없음")
        return rep.finish()
    print(f"열린 MR {len(mrs)}건 확인")

    need, broken = stale_mrs(gl, mrs)
    for b in broken:
        rep.warn(b)                       # 비차단 — 다음 실행에서 다시 본다
    if not need:
        rep.ok(f"재대상 뒤 검사가 빠진 MR 없음 (열린 MR {len(mrs)}건)")
        return rep.finish()

    dry = env("RETARGET_DRY_RUN", "") == "1"
    for mr, why in need:
        iid = mr.get("iid")
        title = (mr.get("title") or "")[:60]
        if dry:
            rep.warn(f"[dry-run] !{iid} {title} — {why}")
            continue
        st, _ = gl.create_mr_pipeline(iid)
        if st in (200, 201):
            rep.ok(f"!{iid} 파이프라인 생성 — {why} · {title}")
        else:
            rep.warn(f"!{iid} 파이프라인 생성 실패(HTTP {st}) — {why}")
    return rep.finish()


if __name__ == "__main__":
    sys.exit(main())
