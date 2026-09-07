#!/bin/sh
# pre-push 의 'MR 만들기 링크(제목·대상 채움)' 검증 — 합성 저장소 5경우 (S15P21B202-322)
#
# 실행: sh scripts/ci/tests/test_prepush_link.sh
#   임시 폴더에 bare 원격 + 로컬 저장소를 만들어 실제로 push 해 본다. 이 리포에는 손대지 않는다.
#   확인하는 것: 첫 push 에 링크 · 제목 = 마지막 커밋 제목(한글·괄호 인코딩) · 두 번째 push 엔 없음 ·
#              키 없는 커밋 제목이면 키만 · hotfix 는 master 대상 · https 원격 URL 에서 주소 도출(.git 제거)
set -e
HOOK=${1:-$(cd "$(dirname "$0")/../../.." && pwd)/.githooks/pre-push}
[ -f "$HOOK" ] || { echo "훅 파일 없음: $HOOK"; exit 1; }
ROOT=$(mktemp -d)
trap 'rm -rf "$ROOT"' EXIT
export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=t@e.st GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=t@e.st
export METAGAME_AUTOREBASE=0              # 리베이스 경로는 test_prepush_rebase.sh 가 본다
export METAGAME_PROJECT_URL="https://lab.example.com/grp/proj"

setup() {   # 원격(bare) + 로컬, develop 한 커밋, 작업 브랜치 하나
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
  git switch -q -c "$1"
  echo mine > mine.txt && git add -A && git commit -qm "$2"
}

# ── 1) 첫 push: 링크가 나오고 제목은 마지막 커밋 제목(한글·괄호·콜론이 %XX 로) ──
setup S15P21B202-1-work "fix(ui): 버튼 (S15P21B202-1)"
OUT=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "merge_requests/new?" || { echo "실패1: 링크 없음"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q "merge_request%5Bsource_branch%5D=S15P21B202-1-work" || { echo "실패1: 소스 브랜치"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q "merge_request%5Btarget_branch%5D=develop" || { echo "실패1: 대상 develop 이어야"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q "merge_request%5Btitle%5D=fix%28ui%29%3A%20%EB%B2%84%ED%8A%BC%20%28S15P21B202-1%29" \
  || { echo "실패1: 제목 인코딩"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q "https://lab.example.com/grp/proj/-/merge_requests/new" || { echo "실패1: 프로젝트 주소"; echo "$OUT"; exit 1; }
git rev-parse -q --verify origin/S15P21B202-1-work >/dev/null || { echo "실패1: push 자체가 안 됐다"; exit 1; }
echo "  1) 첫 push → 제목·대상 채운 링크, 한글·괄호 인코딩 정확, push 는 정상 ✅"

# ── 2) 두 번째 push: 브랜치가 이미 원격에 있으면 링크를 찍지 않는다 ──
echo more > more.txt && git add -A && git commit -qm "fix(ui): 더 (S15P21B202-1)"
OUT=$( { git push; } 2>&1 || true )
echo "$OUT" | grep -q "merge_requests/new?" && { echo "실패2: 두 번째 push 에도 링크"; echo "$OUT"; exit 1; }
echo "  2) 이미 올라간 브랜치 → 링크 없음 ✅"

# ── 3) 커밋 제목에 이슈 키가 없으면 제목은 키만 (나머지는 CI 가 Jira 로 채운다) ──
setup S15P21B202-7-nokey "wip"
OUT=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "merge_request%5Btitle%5D=S15P21B202-7$" || echo "$OUT" | grep -q "merge_request%5Btitle%5D=S15P21B202-7[^0-9]" \
  || { echo "실패3: 키만 들어가야"; echo "$OUT"; exit 1; }
echo "  3) 키 없는 커밋 제목 → 제목은 이슈 키만 ✅"

# ── 4) hotfix 브랜치는 master 대상 ──
setup hotfix/S15P21B202-40-crash "fix(net): 크래시 (S15P21B202-40)"
OUT=$( { git push -u origin HEAD; } 2>&1 || true )
echo "$OUT" | grep -q "merge_request%5Btarget_branch%5D=master" || { echo "실패4: hotfix 대상은 master"; echo "$OUT"; exit 1; }
echo "$OUT" | grep -q "merge_request%5Bsource_branch%5D=hotfix%2FS15P21B202-40-crash" || { echo "실패4: 슬래시 인코딩"; echo "$OUT"; exit 1; }
echo "  4) hotfix/* → 대상 master, 슬래시 %2F ✅"

# ── 5) 원격 URL 에서 프로젝트 주소를 만든다(.git 제거 · ssh 형식은 https 로) ──
#    git 은 pre-push 를 부르기 **전에** 원격에 접속해 ref 를 받아 오므로 가짜 https 원격으로는 훅까지 가지 못한다.
#    그래서 git 이 하는 것과 똑같이 — 인자(원격 이름·URL)와 stdin(보낼 ref 줄)을 주어 — 훅을 직접 부른다.
setup S15P21B202-2-url "chore: url (S15P21B202-2)"
unset METAGAME_PROJECT_URL
SHA=$(git rev-parse HEAD)
LINE="refs/heads/S15P21B202-2-url $SHA refs/heads/S15P21B202-2-url 0000000000000000000000000000000000000000"
OUT=$( { printf '%s\n' "$LINE" | sh .githooks/pre-push origin "https://lab.example.com/team/game.git"; } 2>&1 || true )
echo "$OUT" | grep -q "https://lab.example.com/team/game/-/merge_requests/new" || { echo "실패5: https 원격에서 주소 도출"; echo "$OUT"; exit 1; }
OUT=$( { printf '%s\n' "$LINE" | sh .githooks/pre-push origin "git@lab.example.com:team/game.git"; } 2>&1 || true )
echo "$OUT" | grep -q "https://lab.example.com/team/game/-/merge_requests/new" || { echo "실패5: ssh 원격에서 주소 도출"; echo "$OUT"; exit 1; }
OUT=$( { printf '%s\n' "$LINE" | sh .githooks/pre-push origin "/some/local/path.git"; } 2>&1 || true )
echo "$OUT" | grep -q "merge_requests/new" && { echo "실패5: 로컬 경로 원격인데 링크를 찍었다"; echo "$OUT"; exit 1; }
echo "  5) https·ssh 원격 → .git 을 뗀 https 프로젝트 주소 · 로컬 경로면 링크 생략 ✅"

echo "TEST_PREPUSH_LINK OK"
