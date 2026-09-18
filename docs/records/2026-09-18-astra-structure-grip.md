# Stage1_Astra fixed structure grips

Added scene-local Grabbable components to 297 existing solid structure surfaces:

- MainAssemblyLaunchpad: 8 (deck, entry ramp, pylon bases/columns, rear service and console).
- Spiral walkways, landings, bridges and rails: 97.
- Assembly platforms: 5.
- Shelter and hangar fixed walls/frames/roofs/floors/ramps: 19.
- Hangar approaches, curbs, stairs, bridge fragments, supports and beacon posts: 29.
- Observatory stairs/instruments: 4.
- Fixed lamp feet/bodies/caps: 114.
- Console supports/housings/feet: 6.
- Static supply crate bodies/insets/latches: 15.

Each component is on the existing collider object and uses CanGrab, Climbable,
PhysicsGrip, Transport role and normal grip strength. All selected colliders are
enabled, active, solid and have no attached Rigidbody. No new collider or
Rigidbody was needed. Existing dedicated collision geometry is preserved rather
than adding overlapping render-mesh collision.

Movable door/trolley/jaw pieces, windows/skylights/screens, signal controls,
emissive lamp cores, triggers, invisible map bounds and terrain are excluded.
The existing environment grips and all other components are unchanged. Shared
prefabs remain unchanged; prefab additions are scoped to this scene's instances.

Unity 6000.5.4f1 validation in the isolated project covered saved-scene reload,
real reachable surface queries, world-fixed hand grip/release, 20 PhysX steps
holding against gravity, then 10 steps falling after release for all 297 surfaces.
The final merged scene is verified again using the same checks. Runtime source
and existing regression tests were not changed by this follow-up. Evidence and
the complete selected collider inventory are in artifacts/astra-structure-grip.
Interactive multiplayer play was not tested.
