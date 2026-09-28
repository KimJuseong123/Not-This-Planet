#!/usr/bin/env bash
# 실행 중인 MySQL 8.0 앱 DB의 백업. 서버 Bash에서 실행한다.
# 사용: bash exec/scripts/backup-db.sh metagame-mysql exec/db
set -euo pipefail
umask 077
container="${1:?MySQL 컨테이너 이름이 필요합니다}"
out="${2:?백업 출력 폴더가 필요합니다}"
for tool in docker gzip sha256sum; do
    command -v "$tool" >/dev/null || { echo "필요한 도구: $tool" >&2; exit 1; }
done
mkdir -p -- "$out"
out="$(cd -- "$out" && pwd)"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
name="metagame-$stamp.sql.gz"
dest="$out/$name"
if [[ -e "$dest" || -e "$dest.sha256" ]]; then
    echo '같은 이름의 백업이 이미 있습니다. 덮어쓰지 않습니다.' >&2
    exit 1
fi
tmp="$(mktemp "$out/.backup.XXXXXXXX")"
trap 'rm -f -- "$tmp"' EXIT
docker exec "$container" sh -c '
    : "${MYSQL_ROOT_PASSWORD:?컨테이너에 root 암호가 없습니다}"
    : "${MYSQL_DATABASE:?컨테이너에 DB 이름이 없습니다}"
    MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysqldump --user=root \
        --single-transaction --quick --routines --triggers --events \
        --hex-blob --no-tablespaces --set-gtid-purged=OFF \
        --default-character-set=utf8mb4 "$MYSQL_DATABASE"
' | gzip -c > "$tmp"
gzip -t "$tmp"
# 같은 초에 두 백업이 시작되어도 기존 완료 파일을 덮어쓰지 않는다.
# 임시 파일을 같은 출력 폴더에 만들었으므로 같은 파일시스템의 hard link이다.
ln -- "$tmp" "$dest"
(cd -- "$out" && sha256sum "$name" > "$name.sha256")
echo "백업 생성: $dest"
echo '백업 메타데이터와 별도 DB 복원 결과를 기록하세요.'
