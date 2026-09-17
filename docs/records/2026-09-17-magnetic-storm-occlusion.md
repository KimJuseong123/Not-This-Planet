# Magnetic storm opaque occlusion

## Reproduction and cause

An isolated Unity 6000.5.4f1 URP render uses the production PC pipeline, the
magnetic storm prefab, and a solid cube between camera and storm with its Collider
removed. Compare captures with storm enabled/disabled (480 x 270, RGB difference
threshold 6). Standard URP Lit hides the storm, but the project's Flat, Dough,
CharacterSuitTint, PlanetNoFog, Guidance/NoFog and CockpitNoFog shaders leak
5,356–5,496 storm pixels through the opaque cube.

PC SSAO requests depth and normals before opaque rendering. These six custom
shaders lacked the corresponding depth-normal pass, leaving their surfaces out
of the camera depth texture sampled by the volume ray marcher. Physics collision
has no role in this visibility test. This reproduces the reported symptom, though
the exact object the user encountered in Stage1 was not identified.

## Change

Add DepthNormalsOnly passes to the six opaque shaders, using URP's unlit geometric
normal implementation and each material's existing constant-buffer layout and
culling mode. Flat additionally gains DepthOnly for depth-only prepass paths.
No storm ray-march, physics, scene, material or renderer configuration is changed.
The volume must still clip its ray against scene depth: applying ordinary depth
testing to the volume's back face would wrongly hide smoke in front of a wall
that intersects the volume.

## Validation

`tools/unity/ValidateStormOcclusion.cs` is an isolated batch render regression
harness. Copy it into the isolated project's Assets/Editor and invoke
`ValidateStormOcclusion.Run`. Assertions cover opaque occlusion, unobstructed
visibility, smoke in front of/intersecting walls, wake occlusion and shader errors.
Evidence is in `artifacts/storm-occlusion`: baseline-results.txt,
expanded-baseline-results.txt, results.txt, regression.log and rendered PNGs.
Final batch exited 0 with no shader/compiler errors: all seven tested materials
leak 0 pixels; intersecting-wall captures retain 4,572–4,708 visible storm pixels;
behind-wall wake leaks 0 pixels and unobstructed wake retains 5,357 pixels.

Full live Stage1 gameplay and a player build are not covered. Transparent
materials that intentionally do not write depth are outside this opaque-surface
fix. The existing always-visible red debug boundary is a separate test overlay.
