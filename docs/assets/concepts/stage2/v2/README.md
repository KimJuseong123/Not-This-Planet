# Stage2 컨셉아트 v2 — 컨테이너와 유독 가스

작성: 2026-09-17 · 2D 시안 · 내장 이미지 생성 도구 사용

[전체 자료 안내](../README.md) · [최신 기획서](../../../../planning/chapter-2-plan.md)

![컨셉아트](stage2-containers-gas.png)

## 반영한 방향

- 로켓 파츠를 보관함에 수납하고, 보관함을 연 뒤 크레인으로 인양한다.
- 상부가 열리는 컨테이너로 지붕에 와이어가 막히지 않는 모습을 제시한다.
- 크레인 기둥 옆 승강기로 조종석에 접근하는 방향을 추가한다.
- 고정 작업 타워 대신 옮겨 쓸 수 있는 작업 사다리를 배치한다.
- 총 같은 전동 체결기 대신 수동 스패너를 사용하는 장면으로 변경한다.
- 맵 주변과 컨테이너 사이에 낮은 황록색 유독 가스가 퍼진다.
- 방독면을 집을 수 있는 보관대를 추가한다.

## 게임 규칙 제안

방독면은 일정 시간 신규 오염 누적을 막거나 줄이는 예방 장비, 제독 분사기는 이미 쌓인 표면 오염을 제거하는 지원 장비로 역할을 나눌 수 있다. 방독면으로 기존 오염을 지우지는 않는 방향이다. 효과량·지속 시간·재충전·개수는 미정이며, 실제 방독면의 안전 성능을 설명하는 문서가 아니다.

마우스를 원형으로 움직여 체결하는 조작은 정지 컨셉아트에 직접 표현되지 않는다. 별도 상호작용 설계와 테스트가 필요하다.

## 그림과 실제 제작의 구분

- 이번 그림은 배치·분위기 후보이며 기능 구현이 아니다. 이후 S15P21B202-578에서 관련 논의를 기획서에 반영했으며 규칙은 기획서를 기준으로 한다.
- 생성 결과에는 인원과 컨테이너가 프롬프트보다 추가되었다. 지원 인원수나 필수 파츠 수 변경을 뜻하지 않는다.
- 크레인 훅이 팔 끝에 달린 표현은 구도상의 오차다. 제안된 트롤리 방식을 채택하면 반경 조절용 훅은 이동 트롤리에 연결해야 한다. 트롤리 채택 자체는 아직 외형 후보이다.
- 승강기 상부와 회전 조종석을 잇는 통로, 사다리 설치 면적, 파츠와 컨테이너 간 치수는 아직 검증하지 않았다.
- 스패너는 수동 공구의 방향성을 보여주며, 최종 몽키스패너의 조절 턱 구조는 개별 에셋 시안에서 구체화한다.
- 분사 장면의 명중 대상은 아직 명확하지 않다. 게임의 제독 대상은 팀원이다.
- 전체 컨셉아트는 Tripo의 개별 물체 입력용 이미지가 아니다. 컨테이너·승강기·이동 사다리·몽키스패너·방독면의 개별 변환 시안은 후속 작업이다.
- v1 및 기존 개별 에셋 시안은 보존했다. Tripo 요청·과금·3D 생성·Unity 변경은 수행하지 않았다.

## 참조

- [v1 장면](../v1/scene/stage2-launch-site.png): 수정 전 참고.
- `docs/assets/previews/SF_Rabbit_Base.png`: 기존 캐릭터 정체성 참조.

## 실제 생성 프롬프트

```text
Use case: precise-object-edit / stylized-concept. Rework input image 1 into a NEW VERSION of this same game's Stage 2 environment concept, incorporating the specific gameplay changes below. Image 1 is the previous environment design to revise; image 2 is the actual rabbit astronaut identity to preserve. Preserve the charming stylized 3D quality, ivory/orange human rocket design, muted teal industrial structures, ivory-violet cute rabbit astronauts, rounded bevels, readable surfaces and abandoned Earth setting. Do not preserve obsolete fixed access tower layout. Create ONE cohesive wide landscape 16:9 high oblique overview, not a collage, no labels or UI. Frame the whole crane boom and playable site without clipping.

Main layout: central circular launch pad with bottom and middle of a small HUMAN ROCKET already standing. A compact fixed-pedestal orange horizontal-jib crane behind-right has a travelling trolley and vertical cable lifting a separate conical top capsule from an OPEN STORAGE CONTAINER nearby, not already hanging above the rocket. Show a clear plausible swept path from opened container to pad. The small site is surrounded by broken concrete perimeter walls, ruined human industrial skyline and distant cooling towers, with the damaged ivory-purple alien craft at edge.

NEW REQUIRED FEATURE 1: exactly THREE substantial rectangular industrial ROCKET PART STORAGE CONTAINERS arranged around the central workspace within crane reach, ivory-teal bodies, orange latches and broad side panels. They are purpose-built low cargo coffers, not ordinary tiny boxes: each can contain a full rocket segment. TWO coffers have split roof lids hinged outward fully exposing the top for vertical crane extraction, wide doors open, padded interior cradles visible. The capsule is being hoisted visibly out of one; the other shows an empty cradle because its part is already on the pad. The third container is closed with a big approachable latch. Make containers one of the main visual features in the foreground/midground. No roof blocking the crane cable. No extra full duplicate rockets.
NEW REQUIRED FEATURE 2: a clearly recognizable small industrial ELEVATOR CAGE runs on two vertical rails attached beside the crane's FIXED pedestal, stopped partway up, with ground gate and small circular landing below rotating operator cabin. Cabin at boom root, visible rabbit operator. Elevator and its shaft must be visually distinct from a ladder.
NEW REQUIRED FEATURE 3: REMOVE the large fixed rocket access tower entirely. Replace it with ONE portable tall orange-and-teal folding A-frame WORK LADDER beside the rocket, with wide stabilizing feet and a little top work platform, independent of rocket and ground fixtures. A rabbit astronaut on it uses a BIG conventional adjustable MONKEY WRENCH on an oversized hex bolt on the rocket seam. Wrench visibly has an open adjustable jaw and a long straight handle: NOT pistol or powered drill. A second folded portable ladder lies on a small wheeled handcart nearby to convey portability, not a permanent staircase.
NEW REQUIRED FEATURE 4: poison gas is a substantial environmental mood, with soft translucent yellow-green gas sheets flowing across several low ground areas and between containers, denser near damaged tanks and perimeter, leaving central work surfaces and character silhouettes readable. Slightly cool cloudy post-apocalyptic daylight with warm industrial lamps, more contaminated and atmospheric than original golden clean scene, but NOT horror, no dirty photographic grunge, no uniform opaque green filter.
NEW REQUIRED FEATURE 5: two small wall-mounted emergency RESPIRATOR MASK LOCKERS at different accessible locations, one prominently in the near foreground with lid open and two clearly visible chunky gray masks with round filter cartridges and orange straps. These are temporary protective pickups. Show one ground rabbit wearing a little filter-mask attachment over the lower faceplate while opening the third container; retain recognizable purple face and cyan eyes. No text/timers needed.
Four rabbit teammates total: crane operator, wrench worker on portable ladder, mask-wearing container opener, ground support carrying a teal firefighting-style decon sprayer. If showing spray, aim it at a TEAMMATE, not the rocket. Avoid tiny fiddly details; prioritize legible containers, elevator cage, moveable ladder, hand wrench, toxic atmosphere and mask station.
This is art direction and gameplay staging, not a precise architectural plan. No diagrams, labels, typography, watermark or mechanical control arrows. Entire site and full crane framed with breathing room.
```
