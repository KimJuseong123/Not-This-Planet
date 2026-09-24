# AI 추가팩 배포 담당자 인계

## 현재 상태
- 게임 내 다운로드/취소, MB·백분율 진행률, HTTPS manifest 고정 해시, ZIP 크기/SHA-256, 압축 경로·용량 제한, 필수 실행 파일 확인 후 설치 적용 구현.
- 서버 업로드/공개 URL 설정은 아직 하지 않음. `Assets/StreamingAssets/ai-pack-source.json` 값은 비어 있음.
- 현재 팩은 내부 preview이며 `release_approved: false`. 재배포 라이선스/고지 검토와 깨끗한 PC 확인을 마친 뒤 담당자가 승인해야 함. 승인 전 팩을 인터넷 설치기로 배포할 수 없도록 거부함.
- 게임 빌드와 모델은 분리. 모델을 게임 Assets에 복사하지 않음.

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
방장 로비 → AI 추가팩 다운로드 → 진행률 → 검증/압축 해제 → 설치 완료 → 방 목록 아래 `AI 동료 추가`로 한 명씩(사람 포함 최대 4명, 줄의 `×`로 빼기) → 실제 모델 준비 완료 후 고른 수만큼 AI 생성.
사람끼리 음성 채팅은 팩 불필요. 방장이 아니면 현재 로비 설치 버튼은 표시되지 않음.
설치 경로는 `%USERPROFILE%/AppData/LocalLow/Not This Planet/Not This Planet/AI/p3`.

## 실패와 안전 장치
- 주소 미설정/manifest 불일치/미승인/비호환: 설치하지 않고 안내.
- 다운로드 중 취소/연결 실패: 고유 임시 폴더 정리. 끝까지 받은 ZIP은 설치 폴더 옆 `.download/<sha256>.zip`에 남고, 다시 누르면 크기+SHA-256이 manifest와 맞는 것만 다시 쓰고 나머지만 받음. 받다 끊긴 파일은 처음부터(HTTP Range 재개는 미지원). 설치가 끝나면 `.download`를 비움.
- 디스크 부족/ZIP 해시 불일치/경로 이탈/심볼릭 링크/압축 용량 초과/필수 파일 누락: 적용 금지.
- 설치 중 기존 실행기 중지. 검증 전에는 기존 팩을 바꾸지 않음. 마지막에 같은 디스크에서 폴더 교체, 기존 팩은 `.backup-...`에 보존.
- 게임 강제 종료 시 `.install-...` 잔여 폴더가 남을 수 있음. 다음 설치는 새 임시 폴더 사용. 설치 중인 다른 게임 인스턴스와 동시 설치는 피할 것.
- 설치 완료는 모델 정확도/모든 게임 행동 완료를 뜻하지 않음. 로컬 health 준비도 별도 확인.

## 실제 서버 연결 후 필수 검증
1. 팩 없는 Windows PC에서 버튼 → 다운로드 → 완료 → 모델 Ready → V 명령.
2. 다운로드 중 취소 후 재시도, 네트워크 끊김, 디스크 부족.
3. manifest 해시 1자리 변경/ZIP 손상 시 거절.
4. 기존 팩 보존, 재시작 후 설치 인식, 참가자에게 불필요한 다운로드 강요 없는지.
5. CDN에서 2GB models.zip 전송 시간/타임아웃/비용 확인. 실제 호스팅 환경의 end-to-end 검증은 아직 미실시.
