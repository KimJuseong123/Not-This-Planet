# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Latest feature change: the project now has an IMGUI lobby menu with looping MP4 background/fallback PNG, guest room flow, clipboard room-code copy, fade/loading transitions into the Mirror game scene, and a local Main-scene Esc menu that reuses the full settings UI and returns to the Lobby title. The Stage1 dust storm now receives one random seed per match from the server and applies it to every client without mutating the shared atmosphere asset. The earlier URP 17.5.0 atmosphere and build-safety work remains in place.

- Project root: `METAGAME/`
- Last analyzed: 2026-09-09
- Last analyzed commit: `edeeaaf`; current working branch: `S15P21B202-288-ui`.
- Latest structural change: the `Assets/SpaceFlop/` prototype tree was folded into the `Assets/_Project/` convention layout (folders, assemblies, namespaces, and hardcoded asset paths).

## Confirmed Environment

- Unity version: `6000.5.4f1` (`d550df8bd089`)
- Render pipeline: Universal Render Pipeline, package `17.5.0`; Graphics default is `PC_RPAsset`, and quality levels Very Low/Low/Medium use `Mobile_RPAsset` while High/Very High/Ultra use `PC_RPAsset`.
- Input system: Input System package `1.19.0` is installed and `activeInputHandler: 2` enables both the new and old input paths. The gameplay prototype still reads `UnityEngine.Input` directly (`Controls.cs`); the lobby uses IMGUI mouse/text input and does not require an EventSystem or input-action change.
- Target platforms: Standalone and Android settings are present; the final supported-platform decision is unknown

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Rendering | URP `17.5.0`, explicit PC/Mobile pipeline assets, URP Lit materials, `NotThisPlanet/*` URP shaders, reusable atmosphere shaders/features | Confirmed | `Packages/manifest.json`, `Assets/Settings/`, `ProjectSettings/GraphicsSettings.asset`, `Assets/_Project/Shaders/` |
| Input | Unity Input System `1.19.0` with an input-actions asset | Confirmed | `Packages/manifest.json`, `Assets/InputSystem_Actions.inputactions`, `ProjectSettings/ProjectSettings.asset` |
| Navigation | AI Navigation `2.0.13` | Confirmed | `Packages/manifest.json` |
| UI/content | uGUI `2.5.0`, Timeline `1.8.12`, Visual Scripting `1.9.11` | Confirmed | `Packages/manifest.json` |
| Multiplayer tooling | Mirror runtime with Spring REST/WebSocket lobby protocol; KCP/direct and relay match transports | Confirmed | `Assets/_Project/Scripts/Runtime/Network/`, `LobbyBootstrap.cs`, `Lobby.unity` |
| Testing | Unity Test Framework `1.7.0` with EditMode suites under `Assets/_Project/Scripts/Tests/Editor/` | Confirmed | `Packages/manifest.json`, test source inventory, and `SpaceSelfTest` |

## Directory Structure

| Path | Purpose | Confidence | Evidence |
| --- | --- | --- | --- |
| `Assets/Scenes/` | Enabled build scenes: `Lobby.unity` and `Main.unity` | Confirmed | Directory contents, `ProjectSettings/EditorBuildSettings.asset` |
| `Assets/Settings/` | URP assets, renderer assets, explicit baseline Volume profile, and URP global settings | Confirmed | Directory contents |
| `Assets/_Project/` | All first-party assets, including reusable atmosphere runtime/editor/shader/material resources | Confirmed | Directory contents |
| `Assets/_Project/Scripts/Runtime/` | `Core/`, `Gameplay/` (`Player/`, `World/`, `World/Atmosphere/`), `Network/`, `UI/`, `Bootstrap/` — one assembly per first-party boundary | Confirmed | `.asmdef` files and source inventory |
| `Assets/_Project/UI/` | Lobby fallback PNG and looping MP4 background assets with Unity importer metadata | Confirmed | `Not This Planet.png`, `Not This Planet lobby motion.mp4` |
| `Assets/_Project/Scripts/Editor/` | Build, scene/art tooling, URP setup, and build-safety validation; Editor-platform only | Confirmed | `Metagame.Editor.asmdef` |
| `Assets/InputSystem_Actions.inputactions` | Input action definitions | Confirmed | File exists and is referenced by build settings |
| `Assets/TutorialInfo/` | Template readme asset, editor helper, and tutorial layout | Confirmed | Directory contents and scripts |
| `Packages/` | Manifest and lockfile for package dependencies | Confirmed | `Packages/manifest.json`, `Packages/packages-lock.json` |
| `ProjectSettings/` | Unity project and build/editor configuration | Confirmed | Directory contents |
| `Docs/AI/` | Persistent project context for future agents | Confirmed | This document |

## Assembly Boundaries

| Assembly | Responsibility | Key references | Notes |
| --- | --- | --- | --- |
| `Metagame.Core` | Tuning constants, input polling, primitive/material factory | None | Must stay free of gameplay types — a reference back to `Metagame.Gameplay` would be a cycle |
| `Metagame.Gameplay` | Ragdoll player, world building, level parts, session state | `Metagame.Core` | `GameSession` lives here because it holds `PlayerRig`, `LevelBuilder`, and `Checkpoint` |
| `Metagame.UI` | IMGUI game HUD and lobby menu/background/transition | `Metagame.Core`, `Metagame.Gameplay`, `Metagame.Network`, `UnityEngine.UI` | Reads player/session state; `LobbyBackground` owns VideoPlayer/RenderTexture and `LobbyTransition` survives scene changes |
| `Metagame.Bootstrap` | Lobby/game scene entry points, self-test, screenshot runner | `Metagame.Core`, `Metagame.Gameplay`, `Metagame.UI`, `Metagame.Network`, `Mirror` | `LobbyBootstrap` bridges Spring lobby `GAME_STARTED` into Mirror; a separate assembly keeps `Gameplay` independent of `UI` |
| `Metagame.Editor` | Build pipeline, scene builder, FBX-to-Resources linking, model inspection | The four above, plus `UnityEditor` | `includePlatforms` is `Editor` only |
| `Assembly-CSharp` | Existing template runtime scripts | Unity defaults | `TutorialInfo` remains outside the first-party boundaries |
| `Assembly-CSharp-Editor` | Default editor assembly | `UnityEditor` and Unity defaults | Contains the template `ReadmeEditor` script |

## Scenes And Startup Flow

- 2026-09-13 customization update: `Lobby` solo/together buttons now enter `Assets/Scenes/Customizer.unity`. Solo confirmation continues to `Stage1_Astra`; together confirmation opens the existing room UI inside Customizer through `LobbyBootstrap`, then keeps the Spring/Mirror start flow. `Stage1_Ending → Lobby` is unchanged. `Customizer` is enabled and required by `SpaceBuild`. `CustomizationData` saves cosmetic selections; `PlayerAppearance` applies tint and head/chest accessories, and `NetPlayer` replicates validated selections via Command/SyncVar. See repository-root `docs/records/2026-09-13-character-customizer.md`. Earlier Main-only scene descriptions below are historical.

- Build scenes: `Assets/Scenes/Lobby.unity` and `Assets/Scenes/Main.unity` are enabled; `SpaceBuild` preserves existing entries and ensures both are present.
- Startup scene: `Lobby` is first and `Main` is second in the build list. `Main` holds the static Stage1 puzzle/environment and scene-side ship parts; the player ragdoll, runtime level, HUD, and legacy vacuum/starfield are composed in code.
- Scene loading flow: `LobbyBootstrap` creates the scene-independent `NetworkSession` and IMGUI `LobbyHud`, while `LobbyBackground` loops the MP4 over the PNG fallback. Solo play loads `Main` directly; multiplayer waits for the Spring WebSocket `GAME_STARTED`, fades out, starts Mirror host/client, and lets Mirror move the session to `Main`. `LobbyTransition` then fades in after `GameSession` is present. `Main` still assigns `Stage1AtmosphereProfile.asset` to `SpaceBootstrap`; `Lobby` remains on the neutral baseline Volume profile.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Project maturity | The SpaceFlop physics-ragdoll prototype runs on URP with a reusable atmosphere layer and explicit build-time shader protection | Confirmed | `Assets/Settings/`, `Assets/_Project/Scripts/Runtime/Gameplay/World/Atmosphere/`, `Assets/_Project/Scripts/Editor/UrpRenderValidation.cs` |
| Runtime architecture | Code-driven: one bootstrap component builds the runtime world; lobby bootstrap composes IMGUI menu and Spring/Mirror session flow with the scene's puzzle parts; `GamePauseMenu` owns local Esc presentation and delegates settings to `LobbyHud` | Confirmed | `SpaceBootstrap.cs`, `GamePauseMenu.cs`, `LobbyBootstrap.cs`, `LobbyHud.cs`, `NetworkSession.cs` |
| Assembly architecture | Core to Gameplay, Network and UI, then Bootstrap; Editor sits on top | Confirmed | First-party `.asmdef` files |
| Editor architecture | `Metagame.Editor` build/scene/art tooling, URP setup, renderer-feature wiring, and pink-shader validation, plus the template `ReadmeEditor` | Confirmed | `Assets/_Project/Scripts/Editor/`, `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs` |

## Coding Conventions

- Namespace style: `Metagame.Core`, `Metagame.Gameplay`, `Metagame.UI`, `Metagame.Bootstrap`, `Metagame.EditorTools`, each matching its assembly's `rootNamespace`. Template scripts still use the global namespace.
- File naming: one public type per file, file name equal to the type name.
- Serialized fields: prototype code exposes public fields on `MonoBehaviour`s instead of `[SerializeField] private`, and private fields are not `_camelCase` yet. The README states both rules as recommendations.
- Async/events: gameplay uses per-frame polling in `Update`/`FixedUpdate`; lobby transitions use coroutines, and `NetworkSession` marshals WebSocket events onto the Unity main thread.
- Comments/docs: Korean XML doc comments on most types, explaining the physics reasoning behind the numbers.

## Testing And Validation

- 2026-09-13: Editor/Development builds support local F11 perspective toggling through `GameSession`/`OrbitCamera`; forced vehicle/downed/push cameras retain priority and restore the chosen base perspective. Ending greetings now run sequentially with a face close-up per participant, then everyone boards together. `DemoEnding` derives stage times from snapshot player count; ending snapshots also carry appearance selections. Headless Unity compilation and 52 related tests passed. See repository-root `docs/records/2026-09-13-debug-perspective-ending-greetings.md`.

- Welding guide/material (2026-09-11): `PlasmaWeldingTool` now shares a first-hit ray query between tool actions and a purple collider-projected ring (0.8m maximum). Existing proximity APIs remain available, but the gun uses `WeldBead.CanApplyAt/TryApplyAt`. Each gun owns 60 seconds of successful welding material by default; equip/drop does not reset it. Successful cutting restores material to the cutting gun at the same time-based rate, capped at full. `WorldSync` transmits remaining fraction through `ViewProgress`, including dropped tools, and replicates ray-based bead changes through its existing bead snapshots. `GameHud` projects a yellow annular-sector fill beside the visible gun with fixed 0/25/50/75/100% ticks and no numeric current-remaining readout. No scene/prefab wiring is needed. See `docs/records/2026-09-11-welding-guide-material.md` at repository root for behavior and validation scope.

- Assembly-pad welding (2026-09-11): core parts can weld to static surfaces in the existing assembly region through `WeldBead` contact. Simply placing a part does not create a joint. Contacting bead volume controls joint strength and useful-bead color; cutting the bridge releases it. Tractor grabbing and engine boarding are blocked while the pad weld is intact, so moving the part requires cutting it first. Pad welds are separate from `PartAssemblyLink` and do not add a floor to the ship graph. No scene/prefab setup is required.
- Multiplayer pad welds use existing grow/cut events and bead snapshots. `ChainState.PadWeldIntact` preserves broken/intact status, and `WorldSync.SameWeld` detects changes even when bead shape stays the same. Remote bodies remain kinematic. Unity EditMode validation: 85/85 passed (`WeldBeadJoinTests`, `WeldBeadRestoreTests`, `WeldBeadTests`, `TractorBeamTests`, `AssemblyFloorStateTests`), including core parts 1–4, cutting, excluding ordinary ground outside the pad, and remote intact/broken snapshot restoration. Report: `.local-validation/pad-weld.xml` at repository root. Live host/client play remains unverified: place a part without welding, weld the part/pad seam, observe both peers, cut it, then check late join and authority handover.
- UI accessibility (2026-09-11): Settings > Graphics includes `UI 텍스트 크기 조정` (80–150%, 5% steps). `display.uiTextScale` uses the existing local preview/save/cancel/default flow. `UiScale.Scope` scales IMGUI text and controls together using virtual screen bounds; camera-projected nameplates and pings convert pixels into those coordinates. The first-person tablet scales its model and attached canvas together to preserve internal layout. No scene or prefab wiring is required.
- UI scale validation: generated UI and EditMode-test projects compiled with zero errors; tests were compiled, not executed. Remaining Play Mode checks: lobby and Esc settings at 80/100/150%, slider and scroll input, save/restart, cancel/defaults, HUD screen anchors and ping/nameplate targets, tablet visibility at maximum size, and independent settings on two clients. Check both 1280×720 and 1920×1080.

- EditMode tests: Unity Test Framework suites exist under `Assets/_Project/Scripts/Tests/Editor/`; `AtmosphereProfileTests.cs` covers profile validation/default opt-in behavior. Lobby-specific behavior is currently intended for Play Mode/manual integration checks because it depends on `VideoPlayer`, scene loading, IMGUI, Mirror, and the live Spring WebSocket contract.
- PlayMode tests: none; `Assets/_Project/Scripts/Tests/Runtime/` is still empty.
- Runtime self-test: `SpaceSelfTest` feeds input straight into `PlayerRig.SetInput()` and asserts roughly 112 invariants. The Stage1 exploration flow additionally needs a Play Mode smoke test for Tab, proximity discovery, and tablet input lock. The current Windows-player run reached 110/112 checks; its 2 failures are unrelated existing light-box/crater-geometry checks.
- CI/build validation: no CI configuration. `Metagame.EditorTools.SpaceBuild.BuildWindows` is callable via `-executeMethod` and invokes `UrpRenderValidation` before building; the current static checks compiled `Metagame.Gameplay` and `Metagame.UI` with 0 errors, and the new `GamePauseMenu` source compiled against the generated Unity references with 0 errors. The generated `Metagame.Bootstrap.csproj` still reports pre-existing missing `MeteorClockBridge`/`NetDiagnostics` types in its generated Network reference set. A fresh Unity asset import/Windows build still needs the already-open Editor session to finish importing and acquire the project lock/license.

## Available Unity Tooling

| Capability | Status | Evidence |
| --- | --- | --- |
| Unity MCP/editor connection | unavailable in this session | No Unity-specific MCP tool is exposed to Codex; findings are repository-based |
| Unity test/console/scene inspection | unavailable in this session | No Unity-specific MCP tool is exposed to Codex |
| VS Code attach configuration | available | `METAGAME/.vscode/launch.json`, `extensions.json` |

## Important Constraints

- Commit `Assets/`, `Packages/`, `ProjectSettings/`, and all Unity `.meta` files.
- Do not commit Unity-generated caches, local editor state, generated IDE files, build outputs, or signing credentials; repository-root `.gitignore` covers these.
- Keep `Packages/manifest.json` and `Packages/packages-lock.json` versioned so collaborators resolve the same dependencies.
- Assets moved between folders must carry their `.meta` files so GUIDs, and therefore scene and prefab references, survive.
- `Prim` and the model loaders resolve assets by `Resources.Load` name, so `Shaders/Resources/` and `Prefabs/Resources/` have to stay inside a folder literally named `Resources`.
- `Prim` uses only `NotThisPlanet/*` URP shaders with URP Lit as the last safe fallback; Built-in Standard/Diffuse is not a runtime fallback. The required shaders are protected through Graphics Settings Always Included Shaders and validated before builds.
- Atmosphere is stage-independent: `Main` uses `Assets/_Project/Data/Atmosphere/Stage1AtmosphereProfile.asset`; create another profile under the same folder for Stage2+ and apply it through `AtmosphereController`. Keep Stage-specific values out of the baseline Volume profile and `Lobby`.
- Editor tooling holds absolute asset paths (`ArtLink`, `SpaceBuild`, `SpaceSceneBuilder`, `ArtInspect`, `ModelValidator`); moving the models or the scene means updating those constants.
- Large binary assets are configured for Git LFS in the repository-root `.gitattributes`; Unity YAML assets remain regular text files because project serialization is ForceText.

## Unknowns And Confidence

- Save design and supported release platforms are not established yet.
- The multiplayer flow is a hybrid: Spring owns guest auth, room code, ready/start, and WebSocket state; Mirror owns the match scene and replicated gameplay. It is not a new backend protocol.
- Settings is implemented as a persistent local JSON-backed settings screen in the Lobby and is now reachable from the Main-scene Esc menu through the same preview/save/cancel flow.
- The current Stage1 exploration state is local runtime state and is not network-replicated or persisted; supported release platforms are not established yet.

## Source Files Inspected

- Updated `Assets/_Project/Scripts/Runtime/Gameplay/GameSession.cs`, `Assets/_Project/Scripts/Runtime/Bootstrap/SpaceBootstrap.cs`, `Assets/_Project/Scripts/Runtime/Core/Controls.cs`, and `Assets/_Project/Scripts/Runtime/UI/GameHud.cs` for Stage1 exploration composition, Tab input, tablet pause, and hints.
- Added `Assets/_Project/Scripts/Runtime/UI/GamePauseMenu.cs` and updated `GameSession.cs`/`SpaceBootstrap.cs` for per-player Esc menu toggling, local input suppression without pausing world time, settings entry, and title return.
- Updated `Assets/_Project/Scripts/Runtime/UI/LobbyHud.cs` to expose a settings-only reuse path and `Assets/_Project/Scripts/Runtime/UI/LobbyTransition.cs` to cleanly return to the Lobby scene from Main.
- Added `Assets/_Project/Scripts/Runtime/UI/LobbyBackground.cs`, `Assets/_Project/Scripts/Runtime/UI/LobbyTransition.cs`, and the expanded `Assets/_Project/Scripts/Runtime/UI/LobbyHud.cs` for the looping lobby background, menu/room UI, hover styles, clipboard copy, and fade/loading flow.
- Updated `Assets/_Project/Scripts/Runtime/Network/NetworkSession.cs` with lobby disconnect and client-only guest logout cleanup, and `Assets/_Project/Scripts/Runtime/Bootstrap/LobbyBootstrap.cs`/`Assets/Scenes/Lobby.unity` to connect the visual assets and defer Mirror startup through the transition overlay.
- Added `Assets/_Project/UI/Not This Planet.png` and `Assets/_Project/UI/Not This Planet lobby motion.mp4` with Unity `.meta` files.
- Added `Assets/_Project/Scripts/Runtime/Gameplay/World/Stage1Exploration.cs`, `Assets/_Project/Scripts/Runtime/UI/Stage1ExplorationHud.cs`, and `Assets/_Project/Scripts/Tests/Editor/Stage1ExplorationTests.cs` with Unity `.meta` files.
- Added `Assets/_Project/Scripts/Runtime/Gameplay/World/Atmosphere/` (`AtmosphereProfile`, `AtmosphereController`, `AtmosphereBackdrop`, `AtmosphereMeteorShower`, `Stage1AtmosphereAlert`, `AtmosphereHazeRendererFeature`) and the reusable URP atmosphere shaders/materials. Added `Stage1AtmosphereProfile.asset` and wired it to `Main` through `SpaceBootstrap`; the `Stage1AtmosphereSetup` editor tool is included in the full Stage1 setup. `DustStormClockBridge` and `WorldSync` now share one server-created match seed between the physical hazard and every client visual.
- Added `Assets/_Project/Scripts/Editor/UrpProjectSetup.cs` and `UrpRenderValidation.cs`; updated `SpaceBuild.cs` to enforce the pre-build validation and shader inclusion.
- Converted the first-party material set to URP Lit and rewrote `Assets/_Project/Shaders/Resources/DoughSurface.shader`, `GhostSurface.shader`, and `FlatUnlit.shader` for URP while preserving shader names and runtime property names.
- `ProjectSettings/ProjectVersion.txt`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/GraphicsSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/VersionControlSettings.asset`
- `ProjectSettings/EditorSettings.asset`
- `Assets/TutorialInfo/Scripts/Readme.cs`
- `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs`
- `Assets/_Project/Scripts/Runtime/*/*.asmdef`, `Assets/_Project/Scripts/Editor/Metagame.Editor.asmdef`
- `Assets/_Project/Scripts/**/*.cs`
- `Assets/Scenes/Main.unity`
- `METAGAME/README.md`
- `.gitattributes`
- `README.md`
- `METAGAME/.vscode/*`

<!-- unity-onboarding:generated:end -->
