# Stage1 magnetic storm directional billowing and wake

## Behavior and tuning

The existing `MagneticStormVisual` component exposes serialized
`frontBillowStrength` (1.4) and `rearBillowStrength` (1.8). They multiply the
material's existing billow amount, blend smoothly through the middle of the
cloud, and follow the actual world-space travel vector. Inspector labels are
`Travel Direction Billow` and `Opposite Direction Billow`; serialized names stay
unchanged to retain overrides. The Resources prefab
contains these defaults; the existing Stage1 scene instance inherits them.

### Direction correction

The initial front/rear mask used local Z, but its roller phase and UV warp retained
substantial lateral motion. In the default 24m-wide / 5m-deep stage volume, the
0.032 X warp represented 0.768m versus 0.060 Z warp representing 0.3m. This could
read as billowing perpendicular to movement even though the front/rear mask used Z.

The presenter now supplies `StormPose.Direction` explicitly. Each smoke renderer
projects that vector into its own UV frame using its full transform matrix. The
shader deforms only in the world travel/up plane, with metre-based displacement;
roller phase and noise advection run along travel rather than the width. Wake age
fading uses the same travel projection. Wake placement retains the deterministic
already-traversed path, with tests comparing it against actual positional motion.

`Stage1AtmosphereProfile.dustStorm` exposes `wakeDuration` (4 seconds),
`wakeOpacity` (0.35) and `wakeInfluence` (0.18). Duration zero disables the wake;
opacity and influence can independently disable its visual and physical effects.
These values are stored in the Stage1 atmosphere asset. The original main storm's
path, speed, trigger and impulse settings remain unchanged.

The wake spans the rear face's already-traversed path. At each position it fades
with the square of the remaining lifetime fraction, including after the main
storm finishes. Impulse strength is the existing kick times wakeInfluence times
that fade. The existing player knockdown/ride-along behavior is retained.

Both visual and physical wake poses are reconstructed from shared time and seed,
not local frame history. Lifetime is capped to one storm cycle, allowing at most
two wake volumes (current and previous cycle). The visual reuses smoke-only
renderers and shared assets, without duplicated flux/debris or per-frame spawning.
Only the existing physics authority applies impulses. Main and wake hits share
actor deduplication/cooldown; expired cooldown entries are pruned at cycle changes.

## Validation

- Unity 6000.5.4f1, isolated `outputs/tractor-tests`, actual graphics enabled.
- C# compilation completed, no new compile errors.
- Initial focused suite: **12 passed / 13 total** (`artifacts/storm-wake/final-tests.xml`).
- After the direction correction: **18 passed / 19 total**
  (`artifacts/storm-wake/direction-tests.xml`), with the same pre-existing failure.
  Six additional cases verify rotation-independent world displacement/front masks
  and wake alignment opposite the actual motion across three seeds.
- All four added tests passed: independent billow/wake shader parameters and
  renderer reuse, fixed ground-point decay and stateless reconstruction, persistence
  across the inactive gap and expiry/seed behavior, weak authority-gated residual
  impulses with compound-collider deduplication and cooldown.
- Existing presenter Play Mode test also checks wake hiding on disable.
- One existing test fails at its final second main-body impulse assertion:
  `TriggerKicksInteractiveBodiesAndWholePlayerOncePerSecondOnlyWithAuthority`,
  line 95. Restoring the original HEAD hazard in the isolated project reproduced
  the identical failure (expected difference <0.02, actual 18.4390907).
  Evidence: `artifacts/storm-wake/baseline.xml`. The production hazard was never
  reverted, and the isolated project was restored to the implementation afterward.
- Actual URP main+wake, zero-billow and residual-only images rendered and visually
  inspected. `ShaderUtil.ShaderHasError` returned false after rendering.
  Evidence: `artifacts/storm-wake/*.png`, `render-validation.txt`, `render.log`.
  Direction-corrected final render: `direction-render-final.log`; the orthographic
  `travel-up-wake-down.png` uses actual travel as screen-up and shows the wake below.
  Static lateral phase differences preserve individual lobes while displacement
  and noise transport stay aligned to travel. Final shader render completed without errors.
- `git diff --check` passed. No scene rewiring, package, collider or model changes.

Full Stage1 gameplay, multiple network peers, player builds and target-device GPU
performance were not tested. Additional ray-marched volumes increase GPU work;
the two-renderer bound is a resource limit, not a performance measurement.

Reproduce image validation by copying `tools/unity/ValidateMagneticStormWake.cs`
to an isolated project's Editor folder and invoking its `Run` entry point in batch mode.
