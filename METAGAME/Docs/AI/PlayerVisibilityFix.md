# Player hit/death appearance overlap

- Baseline: Unity 6000.5.4f1; working tree clean before this change.
- Report: solo and multiplayer players show the legacy body over the rabbit model when hit or killed.
- Static evidence: `RagdollBuilder.Build(..., primitiveLook: false)` disables primitive renderers before attaching `SpaceSuit`. `Doll.SetVisible` previously enabled every child renderer, including those primitives. `PlayerRig` uses that method for first-person shadows, third-person recovery/downed views, and riding. Both solo and network players use `PlayerRig`.
- Fix: record the replaced primitive renderers during construction and keep them disabled during visibility transitions. Model visibility, primitive-only appearance, colliders, and joints retain their existing behavior.
- Regression coverage: `DollVisibilityTests.ViewTransitionsPreserveSelectedAppearanceAndPhysics` exercises repeated first-person shadows, third-person, riding/hidden, and restored visibility for model and primitive-only construction. The model case fails on the previous implementation when hidden primitive renderers are enabled.
- Validation: generated Gameplay project compiled with zero errors (16 warnings in existing code). Unity Test Runner and visual multiplayer verification were not run; the project is already open in Unity and no Editor control connector is available. The source path explains the symptom, but runtime confirmation remains pending.

## Runtime verification

1. In Unity Test Runner, run the `DollVisibilityTests` EditMode fixture.
2. Start solo play in first person. Take a hit that causes knockdown and wait for the third-person view. Confirm only the rabbit model appears, including limbs and helmet; recover and repeat.
3. Trigger death and respawn. Confirm no legacy geometry appears during third person or after respawn, and the first-person body shadow remains correct.
4. Repeat with a host and a client, alternating which player takes damage/dies. Check both the affected player's camera and the other peer's view.
5. Enter and exit a vehicle. Confirm the body and shadow are hidden while riding, the rabbit reappears correctly on exit, and grabbing/collisions still work.
