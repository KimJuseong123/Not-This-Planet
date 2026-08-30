#!/bin/sh
# pre-push 의 'develop 최신 자동 반영' 검증 — 합성 저장소 8경우 (S15P21B202-137)
#
# 실행: sh scripts/ci/tests/test_prepush_rebase.sh
#   임시 폴더에 bare 원격 + 로컬 저장소를 만들어 실제로 push 해 본다. 이 리포에는 손대지 않는다.
#   확인하는 것: 첫 push · 이미 올라간 브랜치(force-with-lease 안내) · 충돌 원상복구 ·
#              스위치로 끄기 · 작업 중 변경 보존 · merge 모드 · merge 모드 충돌 · 스택 브랜치는 건너뜀
set -e
HOOK=${1:-$(cd "$(dirname "$0")/../../.." && pwd)/.githooks/pre-push}
[ -f "$HOOK" ] || { echo "훅 파일 없음: $HOOK"; exit 1; }
ROOT=$(mktemp -d)
trap 'rm -rf "$ROOT"' EXIT
export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=t@e.st GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=t@e.st

setup() {   # 원격(bare) + 로컬, develop 이 1커밋 앞선 상태에서 작업 브랜치 하나
  rm -rf "$ROOT/remote" "$ROOT/work"
  git init -q --bare "$ROOT/remote"
  git init -q "$ROOT/work"
  cd "$ROOT/work"
  git remote add origin "$ROOT/remote"
  mkdir -p .githooks && cp "$HOOK" .githooks/pre-push && chmod +x .githooks/pre-push
  git config core.hooksPath .githooks
  echo base > base.txt
  git add -A && git commit -qm "base"
  git branch -M develop
  git push -q -u origin develop
  git switch -q -c S15P21B202-1-work
  echo mine > mine.txt && git add -A && git commit -qm "my work (S15P21B202-1)"
  # develop 이 앞서 나간다(다른 사람 작업)
  git switch -q develop
  echo theirs > theirs.txt && git add -A && git commit -qm "their work"
  git push -q origin develop
  git switch -q S15P21B202-1-work
  git update-ref -d refs/remotes/origin/develop 2>/dev/null || true   # 아직 안 받아 온 상태로 되돌린다
}

# ── 1) 첫 push: 뒤처져 있으면 리베이스하고 push 를 중단, 안내는 -u origin HEAD ──
setup
OUT=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "자동으로 리베이스" || { echo "실패1: 리베이스 안내 없음"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q "git push -u origin HEAD" || { echo "실패1: 첫 push 안내 없음"; echo "$OUT"; exit 1; }
git log --oneline develop..HEAD >/dev/null
test "$(git rev-list --count HEAD..origin/develop)" = "0" || { echo "실패1: 리베이스가 안 됐다"; exit 1; }
test "$(git rev-list --count origin/develop..HEAD)" = "1" || { echo "실패1: 내 커밋이 사라졌다"; exit 1; }
test -f theirs.txt || { echo "실패1: develop 최신 파일이 없다"; exit 1; }
test -f mine.txt || { echo "실패1: 내 파일이 사라졌다"; exit 1; }
echo "  1) 첫 push · 뒤처짐 → 리베이스 후 중단, 내 커밋·상대 커밋 모두 살아 있음 ✅"
OUT2=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT2" | grep -q "자동으로 리베이스" && { echo "실패1b: 최신인데 또 리베이스"; exit 1; }
git rev-parse -q --verify origin/S15P21B202-1-work >/dev/null || { echo "실패1b: 두 번째 push 가 안 됐다"; exit 1; }
echo "  1b) 다시 push → 조용히 성공(최신이면 아무 것도 하지 않음) ✅"

# ── 2) 이미 올라간 브랜치: force-with-lease 를 안내한다 ──
git switch -q develop && git pull -q origin develop
echo more > more.txt && git add -A && git commit -qm "their work 2" && git push -q origin develop
git switch -q S15P21B202-1-work
git update-ref -d refs/remotes/origin/develop
echo mine2 > mine2.txt && git add -A && git commit -qm "my work 2 (S15P21B202-1)"
OUT=$( { git push; } 2>&1 || true )
echo "$OUT" | grep -q -- "--force-with-lease" || { echo "실패2: force-with-lease 안내 없음"; echo "$OUT"; exit 1; }
echo "  2) 이미 올라간 브랜치 → 리베이스 후 --force-with-lease 안내 ✅"
git push -q --force-with-lease
echo "  2b) 안내대로 force-with-lease → push 성공 ✅"

# ── 3) 충돌: 원상복구하고 경고만, push 는 계속된다 ──
setup
git switch -q develop && echo conflict-theirs > shared.txt && git add -A && git commit -qm "shared theirs" && git push -q origin develop
git switch -q S15P21B202-1-work && echo conflict-mine > shared.txt && git add -A && git commit -qm "shared mine (S15P21B202-1)"
git update-ref -d refs/remotes/origin/develop
BEFORE=$(git rev-parse HEAD)
OUT=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "충돌로 멈춰" || { echo "실패3: 충돌 안내 없음"; echo "$OUT"; exit 1; }
test "$(git rev-parse HEAD)" = "$BEFORE" || { echo "실패3: HEAD 가 바뀌었다(원상복구 실패)"; exit 1; }
git rev-parse -q --verify origin/S15P21B202-1-work >/dev/null || { echo "실패3: push 가 막혔다(막지 않아야 한다)"; exit 1; }
echo "  3) 충돌 → 원상복구 + 경고, push 는 계속됨 ✅"

# ── 4) 스위치로 끌 수 있다 ──
setup
OUT=$( { METAGAME_AUTOREBASE=0 git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "자동으로 리베이스" && { echo "실패4: 껐는데도 돌았다"; exit 1; }
git rev-parse -q --verify origin/S15P21B202-1-work >/dev/null || { echo "실패4: push 가 안 됐다"; exit 1; }
echo "  4) METAGAME_AUTOREBASE=0 → 건너뛰고 그대로 push ✅"

# ── 5) 작업 중 변경(dirty)이 있어도 autostash 로 살아남는다 ──
setup
echo "임시 작업중" > wip.txt
git add wip.txt   # staged 상태로 남겨 둔다
OUT=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "리베이스 완료" || { echo "실패5: 리베이스 실패"; echo "$OUT"; exit 1; }
test -f wip.txt || { echo "실패5: 작업 중 파일이 사라졌다"; exit 1; }
grep -q "임시 작업중" wip.txt || { echo "실패5: 작업 중 내용이 바뀌었다"; exit 1; }
echo "  5) 작업 중 변경이 있어도 autostash 로 그대로 복원 ✅"

# ── 6) merge 모드: 커밋 해시가 그대로라 강제 push 가 필요 없다 ──
setup
OUT=$( { METAGAME_AUTOREBASE_MODE=merge git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "merge 완료" || { echo "실패6: merge 안내 없음"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q -- "--force-with-lease" && { echo "실패6: merge 인데 강제 push 를 안내했다"; exit 1; }
test -f theirs.txt || { echo "실패6: develop 최신 파일이 없다"; exit 1; }
test -f mine.txt || { echo "실패6: 내 파일이 사라졌다"; exit 1; }
git rev-parse -q --verify MERGE_HEAD >/dev/null 2>&1 && { echo "실패6: merge 가 끝나지 않았다"; exit 1; }
echo "  6) merge 모드 → merge 후 중단, 강제 push 안내 없음 ✅"
METAGAME_AUTOREBASE_MODE=merge git push -q -u origin HEAD
echo "  6b) 안내대로 다시 push → 성공 ✅"

# ── 7) merge 모드 충돌: 원상복구하고 push 는 계속 ──
setup
git switch -q develop && echo conflict-theirs > shared.txt && git add -A && git commit -qm "shared theirs" && git push -q origin develop
git switch -q S15P21B202-1-work && echo conflict-mine > shared.txt && git add -A && git commit -qm "shared mine (S15P21B202-1)"
git update-ref -d refs/remotes/origin/develop
BEFORE=$(git rev-parse HEAD)
OUT=$( { METAGAME_AUTOREBASE_MODE=merge git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "merge 가 충돌로 멈춰" || { echo "실패7: 충돌 안내 없음"; echo "$OUT"; exit 1; }
test "$(git rev-parse HEAD)" = "$BEFORE" || { echo "실패7: HEAD 가 바뀌었다"; exit 1; }
grep -q conflict-mine shared.txt || { echo "실패7: 내 변경이 사라졌다"; exit 1; }
git rev-parse -q --verify origin/S15P21B202-1-work >/dev/null || { echo "실패7: push 가 막혔다"; exit 1; }
echo "  7) merge 모드 충돌 → 원상복구 + 경고, push 는 계속됨 ✅"

# ── 8) 스택 브랜치: 다른 작업 브랜치 위에 얹혀 있으면 develop 자동 반영을 건너뛴다(스택이 깨지지 않게) ──
setup
METAGAME_AUTOREBASE=0 git push -q -u origin S15P21B202-1-work   # 부모(A)를 원격에 올려 둔다(훅 끔) — 아직 develop 에 안 들어감
git switch -q -c S15P21B202-9-stack                               # A 위에 얹힌 브랜치
echo stack > stack.txt && git add -A && git commit -qm "stacked (S15P21B202-9)"
git update-ref -d refs/remotes/origin/develop
BEFORE=$(git rev-parse HEAD)
OUT=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "위에 얹혀 있어" || { echo "실패8: 스택 안내 없음"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q "자동으로 리베이스" && { echo "실패8: 스택인데 리베이스했다"; exit 1; }
test "$(git rev-parse HEAD)" = "$BEFORE" || { echo "실패8: HEAD 가 바뀌었다(스택 깨짐)"; exit 1; }
git merge-base --is-ancestor origin/S15P21B202-1-work HEAD || { echo "실패8: 부모가 더는 조상이 아니다"; exit 1; }
git rev-parse -q --verify origin/S15P21B202-9-stack >/dev/null || { echo "실패8: push 가 안 됐다"; exit 1; }
echo "  8) 스택 브랜치 → 자동 반영 건너뛰고 그대로 push, 부모 관계 유지 ✅"

echo "TEST_AUTOREBASE OK"
