# [Art] 태블릿 가이드 일러스트 제작

- 담당: 송승우
- 날짜: 2026-09-14
- 이슈: S15P21B202-521

## 목적

게임 목표와 장비 조작을 긴 글 없이 이해하도록 기존 토끼 우주인 디자인을 참고한 디지털 단색 선화 4장을 제작한다.

## 산출물

`METAGAME/Assets/_Project/UI/Resources/TabletGuide/`의 PNG 4장 및 각 meta, 폴더 meta.

- Page1: 흩어진 파츠 → 우주선 조립 → 발사. 엔진·원반·캡슐·상단 견인 모듈 순서.
- Page4: 고정못 잡기 → 클릭 유지 + S로 뒤로 당기기 → 못과 줄이 분리되고 파츠가 살짝 호버링.
- Page2: 몽둥이를 들고 운석을 쳐내기.
- Page3: 흔들리는 파츠 → 떨어져 공중에서 레이저로 이음새 용접 → 안정된 연결.

화면 순서는 Page1 → Page4 → Page2 → Page3이다. S는 기본 뒤로 이동 키의 예시다.

## 제작 방식

built-in image_gen으로 생성/편집했다. 별도 API 호출이나 Tripo 유료 크레딧은 사용하지 않았다. 기존 선화의 색감과 캐릭터 형태를 유지했다.

프롬프트: Wide 3:1 game tablet tutorial, pale cyan-white bold hand-drawn rabbit astronaut line art, smooth dark teal digital background, clear action arrows, no chalkboard texture. Assembly/find/launch; bat/deflect meteor; unstable stack/ranged laser weld/stable stack; grip anchor pin/hold mouse and move backwards S/released pin and hovering part. Welding middle scene must show a floating rabbit separated from the seam with a visible laser. Final pin scene must have a clear gap with NO tether connecting pin and part.

## 검증

- 사용자가 그림체와 수정본을 확인했다. 마지막 고정못 그림의 줄 연결을 제거해 재확인했다.
- PNG와 meta가 함께 존재하며 비정사각 비율 유지와 clamp wrap 설정을 사용한다.
- 태블릿 프리팹 렌더와 리소스 로드 테스트는 후속 UI 작업과 통합해 확인했다. 이 아트 브랜치 자체는 그림을 표시하는 코드를 추가하지 않는다.

## 범위와 머지 순서

아트 파일과 이 기록만 포함한다. 씬·프리팹·물리·게임 조작은 바꾸지 않는다. 이 아트 MR을 develop에 먼저 머지한 뒤 후속 태블릿 UI MR을 검토한다.
