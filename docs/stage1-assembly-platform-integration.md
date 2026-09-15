# Stage1 Astra — imported assembly platforms

Applied in Unity 6000.5.4f1 on 2026-09-15.

## Scene and assets

- Scene: `METAGAME/Assets/Scenes/Stage1_Astra.unity`
- Hierarchy: `Assembly Platforms - Imported FBX` (five prefab instances).
- FBX sources and original textures: `Assets/_Project/Models/AssemblyParts/`.
- Wrapper prefabs: `Assets/_Project/Prefabs/AssemblyParts/`.
- URP Lit materials and packed metallic/smoothness maps: `Assets/_Project/Materials/AssemblyParts/`.
- Editor commands: `Tools > Not This Planet > Assembly Platforms`.

Each wrapper retains the imported FBX transform and references its source mesh. Static MeshColliders use the world layer. Materials use the supplied base color and normal maps; metallic R and inverse roughness A are packed for URP.

## Placement

Dimensions below are world-aligned renderer bounds in Unity units, including ramps and raised details. Deck Y is measured beneath the corresponding part or assembly center.

| Platform | Width × depth | Deck Y |
| --- | ---: | ---: |
| Central assembly dock | 22 × 22 | 0.990 |
| Engine camp | 6.6 × 4.5 | 0.500 |
| Hover observatory | 12.2 × 12.2 | 8.055 |
| Capsule hangar and pickup apron | 35.25 × 22.57 | 5.650 at capsule |
| Tractor dock | 14 × 11 | 0.864 at tractor |

The hangar rotates approximately 168.4° so its rectangular bay sits under the capsule and its connected circular apron reaches the existing pickup area. Thickness is fitted independently to retain the existing elevation and access routes.

Replaced blockout decks, rims and decorative rings are inactive. Their serialized objects remain so existing references are retained. The hangar's structural slab, building, safety curbs, route guides and access ramps remain. Gameplay part roots retain X/Z positions; their height and the 16 clamp roots are adjusted to the imported surfaces. The assembly target remains at Y=0.99.

## Validation

- Unity compiled and executed the integration tool.
- Saved scene contains five imported prefab instances with URP materials and five mesh colliders.
- No missing scene scripts detected.
- All four part hulls sit 0.025 units above their supporting mesh at the sampled center; all 16 clamp roots have supporting surfaces.
- Central surface matches the gameplay launch/docking height within 0.02 units.
- Reviewed the scene overview and each platform render with the actual scene lighting.
- Six-second Play Mode smoke check found all four part hulls and five platform colliders; part positions remained stable. No runtime errors were captured during that observation window. Returned to Edit Mode and reran placement validation successfully.
- Existing debris-prefab reference-type warnings remain in the editor log. Initial temporary inspection failures were corrected; their incidental serialized changes were removed.
- Full pickup/transport/welding gameplay, multiplayer and player builds were not exercised.

Preview images: [overview](assets/previews/stage1-assembly-platforms/Astra-platforms-overview.png), [central](assets/previews/stage1-assembly-platforms/CentralAssemblyDock-scene.png), [engine](assets/previews/stage1-assembly-platforms/EngineCamp-scene.png), [hover](assets/previews/stage1-assembly-platforms/HoverObservatory-scene.png), [hangar](assets/previews/stage1-assembly-platforms/CapsuleHangar-scene.png), [tractor](assets/previews/stage1-assembly-platforms/TractorDock-scene.png).

Detailed placement measurements and the pre-change scene backup are in `tmp/assembly-platform-integration/`. The temporary editor polling bridge is removed after validation.
