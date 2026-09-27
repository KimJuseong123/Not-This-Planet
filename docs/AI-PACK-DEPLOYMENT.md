# AI 추가팩 배포 담당자 인계

## 현재 상태
- 게임 내 다운로드/취소, MB·백분율 진행률, HTTPS manifest 해시 검증, ZIP 크기/SHA-256, 압축 경로·용량 제한, 필수 실행 파일 확인 후 설치 적용 구현.
- 팩은 EC2 `/var/www/metagame-ai-packs/<버전>/` 에 있고 `https://j15b202.p.ssafy.io/ai-packs/<버전>/` 으로 나간다(nginx 는 `runtime.zip` · `models.zip` · `pack-manifest.json` 셋만 연다). 2026-09-25 기준 최신은 `0.3.0-preview`.
- **게임은 서버의 최신 팩을 스스로 찾는다** (S15P21B202-744). 빌드에는 고정 주소 `…/ai-packs/latest-companion-v4/pack-manifest.json` 이 들어가고, 서버의 `latest-companion-v4` 는 최신 팩 폴더를 가리키는 링크다. 게임은 뜰 때 그 manifest 를 한 번 받아 설치기와 같은 규칙으로 검사하고, 그 바이트의 해시로 설치·최신 판단을 한다. 못 받으면 빌드에 박힌 대체값(CI 변수 `AI_PACK_MANIFEST_URL` · `AI_PACK_MANIFEST_SHA256`, 없어도 됨)을 쓴다. **팩을 바꿀 때 CI 변수와 빌드는 건드리지 않는다.**
- 게임 빌드와 모델은 분리. 모델을 게임 Assets에 복사하지 않음. 에디터는 저장소의 빈 `ai-pack-source.json` 을 쓰므로 최신 주소를 보지 않는다.

## 새 팩 올리기 (S15P21B202-744)

`tools/AICompanionService` 의 워커만 바뀌었으면 `runtime.zip` 만 새로 굽고 `models.zip` 은 앞 버전 것을 서버에서 하드링크로 이어 쓴다(복사·업로드 없음). manifest 는 `build_portable.py` 와 같은 형식으로, `version` 을 올리고 `models.zip` 항목은 앞 버전 것을 그대로 옮긴다.

```bash
V=0.4.0-preview; P=0.3.0-preview; D=/var/www/metagame-ai-packs
sudo install -d -o ubuntu -g www-data -m 755 $D/$V
ln $D/$P/models.zip $D/$V/models.zip            # 모델이 같을 때만
# scp 로 runtime.zip 을 먼저, pack-manifest.json 을 마지막에 $D/$V/ 로 올린다
sudo ln -sfn $V $D/latest-companion-v4          # 이것이 "게임에 내보내기" 다
```

- **링크를 마지막에 옮긴다.** 그 전까지는 게임이 새 폴더를 모른다. 되돌리려면 링크를 앞 버전으로 다시 옮긴다
- 앞 버전 폴더는 지우지 않는다 — 옛 링크를 받은 채 설치 중인 사람이 있을 수 있고, 대체값이 그것을 가리킬 수 있다
- 이미 설치한 사람은 다음에 게임을 켤 때 새 팩을 최신으로 보고, "AI 동료 추가" 에서 받는다. **설치가 끝나면 받아 둔 압축을 비우므로 `models.zip` 도 다시 받는다**(약 1.9GB)
- 워커 **프로토콜**(`companion-v4`)이 바뀌는 팩은 새 이름의 링크(`latest-companion-v5`)로 내보내고 게임 쪽 주소도 함께 바꾼다 — 옛 빌드는 옛 링크의 팩을 계속 받는다

## 2026-09-22 검증 기록
- Unity EditMode 필터 `AiPackInstallerTests|AiRuntimeTests|BuildStampTests`: 총 36개, 통과 35개, 실패 0개, 실제 모델 실행 opt-in 검사 1개 제외.
- Windows 개발용 빌드 성공, 오류 0개. 테스트 빌드: `C:/Users/SSAFY/Desktop/NotThisPlanet-installer-test-20260922/NotThisPlanet.exe`.
- 빌드의 `NotThisPlanet_Data/StreamingAssets/ai-pack-source.json` 포함 확인. 주소/해시는 의도적으로 비어 있음.
- 정정: 최초 복사는 Codex MSIX의 파일 경로 리디렉션 때문에 `AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/LocalCache/LocalLow/...`에 저장됨. 587개 파일 해시는 일치했지만 일반 게임이 읽는 실제 LocalLow 설치가 아니었음. 사용자 실행 진단 로그에서 세 필수 파일 모두 DirectoryNotFoundException, Codex 실행 진단에서는 성공했고 GetFinalPathNameByHandle로 실제 리디렉션 위치를 확인함. 로컬 설치 스크립트를 Windows 탐색기에서 직접 실행해 실제 사용자 경로에 설치한 뒤 게임에서 재검증 필요. 게임 내 다운로드 자체는 일반 게임 프로세스의 실제 경로를 사용함.
- 실제 HTTPS 다운로드, 다운로드 UI의 실행 화면, 설치 후 다중 PC 플레이는 아직 미검증. 아래 서버 연결 후 필수 검증을 수행할 것.

## 올릴 파일 3개
저장소 루트 `artifacts/ai-pack/p3/`의 아래 파일을 **같은 HTTPS 디렉터리**에 둔다.

| 파일 | 크기(bytes) | SHA-256 |
|---|---:|---|
| runtime.zip | 128469224 | 806c922b5895547ed352c4253f6c4a07179f2aad09e4597c250ab55e1f9cc977 |
| models.zip | 2207845192 | 499b2339762b36fc4fbe5057e5728dfe83095594cda4f5aeff390673162896d4 |
| pack-manifest.json | 최종 승인 후 달라짐 | 최종 파일로 계산 |

다운로드 약 2.18 GiB, 설치 약 2.46 GiB. ZIP과 압축 해제본을 임시 보관하므로 약 5.2 GiB 여유 공간 필요(기존 팩 별도). 기존 팩 교체 시 backup 폴더는 삭제하지 않으므로 나중에 확인 후 정리.

## 담당자 순서
1. 모델/실행기/포함 라이브러리의 라이선스 및 필수 고지 검토, 새 PC 실행 확인.
2. 배포할 manifest 복사본의 `release_approved`를 승인 후 true로 변경하고 blockers를 최신화. ZIP 파일 자체를 바꾸면 크기/해시도 다시 계산.
3. 버전별 경로에 위 3개 업로드. 기존 버전 URL 내용을 교체하지 않는 방식 권장.
4. HTTPS 유효 인증서, 공개 GET 접근, 로그인·HTML 다운로드 페이지·리다이렉트 없이 직접 파일 응답 확인. ZIP 2GB 이상 지원. 임의 압축/변환 금지. 인증 쿼리 URL은 현재 미지원.
5. 최종 manifest의 SHA-256 계산:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '배포용 pack-manifest.json의 실제 경로'
```

6. 게임의 `METAGAME/Assets/StreamingAssets/ai-pack-source.json` 설정 후 빌드:

```json
{
  "manifest_url": "https://YOUR-DOMAIN/ai-packs/VERSION/pack-manifest.json",
  "manifest_sha256": "최종 manifest의 SHA-256 64자리"
}
```

이미 빌드했다면 `NotThisPlanet_Data/StreamingAssets/ai-pack-source.json`을 같은 값으로 바꾼 뒤 그 전체 빌드 폴더를 배포해도 됨. manifest 바이트가 조금이라도 바뀌면 해시 재설정 필요. URL/해시는 비밀키가 아니며 신뢰된 게임 배포 경로로 전달해야 함. 별도 코드 서명/manifest 서명은 구현하지 않았음.

## 사용자 흐름
방장 로비의 방 목록 아래 `AI 동료 추가`(한 번에 한 명, 사람 포함 최대 4명, 줄의 `×`로 빼기) → 이 PC에 최신 팩이 있으면 창 없이 바로 추가 → 없으면 "AI 동료 기능을 사용하려면 설치가 필요합니다" 창(`확인`/`취소`, 확인이 0.15초보다 오래 걸리면 창부터 뜸) → `확인`하면 다운로드 → 검증/압축 해제 → 설치 → 실제 모델 준비(워커 Ready)까지 한 흐름으로 진행. 창에는 진행 중인지·몇 %·남은 시간만 보이고 버튼은 `재시도`(실패했거나 5초 넘게 멈췄을 때만 눌림)와 `취소`뿐 → 워커가 Ready가 되면 "완료되었습니다"와 `완료` 버튼 → `완료`를 누를 때 그 순간 자리를 다시 확인해 AI 추가(그사이 자리가 찼으면 이유만 표시).
`취소`는 AI 수를 바꾸지 않음. 방을 나가거나 방장이 바뀌면 진행 중인 설치는 취소됨. 배포 주소가 없는 빌드(개발/에디터)는 깔린 팩을 최신으로 보고, 팩도 없으면 "이 빌드에서는 설치할 수 없습니다"와 `취소`만 보임. 넣은 AI의 실행이 실패하면 목록 아래 `다시 시도`가 나옴.
사람끼리 음성 채팅은 팩 불필요. 방장이 아니면 `AI 동료 추가`가 막히고 이유("방장만 추가할 수 있습니다")가 표시됨.
설치 경로는 `%USERPROFILE%/AppData/LocalLow/Not This Planet/Not This Planet/AI/p3`.

## 실패와 안전 장치
- 주소 미설정/manifest 불일치/미승인/비호환: 설치하지 않고 안내.
- 다운로드 중 취소/연결 실패: 고유 임시 폴더 정리. 끝까지 받은 ZIP은 설치 폴더 옆 `.download/<sha256>.zip`에 남고, 다시 누르면 크기+SHA-256이 manifest와 맞는 것만 다시 쓰고 나머지만 받음. 받다 끊긴 파일은 처음부터(HTTP Range 재개는 미지원). 설치가 끝나면 `.download`를 비움.
- 디스크 부족/ZIP 해시 불일치/경로 이탈/심볼릭 링크/압축 용량 초과/필수 파일 누락: 적용 금지.
- 설치 중 기존 실행기 중지. 검증 전에는 기존 팩을 바꾸지 않음. 마지막에 같은 디스크에서 폴더 교체, 기존 팩은 `.backup-...`에 보존.
- 게임 강제 종료 시 `.install-...` 잔여 폴더가 남을 수 있음. 다음 설치는 새 임시 폴더 사용. 설치 중인 다른 게임 인스턴스와 동시 설치는 피할 것.
- 설치 완료는 모델 정확도/모든 게임 행동 완료를 뜻하지 않음. 로컬 health 준비도 별도 확인.

## 실제 서버 연결 후 필수 검증
1. 팩 없는 Windows PC에서 `AI 동료 추가` → 설치 창 `확인` → 다운로드·설치·준비 → "완료되었습니다" → `완료` → V 명령.
2. 다운로드 중 취소 후 재시도, 네트워크 끊김, 디스크 부족.
3. manifest 해시 1자리 변경/ZIP 손상 시 거절.
4. 기존 팩 보존, 재시작 후 설치 인식, 참가자에게 불필요한 다운로드 강요 없는지.
5. CDN에서 2GB models.zip 전송 시간/타임아웃/비용 확인. 실제 호스팅 환경의 end-to-end 검증은 아직 미실시.
