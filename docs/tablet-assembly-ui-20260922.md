# Tablet assembly UI — S15P21B202-702

Base: origin/develop `51cfe4cb`. Branch: `S15P21B202-702-tablet-guide-ui`.
Jira: https://ssafy.atlassian.net/browse/S15P21B202-702
The local visual prototype was approved before this issue was registered for review.

## Scope

- Use the approved four-module chalk illustration, preserving its proportions.
- Missing modules remain grey; correctly placed modules become bright cyan-white.
- Welding adds a check mark; a misplaced/tilted module is red.
- Right-hand steps and action illustration distinguish placement from welding.
- The first module follows the existing `ShipAssembly` base-completion semantics.
- Full placement alone never produces the assembly-complete message.
- No gameplay, physics, network, scene, prefab or input bindings were changed.
- The illustration is stylized, not a new alignment/precision guide.

## Readability

- Tablet labels now use the already packaged static `SUIT-Bold` face. The previous
  Noto variable font produced very thin strokes even with the legacy UI Bold setting.
- Minimum label size is 24 logical pixels; world-space glyph density is 2 instead of 10.
- Compact footer retains the same page indicator and actual control bindings.
- Imported artwork is uncompressed, clamped, trilinear filtered, with mipmaps.
- No global render scale, anti-aliasing or post-processing settings were changed.

## Assets / build inclusion

`Assets/_Project/UI/Resources/TabletAssembly` contains the two approved source images
and the UI ink shader. Source images are unchanged. Runtime UVs select artwork only;
all labels and state text are real Unity UI text, not baked into the image.
The shader extracts pale linework and applies state color. It is a Resources asset so
it is included in player builds. Existing guide illustrations and font license remain.

The editor-only `Tools/Not This Planet/Tablet/Configure Assembly Artwork` menu can
reapply import settings to these two images and the new guide pages 5/6. It is not a global asset postprocessor.
Normal users and builds do not need to run it; settings are saved in the `.meta` files.
The old silhouette is retained solely as a missing-resource fallback.

## Six-page guide follow-up

The original four pages retain their order and resources. Page 5 adds the approved
tractor transport/stacking illustration, with a caption explaining the click-operated
low-gravity field while mounted. Page 6 adds the approved wind/prone illustration;
its caption reads the current prone binding (default Z). Both keep the existing 3:1
illustration frame. The drawn Z key represents the default binding.
The page count, indicator, forward-to-progress and backward-to-last-guide behavior
use the same six-page state. E can still skip to progress from any guide page.
New source images are `TabletGuide/Page5.png` and `TabletGuide/Page6.png`.
The follow-up test report is `Logs/tablet-six-pages.xml` (previous reports do not
validate these two additions).

## Verification

- Before modification: 47 relevant EditMode tests passed.
- Initial assembly UI: 54 relevant EditMode tests passed in Unity 6000.5.4f1
  (`Logs/tablet-after.xml`). This predates the two additional guide pages.
- Final six-page validation: 59 passed, 0 failed, 0 skipped in Unity 6000.5.4f1
  (`Logs/tablet-six-pages.xml`). No C# or shader compilation errors were found.
- Repository offline CI suite: 84 passed, 0 failed (Python UTF-8 and Git Bash on Windows).
- Latest origin/develop remained `51cfe4cb` at validation time.
- Explicit artwork preview test renders empty/placed and all three controls pages at
  1024 and 2048 width. `-tabletGuidePreviewDir` renders the real handheld prefab pose.
- Screenshots in `METAGAME/Logs` are local QA outputs, not game assets.
- This is not a full player build or multiplayer playtest. In-game moving-camera
  readability at the user's resolution still needs a quick manual check.

## Review screenshots

![Empty assembly](assets/tablet-702/empty.png)
![Placed assembly](assets/tablet-702/placed.png)
![Tractor guide](assets/tablet-702/tractor.png)
![Wind and prone guide](assets/tablet-702/prone.png)

Open `METAGAME` in Unity 6000.5.4f1, start the normal game, then use Tab and E to
inspect the assembly page and A/D for other pages. Check missing, partially placed,
fully placed/unwelded and welded modules. Check that the usual guide can be reopened.
