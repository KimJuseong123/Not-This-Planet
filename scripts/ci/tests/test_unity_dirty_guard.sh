#!/usr/bin/env bash
# 빌드 뒤 에셋 가드가 **삭제만 막고 재직렬화는 통과시키는지** (S15P21B202-532).
#
# 실행: bash scripts/ci/tests/test_unity_dirty_guard.sh
# Unity·도커·네트워크 없이 돈다 — 진짜 git 저장소를 임시로 만들어 상태를 흉내 낸다.
#
# 2026-09-15 사고. 릴리스 잡과 개발 빌드 잡이 **같은 위험을 다르게 판정**하고 있었다.
# 릴리스는 삭제(D)만 봤는데 개발 빌드는 `git diff --quiet` 로 바뀌기만 하면 실패했다.
# 배치모드는 차가운 Library 에서 **늘** 재직렬화하므로 개발 빌드는 성공할 수가 없었고,
# 첫 새벽 실행부터 죽었다(재직렬화 20건 · 삭제 0건 · 빌드 자체는 444.5MB 로 성공).
#
# 생각을 복사하면 갈라진다. 이제 판정이 한 파일에 있고, 이 검사가 그 판정을 지킨다.
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
GUARD="$ROOT/scripts/ci/unity_dirty_guard.sh"
fail=0

# **Git Bash(MINGW)에서도 돈다.** 이 검사는 git 과 grep 만 쓰므로 Linux 가 필요 없다 —
# 건너뛰게 두면 만든 사람이 자기 PC 에서 확인하지 못하고 CI 에서 처음 돌게 된다.
command -v git >/dev/null || { echo "  ⏭ git 이 없어 건너뜀"; exit 0; }

# 진짜 git 저장소를 만든다. `git status --porcelain` 의 출력 형식을 흉내 내면
# 그 형식이 바뀌었을 때 이 검사가 **조용히 무의미해진다.**
setup() {
  WORK=$(mktemp -d)
  cd "$WORK" || exit 1
  git init -q .
  git config user.email t@t; git config user.name t
  git config core.autocrlf false   # 줄바꿈 경고로 출력을 어지럽히지 않는다
  mkdir -p METAGAME/Assets
  printf 'a\n' > METAGAME/Assets/Keep.prefab
  printf 'b\n' > METAGAME/Assets/Gone.prefab
  printf 'c\n' > METAGAME/Assets/Touched.asset
  git add -A >/dev/null; git commit -qm init
}

check() {   # check <이름> <기대 종료코드> <기대 문구>
  out=$(sh "$GUARD" 2>&1); rc=$?
  if [ "$rc" != "$2" ]; then
    echo "  ❌ $1 — 종료코드 $rc (기대 $2)"; echo "$out" | sed 's/^/       /'; fail=$((fail+1)); return
  fi
  if ! printf '%s' "$out" | grep -q "$3"; then
    echo "  ❌ $1 — 기대 문구가 없다: $3"; echo "$out" | sed 's/^/       /'; fail=$((fail+1)); return
  fi
  echo "  ✅ $1"
}

# ① 아무것도 안 바뀌었다 → 통과
setup
check "깨끗하면 통과" 0 "삭제된 에셋 없음"
rm -rf "$WORK"

# ② **재직렬화만** 있다 → 통과해야 한다. 이것이 매번 죽던 경우다.
setup
printf 'c2\n' > METAGAME/Assets/Touched.asset
check "재직렬화만 → 통과" 0 "재직렬화 1건"
rm -rf "$WORK"

# ③ 삭제가 있다 → 막는다
setup
rm METAGAME/Assets/Gone.prefab
check "삭제 → 막는다" 1 "추적되는 에셋을 삭제했다"
rm -rf "$WORK"

# ④ 삭제와 재직렬화가 섞여 있어도 **삭제가 이긴다**
setup
rm METAGAME/Assets/Gone.prefab
printf 'c2\n' > METAGAME/Assets/Touched.asset
check "섞이면 삭제가 이긴다" 1 "추적되는 에셋을 삭제했다"
rm -rf "$WORK"

# ⑤ 재직렬화 건수를 **센다** — 0 이라고만 말하면 아무 정보가 없다
setup
printf 'k2\n' > METAGAME/Assets/Keep.prefab
printf 'c2\n' > METAGAME/Assets/Touched.asset
check "재직렬화 2건을 센다" 0 "재직렬화 2건"
rm -rf "$WORK"

# ⑥ **METAGAME/ 밖은 보지 않는다** — 빌드가 만지는 곳이 아니다
setup
mkdir -p docs && printf 'x\n' > docs/x.md && git add -A >/dev/null && git commit -qm docs
rm docs/x.md
check "METAGAME 밖 삭제는 무시" 0 "삭제된 에셋 없음"
rm -rf "$WORK"

if [ "$fail" -eq 0 ]; then echo "TEST_UNITY_DIRTY_GUARD OK"; else echo "실패 ${fail}건"; fi
exit "$fail"
