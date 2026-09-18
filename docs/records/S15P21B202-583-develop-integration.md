# 583 map and develop gameplay integration

- Map source: `f9a750bd` (`S15P21B202-583-art`).
- Gameplay source: `215431cc` (`origin/develop`).
- Integration direction: merge develop into the 583 worktree; do not change develop directly.

## Scene resolution

`Stage1_Astra.unity` conflicted because develop stores the scene through Git LFS while 583 stores its YAML directly. Resolve the actual scene data, then stage one canonical LFS pointer.

Use the complete 583 scene as the base. Preserve all 39 Terrain components and 5,487 of its 5,493 serialized blocks verbatim. The six changed blocks are the two tool GameObjects, their two transforms, DevManager and SceneRoots. Add twelve develop blocks for the assault trigger, two display stands and ToolDisplayMount connections.

- Keep 583 terrain, vegetation, scenery, prefab overrides and map hierarchy.
- Preserve develop's tractor-dock assault trigger, including its collider and transform.
- Preserve develop's shelter stands and the matching bat/welding-tool poses and mount references. These tool poses are the explicit exception to retaining 583 transforms.
- Preserve develop's F9 assault / F10 entry flags.
- Retain 583's disabled legacy basin renderer/collider rather than applying develop's map cleanup.
- All 132 other files changed by 583 since the common ancestor remain identical to the 583 Git blobs, including terrain data, textures and metadata.

## Validation

- 5,505 serialized blocks; no duplicate block IDs or unresolved local fileID references.
- All 247 referenced asset GUIDs resolve in project assets, Unity packages or built-in resources; no duplicate project GUIDs among scene references.
- Canonical three-line LFS pointer; SHA-256 and byte count match the working scene.
- No unresolved Git conflicts.
- Unity 6000.5.4f1 compilation and targeted EditMode run passed: 13 tests, 13 passed, 0 failed, 0 skipped; runner exit code 0.
- `Stage1CutsceneTests`: 11 passed. `ToolDisplayMountTests`: 2 passed, including EnterPlayMode validation of authored tools staying seated, pickup and dropping.
- Test evidence: `tmp/583-merge/unity-tests.xml` and `unity-tests.log`.
- Post-test working tree has no tracked modifications beyond the prepared merge index.
- Local diagnostics: `tmp/583-merge/` (ignored; not committed).

Player build, multiplayer peers and visual comparison are outside this validation run.
