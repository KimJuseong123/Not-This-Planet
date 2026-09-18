# 배수 압력 역류 — 컨셉과 제작 기준

2026-09-18 요청: 가스 외에 Stage2 작업을 방해하는 새 환경 요소.

`scene-concept.png`는 현장 연출, `front.png`, `left.png`, `right.png`, `back.png`는 에셋 하나의 방향별 별도 PNG이다. 내장 imagegen으로 먼저 생성하고, 확인한 다음 Blender MCP에서 모델링했다.

노후 펌프의 압력이 올라가 경고등과 압력계가 반응하고, 주황 배출구에서 고압 물줄기가 짧게 분출한다. 피격은 오염을 추가하지 않고 작업자를 밀친다. 전면의 주황 손잡이는 분출 축에서 옆으로 떨어진 감압 장치다. 솔로도 E 한 번으로 45초 억제할 수 있다.

디자인 기준: 아이보리 축압 탱크, 청록 원심 펌프, 주황 플랜지 배관, 황색 경고등, 원형 압력계, 5개 살 손잡이, 검정/노랑 받침대. 생성된 방향별 이미지 사이 배관 세부 차이는 정면을 기준으로 통일했다. 모델은 참고 이미지의 리얼한 마모를 단순화한 게임용 형상이며 물은 런타임 파티클이다.

생성 프롬프트: abandoned Earth cooperative rabbit astronaut launch yard, aging drainage surge pump, horizontal high-pressure WATER jet, teal centrifugal pump, ivory vertical accumulator, orange flanged outlet, analog gauge, amber beacon, red-orange five-spoke relief wheel, steel skid, weathered industrial materials. 현장 이미지는 배수로와 우주선 조립장을 포함한다. 각 방향 이미지는 현장 이미지를 참조해 만든 정면을 기준으로 single front/left/right/back orthographic view, white background, entire isolated asset, no people/water/environment, one view only 조건으로 생성했다.

모델: `METAGAME/Assets/_Project/Models/Stage2/S2_SurgePump.fbx`, 동명 `.fbm`의 PNG 7장, `Source~/S2_SurgePump.blend`. `ReliefWheel`, `GaugeNeedle`, `OutletFlap`, `WarningBeacon`, `JetOrigin` 피벗 보존. Blender 기존 사용자 씬은 유지했다.
