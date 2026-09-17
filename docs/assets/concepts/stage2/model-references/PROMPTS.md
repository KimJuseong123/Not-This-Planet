# 방향별 아트 재생성용 프롬프트

## 추가 7종 — 2026-09-17

후면 보정: RocketAssembled는 정면 창문과 점검문을 숨긴 아이보리 후면, Bracket은 삼각 보강대와 발판을 가리는 바깥쪽 뒷판, HelmetFilter는 흡입 그릴 대신 뒤쪽 장착 브리지와 연결구가 보이도록 정면 참조를 180도 회전해 다시 생성했다.

[추가 디자인 입력](additional-assets.json): RocketAssembled, BoltLoose, BoltTight, Nut, Bracket, HelmetFilter, FilterRack. 각 에셋 정면을 먼저 생성한 뒤 그 PNG를 참조하여 왼쪽·오른쪽·후면을 각각 별도 생성했다.

정면 지침: Single FRONT orthographic modeling reference. Same stylized abandoned Earth launchyard game art: ivory, teal metal, amber safety accents, restrained wear, neutral gray background. One object fully visible, exact head-on front view, no perspective, no text or labels, no multiview sheet. 여기에 JSON의 label 및 design을 붙였다.

다른 방향 지침: Create a single requested orthographic view of this exact asset. Preserve identical structure and materials. Side views rotate exactly 90 degrees; back rotates 180 degrees and hides front details. Nut side shows the thick edge, back shows the hole. Bracket sides show the L profile. Filter back shows the connection and sides show cartridge thickness. One object, neutral gray background, no text, no layout sheet. 정면 PNG를 참조 이미지로 첨부했다.

내장 ImageGen으로 생성. 외부 이미지·3D 서비스는 사용하지 않았습니다. 아래는 재현을 위한 공통 지침과 에셋별 디자인 명세이며 호출별 전문 로그가 아닙니다.

## 공통 지침

폐허가 된 지구의 산업 발사장. 절제된 낡은 아이보리·청록 강철·안전 주황색, 식생은 먼지 낀 올리브·황토색. 중립 회색 배경, 단일 오브젝트 전체가 보이는 이미지. 인물·문자·치수선·시트 분할 없음. 읽기 쉬운 외형과 게임 모델링 참고용 디테일. 정면을 먼저 만든 후 같은 에셋 정면 이미지를 참조하여 왼쪽, 오른쪽, 후면을 각각 별도 이미지로 생성. 대칭 오브젝트도 별도 파일을 생성하며 추가 오브젝트를 발명하지 않음.

단일 방향 지침: “Show ONLY the requested orthographic view of this exact asset. Preserve the same design, scale, proportions and materials. Rotate the object to the requested side; do not repeat the front view. One asset, one view, no labels, neutral background.”

## 에셋별 디자인 입력

### Crane

20m mast, 25m straight radial horizontal jib, sliding hoist trolley, fixed mid-mast operator balcony at 8m, teal steel orange guards, concrete base, hanging open safety hook. NO elevator in this asset; elevator separate.

초기 목표 치수(Blender XYZ): 28 × 4 × 21m. 실제 모델 치수는 모델 README 기준.

### Elevator

8m dual vertical rails, 3.5m square blue platform cage, orange entry gate, ground and upper landing gate, clear cage travel. Separate cabin from rails.

초기 목표 치수(Blender XYZ): 4 × 4 × 11m. 실제 모델 치수는 모델 README 기준.

### Console

waist-height teal pedestal console, three chunky lever axes, broad orange-edged lower sight shield, no words, meter panels.

초기 목표 치수(Blender XYZ): 1.5 × 0.8 × 1.3m. 실제 모델 치수는 모델 README 기준.

### Launchpad

6m circular low concrete-and-steel base, radial grille sectors, central 2.8m mounting circle, low orange rim, no tower or rocket.

초기 목표 치수(Blender XYZ): 6 × 6 × 0.45m. 실제 모델 치수는 모델 README 기준.

### RocketBase

2.8m diameter 2.1m tall squat ivory rocket base, orange belt, three dark engine bells below, stabilizer feet, flat upper ring connection.

초기 목표 치수(Blender XYZ): 3.4 × 3.4 × 2.1m. 실제 모델 치수는 모델 README 기준.

### RocketTank

2.8m diameter 3m high cylindrical ivory tank module, orange upper belt, flat top and bottom mating rings, three big hex bolt bosses across FRONT LOWER circumference at one height, accessible orange lifting eye on top. No engines.

초기 목표 치수(Blender XYZ): 2.8 × 2.8 × 3m. 실제 모델 치수는 모델 README 기준.

### RocketService

2.8m diameter 3m high ivory cylindrical service module, teal upper band, rectangular access panel, flat top and bottom rings, three prominent front lower seam hex bolt bosses at same height, orange top lifting eye. No engines.

초기 목표 치수(Blender XYZ): 2.8 × 2.8 × 3m. 실제 모델 치수는 모델 README 기준.

### RocketCapsule

2.8m diameter 3m high ivory rounded conical upper capsule, orange lower belt, blue oval porthole at front, circular lower mating flange with three accessible front lower bolts, orange top lifting eye. No engines.

초기 목표 치수(Blender XYZ): 2.8 × 2.8 × 3m. 실제 모델 치수는 모델 README 기준.

### Container

5m square 4m high teal-and-ivory industrial coffer, CLOSED horizontal roof lid with rear hinge and orange side latch, wide front access opening, internal cradle sized for upright rocket module. No contents.

초기 목표 치수(Blender XYZ): 5 × 5 × 4m. 실제 모델 치수는 모델 README 기준.

### Ladder

teal-and-orange free-standing A-frame ladder deployed to 4m work-platform height, wide stabilizer feet, locked small wheels, guardrail top platform, front entry opening, chunky rungs. No people or rocket.

초기 목표 치수(Blender XYZ): 2.6 × 3 × 5m. 실제 모델 치수는 모델 README 기준.

### Wrench

0.55m long thick steel open adjustable jaw and worm screw, teal rubber straight grip, orange butt ring. No power motor.

초기 목표 치수(Blender XYZ): 0.2 × 0.07 × 0.55m. 실제 모델 치수는 모델 README 기준.

### ToolRack

1.2m wide teal pedestal rack with backing plate, orange retaining pegs, empty wrench cradle and nozzle bracket, no tools stored.

초기 목표 치수(Blender XYZ): 1.2 × 0.55 × 1.4m. 실제 모델 치수는 모델 README 기준.

### Sprayer

teal firefighter water nozzle, thick rounded muzzle, pistol grip and orange trigger guard, short rear hose coupling only, no long trailing hose, not a weapon.

초기 목표 치수(Blender XYZ): 0.25 × 0.55 × 0.35m. 실제 모델 치수는 모델 README 기준.

### Shower

walk-through teal and ivory 2.4m tall emergency wash frame, overhead rinse shower, blue grated floor drain, accessible side orange control, no person or water VFX.

초기 목표 치수(Blender XYZ): 2 × 2 × 2.5m. 실제 모델 치수는 모델 README 기준.

### GasTank

rusty olive horizontal cylindrical industrial tank on two feet, broken side pipe nozzle and valve wheel, orange beacon, no smoke or gas rendered.

초기 목표 치수(Blender XYZ): 4 × 2 × 3m. 실제 모델 치수는 모델 README 기준.

### CrashedCraft

5m long small ivory and purple rounded capsule spacecraft lying on side, dark blue oval cockpit, missing wing exposing simple inner frame, bent short landing struts, no crash rubble pile.

초기 목표 치수(Blender XYZ): 4 × 5 × 3m. 실제 모델 치수는 모델 README 기준.

### Wall

4m wide by 2.5m tall low ruined concrete wall module, irregular broken top, two short rebar rods, ochre horizontal paint stripe, sparse ivy at one corner.

초기 목표 치수(Blender XYZ): 4 × 0.4 × 2.5m. 실제 모델 치수는 모델 README 기준.

### Drain

2m long shallow concrete channel with removable dark steel grate, open muddy slot and sparse reeds at one corner.

초기 목표 치수(Blender XYZ): 1 × 2 × 0.25m. 실제 모델 치수는 모델 README 기준.

### Slab

2m square broad flat concrete floor tile, shallow cracks and sparse moss in seams, beveled sides, walkable clear top.

초기 목표 치수(Blender XYZ): 2 × 2 × 0.15m. 실제 모델 치수는 모델 README 기준.

### Rail

2m wide rusted teal/orange guardrail with two horizontal rails and weighted concrete end feet, slightly bent one end, 1.1m high.

초기 목표 치수(Blender XYZ): 2 × 0.35 × 1.1m. 실제 모델 치수는 모델 README 기준.

### Bollard

single stout rounded orange-and-charcoal safety post 0.85m tall, low flange base with four bolts, restrained chipped paint.

초기 목표 치수(Blender XYZ): 0.35 × 0.35 × 0.85m. 실제 모델 치수는 모델 README 기준.

### Facade

4m wide 5m high thick concrete wall frame, large rectangular window grid with missing glass, irregular broken top and restrained rusty beams. No surrounding landscape.

초기 목표 치수(Blender XYZ): 4 × 0.55 × 5m. 실제 모델 치수는 모델 README 기준.

### CoolingTower

standalone 18m tall concrete hourglass cooling tower, open top rim with one damaged missing wedge and support columns beneath, simple industrial silhouette.

초기 목표 치수(Blender XYZ): 10 × 10 × 18m. 실제 모델 치수는 모델 README 기준.

### Chimney

single 12m tall tapering rusty brick-gray smokestack on square footing, faded orange band, chipped rim, no smoke.

초기 목표 치수(Blender XYZ): 2.5 × 2.5 × 12m. 실제 모델 치수는 모델 README 기준.

### Rubble

2m wide low pile of chunky broken concrete masonry, three rust beam fragments, no hazardous sharp spikes, no plants, silhouette under 0.7m.

초기 목표 치수(Blender XYZ): 2 × 1.5 × 0.7m. 실제 모델 치수는 모델 README 기준.

### Floodlight

2.6m high teal tripod on broad weighted feet, two orange square warm lamp heads, cable spool on base, no trailing cable.

초기 목표 치수(Blender XYZ): 1.2 × 1.2 × 2.6m. 실제 모델 치수는 모델 README 기준.

### RouteBlue

2m square flat grey concrete slab, single broad worn blue route stripe runs front to back, shallow chipped corners, no arrows or letters.

초기 목표 치수(Blender XYZ): 2 × 2 × 0.12m. 실제 모델 치수는 모델 README 기준.

### RouteAmber

2m square flat grey concrete slab, single broad worn safety amber route stripe runs front to back, shallow chipped corners, no arrows or letters.

초기 목표 치수(Blender XYZ): 2 × 2 × 0.12m. 실제 모델 치수는 모델 README 기준.

### GrassDry

small 0.35m high dense ochre dry grass tuft, broad stylized ribbon blades, sparse tiny stones at roots, earth plant non glowing.

초기 목표 치수(Blender XYZ): 0.5 × 0.5 × 0.35m. 실제 모델 치수는 모델 README 기준.

### GrassSeed

0.8m bent ochre grass seedhead cluster with slender stems and big simplified drooping seed grains, compact base.

초기 목표 치수(Blender XYZ): 0.65 × 0.65 × 0.8m. 실제 모델 치수는 모델 README 기준.

### WeedRosette

0.25m tall low dusty olive broadleaf weed rosette, thick serrated leaves radiating around central bud, stylized large leaves.

초기 목표 치수(Blender XYZ): 0.6 × 0.6 × 0.25m. 실제 모델 치수는 모델 README 기준.

### Wildflowers

0.45m sparse roadside cluster five small white daisy-like flowers with ochre centers and dusty olive leaves, rooted together.

초기 목표 치수(Blender XYZ): 0.45 × 0.45 × 0.45m. 실제 모델 치수는 모델 README 기준.

### ScrubDry

0.7m tall compact tangled brown shrub with sparse tiny dusty olive leaves and many bare thick simplified twig branches.

초기 목표 치수(Blender XYZ): 0.7 × 0.6 × 0.7m. 실제 모델 치수는 모델 README 기준.

### Shrub

0.8m tall compact stunted olive bush with chunky broad leaves, thick branching stems, asymmetric dome silhouette.

초기 목표 치수(Blender XYZ): 0.9 × 0.8 × 0.8m. 실제 모델 치수는 모델 README 기준.

### IvyLow

1.2m wide creeping ivy strip growing horizontally low across a tiny broken concrete base, clustered matte olive heart-shaped leaves, 0.25m high.

초기 목표 치수(Blender XYZ): 1.2 × 0.35 × 0.25m. 실제 모델 치수는 모델 README 기준.

### IvyHanging

1m wide by 1.5m tall hanging olive ivy vines draped vertically from a small narrow wall-cap fragment, leafy top tapering to trailing tendrils.

초기 목표 치수(Blender XYZ): 1 × 0.3 × 1.5m. 실제 모델 치수는 모델 README 기준.

### Moss

0.65m low irregular cracked concrete chip with dark olive moss cushions on surface, simple dense rounded clumps, only 0.08m tall.

초기 목표 치수(Blender XYZ): 0.65 × 0.5 × 0.08m. 실제 모델 치수는 모델 README 기준.

### Lichen

0.6m low rusted metal fragment with pale gray-green clustered lichen disks and ochre mottles, stylized non glowing, 0.07m tall.

초기 목표 치수(Blender XYZ): 0.6 × 0.45 × 0.07m. 실제 모델 치수는 모델 README 기준.

### Reeds

0.9m tall small green-brown reed clump with 4 dark brown cattail seedheads and broad upright ribbon leaves, no water plane.

초기 목표 치수(Blender XYZ): 0.55 × 0.55 × 0.9m. 실제 모델 치수는 모델 README 기준.

### DeadSapling

1.1m tall small dead forked tree stump with thick angular bark branches and only three remaining ochre leaves, compact roots.

초기 목표 치수(Blender XYZ): 0.7 × 0.55 × 1.1m. 실제 모델 치수는 모델 README 기준.

## 방향 보정 지침

- Wrench/left: Look exactly edge-on at the THIN LEFT EDGE of this flat wrench. The broad flat face must NOT be visible. Show only its 4cm thickness, edge of adjustable jaw and side of screw. Long handle remains horizontal. It should look like a narrow thin steel strip with thicker jaw profile, not the original broad spanner silhouette.
- Wrench/right: Look exactly edge-on at the opposite THIN RIGHT EDGE of this flat wrench. Hide broad flat face completely. Show only its narrow 4cm thickness, opposite side of jaw slider and worm screw. Long handle remains horizontal, no broad face.
- Sprayer/front: Look STRAIGHT INTO the round black spray MUZZLE from the front. A circular muzzle facing camera dominates top center, body and rear coupling occluded behind it, handle below. NOT a side view of the long barrel.
- Sprayer/back: Look STRAIGHT INTO the rear HOSE CONNECTOR from behind. Connector circle at upper center, barrel recedes behind it away from camera, pistol grip below. NOT a side profile.
- CrashedCraft/front: Look exactly head-on at the rounded NOSE and cockpit of this crashed capsule ship. NOSE faces viewer, rear engine hidden behind. Short rounded broad front with damaged side fins visible on either side, not the long side profile.
- CrashedCraft/back: Look exactly end-on at the REAR ENGINE of the crashed capsule ship. Central circular dark thruster at rear faces viewer, nose and cockpit are hidden behind. Short round back silhouette with broken struts, not long side profile.
- GasTank/left: Look exactly end-on from the LEFT end of the long cylindrical tank. Dominant large ROUND circular end cap facing camera, cylinder length recedes behind and is not visible sideways, two support legs beneath. NOT long horizontal tank side silhouette.
- Drain/left: Look at the SHORT LEFT END of the long rectangular drainage channel, 90 degrees from supplied broad front. Narrow U-shaped concrete end cross-section, channel and grate recede away from camera, short narrow end face, not long grille side.

- Crane 좌우: 긴 붐의 측면을 반복하지 말고 붐 끝에서 마스트를 바라보는 끝면을 생성.
- Wrench/right 최종: 왼쪽의 얇은 옆면 이미지를 참조하여 길이 방향으로 180도 돌린 반대 옆면. 손잡이는 가로 유지, 왼쪽 턱·오른쪽 주황 손잡이 끝, 넓은 평면 노출 금지.
