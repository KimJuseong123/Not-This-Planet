#!/usr/bin/env bash
# METAGAME GitLab 프로젝트 러너 — Linux(EC2, Ubuntu/Debian) · Docker 실행기 · 24시간 상주
# 사용: sudo RUNNER_TOKEN=glrt-... bash scripts/runner/setup-linux-runner.sh
#   - 토큰은 환경변수로만 받는다 (팀장: Settings → CI/CD → Runners → New project runner, 태그 linux, Run untagged ✔)
#   - Docker 실행기: .gitlab-ci.yml 의 image: python:3.12-slim 이 그대로 사용됨. Unity 잡(unity-win)은 여기서 돌지 않음
#   - 재부팅 후 자동 시작(systemd). 디스크가 작으면 주기적으로 `docker system prune -f`
set -euo pipefail
: "${RUNNER_TOKEN:?RUNNER_TOKEN 이 비어 있음 — 팀장이 발급한 glrt-… 토큰을 환경변수로}"
GITLAB_URL="${GITLAB_URL:-https://lab.ssafy.com}"
# 태그·Run untagged 는 glrt- 토큰 등록에서 명령줄로 줄 수 없음 — 팀장이 러너 생성/편집 화면에서: 태그 linux, Run untagged ✔
DESC="METAGAME-$(hostname)"

if ! command -v docker >/dev/null; then
  echo "[1/4] Docker 설치"; curl -fsSL https://get.docker.com | sh
fi
systemctl enable --now docker

if ! command -v gitlab-runner >/dev/null; then
  echo "[2/4] gitlab-runner 설치"
  curl -fsSL "https://packages.gitlab.com/install/repositories/runner/gitlab-runner/script.deb.sh" | bash
  apt-get install -y gitlab-runner
fi
gitlab-runner --version

echo "[3/4] 등록: $DESC (태그·untagged 는 GitLab 러너 설정)"
gitlab-runner register --non-interactive \
  --url "$GITLAB_URL" --token "$RUNNER_TOKEN" \
  --executor docker --docker-image "python:3.12-slim" \
  --description "$DESC" \
  --docker-pull-policy if-not-present

# 동시 실행 2 (MR 여러 개가 동시에 열릴 때)
sed -i 's/^concurrent = .*/concurrent = 2/' /etc/gitlab-runner/config.toml

echo "[4/4] 서비스"
systemctl enable --now gitlab-runner
systemctl restart gitlab-runner
gitlab-runner verify

# 주 1회 도커 정리 (디스크)
cat >/etc/cron.weekly/docker-prune <<'EOF'
#!/bin/sh
docker system prune -f >/dev/null 2>&1 || true
EOF
chmod +x /etc/cron.weekly/docker-prune

cat <<EOF

완료. 확인:
 - GitLab Settings → CI/CD → Runners 에 '$DESC' 초록불
 - 팀장: 변수 CI_ENABLE=true → MR 파이프라인이 이 러너에서 실행됨
 - 보안 그룹: 아웃바운드 443 만 필요(인바운드 불필요). 러너 PC/서버는 CI 변수(토큰)를 볼 수 있으므로 팀 외 접근 차단
EOF
