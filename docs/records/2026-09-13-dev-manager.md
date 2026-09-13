# 개발자 단축키 통합

`EndingDebugShortcut`을 `DevManager`로 변경하고 F11/F12 입력을 한 파일로 모았다. 기존 스크립트 GUID는 유지한다.

## 설정

- `Main`, `Stage1_Astra` 씬의 `SpaceBootstrap` 오브젝트에서 `DevManager` 컴포넌트를 선택한다.
- `Enable F11 Perspective`: 로컬 플레이어의 1인칭/3인칭 전환 단축키.
- `Enable F12 Ending`: 엔딩 테스트 확인창 단축키. 기존처럼 Stage1_Astra에서만 열리고 멀티플레이 실행은 호스트만 허용한다.
- 두 필드는 기본적으로 켜져 있어 기존 동작을 유지한다. 컴포넌트 자체를 끄면 두 단축키가 모두 중지된다.
- 플레이 전에 씬에서 설정하고 저장하면 다음 실행에도 적용된다. 실행 중 Inspector 변경은 해당 실행에만 적용된다.
- 에디터와 Development Build에서만 단축키 로직이 컴파일된다.

## 변경 범위

일반 입력, 카메라 전환 구현, 실제 파츠 조립 및 엔딩 판정, 네트워크 권한 로직은 유지한다. `GameSession`에서 F11 입력 부분만 제거하고, `SpaceBootstrap`이 같은 오브젝트의 `DevManager`에 세션을 연결한다. F12를 끄거나 컴포넌트를 비활성화하면 열린 확인창의 입력·커서·일시 메뉴 상태를 복구한다.

## 검증

- Unity 생성 참조를 사용한 C# 컴파일: Gameplay/Bootstrap 에디터 조건 통과, 같은 참조에서 개발·배포 전처리 조건 통과.
- 수정한 Editor 테스트 어셈블리 컴파일 통과.
- 기존 엔딩 테스트에 F12 비활성 시 진입 차단 및 열린 창을 끌 때 입력 복구 검증을 추가했다.
- 복사 프로젝트 `outputs/tractor-tests`에서 Unity 6000.5.4f1 헤드리스 테스트 `DebugPerspectiveTests`, `EndingSceneTests` 11/11 통과. 결과는 `.local-validation/dev-manager.xml`, 실행 로그는 `.local-validation/dev-manager-final.log`에 기록했다.
- 실제 키 입력과 화면 동작은 별도 Play Mode 확인이 필요하다.
