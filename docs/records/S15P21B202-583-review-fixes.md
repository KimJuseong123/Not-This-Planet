# 583 review fixes

Baseline: `b2d1cdba`. Unity 6000.5.4f1.

## Artist asset safety

- Remove TinyFlowerPatchBuilder's InitializeOnLoadMethod/delayCall and global EditorPrefs version check. Generation now requires an explicit menu/window action.
- Update existing palette and mesh objects in place. Only create assets when absent; preserve GUID and local file IDs and references from saved materials/prefabs.
- Regression coverage uses disposable assets, never the artist's real flower assets. It checks retained prefab/material references after regeneration and reimport, as well as absence of startup hooks.

## Terrain ownership and cleanup

The review's single-TerrainData assumption does not match the authored scene. Stage1_Astra contains 39 Terrain components referencing 37 distinct TerrainData assets. Deleting all UUID assets would remove most of the map. Main and existing terrain prefabs also reference separate data.

All moves use AssetDatabase.MoveAsset, preserving GUIDs:

| Owner | TerrainData |
| --- | --- |
| Stage1_Astra's central authored basin | TD_Stage1_AstraBasin.asset |
| Stage1_Astra's 36 additional data assets | TD_Stage1_Astra_X…_Z….asset |
| Main's seven surrounding data assets | TD_Stage1_Main_X…_Z….asset |
| Existing expanded terrain prefab | TD_Stage1_Expanded.asset |
| Existing arena terrain prefab / legacy Main builder | TD_Stage1_Arena.asset |

These files reside in `METAGAME/Assets/_Project/Terrain/Stage1/TerrainData/`. Coordinate suffixes describe each tile's authored local position; one data asset can be shared by several Terrain components. Do not replace them with a single shared TerrainData. The existing Main terrain builder targets Arena; it is not the Astra basin authoring tool.

- Move 44 used TerrainData assets; delete eight with no incoming asset dependencies.
- Consolidate NewLayer assets into 12 descriptive `TL_Stage1_*` layers under `Terrain/Stage1/Layers/`; delete six unused/duplicate layers.
- Redirect duplicate layer slots to identical layer definitions without collapsing paint channels. Preserve height and alpha samples exactly.
- Verify all 44 moved TerrainData assets against their original Git content through Unity: size, resolutions, heights, alphamaps, holes, tree instances and detail density are unchanged.
- Stage1_Astra scene bytes remain unchanged. No flower mesh, palette, material or prefab regeneration is performed.

## Reported tests

- Reproduced the PracticeLobby hierarchy mismatch. Synchronize its tool/clamp child transforms with authored Stage1 while retaining practice-room root placement and gameplay bindings.
- Reproduced the companion speech-hint failure. The streak already reset correctly; NoteUnclear and TraceReceipt both logged the same hint. Keep the complete receipt as the single diagnostic record.
- Initial targeted run: 40 tests, 38 passed, two reproduced failures above.
- CraftMeteorImpactTests: all 32 passed without production changes, including a separate run requested with Stage1 opened beforehand. The reported craft failures were not reproduced locally; no physics logic or assertions were weakened.

Final Unity regression run: **88 passed, 0 failed, 0 skipped**, including CompanionTests, PracticeLobbyTests, CraftMeteorImpactTests, TinyFlowerPatchBuilderTests, Stage1CutsceneTests and ToolDisplayMountTests. Both reproduced failures now pass; the existing real-player pickup/drop/clamp-pull tests also pass after the hierarchy synchronization.

Repository hygiene against develop `215431cc`: **7 passed, 0 warnings, 0 failures**. Asset locations, all added asset/meta pairs, LFS pointers, size limits and path lengths pass. The original Assets-root violation is resolved. No unplanned staged changes to Stage1_Astra or the artist's flower mesh/palette/material/prefab remain relative to the baseline commit.

Local detailed evidence is in the ignored `tmp/583-review/` directory. No player build or multiplayer peer validation is claimed.
