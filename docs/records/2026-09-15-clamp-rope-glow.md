# Clamp rope glow

## Change
- PartClamp explicitly copies the part route BaseColor into its private tether EmissionColor and enables emission. This also handles route materials without authored emission properties.
- Uses the same GuidanceNoFog HDR approach as Beacon: EmissionEnabled=1, EmissionIntensity=4, MaxBrightness=5. The previous brightness cap of 1 was below ConceptVolume Bloom threshold 1.1.
- Rope width increases from 0.055m to 0.085m, including the existing pull-progress width oscillation.
- No particles, colliders, networking changes, shared material mutations or global Bloom changes. Applied automatically when the scene starts.

## Validation
- Unity 6000.5.4f1 / URP 17.5.0 in the isolated outputs/tractor-tests project. Stage1_Astra scene SHA256 matches the original. The user's open Editor was not controlled.
- Play Mode passed for 16 ropes across four parts: HDR, disable/enable visibility, pull progress, release hiding, no rope particles/colliders. See CLAMP_GLOW_SMOKE_PASS in .local-validation/clamp-glow-visual.log.
- Visually inspected all four part color renders in .local-validation/clamp-glow-visual/part-{1..4}-after.png. Bright cores and colored bloom are visible. Each before image restores the brightness cap to 1 and width to 0.055m on ONE representative rope; the other ropes remain enhanced.
- No C# or shader compilation errors. Previously observed UnityEditor.Search startup indexing exception recurred, but the visual smoke run completed successfully.
- Actual multiplayer peers and player builds were not run.
- Existing EditMode suites (PartClampTests, ClampPullTests, ClampOccupancyTests, PartGuidanceVfxTests): 32/41 passed both before and after; the same 9 pull-interaction/timing failures occur with the original HEAD PartClamp as well. No new test failures. Reports: `.local-validation/clamp-glow-baseline.xml` and `.local-validation/clamp-glow.xml`.
