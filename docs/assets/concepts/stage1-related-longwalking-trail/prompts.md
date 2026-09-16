# 생성 프롬프트

내장 `image_gen` 사용. 각 오브젝트별 별도 생성, 일부 시트는 동일 도구로 보정했다. 외부 CLI/API는 사용하지 않았다. 기존 FBM과 모델 미리보기를 눈으로 확인한 후 색상·형태를 텍스트로 반영했다. 기존 에셋 이미지를 직접 입력한 스타일 전이 작업은 아니다.

## L01 · 01-long-walking-trail-0.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
Title: L01 | LONG WALKING TRAIL 0
Subject: Straight entry walkway slab, width 4.4m, thickness .6m, length 13.81m. Five large lavender floor panels, white thick beveled perimeter, twin cyan edge guides, a flush small cyan entry arrow on first panel. Shallow end coupling sockets below deck, dark underside. Horizontal asset in isolation; scene rotation provides gentle uphill slope. No fixed support legs.
```

### 최종 보정

```text
Edit this six-view concept sheet. Preserve the walkway design, materials, title, labels and 3x2 layout. Correct ONLY FRONT and BACK panels to true orthographic end elevations at mid-height: show the short rectangular end face, white corner blocks and dark connector housings, with NO long floor receding behind them. These two panels must NOT be perspective views. All remaining four panels keep their design unchanged. Remove the decorative top-right logo. Clean opaque pale gray background.
```

## L02 · 02-long-walking-trail-1.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L02 | LONG WALKING TRAIL 1
Subject: Straight middle ascent walkway slab, width 4.4m thickness .6m length 13.82m. Five large lavender floor panels with transverse recessed anti-slip bands, white bevel perimeter, segmented cyan edge strips, darker recessed side access panels. Horizontal asset in isolation; scene rotation supplies slope. No fixed support legs.
```

### 최종 보정

```text
Edit this existing six-panel concept sheet ONLY to fix readability: replace dark gradient background in every panel with a uniform opaque very pale gray #F4F4F4. Make all six panel labels and the asset title crisp dark charcoal and clearly legible. Keep the existing object geometry, materials, views, layout, title and labels, and cyan lights unchanged. No dark vignette, no transparent background.
```

## L03 · 03-long-walking-trail-2.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L03 | LONG WALKING TRAIL 2
Subject: Longest straight elevated walkway slab, width 4.4m thickness .6m length 15.95m. Six large lavender floor panels, white beveled perimeter, six cyan edge segments per side, visible slim titanium side beam with recessed ribs contained within .6m slab. No fixed pillars. Horizontal asset; installed slope comes from scene rotation.
```

## L04 · 04-long-walking-trail-3.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L04 | LONG WALKING TRAIL 3
Subject: Straight upper ascent walkway slab, width 4.4m thickness .6m length 11.28m. Four lavender floor panels, white beveled frame, four cyan edge lights per side, a subtle flush directional chevron near rear exit. This is a STRAIGHT module rotated around a bend in the map, NOT a curved bridge. No fixed pillars. Horizontal isolated asset.
Use uniform light-gray paper background and clearly readable DARK labels, no vignetting.
```

## L05 · 05-long-walking-trail-4.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L05 | LONG WALKING TRAIL 4
Subject: Short level summit arrival walkway slab, width 4.4m thickness .6m length 8.51m. Three lavender floor panels, broad pearl-white beveled border, cyan edge strips, circular recessed cyan observatory waypoint icon on last panel. Straight rectangular level deck with open short ends, no stairs or legs.
Use uniform light-gray paper background and clearly readable DARK labels, no vignetting.
```

## L06 · 06-hill-deck-transition.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L06 | HILL DECK TRANSITION
Subject: Short broad transition ramp slab between summit trail and observatory platform, width 3.8m, structural thickness .36m, length 2.78m. Two lavender panels, white beveled frame, cyan lines leading along edges, open flush front and rear joining lips. A single continuous walkable surface, no stair steps or railing. Isolated horizontal local-space part; installed pitch is applied externally. Make it noticeably short and nearly square, not a long bridge.
Uniform pale-gray background; crisp black readable labels, no vignette.
```

### 최종 보정

```text
Edit this existing six-panel sheet, preserve title L06 HILL DECK TRANSITION, six labels, colors, white chamfer framing, cyan lighting, layout. Correct the OBJECT PROPORTIONS in ALL SIX VIEWS: this connector is a short BROAD plate width 3.8 m, length only 2.78m, thickness .36m. Width left-right is 1.37 times length front-back. TOP view must be a squat LANDSCAPE rectangle with width 1.37x its vertical depth, NOT the current tall narrow rectangle. Two broad lavender panels split across depth. CONCEPT shows a wide short connector, FRONT and BACK show wide thin end elevation, RIGHT and LEFT show shorter thin side elevations. Keep the front and rear joining edges flush, no steps. Opaque pale gray background, dark readable labels.
```

## L07 · 07-observatory-entry-stairs.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L07 | OBSERVATORY ENTRY STAIRS
Subject: Compact four-step stair assembly for secondary entrance to circular observatory. Width 3.355m, total depth 2.04m, overall height approximately .81m, four treads advancing .48m, rise .19m. Lavender treads, pearl-white rounded side cheeks, narrow cyan riser markers, concealed dark titanium underside. Front is the lowest step; back is tall solid end. No handrails, no circular platform included. TOP must show all four treads; sides show four rising steps.
Uniform pale-gray background; crisp black readable labels, no vignette.
```

## L08 · 08-observatory-instrument.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L08 | OBSERVATORY INSTRUMENT
Subject: Small low scientific sensor pedestal, .8m diameter by .9m total height including .45m body half-height convention from Unity cylinder. Rounded short cylindrical pearl-white housing, dark low foot, broad flat cyan glass circular sensor on top, a lavender recessed front service hatch, small circular vent on right side and plain left side, rear cable socket near base. No tall telescope, no hologram, no lettering on screen. Low unobtrusive environmental instrument. Same asymmetry maintained in all views.
Uniform pale-gray background; crisp black readable labels, no vignette.
```

## L09 · 09-adjustable-trail-junction.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L09 | TRAIL JUNCTION PLATE
Subject: NEW proposed low-profile angular joint cover for seams between straight walking trails: broad short octagonal fan-like deck plate, 4.4m wide x 2.4m deep x .2m thick. White beveled rim, three overlapping lavender wedge panels allowing mild changes of heading, flush cyan directional inset. Smooth walkable top, dark underside with concealed sliding fasteners. No moving mechanism projecting above deck, no legs, no rails. This covers a turning connection, not an entire curved bridge. Isolated single component.
Uniform pale-gray background; crisp black readable labels, no vignette.
```

### 최종 보정

```text
Edit this existing six-panel concept sheet ONLY to fix readability: replace dark gradient background in every panel with a uniform opaque very pale gray #F4F4F4. Make all six panel labels and the asset title crisp dark charcoal and clearly legible. Keep the existing object geometry, materials, views, layout, title and labels, and cyan lights unchanged. No dark vignette, no transparent background.
```

## L10 · 10-cliff-edge-guardrail.png

### 최초 생성

```text
Use case: stylized-concept. Create ONE polished landscape game environment asset concept sheet, 3 columns by 2 rows, EXACTLY six views of the SAME single object. Panels in reading order labeled exactly "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". CONCEPT is a clear three-quarter perspective; other five panels are orthographic views, no perspective. Front is local -Z looking +Z; right is +X looking -X; left is -X looking +X; top looks down with +Z towards top of page; back is +Z looking -Z. Keep geometry, panel count, fittings and materials consistent in all six views. Each entire object fits inside its panel, no cropping. Professional attractive art-directed 3D render, restrained linework, matte pale gray background, thin panel dividers, tiny tasteful English asset title, no paragraph text, no invented dimensions. Art style from Stage1_Astra imported assets: friendly rounded and chamfered pearl-white ceramic metal framing, desaturated lavender inset panels, dark titanium understructure, narrow cyan emissive strips. Soft studio light, restrained wear, no rusty military aesthetic, no people, no terrain or extra objects. Walkway surfaces broad and unobstructed, flush seams, no stairs unless specified; no tall rails unless specified. This is a newly designed related asset, not a copy of the whole reference platform.
CRITICAL CAMERA: FRONT and BACK must be true flat elevations at object midheight, showing only the short end face of a flat walkway, NOT looking down its length and NOT showing its floor. RIGHT and LEFT are true side elevations with no top floor visible. Only CONCEPT is perspective. TOP is rectangular, no converging edges. No logo or watermark.
Title: L10 | CLIFF EDGE GUARDRAIL
Subject: NEW optional single modular open guardrail for cliff-facing OUTER edge of elevated trail. Length 2m, height 1m, depth .18m. Two rounded white posts and one continuous rounded white top rail, lavender low kick plate occupying only bottom .2m, open large middle void to preserve views, dark mounting feet outside walkway. Thin cyan light inset facing inward below handrail, plain back maintenance caps. Show only ONE straight guardrail component, not a walkway, no glass wall. Front is walking-facing broad side; back is cliff-facing side.
Opaque solid white background (#F4F4F4) every pixel outside object and its subtle contact shadow, black readable labels. NO transparency or black gradients.
```

