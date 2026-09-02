#!/usr/bin/env bash
# METAGAME 배포 전용 러너 — EC2 · **shell 실행기** · 보호 브랜치 전용 (S15P21B202-141)
# 사용: sudo RUNNER_TOKEN=glrt-... bash scripts/runner/setup-deploy-runner.sh
#
# 왜 shell 인가: 배포는 호스트의 `docker compose` 를 직접 불러야 한다. docker 실행기(잡이 컨테이너
#   안)로는 그게 불가능해서 스풀 디렉터리 우회를 만들었는데, shell 실행기 러너 하나면 그 우회가 필요 없다.
# 왜 안전한가: 이 러너는 GitLab 에서 **Protected** 로 만들어야 한다 — 그러면 `develop`·`master` 같은
#   보호 브랜치 파이프라인의 잡만 받는다. MR 파이프라인(비보호)은 이 러너를 쓸 수 없으므로,
#   MR 이 `.gitlab-ci.yml` 을 고쳐 임의 코드를 호스트에서 돌리는 길이 막힌다.
#   → 팀장이 러너 생성/편집 화면에서 **Protected 체크 + 태그 `deploy` + Run untagged 해제**.
#
# 기존 docker 러너(python 잡 전부)는 그대로 둔다. 이 스크립트는 config.toml 에 **항목을 하나 더** 붙인다.
set -euo pipefail
: "${RUNNER_TOKEN:?RUNNER_TOKEN 이 비어 있음 — 팀장이 발급한 glrt-… 토큰을 환경변수로}"
GITLAB_URL="${GITLAB_URL:-https://lab.ssafy.com}"
DESC="METAGAME-deploy-$(hostname)"

command -v gitlab-runner >/dev/null || { echo "gitlab-runner 가 없다 — setup-linux-runner.sh 를 먼저"; exit 1; }

echo "[1/3] 등록 (shell 실행기)"
gitlab-runner register --non-interactive \
  --url "$GITLAB_URL" --token "$RUNNER_TOKEN" \
  --executor shell --shell bash \
  --description "$DESC"

echo "[2/3] 서비스 재시작"
systemctl restart gitlab-runner
sleep 2
systemctl is-active gitlab-runner

echo "[3/3] 등록 결과 (토큰은 출력하지 않는다)"
grep -E '^\s*(name|executor|shell)\s*=' /etc/gitlab-runner/config.toml | sed 's/^/   /'
echo ""
echo "확인할 것"
echo " - GitLab Runners 화면에서 이 러너가 초록불이고 태그가 [deploy] 인지"
echo " - **Protected 체크**가 되어 있는지 (안 되어 있으면 MR 파이프라인도 이 러너를 쓸 수 있다)"
echo " - 잡은 서비스 사용자 권한으로 돈다. docker compose 를 부르므로 그 사용자가 docker 를 쓸 수 있어야 한다"
