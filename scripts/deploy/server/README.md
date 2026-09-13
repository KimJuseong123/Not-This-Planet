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
printf 'DB_PASSWORD=...\nDB_ROOT_PASSWORD=...\nJWT_SECRET=...\n' > .env && chmod 600 .env   # 값은 새로 만든다 — JWT_SECRET 은 표준 base64 여야 한다(아래)
mkdir -p build/libs && cp <새 jar> build/libs/          # 또는 CI 배포 잡이 놓는다
docker compose up -d --build

# 4. nginx + 인증서
sudo cp <리포>/scripts/deploy/server/nginx/upgrade-map.conf /etc/nginx/conf.d/
sudo cp <리포>/scripts/deploy/server/nginx/limits.conf      /etc/nginx/conf.d/   # 로그인 속도 제한(-461)
sudo cp <리포>/scripts/deploy/server/nginx/metagame.conf /etc/nginx/sites-available/metagame
sudo ln -sf /etc/nginx/sites-available/metagame /etc/nginx/sites-enabled/
sudo certbot --nginx -d j15b202.p.ssafy.io      # 인증서 경로 줄을 certbot 이 채운다
sudo nginx -t && sudo systemctl reload nginx

# 5. 확인 — 로그인만 보지 말고 wss 까지
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://j15b202.p.ssafy.io/auth/guest \
  -H 'Content-Type: application/json' -d '{"deviceId":"probe","nickname":"probe"}'   # 200
```

## 비밀 값의 요건 (S15P21B202-284)

**`JWT_SECRET` 은 표준 base64 여야 한다.** `JwtProvider` 가 그 값을 base64 로 디코드한다.
URL-safe 변형(`-`·`_`)을 넣으면 백엔드가 **기동에 실패한다**:

```
io.jsonwebtoken.io.DecodingException: Illegal base64 character: '-'
  at com.metagame.backend.global.auth.JwtProvider.<init>(JwtProvider.java:21)
```

만드는 법: `python3 -c "import base64,os;print(base64.b64encode(os.urandom(48)).decode())"`.
`secrets.token_urlsafe` 는 쓰지 않는다(2026-09-11 여기서 한 번 걸렸다).

DB 비밀은 **MySQL 볼륨이 처음 뜰 때 박힌다.** 나중에 `.env` 만 바꾸면 백엔드가 접속하지
못한다 — 바꾸려면 볼륨을 지우고 다시 초기화해야 한다.

## 개발 서버의 비밀은 따로 만든다 (S15P21B202-284)

`install-dev.sh` 는 prod 의 `.env` 를 복사하라고 안내하지만, **복사한 뒤 세 값을 새로
만들어야 한다.**

| 키 | dev | 왜 |
| --- | --- | --- |
| `JWT_SECRET` | **새로** | 공유하면 **dev 에서 만든 토큰이 prod 에서도 유효하다.** dev 는 아무 계정이나 만들며 시험하는 곳이고, 거기서 받은 JWT 를 prod `/auth/verify` 에 내면 통과한다 — 서버를 나눈 목적이 그것을 막는 것인데 비밀을 공유하면 구멍이 그대로 남는다 |
| `DB_PASSWORD` · `DB_ROOT_PASSWORD` | **새로** | dev DB 가 털려도 prod DB 자격이 아니다. dev 볼륨은 새것이라 새 비밀로 초기화된다 |
| `RELAY_*` · `EDGEGAP_RELAY_TOKEN` | 공유 | 릴레이는 **의도적으로 한 벌**이다 — TCP 17779 하나를 쓰고, 두 번째를 띄우려면 포트를 또 열어야 한다(우리가 정할 수 없다) |

바꿨는지 확인은 **해시 앞 몇 자만** 견준다 — 값을 화면에 내지 않는다:

```sh
for k in JWT_SECRET DB_PASSWORD; do
  a=$(sudo grep "^$k=" ~/metagame/.env     | sha256sum | cut -c1-8)
  b=$(sudo grep "^$k=" ~/metagame-dev/.env | sha256sum | cut -c1-8)
  [ "$a" = "$b" ] && echo "$k: 같음(고쳐야 한다)" || echo "$k: 다름"
done
```

## 이 사본을 고칠 때

서버를 고쳤으면 **여기도 같이 고친다.** 어긋나면 이 폴더는 거짓이 된다. 반대 방향(여기를 고치고 서버에
반영)은 아직 자동화돼 있지 않다 — 서버가 정본이고 이곳은 사본이다.
