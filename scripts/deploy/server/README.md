# EC2 서버 설정 사본 (반입 2026-09-01)

여기 있는 파일은 **EC2(`j15b202.p.ssafy.io`)에서 실제로 돌고 있는 설정의 사본**이다. 서버에만 있으면
인스턴스를 날렸을 때 아무도 복원할 수 없으므로 git 에 둔다. 배포 방식의 결정 근거는
[docs/records/S15P21B202-139-cd-strategy.md](../../../docs/records/S15P21B202-139-cd-strategy.md).

| 파일 | 서버 위치 | 무엇 |
| --- | --- | --- |
| `compose.prod.yml` | `~/metagame/docker-compose.yml` | 운영 compose — mysql + backend. 백엔드는 **`127.0.0.1:8080` 에만** 바인딩한다(도커는 UFW 를 우회하므로 이게 유일한 방어) |
| `nginx/metagame.conf` | `/etc/nginx/sites-available/metagame` | TLS 종료(certbot) + `127.0.0.1:8080` 프록시. **WebSocket 헤더 4줄이 핵심** |
| `nginx/upgrade-map.conf` | `/etc/nginx/conf.d/upgrade-map.conf` | `$connection_upgrade` map. 서버 블록보다 먼저 로드돼야 한다(http 컨텍스트) |

**시크릿은 여기 없다.** `~/metagame/.env`(권한 600)에 `DB_PASSWORD` · `DB_ROOT_PASSWORD` · `JWT_SECRET`
세 개가 있고 값은 서버에만 존재한다. compose 는 전부 `${}` 로 참조하므로 이 사본에는 값이 하나도 없다.

## 서버를 새로 세울 때

```bash
# 1. 도커
curl -fsSL https://get.docker.com | sh && sudo systemctl enable --now docker

# 2. 방화벽 — 22·80·443 만. enable 유지(지급 지침)
sudo ufw allow 22 && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw enable

# 3. 앱
mkdir -p ~/metagame && cd ~/metagame
cp <리포>/scripts/deploy/server/compose.prod.yml docker-compose.yml
cp <리포>/backend/Dockerfile .
printf 'DB_PASSWORD=...\nDB_ROOT_PASSWORD=...\nJWT_SECRET=...\n' > .env && chmod 600 .env   # 값은 새로 만든다
mkdir -p build/libs && cp <새 jar> build/libs/          # 또는 CI 배포 잡이 놓는다
docker compose up -d --build

# 4. nginx + 인증서
sudo cp <리포>/scripts/deploy/server/nginx/upgrade-map.conf /etc/nginx/conf.d/
sudo cp <리포>/scripts/deploy/server/nginx/metagame.conf /etc/nginx/sites-available/metagame
sudo ln -sf /etc/nginx/sites-available/metagame /etc/nginx/sites-enabled/
sudo certbot --nginx -d j15b202.p.ssafy.io      # 인증서 경로 줄을 certbot 이 채운다
sudo nginx -t && sudo systemctl reload nginx

# 5. 확인 — 로그인만 보지 말고 wss 까지
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://j15b202.p.ssafy.io/auth/guest \
  -H 'Content-Type: application/json' -d '{"deviceId":"probe","nickname":"probe"}'   # 200
```

## 이 사본을 고칠 때

서버를 고쳤으면 **여기도 같이 고친다.** 어긋나면 이 폴더는 거짓이 된다. 반대 방향(여기를 고치고 서버에
반영)은 아직 자동화돼 있지 않다 — 서버가 정본이고 이곳은 사본이다.
