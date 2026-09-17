# ObservatoryEntryStairs surface overlap repair

The four structural stair tops coincided with their lavender treads at local
heights 0.24, 0.43, 0.62 and 0.81 m. Blender MCP lowered each chassis upper bevel
and top by 0.01 m (112 vertices total), preserving the treads and lower bevels.

The existing FBX was replaced at its original path. The final file preserves
the original FBX object IDs/names, topology, UVs, normals, texture connections
and Unity `.meta`. The scene, material and collision assets were not changed.
Both copies of the procedural generator now recess the chassis by the same amount.

Validation: all four recesses checked; unchanged non-chassis vertices and UVs;
closed manifold mesh with no degenerate faces; final FBX re-import matches repaired
Blender coordinates, with 2,072 vertices and 4,012 triangles. Runtime visual
verification in Unity remains pending; no Unity MCP is available in this task.

The original multi-asset Blender library was preserved. Automated approval review
rejected replacing that library because it could lose unrelated source data.
The corrected standalone source is `METAGAME/Assets/_Project/Models/LongWalkingTrail/Source~/ObservatoryEntryStairs_Fixed.blend`.
Use that file for subsequent edits to this stair model; the original library still
contains its earlier geometry.

Backup and machine-readable validation: `artifacts/observatory-stairs-fix/`.
The two repair scripts under `tools/blender/` record the Blender mesh adjustment
and subsequent preservation of the original FBX structure.
