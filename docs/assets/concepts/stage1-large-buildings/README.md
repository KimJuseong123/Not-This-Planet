# Stage1 대형 건물 컨셉아트

내장 image_gen 도구로 생성. 각 PNG에는 한 건물의 한 뷰만 포함합니다.

## 파일 구성

각 shelter / hangar 폴더: 01-concept.png, 02-front.png, 03-right.png, 04-left.png, 05-back.png, 06-top.png.

정면은 주 출입구, 오른쪽/왼쪽은 정면에서 바라봤을 때의 건물 측면입니다. 위는 지붕을 포함한 외관 뷰입니다. 생성형 모델링 참고 이미지이므로 세부 패널·지붕 설비 위치와 치수 정합성은 실제 모델링에서 기준 컨셉에 맞춰 조정해야 합니다. 정확한 CAD 투영도는 아닙니다. 제안 크기는 Shelter 약 14×12×7m, Hangar 약 20×18×10m이며 씬 확정 치수가 아닙니다.

## Shelter concept prompt

Use case: stylized-concept. A polished 3D game environment modeling reference for Stage1_Astra: friendly chunky rounded hard-surface sci-fi architecture, pearl white ceramic-metal outer shells, lavender-purple inset panels, restrained cyan luminous strips, charcoal gaskets, sparse amber utility markers, simple readable modular shapes consistent with the prior scene reference. Not photorealistic, no gritty military industrial treatment. One single building and one camera view per image, no collage, no inset, no labels, no lettering, no people. Entire object unclipped with margin on a plain muted blue-gray studio ground, soft neutral lighting exposing shapes without blown highlights. Front means main entrance facade; right means building's right side as seen facing entrance. Concept image camera elevated front-right three-quarter, roof visible. Modeling-friendly architectural detail, not excessive greebles.
Subject: Large explorable expedition SHELTER beside the engine part camp. Approximate building envelope 14m wide x 12m deep x 7m tall. Broad rounded rectangular single-story habitat with softly vaulted segmented roof, four chunky structural corner feet, large central open recessed sliding entrance 4m wide x 4m tall under a shallow front canopy, short wide ramp, two broad horizontal cyan-tinted windows flanking entrance. Right elevation has two paired horizontal windows and a low integrated utility panel. Roof has two inset lavender solar panels and one low rear vent block, small antenna at rear-left. Welcoming refuge with visible empty spacious interior through the entrance, no interior props hiding geometry. Distinct grounded architecture, not a tiny pod or a vehicle. Exterior engine part and its separate pedestal are not included.

## Hangar concept prompt

Use case: stylized-concept. A polished 3D game environment modeling reference for Stage1_Astra: friendly chunky rounded hard-surface sci-fi architecture, pearl white ceramic-metal outer shells, lavender-purple inset panels, restrained cyan luminous strips, charcoal gaskets, sparse amber utility markers, simple readable modular shapes consistent with the prior scene reference. Not photorealistic, no gritty military industrial treatment. One single building and one camera view per image, no collage, no inset, no labels, no lettering, no people. Entire object unclipped with margin on a plain muted blue-gray studio ground, soft neutral lighting exposing shapes without blown highlights. Front means main entrance facade; right means building's right side as seen facing entrance. Concept image camera elevated front-right three-quarter, roof visible. Modeling-friendly architectural detail, not excessive greebles.
Subject: Large explorable CAPSULE CARGO HANGAR replacing the simple shed at Part3. Approximate envelope 20m wide x 18m deep x 10m tall. Rectangular broad warehouse with chamfered arch portal, shallow barrel-vault segmented lavender roof and thick pearl white structural ribs; front has one very large fully open cargo doorway 12m wide x 7m tall framed by substantial chamfered pillars. Short wide low-threshold cargo ramp. Right wall has three structural bays with narrow upper horizontal windows, a small recessed personnel door near front. Roof has one long central inset skylight and paired rear ventilation pods. Through open front see empty unobstructed cargo bay, subtle cyan guide strips and a simple overhead crane beam with no hanging load. Intended for capsule extraction toward an existing separate circular pickup apron; do NOT include the apron, capsule, outdoor platforms, terrain, rocks, or extra buildings. Clearly taller and larger than a personnel shelter.

## Directional-view prompts

각 건물의 01-concept.png를 이미지 참조로 사용했습니다.

Re-render the SAME exact building from the reference image as a single orthographic modeling reference. Preserve all proportions, materials, roof equipment, structural modules, openings and panel layout. Change only the camera except where an unseen rear/left facade must be completed consistently. Single centered object with generous margins, muted blue-gray plain background, neutral studio lighting, no annotations, no text, no collage, no secondary view, no floor grid. Avoid perspective and exaggerated surface wear. Roof attached. 

### 02-front

Strict FRONT orthographic elevation. Camera exactly perpendicular to main entrance facade at mid-height, no roof plane or side facade visible, no perspective. Main open entrance centered. Entire structure including ramp and roof equipment visible.

### 03-right

Strict RIGHT SIDE orthographic elevation, camera perpendicular to the wall on the RIGHT of the entrance as seen in the reference. Front/entrance is at IMAGE LEFT, rear at IMAGE RIGHT. Roofline horizontal, no front face or roof top plane visible. Preserve the visible right-wall design from reference. Show ramp extending slightly beyond the front.

### 04-left

Strict LEFT SIDE orthographic elevation, opposite wall to the reference's visible right wall. Front/entrance is at IMAGE RIGHT, rear at IMAGE LEFT. No front face or roof top plane visible. Match building depth, structural bay spacing and silhouette. Left facade follows the same window/panel system as the right, but has no personnel door. Do not mirror the roof antenna to another physical corner.

### 05-back

Strict REAR orthographic elevation, camera perpendicular to the facade OPPOSITE main entrance. No front doorway, no entrance ramp in front, no roof top plane or side walls visible. Preserve massing/roof height from reference. Rear wall: symmetric solid lavender service panel bays, white corner ribs, low ventilation grilles and two narrow upper horizontal windows. Roof equipment remains in its physical positions, projecting above silhouette.

### 06-top

Strict TOP orthographic view, exactly vertical camera looking down, no side facade or front facade visible, no perspective. Entrance and ramp toward BOTTOM of image, rear toward TOP; right side on IMAGE RIGHT. Roof stays installed, not a cutaway or floor plan. Preserve roof panels, vents, antenna/skylight and outer footprint exactly from reference; include the full ramp and margin.
