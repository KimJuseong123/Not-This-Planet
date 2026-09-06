#!/usr/bin/env bash
# 배포 전용 러너를 등록한다 — 스풀을 공용 러너에서 떼어내기 위한 첫 단계 (S15P21B202-252).
#
# 왜 필요한가
#   배포 장치는 스풀 파일 하나로 신호를 준다 — 잡이 /spool 에 jar 과 RELEASE 를 놓으면 호스트의
#   systemd path 유닛이 deploy.sh 를 돌린다(도커 소켓을 잡에 노출하지 않으려는 -139 결정).
#   그런데 그 스풀을 **공용 러너에 마운트**해 버렸고, 그 러너가 모든 MR 파이프라인을 돈다.
#   MR 파이프라인은 소스 브랜치의 .gitlab-ci.yml 을 쓰므로, **MR 을 열 수 있는 사람은 누구나
#   자기 브랜치에 /spool 에 쓰는 잡을 한 줄 넣어 리뷰 없이 배포를 트리거할 수 있다.**
#   develop·master 가 보호 브랜치인데(push·merge = Maintainers) 그 보호를 우회하는 경로다.
#
# 이 파일의 이력 (2026-09-04, S15P21B202-301)
#   예전 판은 --executor shell 로 등록했다. 잡에서 docker compose 를 직접 부르던 **옛 설계**의
#   잔재이고 지금 설계(스풀 + systemd)와 맞지 않았다. 셸 러너는 호스트 전체 권한이라 더 나쁘다.
#   -252 가 "그 스크립트는 쓰지 않는다" 고 정했는데 저장소에 그대로 남아 있어 다음 사람이
#   실행할 수 있는 함정이었다. 그래서 EC2 에서 실제로 쓴 내용으로 교체했다.
#
# 이 스크립트가 하는 일
#   · docker 실행기로 등록 · volumes 는 /cache 와 스풀만
#     (claude 자격증명·Unity 라이선스는 넣지 않는다 — 배포 잡은 쓰지 않는다)
#   · concurrent 는 건드리지 않는다(전역 설정이라 공용 러너와 공유한다)
#
# 이 스크립트가 **하지 못하는 일** — 여기서 한 번 막혔다
#   태그·Protected·Run untagged 는 **팀장이 러너를 만들 때 UI 에서** 정한다. 등록 명령으로는
#   줄 수 없고, 주면 무시되는 게 아니라 **FATAL 로 죽는다**(아래 주의 참조).
#   그래서 등록만으로는 격리가 생기지 않는다 — 팀장에게 이렇게 요청해야 한다:
#     Settings → CI/CD → Runners → New project runner
#       · 태그 deploy · Run untagged jobs **해제** · **Protected 체크**
#       · 생성 직후 한 번만 보이는 glrt- 문자열 전달
#
# 실행 (토큰이 명령줄·히스토리에 남지 않게):
#   read -rs -p "러너 등록 토큰(glrt-…): " RUNNER_TOKEN && echo
#   export RUNNER_TOKEN
#   sudo -E bash scripts/runner/setup-deploy-runner.sh
#   unset RUNNER_TOKEN
set -uo pipefail

: "${RUNNER_TOKEN:?RUNNER_TOKEN 이 비어 있다 — 위 주석의 read -rs 방법으로 넣어라}"
GITLAB_URL="${GITLAB_URL:-https://lab.ssafy.com}"
DESC="METAGAME-deploy-$(hostname)"
SPOOL="/home/ubuntu/metagame/deploy"
CONF="/etc/gitlab-runner/config.toml"

command -v gitlab-runner >/dev/null || { echo "gitlab-runner 가 없다"; exit 1; }
[ -d "$SPOOL" ] || { echo "스풀 디렉터리가 없다: $SPOOL"; exit 1; }

if grep -q "$DESC" "$CONF" 2>/dev/null; then
  echo "이미 등록돼 있다($DESC) — 중복 등록하지 않는다"
  grep -nE "^\s+(name|executor)" "$CONF" | sed 's/^/  /'
  exit 0
fi

cp -n "$CONF" "$CONF.bak-before-deploy-runner" 2>/dev/null || true
echo "백업: $CONF.bak-before-deploy-runner"
echo

echo "[1/3] 등록 (docker 실행기)"
# 주의: --tag-list·--access-level·--run-untagged 를 넣으면 **등록이 FATAL 로 죽는다.**
#   (gitlab-runner 19.3.1 실측: "Runner configuration other than name and executor
#    configuration is reserved ... cannot be specified when registering with a runner
#    authentication token"). 무시되는 게 아니라 중단된다.
#   태그·Protected·Run untagged 는 팀장이 러너를 만들 때 UI 에서 정한 값이 쓰인다.
#   그래서 등록만으로는 격리가 생기지 않는다 — 아래 "확인할 것" 의 Protected 확인이 -252 의 핵심이다.
gitlab-runner register --non-interactive \
  --url "$GITLAB_URL" \
  --token "$RUNNER_TOKEN" \
  --executor docker \
  --docker-image "python:3.12" \
  --docker-volumes "/cache" \
  --docker-volumes "$SPOOL:/spool:rw" \
  --docker-pull-policy "if-not-present" \
  --description "$DESC" || {
  echo
  echo "등록 실패. 흔한 원인:"
  echo "  · 토큰이 안 맞음    → 위에 'is not valid' 가 찍힌다"
  echo "  · 이미 쓴 토큰      → 같은 토큰을 두 번 등록할 수 없다"
  exit 1; }

echo
echo "[2/3] 서비스 재시작"
systemctl restart gitlab-runner
sleep 3
systemctl is-active gitlab-runner | sed 's/^/  상태: /'

echo
echo "[3/3] 등록 결과 (토큰은 출력하지 않는다)"
grep -nE "^\s+(name|executor|volumes)" "$CONF" | sed 's/^/  /'
echo
echo "확인할 것"
echo "  · GitLab Runners 화면에서 $DESC 가 초록불인지"
echo "  · **태그 [deploy] 와 Protected 가 켜져 있는지** — 등록이 정하지 않는다."
echo "    러너를 만들 때 UI 에서 정한 값이다. 아니면 팀장이 러너 편집에서 고쳐야 한다."
echo "  · 그다음 금준영이 .gitlab-ci.yml 의 deploy-backend 를 tags: [deploy] 로 바꾸고,"
echo "    마지막에 공용 러너에서 /spool 마운트를 제거한다. **순서를 바꾸면 배포 잡이 멈춘다.**"
echo
echo "되돌리기: gitlab-runner unregister --name \"$DESC\" · 또는 $CONF.bak-before-deploy-runner 복원"
