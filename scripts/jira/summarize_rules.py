# -*- coding: utf-8 -*-
r"""summarize_rules — 뜬 규칙 JSON 을 트리거·조건·액션 트리로 펼친다 (읽기 전용).

목적: `docs/jira-gitlab-automation.md` §2 가 실제 규칙과 어긋난 곳을 찾는다.
2026-09-09 에 **R4 의 스프린트 자동 추가 블록이 문서에 없다**는 것이 이렇게 드러났다.

먼저 `dump_rule.py` 로 규칙을 떠야 한다 — 이 스크립트는 그 결과만 읽는다.

    python summarize_rules.py [덤프_디렉터리]

디렉터리를 안 주면 `JIRA_RULES_DIR` 환경변수, 그것도 없으면 `dump_rule.py` 가 쓰는 곳과
같은 자리(`<이 파일의 부모>/rules`)를 본다. 즉 **두 파일을 같은 스크래치패드에 복사해 두면**
경로를 줄 필요가 없다. 파일이 0건이면 **exit 1 로 죽는다** — 규칙 드리프트를 찾는 도구가
조용히 빈 결과를 내면 "어긋난 곳이 없다" 로 잘못 읽힌다.

⚠ **덤프 JSON 을 리포에 커밋하지 않는다.** R10(릴리스 발행) 의 웹훅 액션에 **MM 웹훅 URL 이
그대로 들어 있다.** 덤프는 스크래치패드에만 두고, 여기서는 요약만 문서로 옮긴다.

구조 메모 (실측): 컴포넌트는 `component`(ACTION/CONDITION/…) + `type`(jira.issue.edit 등)
+ `value`(실제 설정) + `conditions`/`children`(중첩). 문자열 필드인 `advancedFields` 는
**JSON 이 문자열로 한 번 더 감싸여 있다.**

아래 타입 문자열은 **전부 2026-09-09 덤프에서 실측한 것**이다 — 추측해 넣으면 매핑이 조용히
빗나가 원시 타입이 그대로 찍힌다(첫 판에서 `jira.comparison.condition` 으로 잘못 적어
R6·R9 의 값 비교 조건이 키 이름만 나왔다).
"""
import datetime
import glob
import io
import json
import os
import sys

NAME = {
    # 액션
    'jira.issue.edit': '액션 · 이슈 편집',
    'jira.issue.transition': '액션 · 전환',
    'jira.issue.comment': '액션 · 코멘트',
    'jira.issue.outgoing.webhook': '액션 · 웹훅',
    'jira.issue.assign': '액션 · 담당자',
    'jira.issue.refresh.issue': '액션 · 이슈 새로 고침',
    'jira.version.release': '액션 · 버전 릴리스',
    'jira.version.create': '액션 · 버전 생성',
    'codebarrel.action.log': '액션 · 로그',
    # 조건
    'jira.issue.condition': '조건 · 필드',
    'jira.jql.condition': '조건 · JQL',
    'jira.comparator.condition': '조건 · 값 비교',
    'jira.user.condition': '조건 · 사용자',
    # 흐름
    'jira.condition.container.block': '컨테이너',
    'jira.condition.if.block': 'if',
    'jira.condition.else.block': 'else',
}
TRIG = {
    'jira.issue.event.trigger:created': '이슈 생성됨',
    'jira.issue.event.trigger:transitioned': '이슈 전환됨',
    'jira.issue.event.trigger:updated': '이슈 수정됨',
    'jira.issue.field.changed': '필드 값 변경됨',
    'devops.branch.event.trigger:created': '브랜치 생성됨 (DevOps)',
    'devops.pr.event.trigger:merged': 'MR 병합됨 (DevOps)',
    'devops.pr.event.trigger:declined': 'MR 닫힘 (DevOps)',
    'jira.incoming.webhook': '수신 웹훅',
}
FIELD = {
    'com.pyxis.greenhopper.jira:gh-sprint': '스프린트',
    'issuetype': '유형',
    'status': '상태',
    'duedate': '기한',
    'labels': '레이블',
    'summary': '제목',
    'parent': '상위 항목',
}


def name_of(c):
    t = c.get('type') or ''
    return NAME.get(t, t or c.get('component') or '?')


def values_of(v):
    """`[{'type': 'ID', 'value': '3'}]` 같은 껍데기에서 값만 뽑는다."""
    if isinstance(v, dict):
        return str(v.get('value', v))
    if isinstance(v, list):
        return ', '.join(values_of(x) for x in v)
    return str(v)


def detail(c):
    t = c.get('type') or ''
    v = c.get('value')
    if v is None:
        return ''
    if isinstance(v, str):
        return v[:200]

    if t == 'jira.jql.condition':
        return v.get('value') or ''

    if t == 'jira.issue.condition':
        ft = v.get('selectedFieldType') or ''
        f = FIELD.get(ft, ft)
        cmp_ = v.get('comparison') or ''
        cv = v.get('compareValue')
        s = ''
        if isinstance(cv, dict):
            s = values_of(cv.get('multiValue') or cv.get('value'))
        return f'{f} {cmp_} {s}'.strip()

    if t == 'jira.comparator.condition':
        return f"{values_of(v.get('first'))} {v.get('operator')} {values_of(v.get('second'))}"

    if t == 'jira.issue.edit':
        parts = []
        af = v.get('advancedFields')
        if af:
            try:
                parts.append(json.dumps(json.loads(af), ensure_ascii=False))
            except (ValueError, TypeError):
                # 드리프트 검사 도구다 — 파싱 실패를 삼키면 안 된다
                parts.append('(advancedFields 파싱 실패) ' + str(af)[:150])
        for op in (v.get('operations') or []):
            fld = (op.get('field') or {}).get('value') or op.get('fieldType') or '?'
            fld = FIELD.get(fld, fld)
            parts.append(f'{fld} := {values_of(op.get("value"))[:70]}')
        return ' · '.join(parts) or '(빈 편집)'

    if t == 'jira.issue.transition':
        return '→ ' + values_of(v.get('destinationStatus'))

    if t == 'jira.issue.comment':
        return (v.get('comment') or '').replace('\n', ' ')[:180]

    if t == 'jira.issue.outgoing.webhook':
        # 웹훅 URL 에 비밀이 들어 있다 — 호스트만 보여 준다
        url = str(v.get('url') or '')
        keep = url.split('//')[-1].split('/')[0] if '//' in url else url[:20]
        return f'POST {keep}/… (URL 전문은 규칙 설정에만 둔다)'

    if t in ('jira.version.release', 'jira.version.create'):
        return f"version={v.get('versionName')}"

    if t in ('jira.condition.if.block', 'jira.condition.else.block',
             'jira.condition.container.block'):
        return v.get('conditionMatchType') or ''

    return ', '.join(sorted(v.keys()))[:160]


def walk(comps, depth, out):
    for c in comps or []:
        d = detail(c)
        out.append('  ' * depth + (f'{name_of(c)}: {d}' if d else name_of(c)))
        if c.get('conditions'):
            out.append('  ' * (depth + 1) + '(조건)')
            walk(c['conditions'], depth + 2, out)
        if c.get('children'):
            walk(c['children'], depth + 1, out)


def rules_dir():
    if len(sys.argv) > 1:
        return sys.argv[1]
    env = os.environ.get('JIRA_RULES_DIR')
    if env:
        return env
    # dump_rule.py 가 쓰는 자리와 같다 (`<스크래치패드>/rules`)
    return os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'rules')


def main():
    # 규칙 코멘트에 —··· 같은 글자가 있다. Windows 콘솔 기본값(cp949) 이면 여기서
    # UnicodeEncodeError 로 죽는다 — 출력 도구가 환경 때문에 죽으면 안 된다.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8')
        except AttributeError:          # 파이프로 감싸인 경우
            pass

    d = rules_dir()
    paths = sorted(glob.glob(os.path.join(d, 'rule-*.json')))
    if not paths:
        sys.stderr.write(
            '규칙 덤프가 없다: %s\n'
            '  먼저 dump_rule.py 로 규칙을 떠야 한다. 이 스크립트는 그 결과만 읽는다.\n'
            '  다른 자리에 있으면 인자나 JIRA_RULES_DIR 로 알려 준다.\n' % d)
        return 1

    # 같은 id 가 여러 파일에 있을 수 있다(옛 덤프) — 가장 최근 것만 남긴다
    best = {}
    for path in paths:
        r = json.load(io.open(path, encoding='utf-8'))
        r = r.get('rule') or r
        if r.get('state') != 'ENABLED':      # DISABLED 는 안 쓰는 것이므로 건너뛴다
            continue
        rid = str(r.get('id'))
        if rid not in best or (r.get('updated') or 0) > (best[rid].get('updated') or 0):
            best[rid] = r

    if not best:
        sys.stderr.write('%s 에 규칙 %d건이 있지만 ENABLED 가 하나도 없다.\n'
                         % (d, len(paths)))
        return 1

    for r in sorted(best.values(), key=lambda x: (x.get('name') or '')):
        print('=' * 78)
        up = r.get('updated')
        when = (datetime.datetime.fromtimestamp(up / 1000).strftime('%Y-%m-%d')
                if isinstance(up, (int, float)) else str(up)[:10])
        print(f"{r.get('name')}   id={r.get('id')}   갱신={when}")
        t = r.get('trigger') or {}
        tt = t.get('type') or ''
        tv = t.get('value') or {}
        extra = ''
        if isinstance(tv, dict):
            if tv.get('toStatus'):
                extra = f" (도착: {values_of(tv['toStatus'])})"
            elif tv.get('field'):
                extra = f" (필드: {values_of(tv['field'])})"
            elif tv.get('tagValue'):
                extra = f" ({tv.get('tagType')}={tv.get('tagValue')})"
        print(f"  트리거: {TRIG.get(tt, tt)}{extra}")
        print(f"  다른 규칙이 이 규칙을 트리거: {r.get('canOtherRuleTrigger')}")
        out = []
        walk(r.get('components') or [], 1, out)
        print('\n'.join(out))
        print()

    print('# ENABLED %d건 · 덤프 %d파일 · %s' % (len(best), len(paths), d))
    return 0


if __name__ == '__main__':
    sys.exit(main())
