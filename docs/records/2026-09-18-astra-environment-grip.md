# Stage1_Astra fixed environment grips

The scene now contains 265 additional Grabbable surfaces: 131 rock/mineral/cliff
surfaces (including the five VariableRocks models), 67 mushroom/tree surfaces,
25 Ion crystals and 42 Bollards. Thirty-one surfaces previously lacking collision
(26 canopy silhouettes and five VariableRocks models) received non-convex
MeshColliders using their render meshes.

Each Grabbable lives on its mesh/collider object with CanGrab, Climbable and
PhysicsGrip enabled, Transport role, normal grip strength and no priority bonus.
None has an attached Rigidbody. Existing Hand.GripTo therefore creates a world
FixedJoint; the environment stays stationary. Terrain, invisible boundaries,
small decorative plants, tools, movable cargo and gameplay parts are unchanged.
Prefab changes are scene instance overrides, not shared asset edits.

Hand.Search and PlayerRig's crosshair selection now share Hand.TryGetGripPoint.
The previous Collider.ClosestPoint call emits a Unity error for non-convex mesh
colliders, reproduced in the baseline survey. Primitive/convex queries retain
ClosestPoint. Non-convex queries sample centre-directed and 26 surrounding rays
against the collider's cooked surface, accepting only real hits within reach.
This is bounded surface sampling, not an exact nearest-triangle solver; a very
small or deeply recessed face can still require adjusting the aim. No runtime
mesh readback, per-query allocation or physics proxy is introduced.

Validation used Unity 6000.5.4f1 in an isolated project with matching gameplay
changes and target assets. All 265 surfaces passed saved-scene reload, reachable
surface lookup, world-fixed grip and release. Actual editor-driven PhysX
simulation held each hand against gravity for 20 steps and let it fall for 10
steps after release. Twenty-six EditMode tests passed across EnvironmentGripTests,
ToolCarryCollisionTests and TractorBeamControllerTests, including player input,
crosshair targeting, hollow mesh gaps and existing tool/cargo behavior.

The merge preserved all existing transforms, materials, lighting, components and
references, changing only component lists/prefab added-component overrides and
adding the new components/required stripped object records. Evidence is under
artifacts/astra-environment-grip. The isolated project still lacks an unrelated
TinyFlowerPatch prefab; its source scene records were preserved. Interactive
multiplayer play was not tested.
