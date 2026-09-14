# 개인 화면 키 안내 HUD

`GameHud`의 좌측 하단에 `KeyGuideHolder` 목록을 표시한다. 기존 코드 기반 IMGUI와 `UiScale`을 사용하므로 씬·프리팹·Inspector 설정은 필요 없다.

- 태블릿: `GameSession.CanToggleTablet`이 참이면 표시한다. 실제 태블릿 입력도 이 조건을 사용한다. 태블릿이 열려 있어도 닫기 키 안내를 유지한다.
- 던지기: 로컬 플레이어가 투척용 도구를 들고 있으며 입력이 가능한 상태에서 표시한다. 참가자의 서버 동기화 도구는 `PresentationTool.IsHeldForView`를 참조한다. 메뉴, 태블릿, 커서 해제, 기절, 탑승, 엔딩, 자동 입력 중에는 숨긴다.
- 키 문구: 개인 설정이 적용된 `Controls.Tablet` / `Controls.ToolAction`을 조회한다. 기본값은 `TAB` / `Q`이며, 변경한 키는 다음 HUD 갱신에 반영된다.
- 확장: `hud.KeyGuides.Register("id", "동작 이름", () => 현재키, () => 사용가능조건)`을 호출한다. 같은 ID는 기존 위치에서 교체하며 `Remove(id)`로 제거한다. 긴 키 이름에 맞춰 너비를 조정하고 공간이 부족하면 위 행으로 배치한다. 안내는 입력을 소비하지 않는다.

검증: Unity 6000.5.4f1의 참조 DLL과 현재 소스로 Core, Gameplay, UI 어셈블리를 별도 컴파일했다. `KeyGuideHolderTests`의 4개 테스트를 독립 실행기로 실행하여 통과했다. Unity Test Runner, Play Mode의 시각적 배치 및 실제 두 피어 검증은 수행하지 않았다.

수동 확인: 빈손의 TAB 표시 → 도구 집기 시 Q 추가 → 놓기/투척 후 Q 제거 → 태블릿 및 메뉴 열기/닫기 → 키 설정 변경 → 참가자 화면에서도 본인 도구에만 안내가 나타나는지 확인한다.
