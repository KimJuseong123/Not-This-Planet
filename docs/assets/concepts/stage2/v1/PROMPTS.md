# Stage2 디자인 프롬프트 모음

[묶음 안내](README.md)

> v1 제작 기록입니다. 전동 체결기와 고정 타워 프롬프트는 이전안이며 최신 기획에 그대로 사용하지 않습니다. [최신 방향과 후속 시안 목록](../README.md)을 먼저 확인하세요.

이 파일은 같은 디자인을 이어서 다듬기 위한 재사용 프롬프트다. 아래 한국어 설명은 제작 의도이며 실제 기능 구현을 의미하지 않는다. 단일 시점 이미지로 보이지 않는 뒷면은 생성 과정에서 추정된다.

## 이미지 시안 공통 기준

- Use case: stylized-concept.
- Clean stylized 3D game asset, smooth painted metal, broad rounded bevels, restrained edge wear, large readable panels, not clay or gritty realism.
- Ivory enamel, muted orange, desaturated teal and charcoal; small cyan indicators.
- Single isolated object, complete silhouette, three-quarter near-orthographic view, generous margins, neutral light-gray studio background.
- No characters, text, logos, watermark, inset views, collage, environment, dramatic shadow or particle effects.
- Preserve the selected reference image's silhouette, color blocks and attachment points. Do not add unrelated accessories.

## 장면 컨셉 재사용 프롬프트

A charming stylized cooperative game scene on ruined Earth: an abandoned human rocket launch site with a small ivory cylindrical rocket, orange identification bands and charcoal connection seams. A fixed-pedestal jib crane uses a horizontal boom, travelling trolley and vertical hook cable to lower the rocket's top capsule. A separate teal service tower with orange ladders and generous platforms stands beside the rocket, with an open rocket-facing work edge. Four ivory-and-violet rabbit astronauts cooperate: crane operation, fastening bolts, ground guidance, and spraying a teammate with pale aqua decontamination fluid. Match the supplied actual rabbit astronaut reference, not generic humans. A damaged alien craft sits at the outskirts; ruined human skyline and limited toxic leakage stay in the background. Warm daylight, readable work surfaces, softened distant blue-violet atmosphere, rounded painted metal, whimsical not horror, no UI or labels. Frame the entire crane and launch site.

## Tripo 보조 설명 / 개별 디자인 프롬프트

Tripo의 텍스트 보조 입력 기능이 제공되는 경우 선택한 물체 하나에 해당하는 설명만 사용한다. 아래 묘사는 물체 설계용이며 자동으로 게임 기능·피벗·Collider가 생긴다는 뜻이 아니다.

### 01 · 로켓 하단

A single detachable bottom engine module of a stylized human rocket. Ivory cylindrical body, muted orange band, charcoal circular upper coupling rim, one broad engine bell underneath and short attached landing supports. Broad rounded panel edges and small cyan status indicators. Preserve the reference design. No upper fuel tank, nose cone, scenery or detached accessories. Keep the upper interface centered and suitable for later manual mating-surface cleanup.

### 02 · 로켓 중단

A single straight cylindrical fuel-tank segment of a stylized human rocket. Ivory body, one muted orange stripe around the lower third, two dark recessed service panels and one small cyan light. Wide charcoal circular coupling rims at top and bottom with closed flat end surfaces. No fins, engine, nose cone or legs. This segment is separate from the other rocket modules; final diameter alignment will be done manually.

### 03 · 로켓 상단

A single conical human rocket crew capsule, ivory panels and muted orange lower band. Rounded truncated-cone silhouette, broad circular base, two narrow cyan windows, one flush side hatch and a small integrated lifting eye at the apex. No rocket body below, engines, external ladder, cables or environment. Preserve the attachment eye opening and simple readable silhouette.

### 04 · 제독 분사기

A single two-handed portable industrial decontamination water-cannon tool for a cute astronaut game. Desaturated teal stout barrel, broad circular charcoal nozzle with ivory rim and orange collar, large rear loop grip, separate chunky underside foregrip and compact integrated ivory liquid reservoir with a cyan level strip. No external hose, emitted water, fog, hands or pedestal. Keep both grip openings and the nozzle opening clear.

### 05 · 체결 도구

A single compact electric impact wrench, clearly a fastening tool and not a firearm. Ivory motor housing, orange rear cap and battery accents, charcoal rubber pistol grip, a short thick steel socket with a visible hexagonal opening, small cyan battery indicators. Broad rounded bevels and simplified readable panels. No drill bit, laser muzzle, hands or loose bolts.

### 06 · 오염 탱크

A single horizontal chemical storage tank on two attached low saddle supports. Rounded sage-gray cylindrical body, broad simple metal panels, restrained paint wear, dark thick elbow pipes, orange fittings and a squat top valve. Include one clearly open short outlet pipe for future game VFX. No gas, liquid, slime, clouds, puddles or ground slab.

### 07 · 작업 발판

A single-storey modular service-platform bay with thick desaturated teal columns and braces, a solid gray deck, orange safety rails and an attached orange ladder. Preserve a clear ladder landing and open rocket-facing working edge. Ivory structural caps, broad rounded bevels, restrained wear. No rocket, crane, character or multiple floors. Railings, ladder and structural members require separation and passage clearance checking after generation.

### 08 · 크레인 조립 참고

A complete fixed-pedestal rotating jib crane: teal vertical pedestal on a low circular bolted base, orange rotary bearing, orange cabin at the root of a horizontal orange box-section boom, short counterweight tail and exactly one travelling trolley with one vertical cable and hook. No vehicle, tracks, load or rocket. Clear mechanical boundaries between stationary base, rotating superstructure, trolley and hook. This image describes an assembled visual reference, not a working rig; moving meshes and pivots must be authored separately.

## 이번 마무리 작업의 실제 이미지 생성 프롬프트

아래 세 항목은 2026-09-17 내장 이미지 생성 도구에 전달한 프롬프트다. 참조 1은 `scene/stage2-launch-site.png`, 참조 2는 기존 `SF_Stage1_Spaceship_Robot.png` 프리뷰다. 앞서 완료된 시안은 위 재사용 프롬프트와 제공된 PNG를 기준으로 이어서 작업한다.

### 오염 탱크

```text
Use case: stylized-concept. ONE single-object 3D asset design draft intended as input to Tripo image-to-3D. Input image 1 is the Stage2 environment visual reference; image 2 is an actual existing game prop style reference. Extract only the specified subject; do not recreate the environment. Clean stylized painted metal, broad rounded bevels, restrained edge wear, simple large panel shapes and seams, charming industrial game prop, no clay texture, no gritty photorealism. Neutral seamless very light gray background, soft diffuse neutral studio lighting, subtle contact shadow only. Three-quarter near-orthographic product view, entire subject visible with generous margins. NO text, numbers, logos, watermarks, frames, character, labels, inset views, grid, floor scenery or loose accessories. One view of ONE object assembly only, clearly readable silhouette.
Subject: one compact horizontal industrial chemical storage tank with two attached low saddle supports, rounded stout cylindrical capsule-shaped tank, matte sage-gray/green painted metal, broad panels and restrained paint wear, two thick dark elbow pipes attached close to the side, squat capped valve on top and a small blank orange warning panel. One damaged short outlet pipe with unobstructed dark opening for future particle effects. NO liquid, slime, fog, fumes, particles, clouds, puddles, plants or ground slab. All pipes terminate within silhouette or clearly visible. Gray-sage body, charcoal pipework, muted orange fittings. Broad simple clean geometry for reconstruction. Keep tank and all attached supports entirely inside frame.
```

### 작업 발판

```text
Use case: stylized-concept. ONE single-object 3D asset design draft intended as input to Tripo image-to-3D. Input image 1 is the Stage2 environment visual reference; image 2 is an actual existing game prop style reference. Extract only the specified subject; do not recreate the environment. Clean stylized painted metal, broad rounded bevels, restrained edge wear, simple large panel shapes and seams, charming industrial game prop, no clay texture, no gritty photorealism. Neutral seamless very light gray background, soft diffuse neutral studio lighting, subtle contact shadow only. Three-quarter near-orthographic product view, entire subject visible with generous margins. NO text, numbers, logos, watermarks, frames, character, labels, inset views, grid, floor scenery or loose accessories. One view of ONE object assembly only, clearly readable silhouette.
Subject: ONE repeatable single-storey ROCKET SERVICE PLATFORM TOWER BAY. Muted desaturated industrial teal main four sturdy square columns and broad diagonal braces, one rectangular solid metal walkway deck at the top with thick orange safety rails on three sides. The fourth rocket-facing edge is intentionally open for service access. Attached thick orange vertical ladder on the near side reaches an obvious opening in the railing with two handrails above the landing; DO NOT terminate ladder into closed railing. NO roof, no crane, no rocket, no characters, no tall multi-floor tower, no terrain base. Squat one-storey modular gantry bay only, about 3m tall and 3m wide design intent, clearly stackable structural flat top/bottom column connections. Closed solid deck with a few broad tread strips rather than fine grate mesh. Simplified large cross braces do NOT block the ladder. Use soft broad rounded bevels, ivory connector caps, restrained wear and tiny cyan indicator on one column. Entire bay including ladder feet and all railings comfortably inside the frame. Three-quarter view shows ladder landing and open working edge. Construction design draft: visible chunky accessible architecture for cute short astronauts.
```

### 크레인

```text
Use case: stylized-concept. ONE single-object 3D asset design draft intended as input to Tripo image-to-3D. Input image 1 is the Stage2 environment visual reference; image 2 is an actual existing game prop style reference. Extract only the specified subject; do not recreate the environment. Clean stylized painted metal, broad rounded bevels, restrained edge wear, simple large panel shapes and seams, charming industrial game prop, no clay texture, no gritty photorealism. Neutral seamless very light gray background, soft diffuse neutral studio lighting, subtle contact shadow only. Three-quarter near-orthographic product view, entire subject visible with generous margins. NO text, numbers, logos, watermarks, frames, character, labels, inset views, grid, floor scenery or loose accessories. One view of ONE object assembly only, clearly readable silhouette.
Subject: ONE complete stylized FIXED-PEDESTAL ROTATING JIB CRANE, a mechanical assembly DESIGN REFERENCE for later separation into moving parts. Thick muted teal stationary vertical cylindrical pedestal on a low bolted circular foundation, large orange horizontal slewing bearing at its top. Above bearing a compact orange operator cabin at the root of a perfectly HORIZONTAL long orange box-section boom. Horizontal boom has a visible straight trolley rail beneath and exactly ONE wheeled teal hoist trolley midway along it. ONE vertical taut dark cable from that trolley descends to a chunky orange hoist block and open steel hook. Short charcoal counterweight tail opposite the long working boom. No angled telescopic truck crane, no truck, no tracks, no wheels, no A-frame gantry, no second hook, no rocket or lifted load. The operator cabin has a large blue-gray front window and a thick lower structural sill that partially blocks downward view; no operator. Distinct visual separations at the rotary bearing, trolley wheels, and hook so a modeler understands the three controlled axes: slew, trolley travel, hoist height. Simplified readable engineering, broad smooth bevels consistent with existing props, restrained edge wear. Frame ENTIRE horizontal boom tip, counterweight, pedestal foundation and hanging hook with at least 12 percent margin. Wide landscape composition, shallow elevated three-quarter view. Orange boom and cabin, desaturated teal pedestal and trolley, charcoal mechanical joints, tiny cyan indicators, ivory corner protectors. This is an assembled reference, not an exploded sheet. No printed axes, labels or dimensions.
```
