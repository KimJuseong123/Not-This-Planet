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
# **왜 배선을 글자로 검사하나.** 이 둘은 파이프라인이 실제로 도는 순간에만 드러나고,
# 그 순간은 하루에 한 번(04:00)뿐이다. 되돌아와도 다음 날 새벽까지 아무도 모른다.
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
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

for job in mm-ci-failed mm-ci-resolved; do
  seg=$(section "$job")

  # ① 예약 실행에서도 잡이 생겨야 한다
  if printf '%s' "$seg" | grep -q 'if: \$CI_PIPELINE_SOURCE == "schedule"'; then
    ok "$job — 예약 실행 규칙 있음"
  else
    bad "$job — rules 에 schedule 이 없다. 새벽 실패가 조용해진다"
  fi

  # ② 예약 실행이 돌리는 잡이 needs 에 있어야 한다. 없으면 규칙만으로는 안 깨어난다.
  for need in build-backend deploy-dev build-devbuild publish-devbuild; do
    if printf '%s' "$seg" | grep -q -- "- job: $need"; then
      ok "$job — needs 에 $need"
    else
      bad "$job — needs 에 $need 가 없다 (-344 와 같은 모양)"
    fi
  done
done

# ③ **두 목록이 같아야 한다.** 다르면 같은 파이프라인에서 🔴 와 ✅ 가 엇갈린다
#    — 파일 주석이 그 사고(-289)를 적어 두고 있다.
a=$(section mm-ci-failed   | grep -o -- "- job: [a-z-]*" | sort)
b=$(section mm-ci-resolved | grep -o -- "- job: [a-z-]*" | sort)
if [ "$a" = "$b" ]; then
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
if grep -q 'p.get("source") != source' "$ROOT/scripts/ci/mm_notify.py"; then
  ok "mm_notify — 직전 파이프라인을 같은 source 로 고른다"
else
  bad "mm_notify — 종류가 다른 파이프라인을 '직전' 으로 삼는다"
fi

if [ "$fail" -eq 0 ]; then echo "TEST_NOTIFY_SCHEDULE OK"; else echo "실패 ${fail}건"; fi
exit "$fail"
