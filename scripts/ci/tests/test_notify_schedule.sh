#!/usr/bin/env bash
# 예약 실행(새벽 배포·아침 점검)의 실패가 **조용하지 않은지** (S15P21B202-533).
#
# 실행: bash scripts/ci/tests/test_notify_schedule.sh
# 네트워크 없이 `.gitlab-ci.yml` 의 배선만 본다 — 이 결함은 코드가 아니라 배선에 있었다.
#
# 2026-09-15 사고. 04:03 새벽 파이프라인에서 `build-devbuild` 가 죽었는데 **아무 카드도
# 안 갔다.** 두 겹으로 막혀 있었다.
#   ① `mm-ci-failed` 의 rules 에 `schedule` 이 없어 그 잡이 아예 안 생긴다
#   ② `needs` 에 `build-devbuild` 가 없어, 설령 생겨도 그 실패로는 안 깨어난다
#      (`-344` 에서 `unity-compile` 이 빠져 있던 것과 같은 모양)
# 그래 놓고 08:31 머지 푸시가 "초록으로 돌아왔다" 를 보냈다 — 알린 적 없는 실패가
# 해소됐다고 알려진 것이다.
#
# 고치다가 **한 번 더 깨뜨렸다**(2026-09-15, `!448` AI 리뷰). ② 를 고치려고 `needs` 에
# `build-devbuild` 를 넣었는데 그 잡은 MR 에서 **버튼**이다 — 버튼이 눌리지 않은
# 파이프라인에서 GitLab 이 알림 잡을 `skipped` 로 만들어, 있던 카드까지 사라졌다.
# 그래서 지금은 **예약 전용 잡(`mm-nightly-*`)이 `needs` 없이** 스테이지 순서로 기다린다.
#
# **왜 배선을 글자로 검사하나.** 이 둘은 파이프라인이 실제로 도는 순간에만 드러나고,
# 그 순간은 하루에 한 번(04:00)뿐이다. 되돌아와도 다음 날 새벽까지 아무도 모른다.
# `-o pipefail` 은 쓰지 않는다 — dash 판에 따라 없고, 없으면 **첫 줄에서** 죽어
# 이 검사가 CI 에서 한 번도 안 도는 상태가 된다(`!448` AI 리뷰).
set -u

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
export ROOT
CI="$ROOT/.gitlab-ci.yml"
fail=0

ok()  { echo "  ✅ $1"; }
bad() { echo "  ❌ $1"; fail=$((fail+1)); }

# 잡 하나가 차지하는 구간만 떼어 낸다 — 파일 전체에서 grep 하면 다른 잡의 줄에 속는다.
section() {  # section <잡 이름>
  awk -v j="$1:" '
    $0 ~ "^" j { on=1; next }
    on && /^[a-z][a-z0-9-]*:/ { exit }
    on { print }
  ' "$CI"
}

# 버튼이 될 수 있는 잡 — `.gitlab-ci.yml` 에서 직접 뽑는다. 목록을 손으로 적어 두면
# 새 버튼이 생겼을 때 이 검사가 그것을 모른다.
manual_jobs=$(awk '
  /^[a-z][a-z0-9-]*:/ { j=$1; sub(":","",j) }
  /when: manual/       { print j }
' "$CI" | sort -u)

# ① 예약 실행 전용 잡이 있어야 한다 — 그리고 **needs 가 없어야** 한다.
for job in mm-nightly-failed; do
  seg=$(section "$job")
  if [ -z "$seg" ]; then
    bad "$job 이 없다. 새벽 실패가 조용해진다"
    continue
  fi
  if printf '%s' "$seg" | grep -q 'if: \$CI_PIPELINE_SOURCE == "schedule"'; then
    ok "$job — 예약 실행 규칙 있음"
  else
    bad "$job — rules 에 schedule 이 없다"
  fi
  # **needs 를 넣으면 안 된다.** 스테이지 순서로 미루는 것이 이 잡의 존재 이유다 —
  # needs 를 달면 그 목록에 빠진 잡의 실패를 다시 놓친다(`-344` 와 같은 모양).
  if printf '%s' "$seg" | grep -q 'needs:'; then
    bad "$job — needs 가 있다. 스테이지 순서로 미뤄야 새벽 잡 전부를 기다린다"
  else
    ok "$job — needs 없음 (스테이지 순서로 새벽 잡을 기다린다)"
  fi
done

# ①b **예약에서는 회복 카드를 보내지 않는다.**
#
# 04:00 새벽 배포와 09:30 아침 점검이 둘 다 `source=schedule` 이라, 09:33 점검이
# 성공하면 04:03 새벽 실패를 '해소' 로 보고 ✅ 를 보내게 된다 — 이 이슈가 없애려던
# 거짓 회복과 같은 모양이다. 둘을 가를 신호가 없다(파이프라인 변수는 403, 생성 시각은
# 손으로 돌리면 제각각). 그래서 **거짓말하느니 말하지 않는다.**
if grep -qE "^mm-nightly-resolved:" "$CI"; then
  bad "mm-nightly-resolved 가 있다 — 09:30 점검 성공이 04:00 실패를 해소한 것처럼 말한다"
else
  ok "예약에는 회복 카드가 없다 (두 예약을 갈라낼 수 없다)"
fi
if grep -q "ci-resolved" "$CI" && section mm-nightly-failed | grep -q "ci-resolved"; then
  bad "mm-nightly-failed 가 ci-resolved 를 부른다"
else
  ok "mm-nightly-failed 는 ci-failed 만 부른다"
fi

# ② **mm-ci-* 의 needs 에는 버튼이 들어가면 안 된다.**
#
# 2026-09-15 `!448` 에서 실제로 겪었다. `build-devbuild`(MR 에서는 버튼)를 needs 에
# 넣자, 버튼을 안 누른 파이프라인에서 GitLab 이 알림 잡을 **skipped** 로 만들었다 —
# `ci-tests`·`ai-review-gate` 가 실패했는데 카드가 한 장도 안 나갔다(파이프라인
# `194534`·`195115`). 같은 시각 옛 목록을 쓰던 `!443` 은 카드를 보냈다.
for job in mm-ci-failed mm-ci-resolved; do
  seg=$(section "$job")
  hit=0
  # **꼬리 주석을 떼고 본다** (`!448` AI 리뷰). needs 줄에는 `- job: ai-review-gate # …`
  # 처럼 주석이 붙는다 — 줄 끝 앵커만 쓰면 주석 달린 버튼 잡을 놓친다.
  bare=$(printf '%s
' "$seg" | sed 's/#.*//')
  for m in $manual_jobs; do
    if printf '%s
' "$bare" | grep -qE "^[[:space:]]*- job: $m[[:space:]]*$"; then
      bad "$job — needs 에 버튼 잡 $m 이 있다. 안 누르면 카드가 통째로 사라진다"
      hit=1
    fi
  done
  [ "$hit" = 0 ] && ok "$job — needs 에 버튼 잡 없음"

  # 예약 실행은 mm-nightly-* 가 맡으므로 여기에는 schedule 규칙이 없어야 한다.
  # 둘 다 있으면 한 파이프라인에서 카드가 두 장 나간다.
  if printf '%s' "$seg" | grep -q 'if: \$CI_PIPELINE_SOURCE == "schedule"'; then
    bad "$job — schedule 규칙이 남아 있다. mm-nightly-* 와 겹쳐 카드가 두 장 나간다"
  else
    ok "$job — schedule 는 mm-nightly-* 가 맡는다"
  fi

  # 예약 실행에도 있는 자동 잡은 그대로 있어야 한다.
  if printf '%s' "$seg" | grep -q -- "- job: build-backend"; then
    ok "$job — needs 에 build-backend"
  else
    bad "$job — needs 에 build-backend 가 없다"
  fi
done

# ③ **두 목록이 같아야 한다.** 다르면 같은 파이프라인에서 🔴 와 ✅ 가 엇갈린다
#    — 파일 주석이 그 사고(-289)를 적어 두고 있다.
a=$(section mm-ci-failed   | grep -o -- "- job: [a-z-]*" | sort)
b=$(section mm-ci-resolved | grep -o -- "- job: [a-z-]*" | sort)
# **둘 다 비면 `=` 가 참이 된다** (`!448` AI 리뷰) — 잡 이름이 바뀌어 `section` 이
# 아무것도 못 읽으면 검사가 통과하면서 실제로는 아무것도 안 본다.
if [ -z "$a" ] || [ -z "$b" ]; then
  bad "mm-ci-* 의 needs 를 읽지 못했다 — 잡 이름이 바뀌었나"
elif [ "$a" = "$b" ]; then
  ok "두 잡의 needs 목록이 같다"
else
  # **프로세스 치환(`<(...)`)을 쓰지 않는다.** CI 이미지의 `/bin/sh` 는 dash 이고
  # dash 에는 그 문법이 없다 — 파일을 **읽는 시점**에 구문 오류로 죽으므로, 이 가지를
  # 타지 않아도 스크립트 전체가 안 돈다. 로컬 Git Bash 에서는 통과해서 안 보인다.
  bad "needs 목록이 다르다"
  printf '%s
' "$a" | sed 's/^/       failed:   /'
  printf '%s
' "$b" | sed 's/^/       resolved: /'
fi

# ④ 예약 실행 전용 문구가 있어야 한다. "브랜치가 빨갛다" 는 예약 실패에 맞지 않는다 —
#    그 커밋은 이미 검사를 통과해 머지된 것이고, 되돌릴 원인 MR 이 없다.
if grep -q "예약 실행이 실패했다" "$ROOT/scripts/ci/mm_notify.py"; then
  ok "mm_notify — 예약 실행 전용 문구 있음"
else
  bad "mm_notify — 예약 실행도 '브랜치가 빨갛다' 로 알린다"
fi

# ⑤ 회복 판정이 **같은 종류끼리** 견줘야 한다. 예약과 푸시는 도는 잡이 달라 비교 대상이 아니다.
if grep -q 'other and other != source' "$ROOT/scripts/ci/mm_notify.py"; then
  ok "mm_notify — 직전 파이프라인을 같은 source 로 고른다"
else
  bad "mm_notify — 종류가 다른 파이프라인을 '직전' 으로 삼는다"
fi

# ②b 회복 문구에 예약 분기가 되살아나면 안 된다 — 위 ①b 와 같은 이유다.
if grep -q "그 시간의 일이 다시 된다" "$ROOT/scripts/ci/mm_notify.py"; then
  bad "branch_resolved 에 예약 전용 문구가 있다 — 예약은 회복 카드를 안 보낸다"
else
  ok "branch_resolved 에 예약 분기 없음"
fi

# ③ 예약 이름이 **KST 로** 찍히는가. UTC 를 그대로 자르면 04:00 예약이 "19:00" 이 된다.
#
# **`python` 을 먼저 본다.** Git Bash 의 `python3` 은 마이크로소프트 스토어 스텁이라
# 실행하면 안내문만 찍고 아무것도 안 한다. 실제로 도는지까지 확인한다.
py=
for c in python python3; do
  if command -v "$c" >/dev/null 2>&1 && "$c" -c "pass" >/dev/null 2>&1; then py=$c; break; fi
done
if [ -z "$py" ]; then
  bad "python 을 찾지 못했다"
else
  # **시각만 견준다.** 한글을 견주면 Windows 콘솔의 cp949 때문에 여기서만 깨진다 —
  # 검사하려는 것은 글자가 아니라 시간대 변환이다.
  out=$(PYTHONIOENCODING=utf-8 "$py" -c "
import os, sys
sys.path.insert(0, os.path.join(os.environ['ROOT'], 'scripts', 'ci'))
os.environ['CI_PIPELINE_CREATED_AT'] = '2026-09-14T19:03:09.863Z'   # 04:03 KST
import mm_notify as N
print(N.schedule_name().split()[0])
" | tr -d "\r")   # Windows 파이썬의 print 는 CR 을 붙인다 — 견주기 전에 뗀다
  if [ "$out" = "04:03" ]; then
    ok "schedule_name — UTC 를 KST 로 바꾼다 ($out)"
  else
    bad "schedule_name — KST 가 아니다: $out (기대: 04:03)"
  fi
fi

if [ "$fail" -eq 0 ]; then echo "TEST_NOTIFY_SCHEDULE OK"; else echo "실패 ${fail}건"; fi
exit "$fail"
