# 압력 역류 검증

Unity 6000.5.4f1 / Windows / URP. 화면 직접 조작 없이 배치 모드와 독립 프로세스로 실행했다.

- `tests.xml`: 29/29 통과. 압력 역류 6개와 기존 씬·조립·가스·제독·배수로 회귀 검사.
- `tests-targeted.xml`: 최종 배치와 효과 재활성화 수정 후 압력/씬 검사 8/8 통과.
- `tests-render.xml`: 효과 재활성화 이후 실제 입자가 다시 생기는지 및 분출/정지 검사, 분출 장면 촬영.
- `network-smoke.json`: Windows 개발 빌드 성공 후 Mirror Telepathy 로컬 호스트·원격 클라이언트·도중 합류 관찰자 3개 프로세스. 경고/분출 복제, 인증된 원격 감압, 감압 결과, 도중 합류, 연결 해제의 6개 판정 통과. 외부 인터넷 지연·패킷 손실·4인 플레이는 측정하지 않았다.
- `pump-0.png`: 실제 Play Mode 분출과 바닥 경고선. `pump-1.png`, `pump-2.png`: 같은 시점에 대기 중인 다른 두 펌프와 주변 배치.

재실행: `Stage2Validation.BuildNetworkSmoke`로 개발 빌드를 만든 뒤 `tools/validation/stage2-pressure-smoke.ps1`. 테스트 실행 필터는 `Stage2PressureTests;Stage2WaterTests;Stage2ArtTests;Stage2ContaminationTests;Stage2Tests`.

신규 렌더/플레이 오류 없음. 빌드 과정의 URP 셰이더 사전 필터 캐시는 되돌렸다. 기존 Mirror Weaver의 BouncyCastle 해석 경고 및 초기 라이선스 채널 재연결 로그는 있었으나 컴파일/빌드/테스트는 성공했다. 플레이어 빌드 폴더와 원시 로그는 커밋 대상에서 제외한다.
