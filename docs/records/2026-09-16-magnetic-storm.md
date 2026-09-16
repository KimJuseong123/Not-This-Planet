# 2026-09-16 — Stage1 magnetic storm

## Delivered

Branch: S15P21B202-564-art. Baseline commit: e85e992f.

Blender MCP (Blender 5.2.2 LTS) created the exported FBX, FBM data/colour textures and a standalone source .blend. No existing Blender scene was replaced. The saved source was linked back for validation: 91 objects, no missing image textures.

Package: METAGAME/Assets/_Project/Models/Stage1MagneticStorm.

- 17 renderers: volume domain 1, flux meshes 7, debris 9.
- 6,836 vertices and 13,632 triangles.
- Procedural density authored with 70 lobes, baked to 128³.
- Repeatable 64³ turbulence; both data textures use R8 in Unity.
- Real volumetric ray marching in URP, 64 steps by default, approximate directional scattering, opaque scene-depth clipping and inside-volume rendering.
- Six-second density/flux/debris loop. This is a procedural volume, not a real-time fluid solver or an FBX-embedded smoke simulation.
- The Resources prefab connects to existing AtmosphereDustStorm. Existing schedule, seed, travel, physical forces and damage remain unchanged. The old sand particles are suppressed while the magnetic volume is present. Existing physical and visual meteor fragments are retained.
- The prefab can also animate autonomously in another scene; the Preview scene demonstrates this without adding gameplay forces.
- Actual StormPose travel is local -X. The 24 × 5 × 10 metre domain matches the existing hazard box; the earlier concept document's +Z convention is not used to change physics.

## Validation

Environment: Unity 6000.5.4f1, URP 17.5.0, Windows editor.

| Check | Evidence | Result |
| --- | --- | --- |
| Baseline import/compile | baseline.log, exit 0 | Passed |
| FBX import and local bounds | import.log; mesh bounds centre 0, size (24,5,10), identity imported transform | Passed |
| Prefab/material/3D texture references | unity-validation.json | Passed |
| C# and shader compile; final render command | validated-render.log, exit 0 | Passed |
| Orthographic and perspective, inside volume, Stage1 context | unity-*.png | Visually inspected |
| Six-second rendered loop | unity-front-t0.png vs unity-front-t6.png | Maximum pixel delta 0 |
| Intermediate frame changes | t0 vs t1_5 | Mean pixel delta 1.8543 / 255 |
| New tests | final-tests.xml | 6 / 6 passed |
| Combined existing/new targeted suite | tests.xml | 16 / 17 passed |
| Old failing test against original HEAD source | baseline-test.xml | Same failure reproduced |
| Saved Blender source image links | Blender MCP linked library inspection | No missing textures |

The new UnityTest actually entered Play Mode and tested resource loading, inactive gap, active storm, shared-time phase, seed change, instance reuse, suppression of original dust, temporary camera-depth request and restoration, disabling/reapplying the presenter and destruction cleanup. The test ran the presenter in an isolated empty scene; it did not run the complete multiplayer Stage1 match.

The pre-existing failure is DustStormScheduleTests.DebrisSource_IsResolvedFromATemporaryInstance. It assumes the first ParticleSystemRenderer has a debris mesh, but the original presenter creates the sand particle renderer first, whose mesh is null. The same single test failed against the untouched HEAD AtmosphereDustStorm.cs. The test was neither disabled nor changed. Current integration source was restored after that comparison, then all six new tests passed again.

The Stage1 screenshot used a temporary prefab placement near the shelter in an opened copy of Stage1_Astra. That placement was not saved to the gameplay scene. During normal gameplay the existing scheduler creates and positions the effect.

## Scope and remaining limits

Feature validation: ready with limitations. Player build, multiplayer peers, target-device GPU performance and mobile rendering were not run. Existing mobile-disable behaviour is retained. Approximate scattering is not physically exact fluid lighting. Other transparent objects follow normal transparent-rendering ordering limitations.

Unity import/test runs rewrote some existing files with different line endings only; their Git diffs contained no semantic changes. Those unrelated files were restored to the starting checkout. The only modified existing runtime file is AtmosphereDustStorm.cs. The new package, two editor tools, one runtime visual component, focused tests and this evidence are additions.

Logs remain local ignored artifacts. No commit or push was performed for this implementation.

